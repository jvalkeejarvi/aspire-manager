namespace AspireManager.Core;

/// <summary>
/// The <c>groups</c> block of <c>~/.aspire-manager.json</c>: how the resource pane is arranged at startup,
/// and which type groups start folded.
/// </summary>
public sealed record GroupSettings(
    string? Mode = null,
    string? Default = null,
    IReadOnlyList<string>? Except = null);

/// <summary>
/// Whether a type group is folded, as a standing rule rather than a set filled in once. A type first seen
/// mid-session — a container started later, or everything after switching AppHost — has to obey the
/// configuration too, and there is nowhere to seed it at that point.
/// </summary>
public sealed class GroupPolicy
{
    private readonly bool _collapseByDefault;
    private readonly IReadOnlyList<string> _except;

    private GroupPolicy(GroupMode mode, bool collapseByDefault, IReadOnlyList<string> except)
    {
        Mode = mode;
        _collapseByDefault = collapseByDefault;
        _except = except;
    }

    /// <summary>Nothing configured: grouped, everything unfolded.</summary>
    public static GroupPolicy Default { get; } = new(GroupMode.Grouped, false, []);

    /// <summary>How the pane starts out; <c>g</c> cycles from there.</summary>
    public GroupMode Mode { get; }

    /// <summary>
    /// The policy, and a warning naming any value that could not be read. An unusable value falls back
    /// rather than refusing to start: the rest of the file is still worth honouring.
    /// </summary>
    public static (GroupPolicy Policy, string? Warning) From(GroupSettings? settings)
    {
        if (settings is null)
        {
            return (Default, null);
        }

        List<string> problems = [];

        GroupMode mode = ParseMode(settings.Mode) ?? Fallback(settings.Mode, "mode", GroupMode.Grouped);
        bool collapse = ParseCollapsed(settings.Default) ?? Fallback(settings.Default, "default", false);

        return (new GroupPolicy(mode, collapse, settings.Except ?? []), problems.Count == 0
            ? null
            : $"groups: {string.Join("; ", problems)}");

        T Fallback<T>(string? value, string key, T fallback)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"{key} \"{value}\" not recognised");
            }

            return fallback;
        }
    }

    /// <summary>
    /// A name matching nothing is not reported: types come and go with what is running, so a name that is
    /// right today matches nothing the moment that resource stops.
    /// </summary>
    public bool IsCollapsed(string resourceType) =>
        _except.Any(pattern => Matches(pattern, resourceType)) ? !_collapseByDefault : _collapseByDefault;

    /// <summary>Case-insensitive, with a trailing <c>*</c> matching by prefix. Nothing else is a wildcard.</summary>
    private static bool Matches(string pattern, string resourceType) =>
        pattern.EndsWith('*')
            ? resourceType.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, resourceType, StringComparison.OrdinalIgnoreCase);

    private static GroupMode? ParseMode(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "grouped" => GroupMode.Grouped,
        "typesuffix" => GroupMode.TypeSuffix,
        "plain" => GroupMode.Plain,
        _ => null,
    };

    private static bool? ParseCollapsed(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "collapsed" => true,
        "expanded" => false,
        _ => null,
    };
}
