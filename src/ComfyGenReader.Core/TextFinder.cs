namespace ComfyGenReader.Core;

/// <summary>Case-insensitive find for the Raw text box.</summary>
public static class TextFinder
{
    /// <summary>Start indexes of every non-overlapping match. An empty query matches nothing.</summary>
    public static List<int> FindAll(string text, string query)
    {
        var matches = new List<int>();
        if (query.Length == 0) return matches;
        // OrdinalIgnoreCase folds char by char, so a match is always query.Length long.
        for (var i = text.IndexOf(query, StringComparison.OrdinalIgnoreCase); i >= 0;
             i = text.IndexOf(query, i + query.Length, StringComparison.OrdinalIgnoreCase))
            matches.Add(i);
        return matches;
    }

    /// <summary>The first match starting at or after <paramref name="position"/>, wrapping to the first; -1 if none.</summary>
    public static int NextIndex(IReadOnlyList<int> matches, int position)
    {
        if (matches.Count == 0) return -1;
        for (var i = 0; i < matches.Count; i++)
            if (matches[i] >= position) return i;
        return 0;
    }

    /// <summary>The last match starting before <paramref name="position"/>, wrapping to the last; -1 if none.</summary>
    public static int PreviousIndex(IReadOnlyList<int> matches, int position)
    {
        if (matches.Count == 0) return -1;
        for (var i = matches.Count - 1; i >= 0; i--)
            if (matches[i] < position) return i;
        return matches.Count - 1;
    }
}
