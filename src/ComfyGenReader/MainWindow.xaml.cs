using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComfyGenReader.Core;
using ComfyGenReader.Core.Media;
using Microsoft.Win32;

namespace ComfyGenReader;

public partial class MainWindow : Window
{
    private const int FallbackDecodeWidth = 4096;

    private readonly ReaderSession session;
    private readonly AppSettings settings;
    private CancellationTokenSource? previewCts;
    private string? previewPath;
    private MediaKind previewKind;
    private int? currentMatch; // start of the current find match in Raw

    public MainWindow(ReaderSession session, AppSettings settings)
    {
        this.session = session;
        this.settings = settings;
        InitializeComponent();
        // Hooked after the initial state, so only the user's changes switch the theme and save.
        DarkModeToggle.IsChecked = settings.DarkMode;
        DarkModeToggle.Checked += DarkMode_Changed;
        DarkModeToggle.Unchecked += DarkMode_Changed;
        WrapToggle.Checked += Wrap_Changed;
        WrapToggle.Unchecked += Wrap_Changed;
        DataContext = session;
        session.PropertyChanged += Session_PropertyChanged;

        VideoControlsHost.Content = Player.ControlsBar;
        // The video surface is a native window above WPF; its overlay forwards drops to the left frame's handlers.
        Player.DropOverlay.DragOver += Preview_DragOver;
        Player.DropOverlay.Drop += Preview_Drop;
        Highlighter.Attach(RawBox);
    }

    protected override void OnClosed(EventArgs e)
    {
        Player.Unload();
        Player.Dispose(); // the app's one LibVLC + MediaPlayer
        base.OnClosed(e);
    }

    private void Player_Failed(object? sender, Exception ex) => session.ShowError(ex);

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

    private void DarkMode_Changed(object sender, RoutedEventArgs e)
    {
        try
        {
            settings.DarkMode = DarkModeToggle.IsChecked == true;
            var (vertical, horizontal) = (RawBox.VerticalOffset, RawBox.HorizontalOffset);
            Theme.Apply(Application.Current, settings.DarkMode);
            // The new theme rebuilds Raw's template, which scrolls it back to the top.
            Dispatcher.InvokeAsync(() =>
            {
                RawBox.ScrollToVerticalOffset(vertical);
                RawBox.ScrollToHorizontalOffset(horizontal);
            }, DispatcherPriority.Loaded);
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void Wrap_Changed(object sender, RoutedEventArgs e) =>
        RawBox.TextWrapping = WrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;

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

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                OpenFind();
            }
            else if (e.Key == Key.F3 && FindBar.IsVisible)
            {
                e.Handled = true;
                if (Keyboard.Modifiers == ModifierKeys.Shift) FindPrevious();
                else FindNext();
            }
            else if (e.Key == Key.Escape && FindBar.IsVisible && (FindBar.IsKeyboardFocusWithin || RawBox.IsKeyboardFocused))
            {
                e.Handled = true;
                CloseFind();
            }
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void FindBox_KeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (Keyboard.Modifiers == ModifierKeys.Shift) FindPrevious();
            else FindNext();
        }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Find as you type: the current match grows or moves on from where it starts.
        try { Find(RawBox.SelectionStart, forward: true); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void RawBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // A new file (or an error) replaced the text: search it from the top.
        try { if (FindBar.IsVisible) Find(0, forward: true); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void FindNext_Click(object sender, RoutedEventArgs e)
    {
        try { FindNext(); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e)
    {
        try { FindPrevious(); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void FindClose_Click(object sender, RoutedEventArgs e)
    {
        try { CloseFind(); }
        catch (Exception ex) { session.ShowError(ex); }
    }

    private void OpenFind()
    {
        // Seed the query with a single-line selection in Raw, as editors do.
        if (RawBox.SelectionLength > 0 && !RawBox.SelectedText.Contains('\n')) FindBox.Text = RawBox.SelectedText;
        FindBar.Visibility = Visibility.Visible;
        FindBox.Focus();
        FindBox.SelectAll();
        Find(RawBox.SelectionStart, forward: true); // reopened with the same query: no TextChanged to repaint it
    }

    private void CloseFind()
    {
        FindBar.Visibility = Visibility.Collapsed;
        FindStatus.Text = "";
        currentMatch = null;
        Highlighter.Clear();
        RawBox.Focus();
    }

    // Past the current match (the caret sits at its start), so it is not found again; else from the caret.
    private void FindNext() =>
        Find(RawBox.SelectionStart == currentMatch ? RawBox.SelectionStart + 1 : RawBox.SelectionStart, forward: true);

    private void FindPrevious() => Find(RawBox.SelectionStart, forward: false);

    /// <summary>
    /// Highlights every case-insensitive match and moves to the next (or previous) one from <paramref name="position"/>,
    /// wrapping. The caret goes to the match's start rather than selecting it, so the highlight shows through.
    /// </summary>
    private void Find(int position, bool forward)
    {
        var query = FindBox.Text;
        var matches = TextFinder.FindAll(RawBox.Text, query);
        var i = forward ? TextFinder.NextIndex(matches, position) : TextFinder.PreviousIndex(matches, position);
        if (i < 0)
        {
            currentMatch = null;
            Highlighter.Clear();
            RawBox.Select(Math.Min(position, RawBox.Text.Length), 0);
            FindStatus.Text = query.Length == 0 ? "" : "No matches";
            return;
        }
        currentMatch = matches[i];
        RevealMatch(matches[i], query.Length);
        Highlighter.Show(matches, query.Length, i);
        FindStatus.Text = $"{i + 1} of {matches.Count}";
    }

    private void RevealMatch(int start, int length)
    {
        RawBox.Select(start, 0);
        RawBox.ScrollToLine(RawBox.GetLineIndexFromCharacterIndex(start));
        // ScrollToLine is vertical only; long lines (NoWrap) also need the match brought into view horizontally.
        RawBox.UpdateLayout();
        var first = RawBox.GetRectFromCharacterIndex(start);
        var last = RawBox.GetRectFromCharacterIndex(start + length);
        if (first.IsEmpty || last.IsEmpty) return;
        if (first.Left < 0 || last.Right > RawBox.ViewportWidth)
            RawBox.ScrollToHorizontalOffset(Math.Max(0, RawBox.HorizontalOffset + first.Left - RawBox.ViewportWidth / 3));
    }

    private static string[] DroppedPaths(DragEventArgs e)
    {
        try
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths
                ? paths
                : [];
        }
        catch (Exception)
        {
            return []; // the drag source's data could not be read (e.g. a COM error from another app): nothing to open
        }
    }

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

            var isVideo = path is not null && kind == MediaKind.Video;
            Player.Visibility = VideoControlsHost.Visibility = isVideo ? Visibility.Visible : Visibility.Collapsed;
            if (isVideo) await Player.LoadAsync(path!);
            else Player.Unload();

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
        // A stream rather than UriSource: Uri unescapes %XX sequences that are legal in file names.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return DecodeStream(stream);
    }

    private static BitmapImage DecodePng(byte[] png)
    {
        using var stream = new MemoryStream(png);
        return DecodeStream(stream);
    }

    /// <summary>Decodes fully (OnLoad) so the caller can dispose the stream and nothing stays locked.</summary>
    private static BitmapImage DecodeStream(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.StreamSource = stream;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
