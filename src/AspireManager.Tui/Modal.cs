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
    /// <summary>Returns the chosen AppHost path, or null if the user backed out.</summary>
    public static string? PickAppHost(IApplication app, IReadOnlyList<AppHost> candidates)
    {
        string? chosen = null;

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

        ListOverlay overlay = ListOverlay.Build(
            "Select AppHost",
            [.. candidates.Select(AppHostSelection.Label)],
            " j/k move   enter select   esc/^g quit",
            0,
            index =>
            {
                chosen = candidates[index].AppHostPath;
                app.RequestStop(window);
            },
            () => app.RequestStop(window));

        window.Add(overlay.Frame);

        void OnKey(object? sender, Key key) => overlay.HandleKey(key);

        app.Keyboard.KeyDown += OnKey;
        try
        {
            overlay.List.SetFocus();
            app.Run(window);
        }
        finally
        {
            app.Keyboard.KeyDown -= OnKey;
            window.Dispose();
        }

        return chosen;
    }
}
