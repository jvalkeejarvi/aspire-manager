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
    /// The row as the picker shows it: name, then what is known about it, padded so the details line up.
    /// </summary>
    public static IReadOnlyList<string> Rows(IReadOnlyList<AppHostOption> options, string? currentPath = null)
    {
        int width = options.Count == 0 ? 0 : options.Max(static o => o.Name.Length);

        return
        [
            .. options.Select(o =>
                (AppHostSelection.SamePath(o.Path, currentPath) ? "* " : "  ")
                + o.Name.PadRight(width)
                + $"   {o.Detail}"),
        ];
    }
}
