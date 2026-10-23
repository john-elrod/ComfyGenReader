using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComfyGenReader.Core.Json;

public static class JsonText
{
    private const int MaxEmbeddedDepth = 8;

    private static readonly JsonDocumentOptions StrictDocumentOptions = new() { AllowDuplicateProperties = false };

    private static readonly JsonSerializerOptions ExportOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly JsonSerializerOptions CompactOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Parses text as JSON, accepting only a top-level object or array. Standalone
    /// NaN, Infinity and -Infinity tokens outside string literals are read as null.
    /// </summary>
    public static bool TryParseLenient(string text, out JsonNode? node)
    {
        node = null;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var trimmed = text.AsSpan().Trim();
        if (trimmed[0] != '{' && trimmed[0] != '[') return false;

        try
        {
            var parsed = JsonNode.Parse(ReplaceNonFiniteTokens(text), documentOptions: StrictDocumentOptions);
            if (parsed is JsonObject or JsonArray)
            {
                node = parsed;
                return true;
            }
        }
        catch (JsonException) { }
        return false;
    }

    public static JsonNode? ExpandEmbedded(JsonNode? node) => Expand(node, 0);

    public static string ToExportJson(JsonNode node) => node.ToJsonString(ExportOptions);

    public static string ToCompactJson(JsonNode node) => node.ToJsonString(CompactOptions);

    private static JsonNode? Expand(JsonNode? node, int depth)
    {
        switch (node)
        {
            case JsonObject obj:
                var newObj = new JsonObject();
                foreach (var (key, value) in obj) newObj[key] = Expand(value, depth);
                return newObj;
            case JsonArray arr:
                var newArr = new JsonArray();
                foreach (var item in arr) newArr.Add(Expand(item, depth));
                return newArr;
            case JsonValue val when val.TryGetValue<string>(out var s):
                if (depth < MaxEmbeddedDepth && TryParseLenient(s, out var inner))
                    return Expand(inner, depth + 1);
                return JsonValue.Create(s);
            case null:
                return null;
            default:
                return node.DeepClone();
        }
    }

    private static string ReplaceNonFiniteTokens(string text)
    {
        var sb = new StringBuilder(text.Length);
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < text.Length) sb.Append(text[++i]);
                else if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; sb.Append(c); continue; }

            if ((c == 'N' || c == 'I' || c == '-') && AtBoundary(text, i))
            {
                var len = MatchToken(text, i);
                if (len > 0)
                {
                    sb.Append("null");
                    i += len - 1;
                    continue;
                }
            }
            sb.Append(c);
        }
        return sb.ToString();
    }

    private static int MatchToken(string text, int i)
    {
        foreach (var token in new[] { "NaN", "Infinity", "-Infinity" })
        {
            if (string.CompareOrdinal(text, i, token, 0, token.Length) == 0
                && (i + token.Length >= text.Length || !IsIdentChar(text[i + token.Length])))
                return token.Length;
        }
        return 0;
    }

    private static bool AtBoundary(string text, int i) => i == 0 || !IsIdentChar(text[i - 1]);

    private static bool IsIdentChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == '$';
}
