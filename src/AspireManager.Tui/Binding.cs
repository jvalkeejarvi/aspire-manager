using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace AspireManager.Tui;

/// <summary>Which panes a binding belongs to. <see cref="All"/> is what "global" means here.</summary>
[Flags]
internal enum Panes
{
    AppHost = 1,
    Resources = 2,
    Logs = 4,
    All = AppHost | Resources | Logs,
}

/// <summary>
/// One key, its description, where it applies and what it does — in a single place, so the `?` list and the
/// dispatcher cannot disagree. Adding a key without a description is not possible.
/// </summary>
/// <param name="Available">Extra condition beyond the pane: a resource being selected, a search running.
/// A binding that is unavailable neither fires nor appears in the help.</param>
internal sealed record Binding(
    string Label,
    string Description,
    Panes Where,
    Func<Key, bool> Matches,
    Action Run,
    Func<bool>? Available = null)
{
    public bool IsGlobal => Where == Panes.All;

    public bool AppliesTo(Panes pane) => Where.HasFlag(pane) && Available?.Invoke() != false;

    /// <summary>Matches a printable character exactly, so 'o' and 'O' can differ.</summary>
    public static Func<Key, bool> Char(char expected) =>
        key => (char)key.AsRune.Value == expected;

    public static Func<Key, bool> AnyChar(params char[] expected) =>
        key => Array.IndexOf(expected, (char)key.AsRune.Value) >= 0;

    public static Func<Key, bool> Ctrl(KeyCode letter) =>
        key => key.IsCtrl && (key.KeyCode & ~KeyCode.CtrlMask) == letter;

    public static Func<Key, bool> Exactly(Key expected) => key => key == expected;
}
