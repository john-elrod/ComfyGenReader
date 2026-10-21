using System.Text;
using System.Text.Json.Nodes;

namespace ComfyGenReader.Core.Json;

public static class DisplayFormatter
{
    public static string ToDisplayText(JsonNode? node)
    {
        if (node is null) return "";
        var sb = new StringBuilder();
        Write(sb, node, 0);
        return sb.ToString();
    }

    private static void Write(StringBuilder sb, JsonNode? node, int indent)
    {
        switch (node)
        {
            case null:
                sb.Append("null");
                break;
            case JsonObject obj:
                if (obj.Count == 0) { sb.Append("{}"); break; }
                sb.Append("{\n");
                var first = true;
                foreach (var (key, value) in obj)
                {
                    if (!first) sb.Append(",\n");
                    first = false;
                    sb.Append(' ', (indent + 1) * 2).Append('"').Append(key).Append("\": ");
                    Write(sb, value, indent + 1);
                }
                sb.Append('\n').Append(' ', indent * 2).Append('}');
                break;
            case JsonArray arr:
                if (arr.Count == 0) { sb.Append("[]"); break; }
                sb.Append("[\n");
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(",\n");
                    sb.Append(' ', (indent + 1) * 2);
                    Write(sb, arr[i], indent + 1);
                }
                sb.Append('\n').Append(' ', indent * 2).Append(']');
                break;
            case JsonValue val when val.TryGetValue<string>(out var s):
                sb.Append('"').Append(s).Append('"');
                break;
            default:
                sb.Append(node.ToJsonString());
                break;
        }
    }
}
