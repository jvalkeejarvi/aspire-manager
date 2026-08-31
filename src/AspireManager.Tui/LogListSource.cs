using System.Collections;
using System.Collections.Specialized;
using AspireManager.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Views;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace AspireManager.Tui;

/// <summary>
/// Draws log lines with search matches highlighted. A string source cannot do this — highlighting is
/// per-substring, and ListView offers at most one attribute for a whole row.
/// </summary>
internal sealed class LogListSource(IApplication app) : IListDataSource
{
    private static readonly Color HighlightForeground = Color.Parse("Black");
    private static readonly Color HighlightBackground = Color.Parse("Yellow");

    private IReadOnlyList<string> _lines = [];
    private string _query = "";

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool SuspendCollectionChangedEvent { get; set; }

    public int Count => _lines.Count;

    public int MaxItemLength => _lines.Count == 0 ? 0 : _lines.Max(static l => l.Length);

    public void Update(IReadOnlyList<string> lines, string query)
    {
        _lines = lines;
        _query = query;

        if (!SuspendCollectionChangedEvent)
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public void Dispose()
    {
        // Nothing owned; the lines come from Core and outlive the source.
    }

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value)
    {
        // Marking is not part of this list.
    }

    public IList ToList() => _lines.ToList();

    public void Render(
        ListView listView,
        bool selected,
        int item,
        int col,
        int row,
        int width,
        int viewportX = 0)
    {
        if (app.Driver is not { } driver || item < 0 || item >= _lines.Count)
        {
            return;
        }

        listView.Move(col, row);

        string line = _lines[item];
        // Only paint the selection while this pane has focus. The log selection is a cursor for navigating,
        // not a statement about what is being shown, so highlighting it from the resource pane is noise.
        TuiAttribute baseline = listView.GetAttributeForRole(
            selected && listView.HasFocus ? VisualRole.Focus : VisualRole.Normal);
        TuiAttribute highlight = new(HighlightForeground, HighlightBackground);
        int used = 0;

        void Write(string text, bool matched)
        {
            if (text.Length == 0 || used >= width)
            {
                return;
            }

            string clipped = text.Length > width - used ? text[..(width - used)] : text;
            driver.CurrentAttribute = matched ? highlight : baseline;
            listView.AddStr(clipped);
            used += clipped.Length;
        }

        int at = 0;
        foreach ((int start, int length) in LogSearch.Matches(line, _query))
        {
            Write(line[at..start], matched: false);
            Write(line.Substring(start, length), matched: true);
            at = start + length;
        }

        Write(line[at..], matched: false);

        // Pad to the full width, or the selection highlight stops at the end of the text.
        driver.CurrentAttribute = baseline;
        if (used < width)
        {
            listView.AddStr(new string(' ', width - used));
        }
    }
}
