using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ComfyGenReader.Core.Media;
using LibVLCSharp.Shared;

namespace ComfyGenReader.Controls;

/// <summary>
/// LibVLC video playback with Play/Pause, a seek bar with frame previews, and Mute.
/// Each file autoplays muted and loops.
/// The control itself is the video surface; <see cref="ControlsBar"/> is placed by the host.
/// </summary>
public partial class VideoPlayer : UserControl, IDisposable
{
    private const int PreviewWidth = 160;
    private static readonly TimeSpan ScrubDebounce = TimeSpan.FromMilliseconds(120);
    private static Task<LibVLC>? warmUp;

    private readonly Dictionary<int, BitmapSource> scrubCache = [];
    private readonly DispatcherTimer scrubTimer = new() { Interval = ScrubDebounce };
    private LibVLC? libVlc;
    private MediaPlayer? player;
    private string? currentPath;
    private long lengthMs;
    private bool muted = true;
    private bool dragging;
    private bool settingFromPlayer;
    private bool scrubHover;
    private int scrubSecond;
    private CancellationTokenSource? scrubCts;
    private bool disposed;

    public VideoPlayer()
    {
        InitializeComponent();
        // Split the two parts: View becomes this control's content, ControlsBar is placed by the host.
        Parts.Children.Clear();
        Content = View;
        // Set in code: an ElementName binding would not resolve once ControlsBar sits in the host's namescope.
        ScrubPopup.PlacementTarget = SeekBar;
        scrubTimer.Tick += ScrubTimer_Tick;
    }

    public event EventHandler<Exception>? Failed;

    /// <summary>
    /// Starts loading LibVLC from lib\libvlc on a background thread at startup: creating the instance scans every
    /// plugin DLL, which takes many seconds on a fresh extraction (antivirus scans each one) and must not block the UI.
    /// A failure is reported on the first <see cref="LoadAsync"/>.
    /// </summary>
    public static void StartLibVlcWarmUp()
    {
        warmUp = Task.Run(() =>
        {
            try { LibVLCSharp.Shared.Core.Initialize(Path.Combine(AppContext.BaseDirectory, "lib", "libvlc")); }
            catch (Exception ex) { throw new InvalidOperationException(@"LibVLC could not be loaded from lib\libvlc.", ex); }
            return new LibVLC("--no-video-title-show");
        });
        // Observed here so an unused failure never surfaces as an unobserved task exception; LoadAsync still rethrows it.
        warmUp.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
    }

    public async Task LoadAsync(string path)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ResetScrub();
        currentPath = path;
        lengthMs = 0;
        SetSeekBar(0);
        UpdateTime(0);
        // Every file starts muted (VolumeChanged re-applies it if the audio output is recreated).
        muted = true;
        UpdateMuteButton(true);

        if (player is null)
        {
            var pending = warmUp ?? throw new InvalidOperationException("LibVLC warm-up was not started.");
            PreparingText.Visibility = Visibility.Visible;
            LibVLC vlc;
            try { vlc = await pending; }
            catch when (disposed || currentPath != path) { return; } // superseded while LibVLC was loading
            finally { PreparingText.Visibility = Visibility.Collapsed; }
            if (disposed) return;
            CreatePlayer(vlc); // on the UI thread; a no-op if another pending load got here first
            if (currentPath != path) return;
        }

        var mp = player!;
        mp.Mute = true;
        using var media = new Media(libVlc!, path, FromType.FromPath);
        // Autoplay and loop: LibVLC restarts the input itself, with no Stop/Play gap (65535 is the option's maximum).
        media.AddOption(":input-repeat=65535");
        // View's native surface only exists once the just-shown control is laid out, and that hands its HWND to the
        // player. Without it, VLC can start first and open its own top-level window instead of drawing in View.
        if (mp.Hwnd == IntPtr.Zero) View.UpdateLayout();
        if (!mp.Play(media))
            throw new InvalidOperationException($"LibVLC could not start {Path.GetFileName(path)}.");
    }

    public void Unload()
    {
        if (currentPath is null) return;
        ResetScrub();
        currentPath = null;
        lengthMs = 0;
        player?.Stop();
        SetSeekBar(0);
        UpdateTime(0);
        UpdatePlayButton(false);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        ResetScrub();
        View.MediaPlayer = null;
        player?.Dispose();
        libVlc?.Dispose();
        View.Dispose();
    }

    private void CreatePlayer(LibVLC vlc)
    {
        if (player is not null) return;
        libVlc = vlc;
        var mp = new MediaPlayer(vlc) { Scale = 0, AspectRatio = null }; // fit to the view, no distortion
        mp.Playing += (_, _) => OnUi(() => UpdatePlayButton(true));
        // Stop() destroys the audio output and a new one starts with the OS session's last mute state,
        // ignoring a Mute set while none existed. Every new output reports its volume: re-apply ours then.
        mp.VolumeChanged += (_, _) => OnUi(() => { if (mp.Mute != muted) mp.Mute = muted; });
        mp.Paused += (_, _) => OnUi(() => UpdatePlayButton(false));
        mp.EndReached += (_, _) => OnUi(() => UpdatePlayButton(false));
        mp.Stopped += (_, _) => OnUi(() => UpdatePlayButton(false));
        mp.PositionChanged += (_, e) => OnUi(() => { if (!dragging) SetSeekBar(e.Position); });
        mp.TimeChanged += (_, e) => OnUi(() => UpdateTime(e.Time));
        mp.LengthChanged += (_, e) => OnUi(() =>
        {
            lengthMs = e.Length;
            UpdateTime(mp.Time);
        });
        mp.Muted += (_, _) => OnUi(() => UpdateMuteButton(true));
        mp.Unmuted += (_, _) => OnUi(() => UpdateMuteButton(false));
        mp.EncounteredError += (_, _) => OnUi(() =>
            Failed?.Invoke(this, new InvalidOperationException($"LibVLC could not play {Path.GetFileName(currentPath)}.")));

        View.MediaPlayer = mp;
        player = mp;
    }

    /// <summary>LibVLC raises events on its own threads (and must not be called back from them).</summary>
    private void OnUi(Action action) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (disposed) return;
            try { action(); }
            catch (Exception ex) { Failed?.Invoke(this, ex); }
        });

    // Clicking the picture toggles playback, like the Play/Pause button.
    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => PlayPause_Click(sender, e);

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (player is not { } mp || currentPath is null) return;
            if (mp.IsPlaying) mp.SetPause(true);
            else if (mp.State == VLCState.Ended)
            {
                mp.Stop(); // an ended input ignores Play
                mp.Play();
            }
            else mp.Play();
        }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
    }

    private void Mute_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            muted = !muted;
            if (player is { } mp) mp.Mute = muted;
            UpdateMuteButton(muted);
        }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
    }

    private void SeekBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Track clicks and keyboard steps seek at once; a thumb drag seeks on release.
        if (settingFromPlayer || dragging) return;
        Seek(e.NewValue);
    }

    private void SeekBar_DragStarted(object sender, DragStartedEventArgs e) => dragging = true;

    private void SeekBar_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        dragging = false;
        Seek(SeekBar.Value);
        if (!SeekBar.IsMouseOver) HideScrub();
    }

    private void Seek(double position)
    {
        try
        {
            if (player is not { IsSeekable: true } mp) return;
            mp.Position = (float)position;
            if (!mp.IsPlaying) UpdateTime((long)(position * lengthMs)); // no TimeChanged while paused
        }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
    }

    private void SeekBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (currentPath is null || lengthMs <= 0 || SeekBar.ActualWidth <= 0) return;
        var x = Math.Clamp(e.GetPosition(SeekBar).X, 0, SeekBar.ActualWidth);
        scrubHover = true;
        // Clamped to the last whole second that has frames (x == ActualWidth would give the full length).
        var second = Math.Min((int)(x / SeekBar.ActualWidth * lengthMs / 1000), (int)((lengthMs - 1) / 1000));
        if (second != scrubSecond) scrubCts?.Cancel(); // the in-flight grab is for a second no longer hovered
        scrubSecond = second;
        ScrubPopup.HorizontalOffset = x - PreviewWidth / 2.0;

        scrubTimer.Stop();
        if (scrubCache.TryGetValue(scrubSecond, out var frame)) ShowScrubFrame(frame);
        else scrubTimer.Start(); // debounce: grab only once the cursor rests
    }

    private void SeekBar_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!dragging) HideScrub();
    }

    private async void ScrubTimer_Tick(object? sender, EventArgs e)
    {
        scrubTimer.Stop();
        if (currentPath is not { } path) return;
        var second = scrubSecond;
        scrubCts?.Cancel();
        var cts = scrubCts = new CancellationTokenSource();
        try
        {
            var width = (int)Math.Round(PreviewWidth * System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX);
            var png = await FfmpegTools.GrabFramePngAsync(path, TimeSpan.FromSeconds(second), width, cts.Token);
            if (cts.IsCancellationRequested || path != currentPath || png is null) return;
            var frame = DecodePng(png);
            scrubCache[second] = frame;
            if (scrubHover && second == scrubSecond) ShowScrubFrame(frame); // never replace a newer second's frame
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Failed?.Invoke(this, ex); }
        finally
        {
            if (scrubCts == cts) scrubCts = null;
            cts.Dispose();
        }
    }

    private void ShowScrubFrame(BitmapSource frame)
    {
        ScrubImage.Source = frame;
        ScrubPopup.IsOpen = true;
    }

    private void HideScrub()
    {
        scrubHover = false;
        scrubTimer.Stop();
        scrubCts?.Cancel();
        ScrubPopup.IsOpen = false;
    }

    /// <summary>Hides the preview and forgets the cached frames (they belong to the current file).</summary>
    private void ResetScrub()
    {
        HideScrub();
        ScrubImage.Source = null;
        scrubCache.Clear();
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

    private void SetSeekBar(double position)
    {
        settingFromPlayer = true;
        try { SeekBar.Value = position; }
        finally { settingFromPlayer = false; }
    }

    private void UpdateTime(long timeMs) =>
        TimeLabel.Text = $"{FormatTime(timeMs)} / {FormatTime(lengthMs)}";

    private static string FormatTime(long ms)
    {
        var t = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
    }

    private void UpdatePlayButton(bool playing) => PlayPauseButton.Content = playing ? "⏸ Pause" : "▶ Play";

    private void UpdateMuteButton(bool isMuted)
    {
        MuteButton.Content = isMuted ? "🔇" : "🔊";
        MuteButton.ToolTip = isMuted ? "Unmute" : "Mute";
    }
}
