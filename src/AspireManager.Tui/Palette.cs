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
    /// Ordinary text: palette slot 7 rather than the terminal's "default foreground", which most themes
    /// map to bright white and which competes with the colours that actually carry meaning. Still a
    /// palette colour, so the terminal theme decides the exact shade.
    /// </summary>
    public static readonly Color Text = Color.Parse("Gray");

    /// <summary>Our text colour on whatever background the theme uses.</summary>
    public static TuiAttribute Normal(TuiAttribute themeNormal) => new(Text, themeNormal.Background);

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
