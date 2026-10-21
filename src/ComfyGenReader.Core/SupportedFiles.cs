using System.Collections.Frozen;

namespace ComfyGenReader.Core;

public enum MediaKind { Unsupported, Image, Video }

public static class SupportedFiles
{
    private static readonly string[] ImageExtensionList = [".jpg", ".jpeg", ".jpe", ".jfif", ".webp", ".png"];
    private static readonly string[] VideoExtensionList = [".mp4", ".webm", ".avi"];

    private static readonly FrozenSet<string> ImageExtensions =
        ImageExtensionList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenSet<string> VideoExtensions =
        VideoExtensionList.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static string DialogFilter { get; } =
        "Supported media|" + string.Join(';', ImageExtensionList.Concat(VideoExtensionList).Select(e => "*" + e));

    public static MediaKind GetKind(string path)
    {
        var ext = Path.GetExtension(path);
        if (ImageExtensions.Contains(ext)) return MediaKind.Image;
        if (VideoExtensions.Contains(ext)) return MediaKind.Video;
        return MediaKind.Unsupported;
    }

    public static bool IsSupported(string path) => GetKind(path) != MediaKind.Unsupported;
}
