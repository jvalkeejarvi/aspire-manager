var builder = DistributedApplication.CreateBuilder(args);

// Executables rather than containers so the fixture runs without Docker; both expose restart/stop,
// which is the command set the TUI drives.
builder.AddExecutable("ticker", "bash", ".", "-c", "while true; do date; sleep 2; done");

// A custom command stands in for the destructive ones integrations inject (delete-azure-resources and
// friends): not in the TUI's allowlist, so it must ask y/n first. Does nothing on purpose.
builder.AddExecutable("noisy", "bash", ".", "-c", "while true; do echo working; sleep 1; done")
    .WithCommand(
        "wipe-everything",
        "Wipe everything",
        _ => Task.FromResult(CommandResults.Success("nothing was wiped, this is a fixture")));

// The TUI itself in an Aspire-hosted terminal, attached to this AppHost: the end-to-end tests drive it with
// tapes through `aspire terminal tape play`. Only they ask for it, so a manual run keeps its two resources
// and the TUI inside never writes this AppHost into the user's recents. WithTerminal is experimental in 13.6.
if (Environment.GetEnvironmentVariable("ASPIRE_MANAGER_END_TO_END") == "1")
{
#pragma warning disable ASPIRETERMINAL001
    builder.AddProject<Projects.AspireManager_Tui>("manager")
        .WithArgs(builder.AppHostDirectory)
        .WithTerminal(o => { o.Columns = 120; o.Rows = 30; });
#pragma warning restore ASPIRETERMINAL001
}

builder.Build().Run();
