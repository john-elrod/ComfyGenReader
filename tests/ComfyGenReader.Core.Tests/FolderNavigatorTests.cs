using ComfyGenReader.Core;

namespace ComfyGenReader.Core.Tests;

public sealed class FolderNavigatorTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("cgr-nav-").FullName;

    public FolderNavigatorTests()
    {
        foreach (var n in new[] { "a2.jpg", "a10.mp4", "a1.webp", "notes.txt", "b.gif" })
            File.WriteAllText(P(n), "x");
    }

    public void Dispose() => Directory.Delete(dir, true);

    private string P(string name) => Path.Combine(dir, name);

    [Fact]
    public void Next_UsesNaturalOrderAndSkipsUnsupported()
    {
        Assert.Equal(P("a2.jpg"), FolderNavigator.Next(P("a1.webp")));
        Assert.Equal(P("a10.mp4"), FolderNavigator.Next(P("a2.jpg")));
        Assert.Null(FolderNavigator.Next(P("a10.mp4")));
    }

    [Fact]
    public void Previous_UsesNaturalOrder() =>
        Assert.Equal(P("a2.jpg"), FolderNavigator.Previous(P("a10.mp4")));

    [Fact]
    public void Previous_AtStart_ReturnsNull() => Assert.Null(FolderNavigator.Previous(P("a1.webp")));

    [Fact]
    public void Next_FromDeletedFile_UsesSortPosition() =>
        Assert.Equal(P("a10.mp4"), FolderNavigator.Next(P("a3.jpg")));

    [Fact]
    public void Previous_FromDeletedFile_UsesSortPosition() =>
        Assert.Equal(P("a2.jpg"), FolderNavigator.Previous(P("a3.jpg")));

    [Fact]
    public void Next_CurrentPathCaseInsensitive() =>
        Assert.Equal(P("a2.jpg"), FolderNavigator.Next(P("A1.WEBP")));

    [Fact]
    public void MissingFolder_ReturnsNull() =>
        Assert.Null(FolderNavigator.Next(Path.Combine(dir, "nope", "a.jpg")));
}
