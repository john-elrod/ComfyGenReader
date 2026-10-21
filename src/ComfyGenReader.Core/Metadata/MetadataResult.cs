using System.Text.Json.Nodes;
using ComfyGenReader.Core.Json;

namespace ComfyGenReader.Core.Metadata;

public sealed record MetadataResult(JsonObject Raw, JsonNode? Prompt, JsonNode? Workflow)
{
    public string? ExportJson
    {
        get
        {
            var node = Workflow ?? Prompt;
            return node is null ? null : JsonText.ToExportJson(node);
        }
    }
}
