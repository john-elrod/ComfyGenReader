using System.Text.Json.Nodes;
using ComfyGenReader.Core.Json;

namespace ComfyGenReader.Core.Metadata;

/// <param name="WorkflowText">The workflow's JSON text exactly as stored in the file; null when there is no workflow.</param>
public sealed record MetadataResult(JsonObject Raw, JsonNode? Prompt, JsonNode? Workflow, string? WorkflowText = null)
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
