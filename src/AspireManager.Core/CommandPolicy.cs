namespace AspireManager.Core;

/// <summary>What the TUI may do with a command on a keypress.</summary>
public enum CommandAvailability
{
    /// <summary>Not offered: the AppHost disabled it, or it needs arguments the TUI does not collect.</summary>
    Unavailable,

    /// <summary>Runs on a single keypress.</summary>
    Instant,

    /// <summary>Runs only after the user types the resource name back.</summary>
    NeedsConfirmation,
}

/// <summary>
/// Decides which commands fire on one keypress. Nothing in the CLI metadata marks a command
/// destructive — <c>delete-azure-resources</c> and <c>restart</c> are shaped identically — so this is
/// an allowlist rather than a denylist: anything a future Aspire version or integration introduces is
/// confirmed by default instead of firing unguarded.
/// </summary>
public static class CommandPolicy
{
    private static readonly HashSet<string> Instant = new(StringComparer.OrdinalIgnoreCase)
    {
        "start",
        "stop",
        "restart",
        "rebuild",
    };

    public static CommandAvailability Classify(string commandName, AspireCommand command)
    {
        if (!string.Equals(command.State, "Enabled", StringComparison.OrdinalIgnoreCase))
        {
            return CommandAvailability.Unavailable;
        }

        if (command.ArgumentInputs is { Count: > 0 })
        {
            return CommandAvailability.Unavailable;
        }

        return Instant.Contains(commandName)
            ? CommandAvailability.Instant
            : CommandAvailability.NeedsConfirmation;
    }
}
