using System.Text.Json;

namespace ComfyGenReader.Core;

/// <summary>User preferences, saved as JSON next to the app. Missing or unreadable values fall back to defaults.</summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string filePath;
    private bool darkMode = true;

    public AppSettings(string filePath)
    {
        this.filePath = filePath;
        Load();
    }

    public static string DefaultPath { get; } =
        Path.Combine(AppContext.BaseDirectory, "data", "settings.json");

    public bool DarkMode
    {
        get => darkMode;
        set
        {
            darkMode = value;
            Save();
        }
    }

    private void Load()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(filePath));
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("darkMode", out var dark) &&
                dark.ValueKind is JsonValueKind.True or JsonValueKind.False)
                darkMode = dark.GetBoolean();
        }
        catch
        {
            // No file yet, or not JSON: keep the defaults.
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(filePath, JsonSerializer.Serialize(new { darkMode }, WriteOptions));
        }
        catch
        {
            // Persistence is best-effort; the in-memory value stays authoritative.
        }
    }
}
