using System.Collections;
using System.Collections.Specialized;
using AspireManager.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AspireManager.Tui;

/// <summary>
/// Draws the resource pane a segment at a time. A plain string source cannot do this: ListView's
/// RowRender hook offers one Attribute for the whole row, which paints the name with the state's colour
/// and overwrites the selection highlight.
/// </summary>
internal sealed class ResourceListSource(IApplication app) : IListDataSource
{
    // Parsed once at startup rather than per row: an unparseable colour name would otherwise throw
    // mid-draw and take the application down, which is how BrightWhite was found not to exist.
    private static readonly Color Healthy = Color.Parse("BrightGreen");
    private static readonly Color Warning = Color.Parse("BrightYellow");
    private static readonly Color Failed = Color.Parse("BrightRed");
    private static readonly Color Inactive = Color.Parse("DarkGray");

    private IReadOnlyList<ResourceRow> _rows = [];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    public bool SuspendCollectionChangedEvent { get; set; }

    public int Count => _rows.Count;

    public int MaxItemLength => _rows.Count == 0 ? 0 : _rows.Max(static r => ShellModel.RowText(r).Length);

    public void Update(IReadOnlyList<ResourceRow> rows)
    {
        _rows = rows;
        if (!SuspendCollectionChangedEvent)
        {
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }

    public void Dispose()
    {
        // Nothing owned; the rows come from Core and outlive the source.
    }

    public bool IsMarked(int item) => false;

    public void SetMark(int item, bool value)
    {
        // Marking is not part of this list.
    }

    public IList ToList() => _rows.Select(ShellModel.RowText).ToList();

    public void Render(
        ListView listView,
        bool selected,
        int item,
        int col,
        int row,
        int width,
        int viewportX = 0)
    {
        if (app.Driver is not { } driver || item < 0 || item >= _rows.Count)
        {
            return;
        }

        listView.Move(col, row);

        // Selected rows keep the focus background so the highlight survives; only the foreground varies.
        TuiAttribute baseline = listView.GetAttributeForRole(selected ? VisualRole.Focus : VisualRole.Normal);
        int used = 0;

        void Write(string text, Color? colour = null, TextStyle style = TextStyle.None)
        {
            if (text.Length == 0 || used >= width)
            {
                return;
            }

            string clipped = text.Length > width - used ? text[..(width - used)] : text;
            driver.CurrentAttribute = colour is null
                ? baseline with { Style = style }
                : new TuiAttribute(colour.Value, baseline.Background, style);

            listView.AddStr(clipped);
            used += clipped.Length;
        }

        switch (_rows[item])
        {
            case TypeHeader header:
                // Bold and the fold marker are enough; colour is reserved for status.
                Write($"{(header.Collapsed ? '▸' : '▾')} {header.ResourceType} ({header.Count})",
                    style: TextStyle.Bold);
                break;

            case ResourceItem entry:
                AspireResource resource = entry.Resource;
                Write(entry.Indented ? "    " : " ");
                Write(ShellModel.StateMark(resource), StatusColour(ShellModel.Tone(entry)));
                Write(" ");
                Write(ShellModel.HealthMark(resource), StatusColour(ShellModel.HealthTone(resource)));
                Write(" ");
                Write(resource.DisplayName);
                break;
        }

        // Pad to the full width, or the selection highlight stops at the end of the text.
        driver.CurrentAttribute = baseline;
        if (used < width)
        {
            listView.AddStr(new string(' ', width - used));
        }
    }

    private static Color? StatusColour(RowTone tone) => tone switch
    {
        RowTone.Healthy => Healthy,
        RowTone.Warning => Warning,
        RowTone.Failed => Failed,
        RowTone.Inactive => Inactive,
        _ => null,
    };
}
