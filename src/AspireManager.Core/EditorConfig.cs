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
internal sealed partial class ConfigJsonContext : JsonSerializerContext;

public static class ConfigFile
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aspire-manager.json");

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
