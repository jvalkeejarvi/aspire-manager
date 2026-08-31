using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AspireManager.Tui;

/// <summary>
/// Puts the terminal into, and back out of, the state a full-screen application needs. Terminal.Gui sets
/// this up at Init and exposes no way to redo it, which suspending and resuming both require.
/// </summary>
internal static class TerminalState
{
    // Captured from what the driver itself emits at startup: alternate screen, cleared and homed, cursor
    // hidden, mouse reporting (1003/1015/1006), bracketed paste, application cursor keys and keypad, and
    // the kitty keyboard protocol pushed with flags 31.
    private const string Setup = "\u001b[?1049h\u001b[2J\u001b[1;1H\u001b[?25l\u001b[?1003h\u001b[?1015h\u001b[?1006h\u001b[?2004h\u001b[?1h\u001b=\u001b[>31u";

    // The reverse. The kitty pop (CSI < u) comes first and matters most: left pushed, the terminal keeps
    // reporting keys to the shell in CSI-u form, which arrives at the prompt as text like "5u" and "1:3u".
    // Terminal.Gui also leaves application cursor-key and keypad modes on, which the shell would inherit.
    private const string Teardown = "\u001b[<u\u001b[?2004l\u001b[?1003l\u001b[?1015l\u001b[?1006l\u001b[0m\u001b[?1049l\u001b[?25h\u001b[0 q\u001b[?1l\u001b>";

    /// <summary>SIGSTOP, which cannot be caught or ignored - unlike SIGTSTP, which something here swallows.</summary>
    private static int StopSignal => OperatingSystem.IsMacOS() ? 17 : 19;

    // DllImport rather than LibraryImport: the signature is two ints, so there is nothing to marshal and
    // no need to turn on unsafe blocks for a source-generated stub.
    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int sig);

    public static void Leave() => Write(Teardown);

    public static void Enter()
    {
        Write(Setup);

        // The shell restores its own line discipline when it takes the terminal back, so raw mode has to be
        // re-applied on resume. stty is the only handle on it without the driver's internals.
        Raw();
    }

    /// <summary>
    /// Hands the terminal back, stops this process, and puts everything back when it is resumed. Returns
    /// once the process is in the foreground again - the stop happens inline, so no SIGCONT handler is
    /// needed and the resume cannot race the redraw.
    /// </summary>
    public static void Suspend()
    {
        Leave();
        kill(Environment.ProcessId, StopSignal);

        // Execution continues here on SIGCONT.
        Enter();
    }

    private static void Write(string sequence)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.Out.Write(sequence);
        Console.Out.Flush();
    }

    private static void Raw()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        try
        {
            using Process? stty = Process.Start(new ProcessStartInfo("/bin/stty")
            {
                ArgumentList = { "raw", "-echo" },
                UseShellExecute = false,
            });

            stty?.WaitForExit(2000);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Without stty the terminal stays cooked on resume; the redraw still happens.
        }
    }
}
