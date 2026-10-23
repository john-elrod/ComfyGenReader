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
        // Each parsed tag with the exact JSON text it was parsed from (after any "name:" prefix).
        var strippedNames = new List<(string Name, JsonNode Node, string Text)>();

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
            var text = value;
            if (JsonText.TryParseLenient(value, out var node))
                parsed = node;
            else
            {
                var m = PrefixPattern.Match(value);
                if (m.Success && JsonText.TryParseLenient(value[m.Length..], out node))
                {
                    parsed = node;
                    prefix = m.Groups[1].Value;
                    text = value[m.Length..];
                }
            }

            if (parsed is null)
            {
                raw[uniqueKey] = value;
                continue;
            }
            // Deep expansion is for display only; Prompt/Workflow keep JSON-looking widget strings as strings.
            raw[uniqueKey] = JsonText.ExpandEmbedded(parsed)!;
            strippedNames.Add((prefix ?? key, parsed, text));
        }

        (JsonNode Node, string Text)? Find(string name)
        {
            foreach (var (n, node, text) in strippedNames)
                if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase) && node is JsonObject)
                    return (node.DeepClone(), text);
            foreach (var (_, node, _) in strippedNames)
                if (node is JsonObject obj)
                    foreach (var p in obj)
                        if (string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase) && UnwrapOnce(p.Value) is (JsonObject found, var text))
                            return (found.DeepClone(), text);
            return null;
        }

        var workflow = Find("workflow");
        return new MetadataResult(raw, Find("prompt")?.Node, workflow?.Node, workflow?.Text);
    }

    /// <summary>
    /// A property value, or what it encodes when it is a JSON string (VHS double-encodes prompt/workflow), with its JSON
    /// text: the string itself, or for a nested value (whose original text is not kept) its compact serialization.
    /// </summary>
    private static (JsonNode? Node, string Text) UnwrapOnce(JsonNode? value) =>
        value is JsonValue v && v.TryGetValue<string>(out var s) && JsonText.TryParseLenient(s, out var inner)
            ? (inner, s)
            : (value, value is null ? "null" : JsonText.ToCompactJson(value));
}
