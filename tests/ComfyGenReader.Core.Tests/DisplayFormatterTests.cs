using System.Text.Json.Nodes;
using ComfyGenReader.Core.Json;

namespace ComfyGenReader.Core.Tests;

public class DisplayFormatterTests
{
    [Fact]
    public void ToDisplayText_UnescapesStrings()
    {
        var n = JsonNode.Parse("""{"t":"line1\nsay \"hi\" é 😀"}""");
        Assert.Equal("{\n  \"t\": \"line1\nsay \"hi\" é 😀\"\n}", DisplayFormatter.ToDisplayText(n).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ToDisplayText_FormatsNestedArraysAndScalars()
    {
        var n = JsonNode.Parse("""{"a":[1,true,null],"b":{}}""");
        Assert.Equal("{\n  \"a\": [\n    1,\n    true,\n    null\n  ],\n  \"b\": {}\n}",
                     DisplayFormatter.ToDisplayText(n).ReplaceLineEndings("\n"));
    }

    [Fact]
    public void ToDisplayText_NullIsEmpty() => Assert.Equal("", DisplayFormatter.ToDisplayText(null));
}
