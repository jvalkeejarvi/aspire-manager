using AspireManager.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AspireManager.Tui;

/// <summary>
/// The startup AppHost picker. It runs as its own toplevel because the main window does not exist yet —
/// the only place a nested <c>app.Run</c> unwinds reliably. The dialog itself is the shared
/// <see cref="ListOverlay"/>, so it looks and behaves like the in-session ones.
/// </summary>
internal static class Modal
{
    /// <summary>
    /// Returns the AppHost to attach to, or null if the user backed out. An option that is not running is
    /// started first, by <paramref name="start"/>, which returns a message if it could not be: the dialog
    /// then stays open with that message rather than dropping the user back at a bare shell.
    /// </summary>
    public static string? PickAppHost(
        IApplication app,
        IReadOnlyList<AppHostOption> options,
        Func<string, string?> start)
    {
        string? chosen = null;

        // No window is laid out yet, and Terminal.Gui has no size of its own until its run loop starts:
        // both `app.Screen` and `Driver.Cols` read zero here, which sizes the dialog to its 38-column
        // floor. The terminal itself is the only source that answers before then.
        int overlayWidth = Math.Max(38, TerminalWidth() - 2);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Borderless and full screen: the ListOverlay frame inside carries the border, so a second one
        // around it would just be a box in a box.
        Window window = new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            BorderStyle = LineStyle.None,
        };

        ListOverlay? overlay = null;
        overlay = ListOverlay.Build(
            "Select AppHost",
            AppHostOptions.Rows(options, null, home, ListOverlay.RowWidth(overlayWidth)),
            " j/k move   enter attach or start   esc/^g quit",
            0,
            index =>
            {
                AppHostOption option = options[index];

                if (!option.Running)
                {
                    // Drawn by hand: `start` blocks for as long as the CLI takes to build, and the run
                    // loop cannot repaint while it does.
                    overlay!.ShowMessage($" starting {option.Name} …");
                    app.LayoutAndDraw(true);

                    if (start(option.Path) is { } error)
                    {
                        overlay.ShowMessage($" {error}");
                        app.LayoutAndDraw(true);
                        return;
                    }
                }

                chosen = option.Path;
                app.RequestStop(window);
            },
            () => app.RequestStop(window),
            maxWidth: overlayWidth);

        window.Add(overlay.Frame);

        void OnKey(object? sender, Key key) => overlay.HandleKey(key);

        app.Keyboard.KeyDown += OnKey;
        try
        {
            overlay.List.SetFocus();
            overlay.ScrollToSelection();
            app.Run(window);
        }
        finally
        {
            app.Keyboard.KeyDown -= OnKey;
            window.Dispose();
        }

        return chosen;
    }

    /// <summary>Falls back to the classic 80 columns when stdout is not a terminal, as it is under a pipe.</summary>
    private static int TerminalWidth()
    {
        try
        {
            return Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        }
        catch (Exception e) when (e is IOException or PlatformNotSupportedException)
        {
            return 80;
        }
    }
}
