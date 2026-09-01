using System.Text.Json;
using System.Text.Json.Serialization;

namespace AspireManager.Core;

/// <summary>
/// The editor command templates. Two, because opening at a line and opening without one need different
/// arguments in every editor that supports both.
/// </summary>
public sealed record EditorSettings(string? Command = null, string? CommandNoLine = null);

/// <summary><c>~/.aspire-manager.json</c>.</summary>
public sealed record AspireManagerConfig(EditorSettings? Editor = null, GroupSettings? Groups = null);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,

    // It is a hand-edited file: neither a comment nor a trailing comma should be a parse error.
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AspireManagerConfig))]
[JsonSerializable(typeof(RecentAppHost[]))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigFile
{
    /// <summary>
    /// Where this tool keeps everything: <c>~/Library/Application Support/aspire-manager</c> on macOS,
    /// <c>~/.config/aspire-manager</c> on Linux, <c>%APPDATA%\aspire-manager</c> on Windows. One call
    /// resolves all three, which is why the state is not scattered across hand-rolled per-OS paths.
    /// </summary>
    public static string Directory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "aspire-manager");

    public static string DefaultPath => Path.Combine(Directory, "config.json");

    /// <summary>Beside the config, but written by the tool rather than by hand.</summary>
    public static string RecentsPath => Path.Combine(Directory, "recents.json");

    /// <summary>
    /// Reads the config. A missing file is not an error — it simply means nothing is configured — but a
    /// malformed one is, and the message says where, because the editor keys are required for `e` to work.
    /// </summary>
    public static (AspireManagerConfig? Config, string? Error) Load(string path)
    {
        if (!File.Exists(path))
        {
            return (null, null);
        }

        try
        {
            return (Parse(File.ReadAllText(path)), null);
        }
        catch (JsonException e)
        {
            return (null, $"{Path.GetFileName(path)}: {e.Message}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return (null, $"could not read {path}: {e.Message}");
        }
    }

    public static AspireManagerConfig? Parse(string json) =>
        JsonSerializer.Deserialize(json, ConfigJsonContext.Default.AspireManagerConfig);
}
