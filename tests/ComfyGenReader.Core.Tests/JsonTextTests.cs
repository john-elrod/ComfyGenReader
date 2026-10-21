using System.Text.Json.Nodes;
using ComfyGenReader.Core.Json;

namespace ComfyGenReader.Core.Tests;

public class JsonTextTests
{
    [Fact]
    public void TryParseLenient_ReplacesBareNaNAndInfinity()
    {
        Assert.True(JsonText.TryParseLenient("""{"a":NaN,"b":-Infinity,"c":"NaN","d":[Infinity]}""", out var n));
        Assert.Null(n!["a"]); Assert.Null(n["b"]); Assert.Equal("NaN", (string)n["c"]!); Assert.Null(n["d"]![0]);
    }

    [Fact]
    public void TryParseLenient_RejectsScalarsAndGarbage()
    {
        Assert.False(JsonText.TryParseLenient("42", out _));
        Assert.False(JsonText.TryParseLenient("prompt: hi", out _));
    }

    [Fact]
    public void TryParseLenient_LeavesNaNInsideIdentifiersAlone()
    {
        Assert.True(JsonText.TryParseLenient("""{"a":"x\"NaN","b":[NaN]}""", out var n));
        Assert.Equal("x\"NaN", (string)n!["a"]!);
        Assert.False(JsonText.TryParseLenient("""{"a":BANaN}""", out _));
    }

    [Fact]
    public void ExpandEmbedded_ParsesDoubleEncodedJson()
    {
        var n = JsonNode.Parse("""{"prompt":"{\"3\":{\"class_type\":\"KSampler\"}}","note":"plain"}""");
        var x = JsonText.ExpandEmbedded(n)!;
        Assert.Equal("KSampler", (string)x["prompt"]!["3"]!["class_type"]!);
        Assert.Equal("plain", (string)x["note"]!);
    }

    [Fact]
    public void TryParseLenient_DuplicateKeys_ReturnsFalse()
    {
        Assert.False(JsonText.TryParseLenient("""{"a":1,"a":2}""", out var n));
        Assert.Null(n);
    }

    [Fact]
    public void ExpandEmbedded_LeavesDuplicateKeyJsonStringAsString()
    {
        var n = JsonNode.Parse("""{"p":"{\"a\":1,\"a\":2}"}""");
        var x = JsonText.ExpandEmbedded(n)!;
        Assert.Equal("""{"a":1,"a":2}""", (string)x["p"]!);
    }

    [Fact]
    public void ToExportJson_IsIndentedAndKeepsUnicode()
    {
        var text = JsonText.ToExportJson(JsonNode.Parse("""{"a":"é<"}""")!);
        Assert.Contains("\n", text);
        Assert.Contains("é<", text);
        Assert.NotNull(JsonNode.Parse(text));
    }
}
