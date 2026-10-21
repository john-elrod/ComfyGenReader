using System.ComponentModel;
using System.Runtime.CompilerServices;
using ComfyGenReader.Core.Json;
using ComfyGenReader.Core.Metadata;

namespace ComfyGenReader.Core;

public enum OpenSource { User, Navigation }

public sealed class ReaderSession : INotifyPropertyChanged
{
    private readonly IMetadataLoader loader;
    private readonly RecentFiles recent;
    private CancellationTokenSource? loadCts;

    private string pathText = "";
    private string? currentFile;
    private MediaKind currentKind;
    private string seedText = "";
    private string promptText = "";
    private string rawText = "";
    private string? exportJson;
    private bool canGoPrevious;
    private bool canGoNext;

    public ReaderSession(IMetadataLoader loader, RecentFiles recent)
    {
        this.loader = loader;
        this.recent = recent;
        recent.Changed += (_, _) => Raise(nameof(Recent));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string PathText { get => pathText; set => Set(ref pathText, value); }
    public string? CurrentFile { get => currentFile; private set => Set(ref currentFile, value); }
    public MediaKind CurrentKind { get => currentKind; private set => Set(ref currentKind, value); }
    public string SeedText { get => seedText; private set => Set(ref seedText, value); }
    public string PromptText { get => promptText; private set => Set(ref promptText, value); }
    public string RawText { get => rawText; private set => Set(ref rawText, value); }
    public bool CanGoPrevious { get => canGoPrevious; private set => Set(ref canGoPrevious, value); }
    public bool CanGoNext { get => canGoNext; private set => Set(ref canGoNext, value); }
    public bool CanExport => exportJson != null;
    public IReadOnlyList<string> Recent => recent.Items;

    public string? ExportJson
    {
        get => exportJson;
        private set
        {
            if (Set(ref exportJson, value)) Raise(nameof(CanExport));
        }
    }

    public async Task<bool> OpenAsync(string path, OpenSource source)
    {
        path = path.Trim().Trim('"').Trim();

        if (!SupportedFiles.IsSupported(path))
        {
            var ext = Path.GetExtension(path);
            RawText = $"Unsupported file type: {(ext.Length == 0 ? "(none)" : ext)}";
            return false;
        }
        if (!File.Exists(path))
        {
            RawText = $"File not found: {path}";
            return false;
        }

        loadCts?.Cancel();
        var cts = loadCts = new CancellationTokenSource();
        var ct = cts.Token;

        CurrentFile = path;
        CurrentKind = SupportedFiles.GetKind(path);
        PathText = path;
        SeedText = "";
        PromptText = "";
        ExportJson = null;
        CanGoPrevious = FolderNavigator.Previous(path) != null;
        CanGoNext = FolderNavigator.Next(path) != null;
        if (source == OpenSource.User) recent.Add(path);

        try
        {
            var result = await loader.LoadAsync(path, ct);
            if (ct.IsCancellationRequested) return false;
            var highlights = ComfyFields.Extract(result.Prompt, result.Workflow);
            RawText = DisplayFormatter.ToDisplayText(result.Raw);
            SeedText = highlights.Seed;
            PromptText = highlights.Prompt;
            ExportJson = result.ExportJson;
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return false;
            ShowError(ex);
        }
        return true;
    }

    public Task GoNextAsync() => GoAsync(FolderNavigator.Next);

    public Task GoPreviousAsync() => GoAsync(FolderNavigator.Previous);

    public void ShowError(Exception ex) => RawText = ErrorFormatter.Format(ex);

    private Task GoAsync(Func<string, string?> neighbour) =>
        currentFile is not null && neighbour(currentFile) is { } target
            ? OpenAsync(target, OpenSource.Navigation)
            : Task.CompletedTask;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    private void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
