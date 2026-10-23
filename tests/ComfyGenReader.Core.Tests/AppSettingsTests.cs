using ComfyGenReader.Core;

namespace ComfyGenReader.Core.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("cgr-settings-").FullName;
    private string file => Path.Combine(dir, "data", "settings.json");

    public void Dispose() => Directory.Delete(dir, true);

    private void WriteRaw(string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
    }

    [Fact]
    public void DarkMode_NoFile_DefaultsOn()
    {
        Assert.True(new AppSettings(file).DarkMode);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void DarkMode_PersistsAcrossInstances()
    {
        new AppSettings(file).DarkMode = false;
        Assert.False(new AppSettings(file).DarkMode);

        new AppSettings(file).DarkMode = true;
        Assert.True(new AppSettings(file).DarkMode);
    }

    [Fact]
    public void Load_CorruptFile_DefaultsOn()
    {
        WriteRaw("{not json");
        Assert.True(new AppSettings(file).DarkMode);
    }

    [Fact]
    public void Load_WrongShape_DefaultsOn()
    {
        WriteRaw("{\"darkMode\": \"no\"}");
        Assert.True(new AppSettings(file).DarkMode);
        WriteRaw("[false]");
        Assert.True(new AppSettings(file).DarkMode);
    }

    [Fact]
    public void Save_UnwritablePath_KeepsValueInMemory()
    {
        WriteRaw("");
        var settings = new AppSettings(Path.Combine(file, "settings.json")); // parent is a file: cannot be created
        settings.DarkMode = false;
        Assert.False(settings.DarkMode);
    }
}
