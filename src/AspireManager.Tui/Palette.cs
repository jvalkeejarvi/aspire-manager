using Terminal.Gui.Drawing;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace AspireManager.Tui;

internal static class Palette
{
    /// <summary>
    /// The selected row's background, as lazygit marks its selection. Named rather than an RGB literal so
    /// the terminal's own palette decides the shade; only the background changes, so each row keeps its
    /// foreground and a green state letter stays green on the highlight.
    /// </summary>
    public static readonly Color SelectionBackground = Color.Parse("Blue");

    /// <summary>
    /// A scheme whose Focus role is the selection. Built explicitly because a Scheme derived from a single
    /// attribute infers Focus by swapping fore- and background, which is where the white highlight came from.
    /// </summary>
    public static Scheme WithSelection(TuiAttribute normal) =>
        new()
        {
            Normal = normal,
            Focus = new TuiAttribute(normal.Foreground, SelectionBackground),
        };
}
