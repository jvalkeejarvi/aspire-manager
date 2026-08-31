using System.Text;
using AspireManager.Core;

namespace AspireManager.Tui;

/// <summary>
/// Writes the lines an editor is about to open. One file per resource at a stable path, overwritten each
/// time, so an editor keeps a single buffer per resource and reverts it rather than opening another.
/// </summary>
internal static class LogSnapshot
{
    public static string Write(string appHostPath, string displayName, IEnumerable<string> lines)
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "aspire-manager",
            Sanitise(AppHostSelection.Name(appHostPath)));

        Directory.CreateDirectory(directory);

        string file = Path.Combine(directory, $"{Sanitise(displayName)}.log");
        // Encoding.UTF8 writes a BOM, which some editors show as a stray character on line 1.
        File.WriteAllText(file, string.Join('\n', lines) + '\n', new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return file;
    }

    /// <summary>
    /// Resource names come from the AppHost, so they are not guaranteed to be safe as a file name; anything
    /// that is not plainly a name becomes an underscore.
    /// </summary>
    private static string Sanitise(string name)
    {
        StringBuilder safe = new(name.Length);
        foreach (char c in name)
        {
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        return safe.Length == 0 ? "unnamed" : safe.ToString();
    }
}
