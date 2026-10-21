using System.Diagnostics;
using System.Text.Json;
using ComfyGenReader.Core.Media;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

public sealed class FfmpegFixture : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cgr-ffmpeg-" + Guid.NewGuid().ToString("N"));

    public string TextFilePath => Path.Combine(_dir, "note.txt");

    public string PathFor(string ext) => Path.Combine(_dir, "video." + ext);

    public FfmpegFixture()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(TextFilePath, "not a video");
        var json = JsonSerializer.Serialize(new { prompt = ApiPromptString, workflow = WorkflowString });
        foreach (var ext in new[] { "mp4", "webm", "avi" })
            Generate(PathFor(ext), json);
    }

    private static string ApiPromptString => ComfySamples.ApiPrompt;
    private static string WorkflowString => ComfySamples.Workflow;

    private static void Generate(string output, string json)
    {
        var psi = new ProcessStartInfo(Path.Combine(FfmpegTools.ToolsDirectory, "ffmpeg.exe"))
        {
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc=duration=2:size=64x64:rate=10",
                                  "-metadata", "comment=" + json, "-y", output })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException($"ffmpeg failed for {output}: {err}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}

public class FfmpegToolsTests(FfmpegFixture fx) : IClassFixture<FfmpegFixture>
{
    [Theory, InlineData("mp4"), InlineData("webm"), InlineData("avi")]
    public async Task ReadTags_FindsComfyComment(string ext)
    {
        var r = MetadataParser.Parse(await FfmpegTools.ReadTagsAsync(fx.PathFor(ext), TestContext.Current.CancellationToken));
        Assert.NotNull(r.Prompt);
        Assert.NotNull(r.Workflow);
    }

    [Fact]
    public async Task GrabFrame_ReturnsPng()
    {
        var png = await FfmpegTools.GrabFramePngAsync(fx.PathFor("mp4"), TimeSpan.FromSeconds(1), 64, TestContext.Current.CancellationToken);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png![..4]);
    }

    [Fact]
    public async Task GrabFrame_NeverUpscales()
    {
        var png = await FfmpegTools.GrabFramePngAsync(fx.PathFor("mp4"), TimeSpan.Zero, 4096, TestContext.Current.CancellationToken);
        var width = (png![16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        Assert.Equal(64, width);
    }

    [Fact]
    public async Task ReadTags_NotAVideo_Throws() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() => FfmpegTools.ReadTagsAsync(fx.TextFilePath, TestContext.Current.CancellationToken));

    [Fact]
    public async Task GrabFrame_NotAVideo_ReturnsNull() =>
        Assert.Null(await FfmpegTools.GrabFramePngAsync(fx.TextFilePath, TimeSpan.Zero, 64, TestContext.Current.CancellationToken));

    [Fact]
    public async Task ReadTags_CallerContextBlockedPastTimeout_StillReturnsTags()
    {
        var path = fx.PathFor("mp4");
        // Warm ffprobe so the short timeout below measures only the call under test.
        await FfmpegTools.ReadTagsAsync(path, TestContext.Current.CancellationToken);

        var saved = FfmpegTools.ReadTagsTimeout;
        FfmpegTools.ReadTagsTimeout = TimeSpan.FromSeconds(1);
        try
        {
            using var ui = new SingleThreadContext();
            var tags = await ui.Run(async () =>
            {
                var pending = FfmpegTools.ReadTagsAsync(path);
                Thread.Sleep(TimeSpan.FromSeconds(2)); // the UI thread is busy (e.g. loading LibVLC) past the timeout
                return await pending;
            });
            Assert.NotNull(MetadataParser.Parse(tags).Prompt);
        }
        finally { FfmpegTools.ReadTagsTimeout = saved; }
    }

    /// <summary>A dispatcher-like context: one thread runs posted callbacks in order.</summary>
    private sealed class SingleThreadContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> queue = [];
        private readonly Thread thread;

        public SingleThreadContext()
        {
            thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in queue.GetConsumingEnumerable()) callback(state);
            }) { IsBackground = true };
            thread.Start();
        }

        public override void Post(SendOrPostCallback d, object? state) => queue.Add((d, state));

        public Task<T> Run<T>(Func<Task<T>> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try { tcs.SetResult(await func()); }
                catch (Exception ex) { tcs.SetException(ex); }
            }, null);
            return tcs.Task;
        }

        public void Dispose()
        {
            queue.CompleteAdding();
            thread.Join();
            queue.Dispose();
        }
    }
}
