var builder = DistributedApplication.CreateBuilder(args);

// Executables rather than containers so the fixture runs without Docker; both expose restart/stop,
// which is the command set the TUI drives.
builder.AddExecutable("ticker", "bash", ".", "-c", "while true; do date; sleep 2; done");

// A custom command stands in for the destructive ones integrations inject (delete-azure-resources and
// friends): not in the TUI's allowlist, so it must demand typed confirmation. Does nothing on purpose.
builder.AddExecutable("noisy", "bash", ".", "-c", "while true; do echo working; sleep 1; done")
    .WithCommand(
        "wipe-everything",
        "Wipe everything",
        _ => Task.FromResult(CommandResults.Success("nothing was wiped, this is a fixture")));

builder.Build().Run();
