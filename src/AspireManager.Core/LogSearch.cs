namespace AspireManager.Core;

/// <summary>
/// Searching the log pane. Unlike the resource filter this hides nothing: it highlights matches in place
/// and moves the selection between them, which is what makes surrounding context still readable.
/// </summary>
public static class LogSearch
{
    /// <summary>Where <paramref name="query"/> occurs in one line, as (start, length) pairs.</summary>
    public static IReadOnlyList<(int Start, int Length)> Matches(string line, string? query)
    {
        if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(line))
        {
            return [];
        }

        List<(int, int)> found = [];
        int from = 0;

        while (from <= line.Length - query.Length)
        {
            int at = line.IndexOf(query, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
            {
                break;
            }

            found.Add((at, query.Length));

            // Step past this match, so overlapping occurrences of a repeating query are not double-counted.
            from = at + query.Length;
        }

        return found;
    }

    /// <summary>Indices of the lines containing the query, in order.</summary>
    public static IReadOnlyList<int> MatchingLines(IReadOnlyList<string> lines, string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        List<int> hits = [];
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                hits.Add(i);
            }
        }

        return hits;
    }

    /// <summary>
    /// The next match position, wrapping at both ends — n at the last match returns to the first, which is
    /// what every editor does and what makes n usable without watching the counter.
    /// </summary>
    public static int Advance(int matchCount, int current, int delta)
    {
        if (matchCount <= 0)
        {
            return 0;
        }

        return ((current + delta) % matchCount + matchCount) % matchCount;
    }

    /// <summary>
    /// The line to select for a given match position, or null when there is nothing to jump to.
    /// </summary>
    public static int? LineForPosition(IReadOnlyList<int> matchingLines, int position) =>
        matchingLines.Count == 0 || position < 0 || position >= matchingLines.Count
            ? null
            : matchingLines[position];

    /// <summary>The status line, mirroring what lazygit shows while searching.</summary>
    public static string Summary(string query, int matchCount, int position) =>
        matchCount == 0
            ? $"no matches for '{query}'"
            : $"matches for '{query}' ({position + 1} of {matchCount})";
}
