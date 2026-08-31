namespace AspireManager.Core;

/// <summary>
/// Turns a configured template into an argument list. The template is split on whitespace *first* and the
/// placeholders substituted into the resulting tokens, so a path containing spaces stays a single argument
/// without any quoting rules. No shell is involved, so pipes and variables do not work by design.
/// </summary>
public static class EditorCommandLine
{
    /// <summary>
    /// The template for this call: with a line, the line-aware one; without, the no-line one. A config that
    /// only sets <c>command</c> falls back to it with line 1, which is where a file opens anyway.
    /// </summary>
    public static (string? Template, int Line) Choose(EditorSettings? editor, int? line)
    {
        if (editor is null)
        {
            return (null, 1);
        }

        if (line is { } wanted)
        {
            return (Blank(editor.Command) ? editor.CommandNoLine : editor.Command, wanted);
        }

        return Blank(editor.CommandNoLine) ? (editor.Command, 1) : (editor.CommandNoLine, 1);
    }

    /// <summary>
    /// The executable and its arguments, or null when nothing is configured. The first token is the command.
    /// </summary>
    public static (string Command, IReadOnlyList<string> Arguments)? Build(string? template, string file, int line)
    {
        if (Blank(template))
        {
            return null;
        }

        string[] tokens = template!.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Substitution happens per token, after the split: a file path with spaces cannot be torn apart.
        string[] substituted = [.. tokens.Select(t => t
            .Replace("{file}", file, StringComparison.Ordinal)
            .Replace("{line}", line.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal))];

        return (substituted[0], substituted[1..]);
    }

    private static bool Blank(string? value) => string.IsNullOrWhiteSpace(value);
}
