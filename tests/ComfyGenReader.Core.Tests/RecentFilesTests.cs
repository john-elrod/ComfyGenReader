using ComfyGenReader.Core;

namespace ComfyGenReader.Core.Tests;

public sealed class RecentFilesTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("cgr-recent-").FullName;
    private string file => Path.Combine(dir, "data", "history.json");

    public void Dispose() => Directory.Delete(dir, true);

    private void WriteRaw(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    [Fact]
    public void Add_MostRecentFirst_DedupesAndCapsAt10()
    {
        var rf = new RecentFiles(file);
        for (int i = 0; i < 12; i++) rf.Add($@"C:\x\{i}.jpg");
        rf.Add(@"c:\X\5.JPG");
        Assert.Equal(10, rf.Items.Count);
        Assert.Equal(@"c:\X\5.JPG", rf.Items[0]);
        Assert.Single(rf.Items, p => p.Equals(@"C:\x\5.jpg", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Add_PersistsAcrossInstances()
    {
        new RecentFiles(file).Add(@"C:\x\a.jpg");
        Assert.Equal([@"C:\x\a.jpg"], new RecentFiles(file).Items);
    }

    [Fact]
    public void Add_ReplacesItemsSnapshotAndRaisesChanged()
    {
        var rf = new RecentFiles(file);
        var before = rf.Items;
        int raised = 0;
        rf.Changed += (_, _) => raised++;
        rf.Add(@"C:\x\a.jpg");
        Assert.NotSame(before, rf.Items);
        Assert.Empty(before);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Load_CorruptFile_StartsEmpty()
    {
        WriteRaw("{not json");
        Assert.Empty(new RecentFiles(file).Items);
    }

    [Fact]
    public void Load_WrongShape_StartsEmpty()
    {
        WriteRaw("{\"recent\": 5}");
        Assert.Empty(new RecentFiles(file).Items);
    }

    [Fact]
    public void Load_MoreThanCapacity_IsTrimmed()
    {
        WriteRaw("{\"recent\": [\"a\",\"b\",\"c\"]}");
        Assert.Equal(["a", "b"], new RecentFiles(file, 2).Items);
    }

    [Fact]
    public void Add_WhenSaveFails_StillUpdatesInMemory()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var lockHandle = File.Open(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        var rf = new RecentFiles(file);
        rf.Add(@"C:\x\a.jpg");
        Assert.Equal([@"C:\x\a.jpg"], rf.Items);
    }
}
