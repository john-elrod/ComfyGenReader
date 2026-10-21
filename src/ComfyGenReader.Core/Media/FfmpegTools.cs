using System.Diagnostics;
using System.Globalization;
using System.Text.Json;

namespace ComfyGenReader.Core.Media;

/// <summary>Wrappers around the bundled ffprobe/ffmpeg executables.</summary>
public static class FfmpegTools
{
    /// <summary>How long ffprobe may run before it is killed; settable for tests.</summary>
    internal static TimeSpan ReadTagsTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public static string ToolsDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "lib", "ffmpeg");

    public static async Task<IReadOnlyList<KeyValuePair<string, string>>> ReadTagsAsync(string path, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(ReadTagsTimeout);

        var (code, stdout, stderr) = await RunTextAsync("ffprobe.exe",
            ["-v", "error", "-print_format", "json", "-show_format", "-show_streams", path], timeout.Token).ConfigureAwait(false);
        if (code != 0)
            throw new InvalidOperationException(stderr.Length > 0 ? stderr : $"ffprobe exited with code {code}");

        var tags = new List<KeyValuePair<string, string>>();
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        if (root.TryGetProperty("format", out var format))
            AddTags(tags, format, "");
        if (root.TryGetProperty("streams", out var streams))
        {
            var position = 0;
            foreach (var stream in streams.EnumerateArray())
            {
                var index = stream.TryGetProperty("index", out var i) && i.TryGetInt32(out var n) ? n : position;
                AddTags(tags, stream, $"stream{index}/");
                position++;
            }
        }
        return tags;
    }

    public static async Task<byte[]?> GrabFramePngAsync(string path, TimeSpan at, int width, CancellationToken ct)
    {
        var seconds = at.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var (code, stdout, _) = await RunAsync("ffmpeg.exe",
            ["-v", "error", "-ss", seconds, "-i", path, "-frames:v", "1",
             "-vf", $"scale='min(iw,{width.ToString(CultureInfo.InvariantCulture)})':-2", // never upscale
             "-f", "image2pipe", "-c:v", "png", "-"], ct).ConfigureAwait(false);
        return code != 0 || stdout.Length == 0 ? null : stdout;
    }

    private static void AddTags(List<KeyValuePair<string, string>> tags, JsonElement owner, string prefix)
    {
        if (!owner.TryGetProperty("tags", out var t) || t.ValueKind != JsonValueKind.Object) return;
        foreach (var p in t.EnumerateObject())
            tags.Add(new(prefix + p.Name, p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.ToString()));
    }

    private static async Task<(int Code, string Text, string Error)> RunTextAsync(string exe, string[] args, CancellationToken ct)
    {
        var (code, bytes, err) = await RunAsync(exe, args, ct).ConfigureAwait(false);
        return (code, System.Text.Encoding.UTF8.GetString(bytes), err);
    }

    private static async Task<(int Code, byte[] Stdout, string Error)> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(Path.Combine(ToolsDirectory, exe))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi };
        process.Start();
        var killed = false;
        // Every await below uses ConfigureAwait(false): a busy caller thread (e.g. the UI) must not count against ct.
        using var registration = ct.Register(() =>
        {
            // ffmpeg/ffprobe spawn no children, so Kill() suffices. Already exited or exiting: nothing to kill,
            // and Cancel() must never throw.
            try
            {
                if (process.HasExited) return;
                process.Kill();
                killed = true;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });

        var outTask = ReadAllAsync(process.StandardOutput.BaseStream);
        var errTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        var stdout = await outTask.ConfigureAwait(false);
        var stderr = (await errTask.ConfigureAwait(false)).Trim();
        registration.Dispose(); // waits for a running callback, so `killed` is final
        // A result that arrived before cancellation is kept; only a killed run is a cancellation.
        if (killed) throw new OperationCanceledException(ct);
        return (process.ExitCode, stdout, stderr);
    }

    private static async Task<byte[]> ReadAllAsync(Stream s)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms).ConfigureAwait(false);
        return ms.ToArray();
    }
}
