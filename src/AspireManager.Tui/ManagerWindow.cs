using AspireManager.Core;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using TuiAttribute = Terminal.Gui.Drawing.Attribute;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace AspireManager.Tui;

/// <summary>
/// The two panes, plus the command palette and confirmation prompt as overlay views. Terminal.Gui exposes
/// no cross-thread marshalling, so the CLI streams write into the locked Core stores from their own tasks
/// and this polls them on a timer. Row text, key mapping and command decisions live in
/// <see cref="ShellModel"/>, where they are testable without a terminal.
/// </summary>
internal sealed class ManagerWindow : Window
{
    private const int _pollMilliseconds = 200;

    /// <summary>How long a footer message stays up before clearing itself, in poll ticks.</summary>
    private const int _statusTicks = 5000 / _pollMilliseconds;

    /// <summary>The three panes, numbered as lazygit numbers its panels.</summary>
    private enum Pane
    {
        AppHost,
        Resources,
        Logs,
    }

    private enum Overlay
    {
        None,
        List,
        Confirm,
        Search,
        LogSearch,
    }

    private readonly IApplication _app;
    private readonly Func<string, AppHostSession> _createSession;
    private readonly ResourceStore _resources;
    private readonly LogStore _logs;
    private AppHostSession _session;

    private readonly ListView _resourceList = new() { Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly ListView _logList = new() { Width = Dim.Fill(), Height = Dim.Fill() };
    private readonly FrameView _appHostFrame;
    private readonly Label _appHostName = new() { X = 1, Y = 0 };
    private readonly View _appHostStatus = new() { Y = 0, Height = 1, Width = Dim.Fill(1) };
    private readonly Label _appHostPath = new() { X = 1, Y = 1, Width = Dim.Fill(1) };
    private readonly FrameView _resourceFrame;
    private readonly FrameView _logFrame;
    private readonly View _footer;

    private readonly ResourceListSource _resourceSource;
    private readonly LogListSource _logSource;
    private IReadOnlyList<ResourceRow> _rowModel = [];
    private List<string> _rows = [];
    private readonly HashSet<string> _collapsed = new(StringComparer.Ordinal);
    private bool _grouped = true;
    private string _logSignature = "";
    private string _status = "";
    private int _statusTicksLeft;
    private ConnectionState _connection = ConnectionState.Connecting;
    private TimeSpan _retryIn = TimeSpan.Zero;

    // Overlays are views in this window, not nested run loops: RequestStop only sets a flag, and a run
    // loop started under another one does not re-check it until the next keypress, so a nested modal
    // needs a second Enter to close. One loop keeps key routing predictable.
    private Pane _pane = Pane.Resources;
    private IReadOnlyList<Binding> _bindings = [];
    private readonly EditorSettings? _editor;

    /// <summary>The key currently being dispatched, so a binding's action can tell j from k.</summary>
    private Key? LastKey { get; set; }

    // Captured before anything is recoloured. Re-reading a frame's attribute after painting it blue would
    // treat blue as the baseline and bleed it into the panel's contents on the next repaint.
    private TuiAttribute _baseAttribute;
    private Overlay _overlay = Overlay.None;
    private FrameView? _overlayFrame;
    private ListOverlay? _list;
    private TextField? _searchInput;
    private string _filter = "";
    private string _logQuery = "";
    private IReadOnlyList<int> _logMatches = [];
    private int _logMatchPos;
    private List<string> _logLines = [];
    private TextField? _confirmInput;
    private Label? _confirmHelp;
    private ConfirmCommand? _pendingConfirm;

    public ManagerWindow(
        IApplication app,
        AppHostSession session,
        Func<string, AppHostSession> createSession,
        ResourceStore resources,
        LogStore logs,
        EditorSettings? editor)
    {
        _editor = editor;
        _app = app;
        _resourceSource = new ResourceListSource(app);
        _resourceList.Source = _resourceSource;
        _logSource = new LogListSource(app);
        _logList.Source = _logSource;
        _session = session;
        _createSession = createSession;
        _resources = resources;
        _logs = logs;

        Title = "aspire-manager";
        BorderStyle = LineStyle.None;

        _appHostFrame = new FrameView
        {
            Title = "AppHost",
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4,
        };
        _appHostStatus.X = Pos.Right(_appHostName) + 3;

        // A Label paints in one attribute, so the status is drawn by hand to colour it on its own.
        _appHostStatus.DrawingContent += (_, e) =>
        {
            if (_app.Driver is not { } driver)
            {
                return;
            }

            _appHostStatus.Move(0, 0);
            TuiAttribute baseline = _appHostStatus.GetAttributeForRole(VisualRole.Normal);
            driver.CurrentAttribute = ToneColour(ShellModel.ConnectionTone(_connection)) is { } colour
                ? new TuiAttribute(colour, baseline.Background)
                : baseline;

            _appHostStatus.AddStr($"[{ShellModel.ConnectionText(_connection, _retryIn)}]");
            e.Cancel = true;
        };

        _appHostFrame.Add(_appHostName, _appHostStatus, _appHostPath);

        _resourceFrame = new FrameView
        {
            Title = "Resources",
            X = 0,
            Y = Pos.Bottom(_appHostFrame),
            Width = Dim.Percent(38),
            Height = Dim.Fill(1),
        };
        _resourceFrame.Add(_resourceList);

        _logFrame = new FrameView
        {
            Title = "Logs",
            X = Pos.Right(_resourceFrame),
            Y = Pos.Bottom(_appHostFrame),
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
        };
        _logFrame.Add(_logList);

        _footer = new View { X = 0, Y = Pos.AnchorEnd(1), Width = Dim.Fill(), Height = 1 };

        // A Label paints in one attribute; the active filter needs to stand out from the key hints.
        _footer.DrawingContent += (_, e) =>
        {
            if (_app.Driver is not { } driver)
            {
                return;
            }

            _footer.Move(0, 0);
            TuiAttribute baseline = _footer.GetAttributeForRole(VisualRole.Normal);
            int width = _footer.Viewport.Width;
            int used = 0;

            void Segment(string text, Color? colour)
            {
                if (text.Length == 0 || used >= width)
                {
                    return;
                }

                string clipped = text.Length > width - used ? text[..(width - used)] : text;
                driver.CurrentAttribute = colour is null
                    ? baseline
                    : new TuiAttribute(colour.Value, baseline.Background);

                _footer.AddStr(clipped);
                used += clipped.Length;
            }

            (string filter, string hint) = FooterParts();

            // Plain Yellow, not BrightYellow: the bright one is nearly white and the filter reads as noise.
            // This is the gold git uses for its commit line.
            Segment(filter, _filterColour);
            Segment(hint, null);

            driver.CurrentAttribute = baseline;
            if (used < width)
            {
                _footer.AddStr(new string(' ', width - used));
            }

            e.Cancel = true;
        };

        Add(_appHostFrame, _resourceFrame, _logFrame, _footer);
        _baseAttribute = GetAttributeForRole(VisualRole.Normal);
        _bindings = BuildBindings();
        RenderAppHost();
        FocusPane(Pane.Resources);

        _resourceList.ValueChanged += (_, _) => RenderLogs(force: true);


        // Application-scoped, not on this Window: the focused ListView consumes letters for its
        // type-to-search navigator, so r/s/b/c/q never reach a view-level handler. This hook runs first.
        app.Keyboard.KeyDown += OnKeyDown;

        app.TimedEvents?.Add(TimeSpan.FromMilliseconds(_pollMilliseconds), Refresh);
    }

    /// <summary>Whichever AppHost is attached now; Program stops this one on exit.</summary>
    public AppHostSession CurrentSession => _session;

    /// <summary>Last command result, shown in the footer.</summary>
    public void SetStatus(string status)
    {
        _status = status;

        // Transient by design: a message about something that already happened should not sit there
        // for the rest of the session pretending to be current.
        _statusTicksLeft = status.Length > 0 ? _statusTicks : 0;
        _footer.SetNeedsDraw();
    }

    /// <summary>Called by the session's stream loop; the AppHost panel owns connection state.</summary>
    public void SetConnection(ConnectionState connection, TimeSpan retryIn)
    {
        _connection = connection;
        _retryIn = retryIn;
        RenderAppHost();
    }

    private static readonly Color _filterColour = Color.Parse("Yellow");

    // Only the focused panel is coloured, the way lazygit marks its active panel; the others keep the
    // terminal's own foreground. The `*` in the title says the same thing on a monochrome terminal.
    private static readonly Color _focusedBorder = Color.Parse("BrightGreen");

    private static Color? ToneColour(RowTone tone) => tone switch
    {
        RowTone.Healthy => Color.Parse("BrightGreen"),
        RowTone.Warning => Color.Parse("BrightYellow"),
        RowTone.Failed => Color.Parse("BrightRed"),
        RowTone.Inactive => Color.Parse("DarkGray"),
        _ => null,
    };

    private void RenderAppHost()
    {
        _appHostName.Text = AppHostSelection.Name(_session.Path);
        _appHostStatus.SetNeedsDraw();
        _appHostPath.Text = AppHostSelection.ShortPath(
            _session.Path,
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Math.Max(10, _appHostFrame.Viewport.Width - 2));
    }

    private const string _hints =
        " 1/2/0 panes   j/k move   / search   enter logs   r/s/b   c cmds   ? help   q quit";

    /// <summary>
    /// The query is returned separately from the rest so it can be coloured on its own. A transient
    /// message *replaces* the hints rather than trailing them — appended, it was simply clipped off the
    /// end of a narrow terminal and never seen.
    /// </summary>
    private (string Query, string Tail) FooterParts()
    {
        if (LogFocused && _logQuery.Length > 0)
        {
            string summary = LogSearch.Summary(_logQuery, _logMatches.Count, _logMatchPos);
            return ($" {summary}", "   n next  N prev  esc/^g clear  tab back");
        }

        string prefix = _filter.Length > 0 ? $" /{_filter}" : "";
        string tail = _status.Length > 0
            ? $"   {_status}"
            : (prefix.Length > 0 ? "   esc clear  " : "") + _hints;

        return (prefix, tail);
    }

    private bool LogFocused => _pane == Pane.Logs;

    /// <summary>
    /// Colours a panel's border and, with it, its title. The Border adornment is not a View and has no
    /// scheme of its own, so the colour is set on the frame; the child list keeps the default scheme so
    /// the panel's contents do not inherit it.
    /// </summary>
    private void PaintBorder(FrameView frame, bool focused, params View[] content)
    {
        TuiAttribute attribute = focused
            ? new TuiAttribute(_focusedBorder, _baseAttribute.Background, TextStyle.Bold)
            : _baseAttribute;

        // Normal and Focus both set: a Scheme built from one attribute derives Focus by swapping
        // foreground and background, which turns the focused title into a solid inverted bar.
        frame.SetScheme(new Scheme { Normal = attribute, Focus = attribute });

        // Children inherit the frame's scheme, so each is pinned back to the default or the whole panel
        // turns blue with its border.
        foreach (View view in content)
        {
            view.SetScheme(new Scheme(_baseAttribute));
        }
    }

    private ResourceRow? SelectedRow() =>
        _resourceList.SelectedItem is { } index && index >= 0 && index < _rowModel.Count
            ? _rowModel[index]
            : null;

    /// <summary>Null when a type heading is selected rather than a resource.</summary>
    private AspireResource? Selected() =>
        SelectedRow() is ResourceItem item ? item.Resource : null;

    private bool Refresh()
    {
        if (_statusTicksLeft > 0 && --_statusTicksLeft == 0)
        {
            SetStatus("");
        }

        Rebuild();
        RenderLogs(force: false);
        return true;
    }

    /// <summary>
    /// Rebuilds the grouped rows. Selection is restored by key, not index: folding a group or a resource
    /// appearing shifts every row below it. A resource hidden by a fold falls back to its heading.
    /// </summary>
    private void Rebuild()
    {
        IReadOnlyList<ResourceRow> model =
            ShellModel.Rows(_resources.Resources(), _collapsed, _filter, _grouped);
        List<string> rows = [.. model.Select(ShellModel.RowText)];

        if (rows.SequenceEqual(_rows))
        {
            _rowModel = model;
            return;
        }

        string? key = SelectedRow() is { } row ? ShellModel.RowKey(row) : null;
        string? fallback = SelectedRow() is ResourceItem item
            ? ShellModel.TypeKey(item.Resource.ResourceType)
            : null;

        _rows = rows;
        _rowModel = model;
        _resourceSource.Update(model);

        int index = ShellModel.IndexOfKey(model, key);
        if (index < 0)
        {
            index = ShellModel.IndexOfKey(model, fallback);
        }

        if (index < 0)
        {
            index = ShellModel.FirstSelectable(model);
        }

        if (index >= 0)
        {
            _resourceList.SelectedItem = index;
            _resourceList.EnsureSelectedItemVisible();
        }
        else
        {
            // Nothing matched. A stale index from the previous set would keep painting a selection band
            // across a pane with no rows in it.
            _resourceList.SelectedItem = null;
        }
    }

    private void RenderLogs(bool force)
    {
        if (Selected() is not { } selected)
        {
            if (_logSignature != "")
            {
                _logSignature = "";
                _logLines = [];
                _logMatches = [];
                _logFrame.Title = "[0] Logs";
                _logSource.Update([], _logQuery);
                _logList.SelectedItem = null;
            }

            return;
        }

        IReadOnlyList<LogLine> lines = _logs.For(selected.DisplayName);
        string signature = $"{selected.DisplayName}|{lines.Count}|{(lines.Count > 0 ? lines[^1].Content : "")}";
        if (!force && signature == _logSignature)
        {
            return;
        }

        _logSignature = signature;
        RenderLogTitle();

        // Follow only while the view is already at the newest line. Otherwise j/k would be undone by the
        // next arriving line, which for a chatty resource is immediately.
        bool wasFollowing = _logQuery.Length == 0 && (force || Following());
        int previous = _logList.SelectedItem ?? 0;

        _logLines = [.. lines.Select(ShellModel.LogRow)];
        _logSource.Update(_logLines, _logQuery);
        _logMatches = LogSearch.MatchingLines(_logLines, _logQuery);

        if (lines.Count > 0)
        {
            _logList.SelectedItem = wasFollowing ? lines.Count - 1 : Math.Clamp(previous, 0, lines.Count - 1);
            _logList.EnsureSelectedItemVisible();
        }
        else
        {
            // A stale index from the previously selected resource would keep painting a highlighted band
            // across an otherwise empty pane.
            _logList.SelectedItem = null;
        }
    }

    private bool Following() =>
        _logList.Source is null
        || _logList.SelectedItem is null
        || _logList.SelectedItem >= _logList.Source.Count - 1;

    private void RenderLogTitle()
    {
        if (Selected() is not { } selected)
        {
            return;
        }

        string shared = _resources.HasAmbiguousLogs(selected) ? "  [shared name]" : "";

        // Scrolling away from the newest line stops the follow; say so, or the pane silently looks stale.
        string paused = Following() ? "" : "  [paused]";
        _logFrame.Title = $"[0] Logs: {selected.DisplayName}{shared}{paused}";
    }

    private void OnKeyDown(object? sender, Key key)
    {
        switch (_overlay)
        {
            case Overlay.List:
                _list?.HandleKey(key);
                return;

            case Overlay.Confirm:
                OnConfirmKey(key);
                return;

            case Overlay.Search:
                OnSearchKey(key);
                return;

            case Overlay.LogSearch:
                OnLogSearchKey(key);
                return;


        }

        LastKey = key;

        foreach (Binding binding in _bindings)
        {
            if (!binding.AppliesTo(CurrentPane) || !binding.Matches(key))
            {
                continue;
            }

            key.Handled = true;
            binding.Run();
            return;
        }
    }

    /// <summary>Tears the old attachment down before starting the new one, so two sets of follow streams
    /// never run at once and the panes never mix two AppHosts' resources.</summary>
    private void SwitchTo(string path)
    {
        if (AppHostSelection.SamePath(path, _session.Path))
        {
            return;
        }

        // ponytail: blocks the loop while the streams stop (bounded at 3s). Worth async only if it drags.
        _session.StopAsync().GetAwaiter().GetResult();

        _resources.Clear();
        _logs.Clear();
        _filter = "";
        _collapsed.Clear();
        _rows = [];
        _rowModel = [];
        _logSignature = "";

        _session = _createSession(path);
        _session.Start();

        SetConnection(ConnectionState.Connecting, TimeSpan.Zero);
        SetStatus($"attached to {AppHostSelection.Name(path)}");
        Rebuild();
        RenderLogs(force: true);
    }

    private void OpenLogSearch()
    {
        _searchInput = new TextField { X = 1, Y = 0, Width = Dim.Fill(2), Text = _logQuery };
        _searchInput.ValueChanged += (_, _) => SetLogQuery(_searchInput?.Text ?? "");

        _overlayFrame = new FrameView
        {
            Title = "Search logs",
            X = 0,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(),
            Height = 3,
        };
        _overlayFrame.Add(_searchInput);

        Add(_overlayFrame);
        _overlay = Overlay.LogSearch;
        _searchInput.SetFocus();
    }

    private void OnLogSearchKey(Key key)
    {
        if (ListKeys.IsCancel(key))
        {
            key.Handled = true;
            CloseOverlay();
            ClearLogSearch();
            return;
        }

        if (key == Key.Enter)
        {
            // Read the field rather than trusting ValueChanged to have fired for every keystroke.
            key.Handled = true;
            SetLogQuery(_searchInput?.Text ?? "");
            CloseOverlay();
            FocusPane(Pane.Logs);
            JumpToMatch(0);
        }
    }

    private void SetLogQuery(string query)
    {
        if (query == _logQuery)
        {
            return;
        }

        _logQuery = query;
        _logMatchPos = 0;
        _logSource.Update(_logLines, _logQuery);
        _logMatches = LogSearch.MatchingLines(_logLines, _logQuery);
        JumpToMatch(0);
    }

    private void ClearLogSearch()
    {
        if (_logQuery.Length == 0)
        {
            return;
        }

        _logQuery = "";
        _logMatchPos = 0;
        _logMatches = [];
        _logSource.Update(_logLines, "");
        SetStatus("log search cleared");
        RenderLogTitle();
        _logList.SetNeedsDraw();
    }

    /// <summary>Moves to another match; delta 0 just re-selects the current one.</summary>
    private void JumpToMatch(int delta)
    {
        if (_logMatches.Count == 0)
        {
            SetStatus(LogSearch.Summary(_logQuery, 0, 0));
            _logList.SetNeedsDraw();
            return;
        }

        _logMatchPos = delta == 0
            ? Math.Clamp(_logMatchPos, 0, _logMatches.Count - 1)
            : LogSearch.Advance(_logMatches.Count, _logMatchPos, delta);

        if (LogSearch.LineForPosition(_logMatches, _logMatchPos) is { } line)
        {
            _logList.SelectedItem = line;
            _logList.EnsureSelectedItemVisible();
        }

        SetStatus(LogSearch.Summary(_logQuery, _logMatches.Count, _logMatchPos));
        RenderLogTitle();
    }

    /// <summary>
    /// The dashboard URL is read from `aspire ps` at the moment it is asked for, not cached: it carries a
    /// login token that the AppHost can rotate.
    /// </summary>
    private void OpenDashboard()
    {
        IReadOnlyList<AppHost> hosts = _session.Cli.ListAppHostsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AppHost? host = hosts.FirstOrDefault(h => AppHostSelection.SamePath(h.AppHostPath, _session.Path));

        if (host?.DashboardUrl is not { } url)
        {
            SetStatus("no dashboard URL reported for this AppHost");
            return;
        }

        SetStatus(UrlOpener.Open(url));
    }

    private void OpenPrimaryUrl(AspireResource resource)
    {
        if (ShellModel.PrimaryUrl(resource) is not { } url)
        {
            SetStatus($"{resource.DisplayName} has no URL");
            return;
        }

        SetStatus(UrlOpener.Open(url.Url));
    }

    /// <summary>Shows the one list dialog; the accept callback runs after it has closed.</summary>
    private void ShowList(string title, IReadOnlyList<string> rows, string help, int selected, Action<int> accept)
    {

        _list = ListOverlay.Build(title, rows, help, selected, index =>
        {
            CloseOverlay();
            accept(index);
        }, CloseOverlay, Math.Max(8, Viewport.Height - 2));

        _overlayFrame = _list.Frame;
        Add(_overlayFrame);
        _overlay = Overlay.List;
        _list.List.SetFocus();

        // On a one-shot timer, not inline: the list has no viewport until the first draw, so scrolling it
        // before then does nothing and a list taller than the dialog opens part-way down.
        ListOverlay opened = _list;
        _app.TimedEvents?.Add(TimeSpan.FromMilliseconds(1), () =>
        {
            opened.ScrollToSelection();
            return false;
        });
    }

    /// <summary>The keys that work right here, rendered from the same table that dispatches them.</summary>
    private void ShowHelp()
    {
        Panes pane = CurrentPane;
        List<(string, string)> rows = [.. _bindings.Where(b => !b.IsGlobal && b.AppliesTo(pane))
            .Select(static b => (b.Label, b.Description))];

        rows.Add(("", ""));
        rows.AddRange(_bindings.Where(b => b.IsGlobal && b.AppliesTo(pane))
            .Select(static b => (b.Label, b.Description)));

        string title = pane switch
        {
            Panes.AppHost => "Keys: AppHost",
            Panes.Logs => "Keys: logs",
            _ => "Keys: resources",
        };


        // Enter does nothing here; the list is a reference, not a menu.
        ShowList(title, HelpText.Align(rows), " / filter   esc/^g close", 0, static _ => { });
    }

    private Panes CurrentPane => _pane switch
    {
        Pane.AppHost => Panes.AppHost,
        Pane.Logs => Panes.Logs,
        _ => Panes.Resources,
    };

    /// <summary>
    /// Every key the panes respond to, with its description and where it applies. `?` renders from this
    /// same list, so a new binding documents itself and the two cannot drift. First match wins, so the
    /// narrower bindings come before ones that would also accept the key.
    /// </summary>
    private IReadOnlyList<Binding> BuildBindings() =>
    [
        new("o", "open the Aspire dashboard", Panes.AppHost, Binding.Char('o'), OpenDashboard),

        new("j / k", "move up and down", Panes.Resources | Panes.Logs, Binding.AnyChar('j', 'k'), MoveInPane),
        new("^d / ^u", "page down and up", Panes.Resources | Panes.Logs, PageKey, PageInPane),

        new("enter", "on a resource: show its logs; on a heading: fold it", Panes.Resources,
            Binding.Exactly(Key.Enter), EnterResource),
        new("r / s / b", "restart, stop, rebuild", Panes.Resources, Binding.AnyChar('r', 's', 'b'),
            RunKeyCommand, HasSelectedResource),
        new("c", "all commands for this resource", Panes.Resources, Binding.Char('c'), OpenPalette,
            HasSelectedResource),
        new("o", "open its first URL", Panes.Resources, Binding.Char('o'),
            () => OpenPrimaryUrl(Selected()!), HasSelectedResource),
        new("O", "choose one of its URLs", Panes.Resources, Binding.Char('O'),
            () => OpenUrlPicker(Selected()!), HasSelectedResource),
        new("e", "open its logs in your editor", Panes.Resources, Binding.Char('e'),
            () => EditBuffered(null), HasSelectedResource),
        new("E", "open its whole log history in your editor", Panes.Resources | Panes.Logs,
            Binding.Char('E'), EditFullHistory, HasSelectedResource),

        new("g", "group by type on/off", Panes.Resources, Binding.Char('g'), ToggleGrouping),
        new("-", "fold every group", Panes.Resources, Binding.Char('-'), () => FoldAll(collapse: true)),
        new("=", "unfold every group", Panes.Resources, Binding.Char('='), () => FoldAll(collapse: false)),
        new("/", "filter by resource name", Panes.Resources, Binding.Char('/'), OpenSearch),
        new("esc / ^g", "clear the filter", Panes.Resources, ListKeys.IsCancel,
            () =>
            {
                SetFilter("");
                SetStatus("filter cleared");
            },
            () => _filter.Length > 0),

        new("e", "open these logs at the selected line", Panes.Logs, Binding.Char('e'),
            () => EditBuffered((_logList.SelectedItem ?? 0) + 1), HasSelectedResource),
        new("/", "search these logs", Panes.Logs, Binding.Char('/'), OpenLogSearch),
        new("n / N", "next and previous match", Panes.Logs, Binding.AnyChar('n', 'N'),
            () => JumpToMatch((char)LastKey!.AsRune.Value == 'n' ? 1 : -1), SearchingLogs),
        new("esc / ^g", "clear the search", Panes.Logs, ListKeys.IsCancel, ClearLogSearch, SearchingLogs),
        new("esc / ^g", "back to resources", Panes.Logs, ListKeys.IsCancel, () => FocusPane(Pane.Resources)),

        new("1 / 2 / 0", "focus AppHost, resources, logs", Panes.All, Binding.AnyChar('1', '2', '0'),
            () => FocusPane((char)LastKey!.AsRune.Value switch
            {
                '1' => Pane.AppHost,
                '2' => Pane.Resources,
                _ => Pane.Logs,
            })),
        new("tab", "next panel", Panes.All, Binding.Exactly(Key.Tab), NextPane),
        new("^r", "switch AppHost", Panes.All, Binding.Ctrl(KeyCode.R), OpenHosts),
        new("^z", "suspend to the shell (fg to return)", Panes.All, Binding.Ctrl(KeyCode.Z), Suspend,
            () => TerminalState.IsSupported),
        new("?", "this list", Panes.All, Binding.Char('?'), ShowHelp),
        new("q", "quit", Panes.All, Binding.Char('q'), () => _app.RequestStop(this)),
    ];

    /// <summary>
    /// Terminal.Gui binds Ctrl-Z to its own Suspend command, which is documented as UnixDriver-only: on this
    /// driver it tears the screen down without ever stopping the process. Handling it here takes precedence,
    /// since the application-level hook runs before Terminal.Gui's own key bindings.
    /// </summary>
    private void Suspend()
    {
        TerminalState.Suspend();

        // Back in the foreground: the screen was handed to the shell, so nothing on it can be trusted.
        _app.LayoutAndDraw(true);
    }

    private static bool PageKey(Key key) =>
        key.IsCtrl && (key.KeyCode & ~KeyCode.CtrlMask) is KeyCode.D or KeyCode.U;

    private bool HasSelectedResource() => Selected() is not null;

    private bool SearchingLogs() => _logQuery.Length > 0;

    private void MoveInPane()
    {
        ListKeys.VimMove(LogFocused ? _logList : _resourceList, LastKey!);
        if (LogFocused)
        {
            RenderLogTitle();
        }
    }

    private void PageInPane()
    {
        ListKeys.PageMove(LogFocused ? _logList : _resourceList, LastKey!);
        if (LogFocused)
        {
            RenderLogTitle();
        }
    }

    /// <summary>Enter drills in: a heading folds, a resource hands focus to its logs.</summary>
    private void EnterResource()
    {
        if (SelectedRow() is TypeHeader)
        {
            ToggleFold();
        }
        else if (SelectedRow() is ResourceItem)
        {
            FocusPane(Pane.Logs);
        }
    }

    private void RunKeyCommand()
    {
        if (ShellModel.CommandForKey((char)LastKey!.AsRune.Value) is { } command && Selected() is { } resource)
        {
            Decide(resource, command);
        }
    }

    private void ToggleGrouping()
    {
        _grouped = !_grouped;
        Rebuild();
        RenderLogs(force: true);
        SetStatus(_grouped ? "grouped by type" : "ungrouped");
    }

    private void NextPane()
    {
        // Leaving the pane leaves its search behind; a highlight you cannot navigate is just clutter.
        if (LogFocused)
        {
            ClearLogSearch();
        }

        FocusPane(_pane switch
        {
            Pane.AppHost => Pane.Resources,
            Pane.Resources => Pane.Logs,
            _ => Pane.AppHost,
        });
    }

    /// <summary>
    /// Opens what the pane holds. The file is exactly the buffered lines, so the selected row's number is
    /// the file's line number with no mapping.
    /// </summary>
    private void EditBuffered(int? line)
    {
        if (Selected() is not { } resource)
        {
            return;
        }

        string file = LogSnapshot.Write(_session.Path, resource.DisplayName, _logLines);
        SetStatus(EditorLauncher.Open(_editor, file, line));
    }

    /// <summary>
    /// Opens everything the AppHost still has, which is far more than the 500-line pane buffer — so there is
    /// no line to jump to and it opens at the top.
    /// </summary>
    private void EditFullHistory()
    {
        if (Selected() is not { } resource)
        {
            return;
        }

        // ponytail: blocks the loop for the fetch, like the other CLI round-trips here.
        IReadOnlyList<LogLine> history = _session.Cli
            .FetchLogsAsync(resource.DisplayName, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (history.Count == 0)
        {
            SetStatus($"no logs for {resource.DisplayName}");
            return;
        }

        string file = LogSnapshot.Write(
            _session.Path,
            resource.DisplayName,
            history.Select(l => ShellModel.LogRow(l with { Content = AnsiText.Strip(l.Content) })));

        SetStatus(EditorLauncher.Open(_editor, file, null));
    }

    private void OpenPalette()
    {
        if (Selected() is not { } resource)
        {
            return;
        }

        IReadOnlyList<string> commands = ShellModel.AvailableCommands(resource);
        if (commands.Count == 0)
        {
            SetStatus($"{resource.DisplayName} has no commands");
            return;
        }

        IReadOnlyList<string> rows = [.. commands.Select(c =>
            ShellModel.Decide(resource, c) is ConfirmCommand ? $"{c}  (confirm)" : c)];

        ShowList($"Commands: {resource.DisplayName}", rows, " j/k move   enter run   esc/^g cancel", 0,
            index => Decide(resource, commands[index]));
    }

    private void OpenHosts()
    {
        // ponytail: blocks the loop for the ~300ms `aspire ps` takes. Make it async if it ever shows.
        IReadOnlyList<AppHost> hosts = _session.Cli.ListAppHostsAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        List<AppHost> running = [.. AppHostSelection.Sorted(hosts.Where(static h =>
            string.Equals(h.Status, "running", StringComparison.OrdinalIgnoreCase)))];

        if (running.Count == 0)
        {
            SetStatus("no running AppHosts");
            return;
        }

        IReadOnlyList<string> rows = [.. running.Select(h =>
            (AppHostSelection.SamePath(h.AppHostPath, _session.Path) ? "* " : "  ") + AppHostSelection.Label(h))];

        // Open on the host we are attached to, so Enter is a no-op rather than a surprise switch.
        int current = running.FindIndex(h => AppHostSelection.SamePath(h.AppHostPath, _session.Path));

        ShowList("Switch AppHost", rows, " j/k move   enter attach   esc/^g cancel", Math.Max(current, 0),
            index => SwitchTo(running[index].AppHostPath));
    }

    private void OpenUrlPicker(AspireResource resource)
    {
        IReadOnlyList<AspireUrl> urls = ShellModel.Urls(resource);
        if (urls.Count == 0)
        {
            SetStatus($"{resource.DisplayName} has no URLs");
            return;
        }

        ShowList($"URLs: {resource.DisplayName}", [.. urls.Select(ShellModel.UrlLabel)],
            " j/k move   enter open   esc/^g cancel", 0,
            index => SetStatus(UrlOpener.Open(urls[index].Url)));
    }

    private void OpenSearch()
    {
        _searchInput = new TextField { X = 1, Y = 0, Width = Dim.Fill(2), Text = _filter };

        // Live: the field owns every key but Enter and cancel, so the filter follows what is typed.
        _searchInput.ValueChanged += (_, _) => SetFilter(_searchInput?.Text ?? "");

        _overlayFrame = new FrameView
        {
            Title = "Search resources",
            X = 0,
            Y = Pos.AnchorEnd(4),
            Width = Dim.Fill(),
            Height = 3,
        };
        _overlayFrame.Add(_searchInput);

        Add(_overlayFrame);
        _overlay = Overlay.Search;
        _searchInput.SetFocus();
    }

    private void OnSearchKey(Key key)
    {
        if (ListKeys.IsCancel(key))
        {
            key.Handled = true;
            CloseOverlay();
            SetFilter("");
            SetStatus("search cancelled");
            return;
        }

        if (key == Key.Enter)
        {
            // Keep the filter and hand the keyboard back, the way lazygit does.
            key.Handled = true;
            SetFilter(_searchInput?.Text ?? "");
            CloseOverlay();
        }
    }

    private void SetFilter(string filter)
    {
        if (filter == _filter)
        {
            return;
        }

        _filter = filter;
        Rebuild();
        RenderLogs(force: true);
        _footer.SetNeedsDraw();
    }

    /// <summary>Folds or unfolds every group at once.</summary>
    private void FoldAll(bool collapse)
    {
        _collapsed.Clear();

        if (collapse)
        {
            foreach (string type in _resources.Resources().Select(static r => r.ResourceType).Distinct())
            {
                _collapsed.Add(type);
            }
        }

        Rebuild();
        RenderLogs(force: true);
        SetStatus(collapse ? "all groups folded" : "all groups unfolded");
    }

    /// <summary>Folds the selected heading. Only headings fold; Enter on a resource focuses its logs.</summary>
    private void ToggleFold()
    {
        if (SelectedRow() is not TypeHeader header)
        {
            return;
        }

        string type = header.ResourceType;

        if (!_collapsed.Remove(type))
        {
            _collapsed.Add(type);
        }

        Rebuild();
        RenderLogs(force: true);
    }

    private void Decide(AspireResource resource, string command)
    {
        switch (ShellModel.Decide(resource, command))
        {
            case RefuseCommand refuse:
                SetStatus(refuse.Reason);
                break;

            case RunCommand run:
                Start(run.DisplayName, run.Command);
                break;

            case ConfirmCommand confirm:
                OpenConfirm(confirm);
                break;
        }
    }

    private void OpenConfirm(ConfirmCommand confirm)
    {
        _pendingConfirm = confirm;

        Label prompt = new()
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = 3,
            Text = $"{confirm.Command} on {confirm.DisplayName}.\nThis is not a routine command.\nType the resource name to continue:",
        };

        _confirmInput = new TextField { X = 1, Y = 3, Width = Dim.Fill(2) };
        _confirmHelp = new Label
        {
            X = 1,
            Y = 5,
            Width = Dim.Fill(1),
            Text = " enter confirm   esc cancel",
        };

        _overlayFrame = new FrameView
        {
            Title = $"Confirm {confirm.Command}",
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = Math.Max(52, confirm.Expected.Length + 26),
            Height = 8,
        };
        _overlayFrame.Add(prompt, _confirmInput, _confirmHelp);

        Add(_overlayFrame);
        _overlay = Overlay.Confirm;
        _confirmInput.SetFocus();
    }

    private void OnConfirmKey(Key key)
    {
        if (ListKeys.IsCancel(key))
        {
            key.Handled = true;
            string command = _pendingConfirm?.Command ?? "command";
            CloseOverlay();
            SetStatus($"{command} cancelled");
            return;
        }

        if (key != Key.Enter)
        {
            // Everything else has to reach the text field, including the r, s and b in resource names.
            return;
        }

        key.Handled = true;
        if (_pendingConfirm is not { } confirm)
        {
            CloseOverlay();
            return;
        }

        if (!ShellModel.ConfirmationMatches(confirm.Expected, _confirmInput?.Text ?? ""))
        {
            if (_confirmHelp is not null)
            {
                _confirmHelp.Text = " name does not match   esc cancel";
            }

            return;
        }

        CloseOverlay();
        Start(confirm.DisplayName, confirm.Command);
    }

    private void CloseOverlay()
    {
        if (_overlayFrame is { } frame)
        {
            Remove(frame);
            frame.Dispose();
        }

        _overlayFrame = null;
        _confirmInput = null;
        _confirmHelp = null;
        _pendingConfirm = null;
        _list = null;
        _searchInput = null;
        _overlay = Overlay.None;
        FocusPane(_pane);
    }

    /// <summary>
    /// The focused pane is shown by its border colour alone; the panels are numbered so they can be jumped
    /// to. Focus is tracked here rather than left to Terminal.Gui's Tab traversal, which did not reliably
    /// land on the other pane's list.
    /// </summary>
    private void FocusPane(Pane pane)
    {
        _pane = pane;
        _appHostFrame.Title = "[1] AppHost";
        _resourceFrame.Title = "[2] Resources";
        RenderLogTitle();

        // The AppHost panel holds no list, so focus goes to its frame: neither list should then paint a
        // selection, and the app-level key handler routes by _pane regardless.
        PaintBorder(_appHostFrame, pane == Pane.AppHost, _appHostName, _appHostStatus, _appHostPath);
        PaintBorder(_resourceFrame, pane == Pane.Resources, _resourceList);
        PaintBorder(_logFrame, pane == Pane.Logs, _logList);

        View target = pane switch
        {
            Pane.AppHost => _appHostFrame,
            Pane.Resources => _resourceList,
            _ => _logList,
        };

        target.SetFocus();
    }

    private void Start(string displayName, string command)
    {
        SetStatus($"{command} {displayName}...");
        _ = RunAsync(displayName, command);
    }

    private async Task RunAsync(string displayName, string command)
    {
        CommandResult result = await _session.Cli.RunCommandAsync(displayName, command, CancellationToken.None);

        SetStatus(result.Success
            ? $"{command} {displayName} ok"
            : $"{command} {displayName} failed: {FirstLine(result.Output)}");
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "unknown error";
}
