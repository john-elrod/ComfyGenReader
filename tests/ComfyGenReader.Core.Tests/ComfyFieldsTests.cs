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
    [Fact] public void WorkflowSeed_KSamplerAdvanced_UsesNoiseSeedWidget()
    {
        var w = JsonNode.Parse("""{"nodes":[{"id":3,"type":"KSamplerAdvanced","widgets_values":["enable",123,"randomize",20,8,"euler","normal",0,10000,"disable"]}]}""");
        Assert.Equal("123", ComfyFields.Extract(null, w).Seed);
    }
    [Fact] public void WorkflowSeed_KSamplerSelectPlusRandomNoise_UsesRandomNoise()
    {
        var w = JsonNode.Parse("""{"nodes":[{"id":1,"type":"KSamplerSelect","widgets_values":["euler"]},{"id":2,"type":"RandomNoise","widgets_values":[456,"randomize"]},{"id":3,"type":"SamplerCustomAdvanced","widgets_values":[]}]}""");
        Assert.Equal("456", ComfyFields.Extract(null, w).Seed);
    }
    [Fact] public void WorkflowSeed_OtherSampler_UsesFirstIntegralWidget()
    {
        var w = JsonNode.Parse("""{"nodes":[{"id":1,"type":"KSampler (Efficient)","widgets_values":["euler",1.5,"789"]}]}""");
        Assert.Equal("789", ComfyFields.Extract(null, w).Seed);
    }
    [Fact] public void WorkflowPrompt_FollowsSamplerPositiveLink()
    {
        var w = JsonNode.Parse("""
            {"nodes":[
              {"id":7,"type":"CLIPTextEncode","widgets_values":["blurry"]},
              {"id":6,"type":"CLIPTextEncode","widgets_values":["a cat"]},
              {"id":3,"type":"KSampler","inputs":[{"name":"model","link":1},{"name":"positive","link":2},{"name":"negative","link":3}],
               "widgets_values":[42,"randomize",20,8,"euler","normal",1]}],
             "links":[[1,4,0,3,0,"MODEL"],[3,7,0,3,2,"CONDITIONING"],[2,6,0,3,1,"CONDITIONING"]]}
            """);
        var h = ComfyFields.Extract(null, w);
        Assert.Equal("a cat", h.Prompt); Assert.Equal("42", h.Seed);
    }
    [Fact] public void Extract_FallsBackToFirstClipTextEncode_AndSkipsMalformed()
    {
        var p = JsonNode.Parse("""{"x":5,"2":{"inputs":3},"10":{"class_type":"CLIPTextEncode","inputs":{"text":"ten"}},"9":{"class_type":"CLIPTextEncode","inputs":{"text":"nine"}}}""");
        Assert.Equal("nine", ComfyFields.Extract(p, null).Prompt);
    }
    [Fact] public void Prompt_MiniMaxH3Director_UsesFirstDirectorPrompt()
    {
        // The Dasiwa MiniMax H3 workflow: no CLIPTextEncode; the prompt is the director's "prompt" input
        // (repeated inside its timeline_data, which is ignored).
        var p = JsonNode.Parse("""
            {"1":{"class_type":"MiniMaxH3DirectorGuide","inputs":{"guide":["2730",0]}},
             "2730":{"class_type":"MiniMaxH3Director","inputs":{"mode":"REF2VA","prompt":"first","timeline_data":"{\"simple_prompt\":\"first\"}"}},
             "2731":{"class_type":"MiniMaxH3Director","inputs":{"prompt":"second"}}}
            """);
        Assert.Equal("first", ComfyFields.Extract(p, null).Prompt);
    }
    [Fact] public void Prompt_MiniMaxH3Director_WinsOverClipTextEncode()
    {
        var p = JsonNode.Parse("""{"5":{"class_type":"CLIPTextEncode","inputs":{"text":"other"}},"9":{"class_type":"MiniMaxH3Director","inputs":{"prompt":"director"}}}""");
        Assert.Equal("director", ComfyFields.Extract(p, null).Prompt);
    }
    [Fact] public void Seed_DasiwaSeedControl_UsesSeedValue()
    {
        var p = JsonNode.Parse("""{"2739":{"class_type":"DaSiWa_SeedControl","inputs":{"seed_value":2,"seed_control_state":"{\"last_seed\":\"9\",\"recent\":[\"9\"]}"}}}""");
        Assert.Equal("2", ComfyFields.Extract(p, null).Seed);
    }
    [Fact] public void WorkflowSeed_DasiwaSeedControl_UsesFirstWidget()
    {
        var w = JsonNode.Parse("""{"nodes":[{"id":2739,"type":"DaSiWa_SeedControl","widgets_values":[2,"{\"last_seed\":\"9\"}",""]}]}""");
        Assert.Equal("2", ComfyFields.Extract(null, w).Seed);
    }
    [Fact] public void WorkflowPrompt_MiniMaxH3Director_UsesPromptWidget()
    {
        var w = JsonNode.Parse("""{"nodes":[{"id":5,"type":"CLIPTextEncode","widgets_values":["other"]},{"id":2730,"type":"MiniMaxH3Director","widgets_values":["REF2VA","director",832,832]}]}""");
        Assert.Equal("director", ComfyFields.Extract(null, w).Prompt);
    }
}
