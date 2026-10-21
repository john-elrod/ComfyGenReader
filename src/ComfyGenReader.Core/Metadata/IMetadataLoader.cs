using ComfyGenReader.Core.Media;

namespace ComfyGenReader.Core.Metadata;

public interface IMetadataLoader
{
    Task<MetadataResult> LoadAsync(string path, CancellationToken ct);
}

public sealed class MetadataLoader : IMetadataLoader
{
    public async Task<MetadataResult> LoadAsync(string path, CancellationToken ct)
    {
        var tags = SupportedFiles.GetKind(path) == MediaKind.Video
            ? await FfmpegTools.ReadTagsAsync(path, ct).ConfigureAwait(false)
            : await Task.Run(() => ImageTagReader.Read(path), ct).ConfigureAwait(false);
        return MetadataParser.Parse(tags);
    }
}
