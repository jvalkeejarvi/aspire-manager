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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(AspireResource))]
[JsonSerializable(typeof(LogLine))]
[JsonSerializable(typeof(AppHost[]))]
[JsonSerializable(typeof(DescribeSnapshot))]
internal sealed partial class AspireJsonContext : JsonSerializerContext;

/// <summary>`aspire describe` without --follow wraps its resources; with --follow it emits them bare.</summary>
internal sealed record DescribeSnapshot(IReadOnlyList<AspireResource> Resources);

/// <summary>Parses the NDJSON the aspire CLI emits. Source-generated throughout so the TUI stays AOT-clean.</summary>
public static class AspireJson
{
    /// <summary>One line of <c>aspire describe --follow</c>. Null when the line is blank or malformed.</summary>
    public static AspireResource? ParseResource(string line) =>
        TryParse(line, static l => JsonSerializer.Deserialize(l, AspireJsonContext.Default.AspireResource));

    /// <summary>One line of <c>aspire logs --follow</c>.</summary>
    public static LogLine? ParseLogLine(string line) =>
        TryParse(line, static l => JsonSerializer.Deserialize(l, AspireJsonContext.Default.LogLine));

    /// <summary>The whole document from <c>aspire ps --format Json</c>.</summary>
    public static IReadOnlyList<AppHost> ParseAppHosts(string json) =>
        TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.AppHostArray)) ?? [];

    /// <summary>The whole document from <c>aspire describe --format Json</c> without --follow.</summary>
    public static IReadOnlyList<AspireResource> ParseSnapshot(string json) =>
        TryParse(json, static j => JsonSerializer.Deserialize(j, AspireJsonContext.Default.DescribeSnapshot))?.Resources ?? [];

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
