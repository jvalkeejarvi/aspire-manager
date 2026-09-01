using System.Runtime.InteropServices;
using AspireManager.Core;
using AspireManager.Tui;
using Terminal.Gui.App;

using CancellationTokenSource cts = new();

// Set once the UI exists. Cancelling the token is not enough to stop Terminal.Gui: its loop blocks reading
// stdin, so it notices nothing until a key arrives — on a signal, with nobody typing, the process would
// simply keep running. Tear down here instead and exit.
Action? shutdown = null;

// Not just Ctrl-C: on SIGTERM or a closed terminal the `finally` blocks in AspireCli must still run, or
// the `aspire --follow` children outlive us and keep streaming from the AppHost forever.
using PosixSignalRegistration sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Handle);

// SIGHUP does not exist on Windows and registering it throws, which would kill the app at startup.
using PosixSignalRegistration? sighup = OperatingSystem.IsWindows()
    ? null
    : PosixSignalRegistration.Create(PosixSignal.SIGHUP, Handle);

void Handle(PosixSignalContext context)
{
    context.Cancel = true;

    // ReSharper disable once AccessToDisposedClosure
    // Safe by disposal order, which the inspection cannot see: `using` declarations dispose in reverse, so
    // both signal registrations are unregistered before `cts` is. Once the source is disposed there is
    // nothing left that could invoke this handler.
    cts.Cancel();
    shutdown?.Invoke();
}

string? requested = args.FirstOrDefault(a => !a.StartsWith('-'));
IReadOnlyList<AppHost> hosts = requested is null
    ? await new AspireCli(".").ListAppHostsAsync(cts.Token)
    : [];

AppHostChoice choice = AppHostSelection.Select(hosts, requested);
if (choice is NoAppHost)
{
    Console.Error.WriteLine("no running AppHosts; start one with `aspire run`");
    return 1;
}

IApplication app = Application.Create();
app.Init();

// Emit the 16 ANSI colours rather than truecolor, so every colour comes from the terminal's own palette —
// the same choice lazygit makes (its defaults are named colours: green, blue, default). Without this the
// app paints fixed RGB values and ignores the theme the user actually configured.
if (app.Driver is { } driver)
{
    driver.Force16Colors = true;
}

// Terminal.Gui exits leaving application cursor-key mode (ESC[?1h) and application keypad mode (ESC=)
// switched on, and never restores the cursor style. The shell inherits all three, so the prompt comes
// back with the wrong cursor and arrow keys can misbehave. Put them back the way we found them.
void RestoreTerminal() => TerminalState.Leave();

string? appHostPath = choice switch
{
    UseAppHost use => use.Path,
    ChooseAppHost pick => Modal.PickAppHost(app, pick.Candidates),
    _ => null,
};

if (appHostPath is null)
{
    app.Dispose();
    RestoreTerminal();
    return 1;
}

(AspireManagerConfig? config, string? configError) = ConfigFile.Load(ConfigFile.DefaultPath);
(GroupPolicy groups, string? groupsWarning) = GroupPolicy.From(config?.Groups);
configError ??= groupsWarning;

ResourceStore resources = new();
LogStore logs = new();

ManagerWindow? window = null;

// The window outlives every session that reports to it: the finally below stops the current session and
// waits for it before `app.Dispose()` disposes the window, so a connection callback cannot arrive after
// that. The null-conditional covers the opposite end, the moment before `window` is assigned below.
AppHostSession Create(string path) =>
    // ReSharper disable once AccessToModifiedClosure
    new(path, resources, logs, (state, retryIn) => window?.SetConnection(state, retryIn));

AppHostSession session = Create(appHostPath);
window = new ManagerWindow(app, session, Create, resources, logs, config?.Editor, groups);
session.Start();

shutdown = () =>
{
    // Bounded and best effort: what matters is that the `aspire --follow` children are killed and the
    // terminal handed back. Then exit directly — the run loop this would otherwise unwind through is
    // blocked on input and will not return on its own.
    try
    {
        window?.CurrentSession.StopAsync().Wait(TimeSpan.FromSeconds(3));
    }
    catch (Exception e) when (e is AggregateException or OperationCanceledException)
    {
        // Shutting down anyway.
    }

    TerminalState.Leave();
    Environment.Exit(0);
};

if (configError is not null)
{
    window.SetStatus(configError);
}

try
{
    await app.RunAsync(window, cts.Token);
}
finally
{
    await cts.CancelAsync();

    // The window may have switched AppHost, so stop whichever session it is holding now.
    await window.CurrentSession.StopAsync();

    app.Dispose();
    RestoreTerminal();
}

return 0;
