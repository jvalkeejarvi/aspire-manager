using System.Runtime.InteropServices;
using AspireManager.Core;
using AspireManager.Tui;
using Terminal.Gui.App;

using CancellationTokenSource cts = new();

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
    cts.Cancel();
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

ResourceStore resources = new();
LogStore logs = new();

ManagerWindow? window = null;
AppHostSession Create(string path) =>
    new(path, resources, logs, (state, retryIn) => window?.SetConnection(state, retryIn));

AppHostSession session = Create(appHostPath);
window = new ManagerWindow(app, session, Create, resources, logs, config?.Editor);
session.Start();

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
