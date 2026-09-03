namespace AspireManager.Core;

/// <summary>
/// One row of the AppHost picker. <paramref name="Running"/> decides whether choosing it attaches or has
/// to start it first.
/// </summary>
public sealed record AppHostOption(string Path, string Name, string Detail, bool Running);

/// <summary>
/// The AppHosts worth offering: the ones running, the ones attached to before, and the ones in this
/// directory. Kept out of the widget layer because the ordering and the deduplication are the whole of it.
/// </summary>
public static class AppHostOptions
{
    /// <summary>
    /// Running first, then recents newest-first, then whatever the workspace scan found. An AppHost
    /// reached by more than one route appears once, under the first of those, since a running AppHost is
    /// attached to rather than started and the distinction is the point of the list.
    /// </summary>
    public static IReadOnlyList<AppHostOption> Build(
        IReadOnlyList<AppHost> running,
        IReadOnlyList<RecentAppHost> recents,
        IReadOnlyList<AppHostCandidate> candidates,
        DateTimeOffset now)
    {
        List<AppHostOption> options = [];

        foreach (AppHost host in AppHostSelection.Sorted(running.Where(static h =>
                     string.Equals(h.Status, "running", StringComparison.OrdinalIgnoreCase))))
        {
            Add(host.AppHostPath, $"pid {host.AppHostPid}", true);
        }

        foreach (RecentAppHost recent in recents.OrderByDescending(static r => r.LastUsedAt))
        {
            Add(recent.Path, Age(now - recent.LastUsedAt), false);
        }

        foreach (AppHostCandidate candidate in candidates
                     .OrderBy(static c => AppHostSelection.Name(c.Path), StringComparer.OrdinalIgnoreCase))
        {
            Add(candidate.Path, "in this directory", false);
        }

        return options;

        void Add(string path, string detail, bool isRunning)
        {
            if (string.IsNullOrWhiteSpace(path) || options.Any(o => AppHostSelection.SamePath(o.Path, path)))
            {
                return;
            }

            options.Add(new AppHostOption(path, AppHostSelection.Name(path), detail, isRunning));
        }
    }

    /// <summary>
    /// How long ago, at the coarseness a picker row can use. Anything older than a week is a date: "39d
    /// ago" is not something anyone reads as a time.
    /// </summary>
    public static string Age(TimeSpan since) => since switch
    {
        { TotalMinutes: < 1 } => "just now",
        { TotalHours: < 1 } => $"{(int)since.TotalMinutes}m ago",
        { TotalDays: < 1 } => $"{(int)since.TotalHours}h ago",
        { TotalDays: < 7 } => $"{(int)since.TotalDays}d ago",
        _ => "over a week ago",
    };

    /// <summary>
    /// The row as the picker shows it: name, then the directory holding the project, then what is known
    /// about it, each padded so the columns line up. The directory is there because two checkouts of one
    /// repository produce rows with the same name, and the name is then not enough to choose by.
    /// <paramref name="width"/> is the room a row has; zero means unbounded.
    /// </summary>
    public static IReadOnlyList<string> Rows(
        IReadOnlyList<AppHostOption> options,
        string? currentPath = null,
        string? home = null,
        int width = 0)
    {
        if (options.Count == 0)
        {
            return [];
        }

        int names = options.Max(static o => o.Name.Length);
        int details = options.Max(static o => o.Detail.Length);

        // What the fixed columns leave the path. The floor keeps a terminal too narrow for all of this
        // from asking for a negative width, which reads as "unbounded" and would print the whole path.
        int budget = width <= 0 ? int.MaxValue : Math.Max(12, width - names - details - 8);

        string[] paths =
        [
            .. options.Select(o =>
                AppHostSelection.PathKeepingHead(AppHostSelection.Directory(o.Path), home, budget)),
        ];

        int dirs = paths.Max(static p => p.Length);

        return
        [
            .. options.Select((o, i) =>
                (AppHostSelection.SamePath(o.Path, currentPath) ? "* " : "  ")
                + o.Name.PadRight(names)
                + $"   {paths[i].PadRight(dirs)}   {o.Detail}"),
        ];
    }
}
