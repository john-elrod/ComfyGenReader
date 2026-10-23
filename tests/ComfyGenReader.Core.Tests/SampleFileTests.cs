using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

/// <summary>Real files from ComfyUI workflows (Samples\), each with the prompt and seed it should yield.</summary>
public class SampleFileTests
{
    private static string Sample(string name) => Path.Combine(AppContext.BaseDirectory, "Samples", name);

    private static string ExpectedPrompt(string name) =>
        File.ReadAllText(Sample(name)).ReplaceLineEndings("\n").Trim();

    [Fact]
    public async Task DasiwaMiniMaxH3_Webm_ExtractsDirectorPromptAndSeedControlSeed()
    {
        var r = await new MetadataLoader().LoadAsync(Sample("example_dasiwa_mmh3.webm"), TestContext.Current.CancellationToken);
        var expected = ExpectedPrompt("example_dasiwa_mmh3.prompt.txt");

        var fromApi = ComfyFields.Extract(r.Prompt, r.Workflow);
        var fromWorkflow = ComfyFields.Extract(null, r.Workflow);
        Assert.Equal(expected, fromApi.Prompt.Trim());
        Assert.Equal(expected, fromWorkflow.Prompt.Trim());
        Assert.Equal("2", fromApi.Seed);
        Assert.Equal("2", fromWorkflow.Seed);
    }
}
