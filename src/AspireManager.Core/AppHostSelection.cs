namespace AspireManager.Core;

/// <summary>Which AppHost to attach to at startup.</summary>
public abstract record AppHostChoice;

public sealed record UseAppHost(string Path) : AppHostChoice;

public sealed record ChooseAppHost(IReadOnlyList<AppHost> Candidates) : AppHostChoice;

public sealed record NoAppHost : AppHostChoice;

public static class AppHostSelection
{
    /// <summary>
    /// An explicit path always wins. Otherwise attach to the single running AppHost, or ask. Never guess
    /// between several: the CLI resolves silently in that case and would act on the wrong one.
    /// </summary>
    public static AppHostChoice Select(IReadOnlyList<AppHost> hosts, string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return new UseAppHost(explicitPath);
        }

        // `aspire ps` also lists AppHosts that are starting or already stopped.
        List<AppHost> running = [.. hosts.Where(static h =>
            string.Equals(h.Status, "running", StringComparison.OrdinalIgnoreCase))];

        return running.Count switch
        {
            0 => new NoAppHost(),
            1 => new UseAppHost(running[0].AppHostPath),
            _ => new ChooseAppHost(Sorted(running)),
        };
    }

    /// <summary>
    /// Alphabetical by project name, so a list of AppHosts is in the same order every time rather than in
    /// whatever order `aspire ps` happened to report them.
    /// </summary>
    public static IReadOnlyList<AppHost> Sorted(IEnumerable<AppHost> hosts) =>
        [.. hosts
            .OrderBy(static h => Name(h.AppHostPath), StringComparer.OrdinalIgnoreCase)
            .ThenBy(static h => h.AppHostPath, StringComparer.Ordinal)];

    /// <summary>Trims the repeated directory noise so a picker row is readable.</summary>
    public static string Label(AppHost host) =>
        $"{Path.GetFileNameWithoutExtension(host.AppHostPath)}  (pid {host.AppHostPid})";

    /// <summary>
    /// Whether two paths name the same AppHost. `aspire ps` reports absolute paths while the command line
    /// may have given a relative one, so a plain string compare says "different" about the same project.
    /// </summary>
    public static bool SamePath(string? left, string? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        // Windows paths are case-insensitive: an Ordinal compare there would treat one AppHost as two.
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Equals(left, right, comparison);
        }
    }

    /// <summary>The AppHost project's own name, which is what identifies it to a human.</summary>
    public static string Name(string appHostPath) =>
        Path.GetFileNameWithoutExtension(appHostPath) is { Length: > 0 } name ? name : appHostPath;

    /// <summary>The directory the project sits in, which is what separates two checkouts of one repository.</summary>
    public static string Directory(string appHostPath) =>
        Path.GetDirectoryName(appHostPath) is { Length: > 0 } dir ? dir : appHostPath;

    /// <summary>
    /// The path shortened for a narrow panel: home becomes <c>~</c> and the front is elided, keeping the
    /// project and the directory holding it, which is what identifies the AppHost when nothing names it.
    /// </summary>
    public static string PathKeepingTail(string appHostPath, string? home, int width)
    {
        string path = Tilde(appHostPath, home);

        if (width <= 0 || path.Length <= width)
        {
            return path;
        }

        const string ellipsis = "\u2026";
        return width <= ellipsis.Length
            ? path[^width..]
            : ellipsis + path[^(width - ellipsis.Length)..];
    }

    /// <summary>
    /// The mirror of <see cref="PathKeepingTail"/>, for a row that already names the project beside it:
    /// the tail then only repeats what is on screen, while the leading directories are the whole of what
    /// tells one checkout from another. Which end survives is the only difference between the two.
    /// </summary>
    public static string PathKeepingHead(string appHostPath, string? home, int width)
    {
        string path = Tilde(appHostPath, home);

        if (width <= 0 || path.Length <= width)
        {
            return path;
        }

        const string ellipsis = "\u2026";
        return width <= ellipsis.Length
            ? path[..width]
            : path[..(width - ellipsis.Length)] + ellipsis;
    }

    private static string Tilde(string path, string? home) =>
        !string.IsNullOrEmpty(home) && path.StartsWith(home, StringComparison.Ordinal)
            ? string.Concat("~", path.AsSpan(home.Length))
            : path;
}
