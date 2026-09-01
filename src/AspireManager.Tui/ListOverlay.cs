using System.Collections.ObjectModel;
using AspireManager.Core;
using Terminal.Gui.Drawing;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AspireManager.Tui;

/// <summary>
/// The one pick-from-a-list dialog: the command palette, the AppHost switcher, the URL picker, the help
/// list and the startup picker are all this. They were four copies until a padding change had to be made
/// four times. `/` narrows the list in place; the accepted index is mapped back to the unfiltered row.
/// </summary>
internal sealed class ListOverlay
{
    private readonly IReadOnlyList<string> _all;
    private readonly Label _hint;
    private readonly string _help;
    private readonly Action<int> _accept;
    private readonly Action _cancel;

    private List<int> _visible;
    private string _filter = "";
    private bool _filtering;

    private ListOverlay(
        FrameView frame,
        ListView list,
        Label hint,
        IReadOnlyList<string> all,
        string help,
        Action<int> accept,
        Action cancel)
    {
        Frame = frame;
        List = list;
        _hint = hint;
        _all = all;
        _help = help;
        _accept = accept;
        _cancel = cancel;
        _visible = [.. Enumerable.Range(0, all.Count)];
    }

    public FrameView Frame { get; }

    /// <summary>Replaces the help line, for progress and errors that belong to this dialog.</summary>
    public void ShowMessage(string text) => _hint.Text = text;

    public ListView List { get; }

    public static ListOverlay Build(
        string title,
        IReadOnlyList<string> rows,
        string help,
        int selected,
        Action<int> accept,
        Action cancel,
        int maxHeight = 20)
    {
        ListView list = new() { X = 2, Y = 1, Width = Dim.Fill(2), Height = Dim.Fill(2) };
        list.SetSource(new ObservableCollection<string>(rows));

        Label hint = new() { X = 2, Y = Pos.AnchorEnd(1), Width = Dim.Fill(2), Text = help };

        int widest = Math.Max(rows.Count == 0 ? 0 : rows.Max(static r => r.Length), help.Length);

        FrameView frame = new()
        {
            Title = title,
            X = Pos.Center(),
            Y = Pos.Center(),

            // Sized to content rather than a percentage of the screen, plus room for the border and the
            // blank line above the list and above the help.
            Width = Math.Clamp(widest + 8, 38, 104),
            Height = Math.Min(rows.Count + 5, Math.Max(8, maxHeight)),
        };
        frame.Add(list, hint);

        // Dialogs highlight the same way the panes do rather than falling back to the theme's inverse.
        TuiAttribute normal = frame.GetAttributeForRole(VisualRole.Normal);
        list.SetScheme(Palette.WithSelection(normal));

        // The frame too: a FrameView draws its title with the Focus role while it holds focus, which would
        // otherwise invert to a solid white title bar.
        frame.SetScheme(new Scheme { Normal = normal, Focus = normal });

        ListOverlay overlay = new(frame, list, hint, rows, help, accept, cancel);

        // SelectedItem does not stick before layout, so it is set once the views are assembled, and the
        // viewport is scrolled to it explicitly — otherwise a list taller than the dialog opens part-way
        // down and the first rows are simply not there.
        list.SelectedItem = rows.Count == 0 ? null : Math.Clamp(selected, 0, rows.Count - 1);
        return overlay;
    }

    /// <summary>
    /// Scrolls to the selection. Must run after the dialog is on screen: before layout the list has no
    /// viewport to scroll, and a list taller than the dialog then opens part-way down with its first rows
    /// simply missing.
    /// </summary>
    public void ScrollToSelection()
    {
        int wanted = List.SelectedItem ?? 0;

        // Walk to the end and back: a freshly built list opens scrolled to the bottom, and asking it to
        // show an item already inside its (stale) viewport does nothing.
        List.MoveEnd();
        List.MoveHome();

        if (wanted > 0)
        {
            List.SelectedItem = wanted;
            List.EnsureSelectedItemVisible();
        }
    }

    /// <summary>Returns true when the key belonged to this dialog.</summary>
    public bool HandleKey(Key key)
    {
        if (ListKeys.IsCancel(key))
        {
            key.Handled = true;

            // Esc backs out of the filter first, and only then out of the dialog.
            if (_filtering)
            {
                _filtering = false;
                _filter = "";
                Apply();
            }
            else
            {
                _cancel();
            }

            return true;
        }

        if (key == Key.Enter)
        {
            key.Handled = true;
            Accept();
            return true;
        }

        if (_filtering)
        {
            return HandleFilterKey(key);
        }

        if ((char)key.AsRune.Value == '/')
        {
            key.Handled = true;
            _filtering = true;
            Apply();
            return true;
        }

        return ListKeys.VimMove(List, key) || ListKeys.PageMove(List, key);
    }

    private bool HandleFilterKey(Key key)
    {
        if (key.KeyCode == KeyCode.Backspace)
        {
            key.Handled = true;
            _filter = _filter.Length > 0 ? _filter[..^1] : "";
            Apply();
            return true;
        }

        // Arrows still navigate while typing; j and k are letters here.
        if (key.AsRune.Value is < 32 or > 126)
        {
            return false;
        }

        key.Handled = true;
        _filter += (char)key.AsRune.Value;
        Apply();
        return true;
    }

    private void Accept()
    {
        if (_visible.Count == 0)
        {
            return;
        }

        int index = List.SelectedItem is { } selected and >= 0 && selected < _visible.Count
            ? selected
            : 0;

        _accept(_visible[index]);
    }

    private void Apply()
    {
        _visible = _filter.Length == 0
            ? [.. Enumerable.Range(0, _all.Count)]

            // Reuses the log search's matcher: same case-insensitive "contains", already tested.
            : [.. LogSearch.MatchingLines(_all, _filter)];

        List.SetSource(new ObservableCollection<string>(_visible.Select(i => _all[i])));
        List.SelectedItem = _visible.Count == 0 ? null : 0;
        List.EnsureSelectedItemVisible();

        _hint.Text = _filtering
            ? $" /{_filter}   esc/^g clear   enter select"
            : _help;
    }
}
