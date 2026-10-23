using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ComfyGenReader.Core.Metadata;

public sealed record ComfyHighlights(string Seed, string Prompt);

public static class ComfyFields
{
    const int MaxHops = 5;
    static readonly string[] ScalarInputs = ["seed", "noise_seed", "value", "int", "number", "text", "string"];
    // widgets_values index of the seed for known node types; other "*Sampler*" types use their first integral widget.
    static readonly Dictionary<string, int> SeedWidgetIndex = new(StringComparer.Ordinal)
    {
        ["KSampler"] = 0, ["KSamplerAdvanced"] = 1, ["SamplerCustom"] = 1, ["RandomNoise"] = 0,
        ["DaSiWa_SeedControl"] = 0, // Dasiwa workflows; its second widget is a state blob whose last_seed/recent are history
    };
    // Node types that hold the prompt themselves (API input name, widgets_values index), checked before CLIPTextEncode.
    // MiniMaxH3Director: the Dasiwa MiniMax H3 workflow, which has no CLIPTextEncode; its timeline_data repeats the prompt.
    static readonly Dictionary<string, (string Input, int Widget)> PromptNodes = new(StringComparer.Ordinal)
    {
        ["MiniMaxH3Director"] = ("prompt", 1),
    };

    public static ComfyHighlights Extract(JsonNode? apiPrompt, JsonNode? workflow)
    {
        var nodes = OrderedApiNodes(apiPrompt);
        var seed = ApiSeed(nodes);
        var prompt = ApiPrompt(nodes);
        var workflowNodes = workflow?["nodes"] as JsonArray;
        if (seed.Length == 0) seed = WorkflowSeed(workflowNodes);
        if (prompt.Length == 0) prompt = WorkflowPrompt(workflowNodes, workflow?["links"] as JsonArray);
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
            var value = inputs["seed"] ?? inputs["noise_seed"] ?? inputs["seed_value"]; // seed_value: DaSiWa_SeedControl
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
            if (Scalar(node["class_type"]) is { } cls && PromptNodes.TryGetValue(cls, out var known)
                && Inputs(node)?[known.Input] is { } value && Resolve(value, nodes, 0) is { Length: > 0 } text) return text;
        }
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

    static IEnumerable<(JsonObject Node, string Type)> TypedNodes(JsonArray? nodes)
    {
        if (nodes is null) yield break;
        foreach (var n in nodes)
            if (n is JsonObject node && node["type"] is JsonValue t && t.TryGetValue<string>(out var type))
                yield return (node, type);
    }

    static JsonArray? Widgets(JsonObject node) => node["widgets_values"] as JsonArray;

    static string WorkflowSeed(JsonArray? nodes)
    {
        var typed = TypedNodes(nodes).ToList();
        // Known types first, so e.g. a SamplerEulerAncestral's integral eta never wins over RandomNoise.
        foreach (var (node, type) in typed)
            if (SeedWidgetIndex.TryGetValue(type, out var index) && Widgets(node) is { } w && index < w.Count
                && Integral(w[index]) is { } seed) return seed;
        foreach (var (node, type) in typed)
            if (!SeedWidgetIndex.ContainsKey(type) && type.Contains("Sampler", StringComparison.Ordinal)
                && Widgets(node)?.Select(Integral).FirstOrDefault(s => s is not null) is { } seed) return seed;
        return "";
    }

    static string WorkflowPrompt(JsonArray? nodes, JsonArray? links)
    {
        var typed = TypedNodes(nodes).ToList();
        foreach (var (node, type) in typed)
            if (PromptNodes.TryGetValue(type, out var known) && Widgets(node) is { } w && known.Widget < w.Count
                && Scalar(w[known.Widget]) is { Length: > 0 } text) return text;
        // Prefer the CLIPTextEncode wired to a sampler's "positive" input; nodes[] order may put the negative first.
        foreach (var (node, _) in typed)
        {
            if (PositiveOrigin(node, links) is not { } originId) continue;
            var origin = typed.FirstOrDefault(n => n.Type == "CLIPTextEncode" && Scalar(n.Node["id"]) == originId);
            if (origin.Node is not null && FirstWidgetText(origin.Node) is { } text) return text;
        }
        foreach (var (node, type) in typed)
            if (type == "CLIPTextEncode" && FirstWidgetText(node) is { } text) return text;
        return "";
    }

    static string? FirstWidgetText(JsonObject node) => Widgets(node) is { Count: > 0 } w ? Scalar(w[0]) : null;

    // The origin node id of the link feeding this node's "positive" input; links are [id, origin, slot, target, slot, type].
    static string? PositiveOrigin(JsonObject node, JsonArray? links)
    {
        if (links is null || node["inputs"] is not JsonArray inputs) return null;
        foreach (var input in inputs)
        {
            if (input is not JsonObject i || Scalar(i["name"]) != "positive" || Scalar(i["link"]) is not { } linkId) continue;
            foreach (var l in links)
                if (l is JsonArray { Count: >= 2 } link && Scalar(link[0]) == linkId) return Scalar(link[1]);
        }
        return null;
    }

    // An integral number, or a string that parses as one (seeds may exceed long, hence BigInteger).
    static string? Integral(JsonNode? value)
    {
        var text = Scalar(value)?.Trim();
        return text is not null && BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _)
            ? text : null;
    }
}
