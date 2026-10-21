using System.Runtime.InteropServices;

namespace ComfyGenReader.Core;

public static partial class FolderNavigator
{
    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);

    public static string? Next(string current)
    {
        var (siblings, index) = Locate(current);
        if (siblings is null) return null;
        // index is current's position, or the first sibling sorting after it when current is gone.
        var target = index < siblings.Count && IsSame(siblings[index], current) ? index + 1 : index;
        return target < siblings.Count ? siblings[target] : null;
    }

    public static string? Previous(string current)
    {
        var (siblings, index) = Locate(current);
        if (siblings is null) return null;
        return index > 0 ? siblings[index - 1] : null;
    }

    private static bool IsSame(string a, string b) => a.Equals(b, StringComparison.OrdinalIgnoreCase);

    private static (List<string>? Siblings, int Index) Locate(string current)
    {
        var folder = Path.GetDirectoryName(current);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return (null, 0);

        List<string> siblings;
        try
        {
            siblings = Directory.EnumerateFiles(folder).Where(SupportedFiles.IsSupported).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, 0);
        }
        siblings.Sort(CompareNames);

        var exact = siblings.FindIndex(p => IsSame(p, current));
        if (exact >= 0) return (siblings, exact);

        var pos = siblings.FindIndex(p => CompareNames(p, current) > 0);
        return (siblings, pos < 0 ? siblings.Count : pos);
    }

    private static int CompareNames(string a, string b) =>
        StrCmpLogicalW(Path.GetFileName(a), Path.GetFileName(b));
}
