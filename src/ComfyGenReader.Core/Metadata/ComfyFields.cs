using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComfyGenReader.Core.Metadata;

public sealed record ComfyHighlights(string Seed, string Prompt);

public static class ComfyFields
{
    const int MaxHops = 5;
    static readonly string[] ScalarInputs = ["seed", "noise_seed", "value", "int", "number", "text", "string"];

    public static ComfyHighlights Extract(JsonNode? apiPrompt, JsonNode? workflow)
    {
        var nodes = OrderedApiNodes(apiPrompt);
        var seed = ApiSeed(nodes);
        var prompt = ApiPrompt(nodes);
        if (seed.Length == 0) seed = WorkflowWidget(workflow, "KSampler", partial: true);
        if (prompt.Length == 0) prompt = WorkflowWidget(workflow, "CLIPTextEncode", partial: false);
        return new ComfyHighlights(seed, prompt);
    }

    static List<(string Id, JsonObject Node)> OrderedApiNodes(JsonNode? apiPrompt)
    {
        var list = new List<(string Id, JsonObject Node)>();
        if (apiPrompt is not JsonObject root) return list;
        foreach (var (id, value) in root)
            if (value is JsonObject node) list.Add((id, node));
        return list
            .OrderBy(n => long.TryParse(n.Id, out _) ? 0 : 1)
            .ThenBy(n => long.TryParse(n.Id, out var v) ? v : 0)
            .ThenBy(n => n.Id, StringComparer.Ordinal)
            .ToList();
    }

    static JsonObject? Inputs(JsonObject node) => node["inputs"] as JsonObject;

    static string ApiSeed(List<(string Id, JsonObject Node)> nodes)
    {
        var seeds = new List<string>();
        foreach (var (_, node) in nodes)
        {
            if (Inputs(node) is not { } inputs) continue;
            var value = inputs["seed"] ?? inputs["noise_seed"];
            if (value is null) continue;
            var text = Resolve(value, nodes, 0);
            if (text is not null && !seeds.Contains(text)) seeds.Add(text);
        }
        return string.Join(", ", seeds);
    }

    static string ApiPrompt(List<(string Id, JsonObject Node)> nodes)
    {
        foreach (var (_, node) in nodes)
        {
            if (Inputs(node) is not { } inputs || !TryLink(inputs["positive"], out var targetId)) continue;
            var target = Find(nodes, targetId);
            if (target is not null && NodeText(target, nodes, 1) is { } text) return text;
        }
        foreach (var (_, node) in nodes)
        {
            if (node["class_type"] is JsonValue ct && ct.TryGetValue<string>(out var cls) && cls == "CLIPTextEncode"
                && NodeText(node, nodes, 0) is { } text) return text;
        }
        return "";
    }

    static string? NodeText(JsonObject node, List<(string Id, JsonObject Node)> nodes, int hops)
    {
        if (Inputs(node) is not { } inputs) return null;
        var value = inputs["text"] ?? inputs["text_g"];
        return value is null ? null : Resolve(value, nodes, hops);
    }

    // Returns the scalar text of a value, following links up to MaxHops; null if unresolved.
    static string? Resolve(JsonNode value, List<(string Id, JsonObject Node)> nodes, int hops)
    {
        if (TryLink(value, out var id))
        {
            if (hops >= MaxHops) return null;
            var target = Find(nodes, id);
            if (target is null || Inputs(target) is not { } inputs) return null;
            foreach (var key in ScalarInputs)
                if (inputs[key] is { } next) return Resolve(next, nodes, hops + 1);
            return null;
        }
        return Scalar(value);
    }

    static string? Scalar(JsonNode? value)
    {
        if (value is not JsonValue v) return null;
        return v.GetValueKind() switch
        {
            JsonValueKind.String => v.GetValue<string>(),
            JsonValueKind.Number => v.ToJsonString(),
            _ => null,
        };
    }

    static bool TryLink(JsonNode? value, out string id)
    {
        id = "";
        if (value is JsonArray { Count: 2 } arr && arr[0] is JsonValue idValue && arr[1] is JsonValue idx
            && idx.GetValueKind() == JsonValueKind.Number && Scalar(idValue) is { } s)
        {
            id = s;
            return true;
        }
        return false;
    }

    static JsonObject? Find(List<(string Id, JsonObject Node)> nodes, string id) =>
        nodes.FirstOrDefault(n => n.Id == id).Node;

    static string WorkflowWidget(JsonNode? workflow, string typeName, bool partial)
    {
        if (workflow?["nodes"] is not JsonArray arr) return "";
        foreach (var n in arr)
        {
            if (n is not JsonObject node || node["type"] is not JsonValue t || !t.TryGetValue<string>(out var type)) continue;
            if (partial ? !type.Contains(typeName, StringComparison.Ordinal) : type != typeName) continue;
            if (node["widgets_values"] is JsonArray { Count: > 0 } w && Scalar(w[0]) is { } text) return text;
        }
        return "";
    }
}
