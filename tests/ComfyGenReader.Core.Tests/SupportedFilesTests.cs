using ComfyGenReader.Core;

namespace ComfyGenReader.Core.Tests;

public class SupportedFilesTests
{
    [Theory]
    [InlineData(@"C:\a\x.JPG", MediaKind.Image)]
    [InlineData(@"C:\a\x.jpeg", MediaKind.Image)]
    [InlineData(@"C:\a\x.jpe", MediaKind.Image)]
    [InlineData(@"C:\a\x.jfif", MediaKind.Image)]
    [InlineData(@"C:\a\x.webp", MediaKind.Image)]
    [InlineData(@"C:\a\x.mp4", MediaKind.Video)]
    [InlineData(@"C:\a\x.WebM", MediaKind.Video)]
    [InlineData(@"C:\a\x.avi", MediaKind.Video)]
    [InlineData(@"C:\a\x.PNG", MediaKind.Image)]
    [InlineData(@"C:\a\x.gif", MediaKind.Unsupported)]
    [InlineData(@"C:\a\noext", MediaKind.Unsupported)]
    public void GetKind_ByExtension(string path, MediaKind expected) =>
        Assert.Equal(expected, SupportedFiles.GetKind(path));

    [Fact]
    public void DialogFilter_ListsAllExtensions() =>
        Assert.Equal("Supported media|*.jpg;*.jpeg;*.jpe;*.jfif;*.webp;*.png;*.mp4;*.webm;*.avi",
                     SupportedFiles.DialogFilter);
}
