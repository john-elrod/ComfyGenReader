using System.Text.Json.Nodes;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

public class ComfyFieldsTests
{
    [Fact] public void Extract_FromApiPrompt()
    {
        var h = ComfyFields.Extract(JsonNode.Parse(ComfySamples.ApiPrompt), null);
        Assert.Equal("156680208700286", h.Seed); Assert.Equal("a cat\nwearing a hat", h.Prompt);
    }
    [Fact] public void Extract_FallsBackToWorkflow()
    {
        var h = ComfyFields.Extract(null, JsonNode.Parse(ComfySamples.Workflow));
        Assert.Equal("156680208700286", h.Seed); Assert.Equal("a cat\nwearing a hat", h.Prompt);
    }
    [Fact] public void Seed_FollowsLinkToPrimitive()
    {
        var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":["12",0]}},"12":{"class_type":"PrimitiveInt","inputs":{"value":42}}}""");
        Assert.Equal("42", ComfyFields.Extract(p, null).Seed);
    }
    [Fact] public void Prompt_FollowsTextLink()
    {
        var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"positive":["6",0]}},"6":{"class_type":"CLIPTextEncode","inputs":{"text":["9",0]}},"9":{"class_type":"PrimitiveString","inputs":{"value":"linked text"}}}""");
        Assert.Equal("linked text", ComfyFields.Extract(p, null).Prompt);
    }
    [Fact] public void Extract_MultipleSamplersJoinsDistinctSeeds()
    {
        var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":1}},"4":{"class_type":"KSamplerAdvanced","inputs":{"noise_seed":2}},"5":{"class_type":"KSampler","inputs":{"seed":1}}}""");
        Assert.Equal("1, 2", ComfyFields.Extract(p, null).Seed);
    }
    [Fact] public void Extract_CyclicLinks_DoesNotHang()
    {
        var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":["4",0]}},"4":{"class_type":"X","inputs":{"value":["3",0]}}}""");
        Assert.Equal("", ComfyFields.Extract(p, null).Seed);
    }
    [Fact] public void Extract_PerFieldWorkflowFallback()
    {
        var p = JsonNode.Parse("""{"3":{"class_type":"KSampler","inputs":{"seed":7}}}""");
        var h = ComfyFields.Extract(p, JsonNode.Parse(ComfySamples.Workflow));
        Assert.Equal("7", h.Seed); Assert.Equal("a cat\nwearing a hat", h.Prompt);
    }
    [Fact] public void Extract_FallsBackToFirstClipTextEncode_AndSkipsMalformed()
    {
        var p = JsonNode.Parse("""{"x":5,"2":{"inputs":3},"10":{"class_type":"CLIPTextEncode","inputs":{"text":"ten"}},"9":{"class_type":"CLIPTextEncode","inputs":{"text":"nine"}}}""");
        Assert.Equal("nine", ComfyFields.Extract(p, null).Prompt);
    }
}
