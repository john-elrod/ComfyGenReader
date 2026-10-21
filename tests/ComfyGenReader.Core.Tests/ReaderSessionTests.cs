using System.Text.Json.Nodes;
using ComfyGenReader.Core;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core.Tests;

public sealed class ReaderSessionTests : IDisposable
{
    private sealed class FakeLoader : IMetadataLoader
    {
        public MetadataResult Result { get; set; } = new(new JsonObject(), JsonNode.Parse(ComfySamples.ApiPrompt), JsonNode.Parse(ComfySamples.Workflow));
        public Exception? Throw { get; set; }
        public Func<string, CancellationToken, Task>? Gate { get; set; }

        public async Task<MetadataResult> LoadAsync(string path, CancellationToken ct)
        {
            if (Gate is not null) await Gate(path, ct);
            if (Throw is not null) throw Throw;
            return Result;
        }
    }

    private readonly string dir = Directory.CreateTempSubdirectory("cgr-session-").FullName;
    private readonly FakeLoader loader = new();
    private readonly ReaderSession s;
    private readonly string jpg, next, gif;

    public ReaderSessionTests()
    {
        jpg = Touch("a.jpg");
        next = Touch("b.jpg");
        gif = Touch("c.gif");
        s = new ReaderSession(loader, new RecentFiles(Path.Combine(dir, "data", "history.json")));
    }

    public void Dispose() => Directory.Delete(dir, true);

    private string Touch(string name)
    {
        var p = Path.Combine(dir, name);
        File.WriteAllBytes(p, []);
        return p;
    }

    [Fact] public async Task Open_User_AddsToHistoryAndFillsHighlights()
    {
        Assert.True(await s.OpenAsync(jpg, OpenSource.User));
        Assert.Equal(jpg, s.CurrentFile); Assert.Equal(jpg, s.PathText); Assert.Equal(jpg, s.Recent[0]);
        Assert.Equal(MediaKind.Image, s.CurrentKind);
        Assert.Equal("156680208700286", s.SeedText); Assert.Equal("a cat\nwearing a hat", s.PromptText);
        Assert.True(s.CanExport);
        Assert.False(s.CanGoPrevious); Assert.True(s.CanGoNext);
    }
    [Fact] public async Task Navigation_DoesNotAddToHistory()
    {
        await s.OpenAsync(jpg, OpenSource.User);
        await s.GoNextAsync();
        Assert.Equal(next, s.CurrentFile);
        Assert.Equal([jpg], s.Recent);
        Assert.True(s.CanGoPrevious); Assert.False(s.CanGoNext);
        await s.GoPreviousAsync();
        Assert.Equal(jpg, s.CurrentFile);
    }
    [Fact] public async Task Go_WithNoFile_DoesNothing()
    {
        await s.GoNextAsync(); await s.GoPreviousAsync();
        Assert.Null(s.CurrentFile);
    }
    [Fact] public async Task Open_TrimsQuotesAndWhitespace() =>
        Assert.True(await s.OpenAsync($"  \"{jpg}\" ", OpenSource.User));
    [Fact] public async Task Open_Unsupported_LeavesStateAndReports()
    {
        Assert.False(await s.OpenAsync(gif, OpenSource.User));
        Assert.Null(s.CurrentFile); Assert.Equal("Unsupported file type: .gif", s.RawText); Assert.Empty(s.Recent);
    }
    [Fact] public async Task Open_NoExtension_ReportsNone()
    {
        Assert.False(await s.OpenAsync(Path.Combine(dir, "readme"), OpenSource.User));
        Assert.Equal("Unsupported file type: (none)", s.RawText);
    }
    [Fact] public async Task Open_Missing_ReportsNotFound()
    {
        Assert.False(await s.OpenAsync(@"C:\nope\gone.jpg", OpenSource.User));
        Assert.Equal(@"File not found: C:\nope\gone.jpg", s.RawText);
    }
    [Fact] public async Task Open_LoaderThrows_ShowsErrorAndKeepsPreview()
    {
        await s.OpenAsync(jpg, OpenSource.User);
        loader.Throw = new IOException("disk gone");
        await s.OpenAsync(next, OpenSource.User);
        Assert.StartsWith("System.IO.IOException: disk gone", s.RawText); Assert.Equal(next, s.CurrentFile);
        Assert.Equal(next, s.PathText); Assert.Equal("", s.SeedText); Assert.Null(s.ExportJson); Assert.False(s.CanExport);
    }
    [Fact] public async Task Open_NoComfyData_DisablesExport()
    {
        loader.Result = MetadataParser.Parse([new("Exif IFD0/Make", "Canon")]);
        await s.OpenAsync(jpg, OpenSource.User);
        Assert.False(s.CanExport); Assert.Equal("", s.SeedText); Assert.Contains("Canon", s.RawText);
    }
    [Fact] public async Task Open_ClearsPreviousHighlightsWhileLoading()
    {
        await s.OpenAsync(jpg, OpenSource.User);
        var gate = new TaskCompletionSource();
        loader.Gate = (_, _) => gate.Task;
        var pending = s.OpenAsync(next, OpenSource.User);
        Assert.Equal("", s.SeedText); Assert.Equal("", s.PromptText); Assert.Null(s.ExportJson);
        gate.SetResult();
        Assert.True(await pending);
        Assert.Equal("156680208700286", s.SeedText);
    }
    [Fact] public async Task Open_Superseded_DoesNotOverwriteNewerState()
    {
        var first = new TaskCompletionSource();
        loader.Gate = (p, ct) => p == jpg ? first.Task : Task.CompletedTask;
        var older = s.OpenAsync(jpg, OpenSource.User);
        loader.Result = MetadataParser.Parse([new("Exif IFD0/Make", "Canon")]);
        Assert.True(await s.OpenAsync(next, OpenSource.User));
        first.SetResult();
        Assert.False(await older);
        Assert.Equal(next, s.CurrentFile); Assert.Contains("Canon", s.RawText);
    }
    [Fact] public async Task Open_SupersededLoaderCancellation_IsNotAnError()
    {
        var first = new TaskCompletionSource();
        loader.Gate = (p, ct) => p == jpg ? first.Task.WaitAsync(ct) : Task.CompletedTask;
        var older = s.OpenAsync(jpg, OpenSource.User);
        await s.OpenAsync(next, OpenSource.User);
        Assert.False(await older);
        Assert.DoesNotContain("OperationCanceled", s.RawText);
    }
    [Fact] public async Task Recent_RaisesPropertyChanged()
    {
        var names = new List<string?>();
        s.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        await s.OpenAsync(jpg, OpenSource.User);
        Assert.Contains("Recent", names); Assert.Contains("CurrentFile", names); Assert.Contains("CanExport", names);
    }
}
