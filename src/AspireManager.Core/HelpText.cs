namespace AspireManager.Core;

/// <summary>Which pane the help is being asked from; the bindings differ per pane.</summary>
public enum HelpContext
{
    AppHost,
    Resources,
    Logs,
}

/// <summary>
/// The keybinding list behind `?`. Context-sensitive like lazygit's: the pane's own keys first, then the
/// ones that work anywhere. Kept here so the bindings can be asserted without a terminal — a help screen
/// that drifts from the real keys is worse than none.
/// </summary>
public static class HelpText
{
    public static IReadOnlyList<(string Key, string Action)> Bindings(
        HelpContext context,
        bool filterActive,
        bool logSearchActive)
    {
        List<(string, string)> rows = [];

        switch (context)
        {
            case HelpContext.AppHost:
                rows.Add(("o", "open the Aspire dashboard"));
                break;

            case HelpContext.Resources:
                rows.Add(("j / k", "move up and down"));
                rows.Add(("^d / ^u", "page down and up"));
                rows.Add(("enter", "on a resource: show its logs; on a heading: fold it"));
                rows.Add(("r / s / b", "restart, stop, rebuild"));
                rows.Add(("c", "all commands for this resource"));
                rows.Add(("o / O", "open first URL, or choose one"));
                rows.Add(("g", "group by type on/off"));
                rows.Add(("- / =", "fold or unfold every group"));
                rows.Add(("/", "filter by resource name"));

                if (filterActive)
                {
                    rows.Add(("esc / ^g", "clear the filter"));
                }

                break;

            case HelpContext.Logs:
                rows.Add(("j / k", "move up and down"));
                rows.Add(("^d / ^u", "page down and up"));
                rows.Add(("/", "search these logs"));

                if (logSearchActive)
                {
                    rows.Add(("n / N", "next and previous match"));
                    rows.Add(("esc / ^g", "clear the search"));
                }
                else
                {
                    rows.Add(("esc / ^g", "back to resources"));
                }

                break;
        }

        rows.Add(("", ""));
        rows.Add(("1 / 2 / 0", "focus AppHost, resources, logs"));
        rows.Add(("tab", "next panel"));
        rows.Add(("^r", "switch AppHost"));
        rows.Add(("?", "this list"));
        rows.Add(("q", "quit"));

        return rows;
    }

    /// <summary>Rows with the key column padded so the descriptions line up.</summary>
    public static IReadOnlyList<string> Rows(HelpContext context, bool filterActive, bool logSearchActive)
    {
        IReadOnlyList<(string Key, string Action)> bindings = Bindings(context, filterActive, logSearchActive);
        int width = bindings.Max(static b => b.Key.Length);

        return [.. bindings.Select(b => b.Key.Length == 0 ? "" : $"{b.Key.PadRight(width)}   {b.Action}")];
    }

    public static string Title(HelpContext context) => context switch
    {
        HelpContext.AppHost => "Keys: AppHost",
        HelpContext.Logs => "Keys: logs",
        _ => "Keys: resources",
    };
}
