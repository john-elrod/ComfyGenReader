using System.Text.Json.Nodes;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

public class MetadataParserTests
{
    static KeyValuePair<string,string> T(string k, string v) => new(k, v);

    [Fact] public void Parse_ExifPrefixedStrings()
    {
        var r = MetadataParser.Parse([T("Exif IFD0/Model", "prompt:" + ComfySamples.ApiPrompt),
                                      T("Exif IFD0/Make", "workflow:" + ComfySamples.Workflow)]);
        Assert.Equal("KSampler", (string)r.Prompt!["3"]!["class_type"]!);
        Assert.NotNull(r.Workflow!["nodes"]);
        Assert.Equal(r.Workflow.ToJsonString(), JsonNode.Parse(r.ExportJson!)!.ToJsonString());
        Assert.NotNull(r.Raw["Exif IFD0/Model"]);
    }
    [Fact] public void Parse_VideoCommentWithDoubleEncodedValues()
    {
        var comment = new JsonObject { ["prompt"] = ComfySamples.ApiPrompt, ["workflow"] = ComfySamples.Workflow }.ToJsonString();
        var r = MetadataParser.Parse([T("COMMENT", comment), T("encoder", "Lavf61.7.100")]);
        Assert.IsType<JsonObject>(r.Prompt); Assert.IsType<JsonObject>(r.Workflow);
        Assert.NotNull(r.Prompt!["3"]); Assert.NotNull(r.Workflow!["nodes"]);
        Assert.Equal("Lavf61.7.100", (string)r.Raw["encoder"]!);
    }
    [Fact] public void Parse_PromptOnly_ExportsPrompt()
    {
        var r = MetadataParser.Parse([T("prompt", ComfySamples.ApiPrompt)]);
        Assert.Null(r.Workflow);
        Assert.NotNull(JsonNode.Parse(r.ExportJson!)!["3"]);
    }
    [Fact] public void Parse_NoComfyData_ExportIsNull()
    {
        var r = MetadataParser.Parse([T("Exif IFD0/Make", "Canon"), T("Exif IFD0/Model", "EOS R5")]);
        Assert.Null(r.Prompt); Assert.Null(r.Workflow); Assert.Null(r.ExportJson);
        Assert.Equal("Canon", (string)r.Raw["Exif IFD0/Make"]!);
    }
    [Fact] public void Parse_DuplicateKeys_GetSuffixes()
    {
        var r = MetadataParser.Parse([T("a", "1"), T("a", "2"), T("a", "3")]);
        Assert.Equal("1", (string)r.Raw["a"]!);
        Assert.Equal("2", (string)r.Raw["a (2)"]!);
        Assert.Equal("3", (string)r.Raw["a (3)"]!);
    }
    [Fact] public void Parse_UppercasePromptKey_AndNonObjectIgnored()
    {
        var r = MetadataParser.Parse([T("PROMPT", ComfySamples.ApiPrompt), T("workflow", "[1,2]")]);
        Assert.NotNull(r.Prompt); Assert.Null(r.Workflow);
    }

    // A widget whose value is a JSON-looking string (KJNodes SplineEditor/PointsEditor).
    const string PointsWidget = """[{"x":1,"y":2}]""";
    static readonly string WorkflowWithJsonStringWidget = new JsonObject
    {
        ["nodes"] = new JsonArray(new JsonObject
        {
            ["id"] = 1, ["type"] = "SplineEditor", ["widgets_values"] = new JsonArray(PointsWidget, 5),
        }),
        ["links"] = new JsonArray(),
    }.ToJsonString();

    static void AssertWidgetStaysString(JsonNode? workflow)
    {
        var widget = workflow!["nodes"]![0]!["widgets_values"]![0];
        Assert.Equal(System.Text.Json.JsonValueKind.String, widget!.GetValueKind());
        Assert.Equal(PointsWidget, (string)widget!);
    }

    [Fact] public void Export_KeepsJsonStringWidgetAsString()
    {
        var r = MetadataParser.Parse([T("workflow", WorkflowWithJsonStringWidget)]);
        AssertWidgetStaysString(r.Workflow);
        AssertWidgetStaysString(JsonNode.Parse(r.ExportJson!));
    }
    [Fact] public void Export_DoubleEncodedComment_KeepsJsonStringWidgetAsString()
    {
        var comment = new JsonObject { ["prompt"] = ComfySamples.ApiPrompt, ["workflow"] = WorkflowWithJsonStringWidget }.ToJsonString();
        var r = MetadataParser.Parse([T("comment", comment)]);
        Assert.IsType<JsonObject>(r.Prompt); Assert.IsType<JsonObject>(r.Workflow);
        AssertWidgetStaysString(JsonNode.Parse(r.ExportJson!));
    }
    [Fact] public void Raw_StillExpandsNestedJsonStrings()
    {
        var comment = new JsonObject { ["workflow"] = WorkflowWithJsonStringWidget }.ToJsonString();
        var r = MetadataParser.Parse([T("comment", comment)]);
        var widget = r.Raw["comment"]!["workflow"]!["nodes"]![0]!["widgets_values"]![0];
        Assert.Equal(1, (int)widget![0]!["x"]!);
    }
}
