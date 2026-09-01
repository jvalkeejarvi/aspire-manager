using System.Text.Json;

namespace AspireManager.Core;

/// <summary>An AppHost this tool has attached to before, newest first in the file.</summary>
public sealed record RecentAppHost(string Path, DateTimeOffset LastUsedAt);

/// <summary>
/// The AppHosts worth offering to start when none is running. Tool-written state rather than
/// configuration, so it lives beside the config file but in its own file: the config is hand-edited and
/// may carry comments, which rewriting it would drop.
/// </summary>
public static class Recents
{
    public const int Capacity = 10;

    /// <summary>The list with <paramref name="path"/> at the front, deduplicated and capped.</summary>
    public static IReadOnlyList<RecentAppHost> Record(
        IReadOnlyList<RecentAppHost> existing,
        string path,
        DateTimeOffset now) =>
    [
        new RecentAppHost(path, now),
        .. existing.Where(e => !AppHostSelection.SamePath(e.Path, path)).Take(Capacity - 1),
    ];

    /// <summary>
    /// Newest first, dropping any project file that has since been deleted or renamed. The pruning is not
    /// written back — the next attach rewrites the file anyway, and a path on an unmounted volume should
    /// not be forgotten just for being unreachable once.
    /// </summary>
    public static IReadOnlyList<RecentAppHost> Load(string file, Func<string, bool>? exists = null)
    {
        exists ??= File.Exists;

        try
        {
            if (!File.Exists(file))
            {
                return [];
            }

            RecentAppHost[]? entries = JsonSerializer.Deserialize(
                File.ReadAllText(file),
                ConfigJsonContext.Default.RecentAppHostArray);

            return
            [
                .. (entries ?? [])
                    .Where(e => !string.IsNullOrWhiteSpace(e.Path) && exists(e.Path))
                    .OrderByDescending(static e => e.LastUsedAt)
                    .Take(Capacity),
            ];
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            // State we can rebuild: a corrupt or unreadable file is not worth a message on startup.
            return [];
        }
    }

    /// <summary>Best effort: failing to remember an AppHost must never stop the tool from attaching to it.</summary>
    public static void Save(string file, IReadOnlyList<RecentAppHost> entries)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(
                file,
                JsonSerializer.Serialize(entries.ToArray(), ConfigJsonContext.Default.RecentAppHostArray));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Nothing to say and nowhere to say it: this runs while the UI is starting up.
        }
    }
}
