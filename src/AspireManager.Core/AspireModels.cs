using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspireManager.Core;

/// <summary>One resource as reported by <c>aspire describe</c>.</summary>
/// <param name="Name">Unique, but carries a random suffix (<c>ticker-srcbhsae</c>). Not for display.</param>
/// <param name="DisplayName">What the user typed in the AppHost, and the key <c>aspire logs</c> and
/// <c>aspire resource</c> both use. Not guaranteed unique — see <see cref="ResourceStore"/>.</param>
public sealed record AspireResource(
    string Name,
    string DisplayName,
    string ResourceType,
    string State,
    string? HealthStatus,
    DateTimeOffset? StartTimestamp,
    IReadOnlyDictionary<string, AspireCommand>? Commands,
    IReadOnlyList<AspireUrl>? Urls = null);

/// <summary>An endpoint the AppHost publishes for a resource.</summary>
/// <param name="IsInternal">Set for endpoints meant for tooling rather than people, such as an emulator's
/// health probe. Those are listed but never the one a bare "open" picks.</param>
public sealed record AspireUrl(
    string Url,
    string? Name = null,
    string? DisplayName = null,
    bool IsInternal = false);

/// <summary>A command offered on a resource. The command's own name is the dictionary key.</summary>
public sealed record AspireCommand(
    string DisplayName,
    string? Description,
    string State,
    int SortOrder,
    IReadOnlyList<AspireCommandInput>? ArgumentInputs);

/// <summary>Only the name is modelled; the TUI refuses argument-taking commands rather than filling them.</summary>
public sealed record AspireCommandInput(string Name);

/// <summary>One line from <c>aspire logs</c>. <paramref name="ResourceName"/> is a
/// <see cref="AspireResource.DisplayName"/>, never a <see cref="AspireResource.Name"/>.</summary>
public sealed record LogLine(
    string ResourceName,
    DateTimeOffset Timestamp,
    string Content,
    bool IsError);

/// <summary>One running AppHost as reported by <c>aspire ps</c>.</summary>
public sealed record AppHost(
    string AppHostPath,
    int AppHostPid,
    string Status,
    string? DashboardUrl);

/// <summary>One AppHost project found in the workspace by <c>aspire ls</c>; it need not be running.</summary>
public sealed record AppHostCandidate(string Path, string? Status = null);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AspireResource))]
[JsonSerializable(typeof(AppHostCandidate[]))]
[JsonSerializable(typeof(LogLine))]
[JsonSerializable(typeof(AppHost[]))]
[JsonSerializable(typeof(DescribeSnapshot))]
[JsonSerializable(typeof(LogDocument))]
internal sealed partial class AspireJsonContext : JsonSerializerContext;

/// <summary>`aspire describe` without --follow wraps its resources; with --follow it emits them bare.</summary>
internal sealed record DescribeSnapshot(IReadOnlyList<AspireResource> Resources);

/// <summary>`aspire logs` does the same: NDJSON with --follow, a wrapped pretty-printed document without.</summary>
internal sealed record LogDocument(IReadOnlyList<LogLine> Logs);

/// <summary>Parses the NDJSON the aspire CLI emits. Source-generated throughout so the TUI stays AOT-clean.</summary>
public static class AspireJson
{
    /// <summary>One line of <c>aspire describe --follow</c>. Null when the line is blank or malformed.</summary>
    public static AspireResource? ParseResource(string line) =>
        Normalise(TryParse(line, static l => JsonSerializer.Deserialize(l, AspireJsonContext.Default.AspireResource)));

    /// <summary>One line of <c>aspire logs --follow</c>.</summary>
    public static LogLine? ParseLogLine(string line) =>
        Normalise(TryParse(line, static l => JsonSerializer.Deserialize(l, AspireJsonContext.Default.LogLine)));

    /// <summary>
    /// The records declare these strings non-nullable, but the deserialiser fills in null for any field the
    /// CLI omits — and it does omit them: a resource caught mid-restart arrives with no <c>state</c>, which
    /// crashed everything downstream that reasonably assumed a string. Anything unusable is dropped, the
    /// rest is filled in, so no caller has to null-check.
    /// </summary>
    private static AspireResource? Normalise(AspireResource? resource)
    {
        if (resource is null || string.IsNullOrEmpty(resource.Name))
        {
            return null;
        }

        return resource with
        {
            DisplayName = string.IsNullOrEmpty(resource.DisplayName) ? resource.Name : resource.DisplayName,
            // Not redundant, however non-nullable the record declares these: the deserialiser fills in
            // null for any field the CLI omits, and a resource caught mid-restart has no `state`.
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            ResourceType = resource.ResourceType ?? "Unknown",
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            State = resource.State ?? "",
        };
    }

    private static LogLine? Normalise(LogLine? line) =>
        line is null || string.IsNullOrEmpty(line.ResourceName)
            ? null
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            : line with { Content = line.Content ?? "" };

    /// <summary>The whole document from <c>aspire ps --format Json</c>.</summary>
    public static IReadOnlyList<AppHost> ParseAppHosts(string json) =>
    [
        .. (TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.AppHostArray)) ?? [])
            .Where(static h => !string.IsNullOrEmpty(h.AppHostPath))
            .Select(static h => h with { Status = h.Status }),
    ];

    /// <summary>The whole document from <c>aspire ls --format Json</c>.</summary>
    public static IReadOnlyList<AppHostCandidate> ParseCandidates(string json) =>
    [
        .. (TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.AppHostCandidateArray)) ?? [])
            .Where(static c => !string.IsNullOrEmpty(c.Path)),
    ];

    /// <summary>The whole document from <c>aspire logs --format Json</c> without --follow.</summary>
    public static IReadOnlyList<LogLine> ParseLogDocument(string json) =>
    [
        .. (TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.LogDocument))?.Logs
            ?? []).Select(Normalise).OfType<LogLine>(),
    ];

    /// <summary>The whole document from <c>aspire describe --format Json</c> without --follow.</summary>
    public static IReadOnlyList<AspireResource> ParseSnapshot(string json) =>
    [
        .. (TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.DescribeSnapshot))?.Resources
            ?? []).Select(Normalise).OfType<AspireResource>(),
    ];

    // A half-written line on a stream that is still being appended to is expected, not exceptional.
    private static T? TryParse<T>(string text, Func<string, T?> parse)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }

        try
        {
            return parse(text);
        }
        catch (JsonException)
        {
            return default;
        }
    }
}
