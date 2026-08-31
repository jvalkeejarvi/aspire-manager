namespace AspireManager.Core;

/// <summary>
/// Formats a keybinding list. The bindings themselves live with the code that dispatches them, so a new
/// key cannot be added without its description; this only lays them out.
/// </summary>
public static class HelpText
{
    /// <summary>
    /// Rows with the key column padded so descriptions line up. A pair with an empty key renders as a
    /// blank separator.
    /// </summary>
    public static IReadOnlyList<string> Align(IReadOnlyList<(string Key, string Action)> bindings)
    {
        if (bindings.Count == 0)
        {
            return [];
        }

        int width = bindings.Max(static b => b.Key.Length);

        return [.. bindings.Select(b => b.Key.Length == 0 ? "" : $"{b.Key.PadRight(width)}   {b.Action}")];
    }
}
