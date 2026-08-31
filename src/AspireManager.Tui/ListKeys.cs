using AspireManager.Core;
using Terminal.Gui.Drivers;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace AspireManager.Tui;

internal static class ListKeys
{
    /// <summary>
    /// Moves a list on j/k and reports whether it consumed the key. The move has to be done here rather
    /// than left to the widget: a ListView treats bare letters as type-to-search, so j and k would jump
    /// to items starting with those letters instead of stepping one row.
    /// </summary>
    /// <summary>
    /// Esc, or Ctrl-G — the readline/emacs abort that vim users reach for, accepted everywhere Esc is.
    /// </summary>
    public static bool IsCancel(Key key) =>
        key == Key.Esc || (key.IsCtrl && (key.KeyCode & ~KeyCode.CtrlMask) == KeyCode.G);

    /// <summary>Ctrl-D / Ctrl-U as page down / page up, the way vim binds them.</summary>
    public static bool PageMove(ListView list, Key key)
    {
        if (!key.IsCtrl)
        {
            return false;
        }

        // A Ctrl combination carries no rune (AsRune is 0), so the letter has to come from the key code
        // with the modifier masked off.
        bool down = (key.KeyCode & ~KeyCode.CtrlMask) == KeyCode.D;
        bool up = (key.KeyCode & ~KeyCode.CtrlMask) == KeyCode.U;

        if (!down && !up)
        {
            return false;
        }

        if ((list.Source?.Count ?? 0) == 0)
        {
            key.Handled = true;
            return true;
        }

        list.SelectedItem ??= 0;

        if (down)
        {
            list.MovePageDown();
        }
        else
        {
            list.MovePageUp();
        }

        list.EnsureSelectedItemVisible();
        key.Handled = true;
        return true;
    }

    public static bool VimMove(ListView list, Key key)
    {
        char pressed = char.ToLowerInvariant((char)key.AsRune.Value);
        if (pressed is not ('j' or 'k'))
        {
            return false;
        }

        // Setting SelectedItem on an empty list throws, and an empty list is ordinary here.
        if (ShellModel.NextIndex(list.Source?.Count ?? 0, list.SelectedItem, pressed == 'j' ? 1 : -1)
            is not { } next)
        {
            key.Handled = true;
            return true;
        }

        list.SelectedItem = next;
        list.EnsureSelectedItemVisible();
        key.Handled = true;
        return true;
    }
}
