namespace AspireManager.Core;

/// <summary>What pressing a key on a resource should do.</summary>
public abstract record CommandDecision;

public sealed record RunCommand(string DisplayName, string Command) : CommandDecision;

/// <summary>The user must type <paramref name="Expected"/> back before this runs.</summary>
public sealed record ConfirmCommand(string DisplayName, string Command, string Expected) : CommandDecision;

public sealed record RefuseCommand(string Reason) : CommandDecision;

/// <summary>
/// How a row should read at a glance. Semantic rather than a colour, so Core stays free of any terminal
/// library; the window maps these onto attributes.
/// </summary>
public enum RowTone
{
    /// <summary>Leave the theme's own colour alone.</summary>
    Normal,

    Heading,

    /// <summary>Running and healthy.</summary>
    Healthy,

    /// <summary>Running but unhealthy or degraded.</summary>
    Warning,

    /// <summary>Failed to start, or exited badly.</summary>
    Failed,

    /// <summary>Not started, or finished - nothing wrong, nothing running.</summary>
    Inactive,
}

/// <summary>How the attachment to an AppHost is faring.</summary>
public enum ConnectionState
{
    Connecting,
    Connected,
    Reconnecting,
}

/// <summary>One line of the resource pane: either a type heading or a resource under it.</summary>
public abstract record ResourceRow;

public sealed record TypeHeader(string ResourceType, int Count, bool Collapsed) : ResourceRow;

/// <param name="Indented">False when ungrouped: there is no heading to sit under, so the indent is waste.</param>
public sealed record ResourceItem(AspireResource Resource, bool Indented = true) : ResourceRow;

/// <summary>
/// The presentation logic, kept out of the widget layer so it is testable without a terminal —
/// Terminal.Gui 2.4.17 exposes input injection but no headless driver to render into.
/// </summary>
public static class ShellModel
{
    private static readonly Dictionary<char, string> _keyCommands = new()
    {
        ['r'] = "restart",
        ['s'] = "stop",
        ['b'] = "rebuild",
    };

    public static string? CommandForKey(char key) =>
        _keyCommands.GetValueOrDefault(char.ToLowerInvariant(key));

    /// <summary>
    /// One row of the resource pane: state initial, health initial, name. Initials rather than words
    /// because the pane is narrow and the colour carries most of the meaning.
    /// </summary>
    public static string Row(AspireResource resource) =>
        $"{StateMark(resource)} {HealthMark(resource)} {resource.DisplayName}";

    /// <summary>
    /// Resources grouped under a heading per <c>resourceType</c>, types alphabetical and resources
    /// alphabetical within a type, so the list never reshuffles as states change. A collapsed type keeps
    /// its heading and drops its members. Headings are rows in their own right — they are what the user
    /// selects to fold a group.
    /// </summary>
    public static IReadOnlyList<ResourceRow> Rows(
        IReadOnlyList<AspireResource> resources,
        IReadOnlySet<string>? collapsedTypes = null,
        string? filter = null,
        bool grouped = true)
    {
        string? needle = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();

        // The filter matches resource names only, never type names: searching "sql" should find sqlPass,
        // not every member of SqlServerDatabaseResource.
        IEnumerable<AspireResource> visible = needle is null
            ? resources
            : resources.Where(r => r.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase));

        // Ungrouped: one flat alphabetical list, no headings and nothing to fold.
        if (!grouped)
        {
            return [.. visible
                .OrderBy(static r => r.DisplayName, StringComparer.Ordinal)
                .Select(static r => new ResourceItem(r, Indented: false))];
        }

        List<ResourceRow> rows = [];

        foreach (IGrouping<string, AspireResource> group in visible
                     .GroupBy(static r => r.ResourceType, StringComparer.Ordinal)
                     .OrderBy(static g => g.Key, StringComparer.Ordinal))
        {
            List<AspireResource> members = [.. group.OrderBy(static r => r.DisplayName, StringComparer.Ordinal)];

            // While searching, a folded group would hide its own matches.
            bool collapsed = needle is null && collapsedTypes?.Contains(group.Key) == true;
            rows.Add(new TypeHeader(group.Key, members.Count, collapsed));

            if (!collapsed)
            {
                rows.AddRange(members.Select(static r => new ResourceItem(r)));
            }
        }

        return rows;
    }

    public static string RowText(ResourceRow row) => row switch
    {
        TypeHeader header => $"{(header.Collapsed ? '\u25b8' : '\u25be')} {header.ResourceType} ({header.Count})",

        // Indented so the heading above it reads as a heading without needing colour.
        ResourceItem item => item.Indented ? $"    {Row(item.Resource)}" : $" {Row(item.Resource)}",
        _ => "",
    };

    /// <summary>
    /// Identity of a row, stable across rebuilds. Grouping and folding both shift every later index, so
    /// selection is restored by key rather than by position.
    /// </summary>
    public static string RowKey(ResourceRow row) => row switch
    {
        TypeHeader header => TypeKey(header.ResourceType),
        ResourceItem item => $"res:{item.Resource.Name}",
        _ => "",
    };

    public static string TypeKey(string resourceType) => $"type:{resourceType}";

    public static int IndexOfKey(IReadOnlyList<ResourceRow> rows, string? key)
    {
        if (key is null)
        {
            return -1;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            if (string.Equals(RowKey(rows[i]), key, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where the selection should start: the first actual resource, so the log pane has something to
    /// show. Falls back to the first heading when every group is folded, and -1 when there is nothing.
    /// </summary>
    public static int FirstSelectable(IReadOnlyList<ResourceRow> rows)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i] is ResourceItem)
            {
                return i;
            }
        }

        return rows.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Mirrors what `aspire describe` colours: state at a glance, health when it disagrees with the state.
    /// </summary>
    public static RowTone Tone(ResourceRow row)
    {
        if (row is TypeHeader)
        {
            return RowTone.Heading;
        }

        if (row is not ResourceItem item)
        {
            return RowTone.Normal;
        }

        AspireResource resource = item.Resource;

        if (resource.State.Contains("Fail", StringComparison.OrdinalIgnoreCase))
        {
            return RowTone.Failed;
        }

        if (!string.Equals(resource.State, "Running", StringComparison.OrdinalIgnoreCase))
        {
            return RowTone.Inactive;
        }

        // Running: health is the only thing left that distinguishes fine from not fine. Resources with no
        // health check report null, which is not a problem.
        return resource.HealthStatus is null or "Healthy" ? RowTone.Healthy : RowTone.Warning;
    }

    /// <summary>The part of a row that carries the state, coloured by <see cref="Tone"/>.</summary>
    public static string StateMark(AspireResource resource) =>
        resource.State.Length > 0 ? resource.State[..1] : "?";

    /// <summary>
    /// The health initial: H, U (unhealthy), D (degraded). Resources with no health check report null and
    /// get a dash — that is "not measured", not "unhealthy".
    /// </summary>
    public static string HealthMark(AspireResource resource) =>
        string.IsNullOrEmpty(resource.HealthStatus)
            ? "-"
            : resource.HealthStatus[..1].ToUpperInvariant();

    /// <summary>Health is coloured on its own; a running resource can still be unhealthy.</summary>
    public static RowTone HealthTone(AspireResource resource) => resource.HealthStatus switch
    {
        null or "" => RowTone.Inactive,
        "Healthy" => RowTone.Healthy,
        "Unhealthy" => RowTone.Failed,
        _ => RowTone.Warning,
    };

    public static string ConnectionText(ConnectionState state, TimeSpan retryIn) => state switch
    {
        ConnectionState.Connected => "connected",
        ConnectionState.Reconnecting => $"reconnecting in {retryIn.TotalSeconds:0}s",
        _ => "connecting",
    };

    /// <summary>Reuses the row tones so the panel and the list speak the same colour language.</summary>
    public static RowTone ConnectionTone(ConnectionState state) => state switch
    {
        ConnectionState.Connected => RowTone.Healthy,
        ConnectionState.Reconnecting => RowTone.Failed,
        _ => RowTone.Warning,
    };

    /// <summary>
    /// Where a one-row move lands. Returns null when there is nothing to select — an empty list is a real
    /// state (no logs yet, a filter matching nothing) and moving in it must be a no-op, not a crash.
    /// Clamps rather than wraps, and copes with a stale index left over from a shorter rebuild.
    /// </summary>
    public static int? NextIndex(int count, int? current, int delta)
    {
        if (count <= 0)
        {
            return null;
        }

        // Nothing selected yet: the first move selects the first row rather than stepping past it.
        if (current is not { } from || from < 0)
        {
            return 0;
        }

        return Math.Clamp(Math.Min(from, count - 1) + delta, 0, count - 1);
    }

    /// <summary>
    /// Only http and https are opened. The URL is handed to the operating system's shell, so anything else
    /// — a file path, a custom scheme — is refused rather than launched on an AppHost's say-so.
    /// </summary>
    public static bool IsOpenable(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed)
        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps);

    /// <summary>Every URL worth listing, in the order the AppHost reported them.</summary>
    public static IReadOnlyList<AspireUrl> Urls(AspireResource resource) =>
        resource.Urls is null ? [] : [.. resource.Urls.Where(static u => IsOpenable(u.Url))];

    /// <summary>What a bare "open" picks: the first endpoint meant for a person.</summary>
    public static AspireUrl? PrimaryUrl(AspireResource resource) =>
        Urls(resource).FirstOrDefault(static u => !u.IsInternal);

    /// <summary>One row of the URL picker.</summary>
    public static string UrlLabel(AspireUrl url)
    {
        string? name = url.DisplayName ?? url.Name;
        string prefix = string.IsNullOrEmpty(name) ? "" : $"{name}  ";
        return $"{prefix}{url.Url}{(url.IsInternal ? "  (internal)" : "")}";
    }

    public static string LogRow(LogLine line) =>
        $"{line.Timestamp.ToLocalTime():HH:mm:ss} {line.Content}";

    /// <summary>Commands worth showing for a resource, in the AppHost's own order.</summary>
    public static IReadOnlyList<string> AvailableCommands(AspireResource resource) =>
        resource.Commands is null
            ? []
            : [.. resource.Commands
                .Where(c => CommandPolicy.Classify(c.Key, c.Value) != CommandAvailability.Unavailable)
                .OrderBy(c => c.Value.SortOrder)
                .Select(c => c.Key)];

    public static CommandDecision Decide(AspireResource resource, string command)
    {
        if (resource.Commands?.TryGetValue(command, out AspireCommand? definition) is not true)
        {
            return new RefuseCommand($"{resource.DisplayName} has no {command}");
        }

        return CommandPolicy.Classify(command, definition) switch
        {
            CommandAvailability.Instant => new RunCommand(resource.DisplayName, command),
            CommandAvailability.NeedsConfirmation =>
                new ConfirmCommand(resource.DisplayName, command, resource.DisplayName),
            _ => new RefuseCommand($"{command} is not available on {resource.DisplayName}"),
        };
    }

    /// <summary>
    /// Exact match after trimming. Deliberately not case-insensitive or fuzzy: the typing is the guard,
    /// and a guard you can satisfy by accident is not one.
    /// </summary>
    public static bool ConfirmationMatches(string expected, string typed) =>
        string.Equals(expected, typed.Trim(), StringComparison.Ordinal);
}
