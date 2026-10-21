using System.Text.Json;

namespace ComfyGenReader.Core;

public sealed class RecentFiles
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string filePath;
    private readonly int capacity;
    private IReadOnlyList<string> items;

    public RecentFiles(string filePath, int capacity = 10)
    {
        this.filePath = filePath;
        this.capacity = capacity;
        items = Load();
    }

    public static string DefaultPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "data", "history.json");

    public IReadOnlyList<string> Items => items;

    public event EventHandler? Changed;

    public void Add(string path)
    {
        var next = new List<string>(capacity + 1) { path };
        next.AddRange(items.Where(p => !p.Equals(path, StringComparison.OrdinalIgnoreCase)));
        if (next.Count > capacity) next.RemoveRange(capacity, next.Count - capacity);
        items = next;
        Save();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private IReadOnlyList<string> Load()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("recent", out var recent) ||
                recent.ValueKind != JsonValueKind.Array)
                return [];
            return recent.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()!)
                .Take(capacity)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new { recent = items }, WriteOptions));
        }
        catch
        {
            // Persistence is best-effort; the in-memory list stays authoritative.
        }
    }
}
