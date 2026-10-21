using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ComfyGenReader.Core;
using ComfyGenReader.Core.Media;
using Microsoft.Win32;

namespace ComfyGenReader;

public partial class MainWindow : Window
{
    private const int FallbackDecodeWidth = 4096;

    private readonly ReaderSession session;
    private CancellationTokenSource? previewCts;
    private string? previewPath;
    private MediaKind previewKind;

    public MainWindow(ReaderSession session)
    {
        this.session = session;
        InitializeComponent();
        DataContext = session;
        session.PropertyChanged += Session_PropertyChanged;
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new OpenFileDialog { Filter = SupportedFiles.DialogFilter };
            if (dialog.ShowDialog(this) == true)
                await session.OpenAsync(dialog.FileName, OpenSource.User);
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        try { await session.OpenAsync(session.PathText, OpenSource.User); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private async void PathBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            await session.OpenAsync(session.PathText, OpenSource.User);
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private async void RecentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            // Resetting SelectedIndex to -1 (or Recent being replaced) re-enters with no selection.
            if (RecentCombo.SelectedItem is not string path) return;
            try { await session.OpenAsync(path, OpenSource.User); }
            finally { RecentCombo.SelectedIndex = -1; }
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        try { await session.GoPreviousAsync(); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        try { await session.GoNextAsync(); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void Preview_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DroppedPaths(e).Any(SupportedFiles.IsSupported) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Preview_Drop(object sender, DragEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (DroppedPaths(e).FirstOrDefault(SupportedFiles.IsSupported) is { } path)
                await session.OpenAsync(path, OpenSource.User);
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (session.ExportJson is not { } json || session.CurrentFile is not { } file) return;
            var dialog = new SaveFileDialog
            {
                Filter = "JSON|*.json",
                DefaultExt = ".json",
                FileName = Path.GetFileNameWithoutExtension(file) + ".json",
            };
            if (dialog.ShowDialog(this) == true)
                File.WriteAllText(dialog.FileName, json, new UTF8Encoding(false));
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private static string[] DroppedPaths(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths
            : [];

    private void Session_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // OpenAsync sets CurrentFile and CurrentKind back to back; defer so both are settled.
        if (e.PropertyName is nameof(ReaderSession.CurrentFile) or nameof(ReaderSession.CurrentKind))
            Dispatcher.InvokeAsync(RefreshPreview);
    }

    private async void RefreshPreview()
    {
        try
        {
            var path = session.CurrentFile;
            var kind = session.CurrentKind;
            if (path == previewPath && kind == previewKind) return;
            previewPath = path;
            previewKind = kind;

            previewCts?.Cancel();
            var ct = (previewCts = new CancellationTokenSource()).Token;
            PreviewImage.Source = null;
            if (path is null || kind != MediaKind.Image) return;

            try
            {
                var image = await LoadImageAsync(path, ct);
                if (!ct.IsCancellationRequested) PreviewImage.Source = image;
            }
            catch (Exception) when (ct.IsCancellationRequested)
            {
                // A newer file replaced this one; drop the stale result.
            }
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private static async Task<BitmapSource> LoadImageAsync(string path, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => DecodeWithWic(path), ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // WIC could not decode it (e.g. no WebP codec installed); let ffmpeg render it to PNG.
            var png = await FfmpegTools.GrabFramePngAsync(path, TimeSpan.Zero, FallbackDecodeWidth, ct);
            if (png is null) throw;
            return DecodePng(png);
        }
    }

    private static BitmapImage DecodeWithWic(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(Path.GetFullPath(path));
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static BitmapImage DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
