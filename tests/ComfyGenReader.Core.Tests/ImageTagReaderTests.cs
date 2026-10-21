using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

public class ImageTagReaderTests
{
    [Theory, InlineData(".jpg"), InlineData(".webp")]
    public void Read_ReturnsFullExifStrings(string ext)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ext);
        try
        {
            var tags = new Dictionary<ushort, string>
            {
                [0x0110] = "prompt:" + ComfySamples.ApiPrompt,
                [0x010F] = "workflow:" + ComfySamples.Workflow
            };
            if (ext == ".jpg") ExifFixtures.WriteJpeg(path, tags); else ExifFixtures.WriteWebP(path, tags);
            var read = ImageTagReader.Read(path);
            Assert.Contains(read, kv => kv.Key == "Exif IFD0/Model" && kv.Value == "prompt:" + ComfySamples.ApiPrompt);
            var r = MetadataParser.Parse(read);
            Assert.NotNull(r.Prompt); Assert.NotNull(r.Workflow);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Read_PngTextChunks_KeyedByKeyword()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        try
        {
            ExifFixtures.WritePng(path, new Dictionary<string, string>
            {
                ["prompt"] = ComfySamples.ApiPrompt,
                ["workflow"] = ComfySamples.Workflow
            });
            var read = ImageTagReader.Read(path);
            Assert.Contains(read, kv => kv.Key == "prompt" && kv.Value == ComfySamples.ApiPrompt);
            var r = MetadataParser.Parse(read);
            Assert.NotNull(r.Prompt); Assert.NotNull(r.Workflow);
        }
        finally { File.Delete(path); }
    }
}
