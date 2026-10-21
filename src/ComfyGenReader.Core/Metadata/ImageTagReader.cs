using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.Xmp;

namespace ComfyGenReader.Core.Metadata;

/// <summary>Reads raw text metadata tags from JPEG, WebP and PNG files.</summary>
public static class ImageTagReader
{
    public static IReadOnlyList<KeyValuePair<string, string>> Read(string path)
    {
        var result = new List<KeyValuePair<string, string>>();
        foreach (var directory in ImageMetadataReader.ReadMetadata(path))
        {
            if (directory is XmpDirectory xmp)
            {
                foreach (var (name, value) in xmp.GetXmpProperties())
                    if (!string.IsNullOrEmpty(value))
                        result.Add(new($"{directory.Name}/{name}", value));
                continue;
            }

            foreach (var tag in directory.Tags)
            {
                if (directory is PngDirectory && tag.Type == PngDirectory.TagTextualData)
                {
                    if (directory.GetObject(tag.Type) is IEnumerable<MetadataExtractor.KeyValuePair> pairs)
                        foreach (var pair in pairs)
                            result.Add(new(pair.Key, pair.Value.ToString()));
                    continue;
                }

                var text = ReadText(directory, tag);
                if (!string.IsNullOrEmpty(text))
                    result.Add(new($"{directory.Name}/{tag.Name}", text));
            }
        }
        return result;
    }

    private static string? ReadText(MetadataExtractor.Directory directory, Tag tag)
    {
        var value = directory.GetObject(tag.Type);
        if (value is string s) return s;
        if (value is StringValue sv) return sv.ToString();
        if (value is byte[] bytes && directory is ExifSubIfdDirectory && tag.Type == ExifDirectoryBase.TagUserComment)
            return DecodeUserComment(bytes);
        return null;
    }

    private static string? DecodeUserComment(byte[] bytes)
    {
        if (bytes.Length < 8) return null;
        var header = Encoding.ASCII.GetString(bytes, 0, 8).TrimEnd('\0', ' ');
        var body = bytes.AsSpan(8);
        string text;
        switch (header)
        {
            case "ASCII":
            case "":
                text = Encoding.UTF8.GetString(body);
                break;
            case "UNICODE":
                var littleEndian = body.Length >= 2 && body[1] == 0 && body[0] != 0;
                text = (littleEndian ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(body);
                break;
            default:
                return null;
        }
        return text.TrimEnd('\0', ' ');
    }
}
