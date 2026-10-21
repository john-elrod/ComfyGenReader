using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ComfyGenReader.Core.Json;

namespace ComfyGenReader.Core.Metadata;

public static class MetadataParser
{
    private static readonly Regex PrefixPattern = new(@"^([A-Za-z0-9_]+):", RegexOptions.Compiled);

    public static MetadataResult Parse(IEnumerable<KeyValuePair<string, string>> tags)
    {
        var raw = new JsonObject();
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        var strippedNames = new List<(string Name, JsonNode Node)>();

        foreach (var (key, value) in tags)
        {
            var uniqueKey = key;
            if (names.TryGetValue(key, out var count))
            {
                count++;
                names[key] = count;
                uniqueKey = $"{key} ({count})";
            }
            else names[key] = 1;

            string? prefix = null;
            JsonNode? parsed = null;
            if (JsonText.TryParseLenient(value, out var node))
                parsed = node;
            else
            {
                var m = PrefixPattern.Match(value);
                if (m.Success && JsonText.TryParseLenient(value[m.Length..], out node))
                {
                    parsed = node;
                    prefix = m.Groups[1].Value;
                }
            }

            if (parsed is null)
            {
                raw[uniqueKey] = value;
                continue;
            }
            var expanded = JsonText.ExpandEmbedded(parsed)!;
            raw[uniqueKey] = expanded;
            strippedNames.Add((prefix ?? key, expanded));
        }

        JsonNode? Find(string name)
        {
            foreach (var (n, node) in strippedNames)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && node is JsonObject)
                    return node.DeepClone();
            foreach (var (_, node) in strippedNames)
                if (node is JsonObject obj)
                    foreach (var p in obj)
                        if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase) && p.Value is JsonObject)
                            return p.Value.DeepClone();
            return null;
        }

        return new MetadataResult(raw, Find("prompt"), Find("workflow"));
    }
}
