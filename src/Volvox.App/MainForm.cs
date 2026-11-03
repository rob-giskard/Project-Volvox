using System.Diagnostics;
using Volvox.Sim;

namespace Volvox.App;

internal sealed class MainForm : Form
{
    private enum SortMode { Time, Lifetime, Event, Interaction, Category }

    // Pause plus three speed modes. Rates are sim ticks (seconds) per real second.
    private static readonly (string Label, double Rate)[] Speeds =
    {
        ("PAUSE", 0), ("1x", 30), ("10x", 300), ("100x", 3000),
    };

    private readonly SimConfig _baseConfig;
    private Simulation _sim = null!;
    private int _seed;

    private readonly MapView _map = new();
    private readonly InspectorView _inspector = new();
    private readonly EventListView _log = new();
    private readonly SplitContainer _mainSplit = new();
    private readonly SplitContainer _topSplit = new();

    private readonly TextBox _search = new();
    private readonly ComboBox _sortBox = new();
    private readonly Button _dirButton = new();
    private readonly CheckBox _follow = new();
    private readonly Label _count = new();
    private readonly Dictionary<EventCategory, CheckBox> _catToggles = new();
    private readonly List<Button> _speedButtons = new();
    private readonly TextBox _seedBox = new();
    private readonly CheckBox _autoNew = new();
    private CheckBox? _beliefsToggle, _effortToggle, _trailToggle;

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private readonly System.Windows.Forms.Timer _searchDebounce = new() { Interval = 220 };
    private readonly Stopwatch _frameClock = new();
    private double _tickDebt;
    private int _speed = 1;
    private int _lastRunSpeed = 1;
    private bool _lagging;
    private DateTime _diedAt = DateTime.MaxValue;

    private SortMode _sort = SortMode.Time;
    private bool _descending;
    private long _lastSeenId;
    private bool _dirty;
    private DateTime _lastRebuild = DateTime.MinValue;
    private DateTime _lastPull = DateTime.MinValue;

    public MainForm(SimConfig config, int seed)
    {
        _baseConfig = config;
        _seed = seed;

        Text = "VOLVOX · artificial life observatory · MVP-0";
        Size = new Size(1440, 920);
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Theme.Panel;
        ForeColor = Theme.Ink;
        Font = Theme.Mono;
        KeyPreview = true;

        BuildLayout();
        NewSimulation(seed);

        _timer.Tick += (_, _) => Frame();
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); Rebuild(); };
    }

    // ── Layout ───────────────────────────────────────────────

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Theme.Panel,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildSimToolbar(), 0, 0);

        _topSplit.Dock = DockStyle.Fill;
        _topSplit.Orientation = Orientation.Vertical;
        _topSplit.FixedPanel = FixedPanel.Panel2;
        _topSplit.SplitterWidth = 5;
        _topSplit.BackColor = Theme.Line;
        _topSplit.Panel1.Controls.Add(_map);
        _topSplit.Panel2.Controls.Add(_inspector);

        _mainSplit.Dock = DockStyle.Fill;
        _mainSplit.Orientation = Orientation.Horizontal;
        _mainSplit.FixedPanel = FixedPanel.Panel2;
        _mainSplit.SplitterWidth = 5;
        _mainSplit.BackColor = Theme.Line;
        _mainSplit.Panel1.Controls.Add(_topSplit);

        var logLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty, Padding = Padding.Empty,
            BackColor = Theme.Panel,
        };
        logLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        logLayout.Controls.Add(BuildLogToolbar(), 0, 0);
        logLayout.Controls.Add(_log, 0, 1);
        _mainSplit.Panel2.Controls.Add(logLayout);

        root.Controls.Add(_mainSplit, 0, 1);
        Controls.Add(root);

        _log.SelectedIndexChanged += (_, _) =>
        {
            var ev = _log.Selected;
            if (ev != null) _map.CenterOn(new V2(ev.X, ev.Y));
        };
        _log.ColumnClick += (_, e) => OnColumnClick(e.Column);
        _log.MouseWheel += (_, _) => { if (_follow.Checked && !_descending) _follow.Checked = false; };
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Safe(() => _mainSplit.SplitterDistance = Math.Max(250, _mainSplit.Height - 300));
        Safe(() => _topSplit.SplitterDistance = Math.Max(300, _topSplit.Width - 360));
        Safe(() => _mainSplit.Panel1MinSize = 200);
        Safe(() => _mainSplit.Panel2MinSize = 120);
        Safe(() => _topSplit.Panel1MinSize = 300);
        Safe(() => _topSplit.Panel2MinSize = 260);

        _map.FitWorld();
        _frameClock.Start();
        _timer.Start();
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (InvalidOperationException) { }
        catch (ArgumentException) { }
    }

    private static FlowLayoutPanel Toolbar() => new()
    {
        Dock = DockStyle.Fill, WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
        Padding = new Padding(8, 5, 8, 0), Margin = Padding.Empty, BackColor = Theme.Panel,
    };

    private static Label Caption(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.InkSoft, Font = Theme.MonoBold,
        Margin = new Padding(8, 6, 4, 0),
    };

    private static Label Divider() => new()
    {
        Text = "│", AutoSize = true, ForeColor = Theme.Line, Margin = new Padding(6, 5, 6, 0),
    };

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Paper, ForeColor = Theme.Ink, Font = Theme.Mono, Height = 24,
            Margin = new Padding(2, 0, 2, 0), Padding = new Padding(6, 0, 6, 0), TabStop = false,
        };
        b.FlatAppearance.BorderColor = Theme.Line;
        b.FlatAppearance.MouseOverBackColor = Theme.Hover;
        b.Click += onClick;
        return b;
    }

    private static CheckBox MakeToggle(string text, bool initial, Color? onColor = null)
    {
        var c = new CheckBox
        {
            Text = text, Appearance = Appearance.Button, Checked = initial, AutoSize = true, FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Paper, ForeColor = onColor ?? Theme.Ink, Font = Theme.MonoBold, TextAlign = ContentAlignment.MiddleCenter,
            Margin = new Padding(2, 0, 2, 0), Padding = new Padding(6, 0, 6, 0), MinimumSize = new Size(0, 24), TabStop = false,
        };
        c.FlatAppearance.BorderColor = Theme.Line;
        c.FlatAppearance.CheckedBackColor = Theme.Select;
        c.FlatAppearance.MouseOverBackColor = Theme.Hover;
        return c;
    }

    private Control BuildSimToolbar()
    {
        var bar = Toolbar();
        bar.Controls.Add(Caption("VOLVOX"));
        bar.Controls.Add(Divider());
        bar.Controls.Add(Caption("SPEED"));
        for (int i = 0; i < Speeds.Length; i++)
        {
            int idx = i;
            var b = MakeButton(Speeds[i].Label, (_, _) => SetSpeed(idx));
            _speedButtons.Add(b);
            bar.Controls.Add(b);
        }
        bar.Controls.Add(Divider());

        bar.Controls.Add(Caption("SEED"));
        _seedBox.Width = 70;
        _seedBox.BorderStyle = BorderStyle.FixedSingle;
        _seedBox.BackColor = Theme.Paper;
        _seedBox.Margin = new Padding(2, 1, 2, 0);
        bar.Controls.Add(_seedBox);
        bar.Controls.Add(MakeButton("NEW", (_, _) => NewFromBox()));
        bar.Controls.Add(MakeButton("RESTART", (_, _) => NewSimulation(_seed)));
        _autoNew.Text = "AUTO-NEW";
        var auto = MakeToggle("AUTO-NEW", false);
        auto.CheckedChanged += (_, _) => _autoNew.Checked = auto.Checked;
        bar.Controls.Add(auto);
        bar.Controls.Add(Divider());

        bar.Controls.Add(Caption("VIEW"));
        var beliefs = MakeToggle("BELIEFS", true, Theme.Belief);
        beliefs.CheckedChanged += (_, _) => _map.ShowBeliefs = beliefs.Checked;
        var effort = MakeToggle("EFFORT", false, Theme.Accent);
        effort.CheckedChanged += (_, _) => _map.ShowEffort = effort.Checked;
        var trail = MakeToggle("TRAIL", true);
        trail.CheckedChanged += (_, _) => _map.ShowTrail = trail.Checked;
        bar.Controls.Add(beliefs);
        bar.Controls.Add(effort);
        bar.Controls.Add(trail);
        _beliefsToggle = beliefs;
        _effortToggle = effort;
        _trailToggle = trail;
        bar.Controls.Add(MakeButton("FIT", (_, _) => _map.FitWorld()));
        bar.Controls.Add(Divider());
        bar.Controls.Add(new Label
        {
            Text = "space pause · 1/2/3 speed · wheel zoom · drag pan · B/E/T views · N new",
            AutoSize = true, ForeColor = Theme.InkFaint, Font = Theme.MonoSmall, Margin = new Padding(4, 8, 0, 0),
        });
        return bar;
    }

    private Control BuildLogToolbar()
    {
        var bar = Toolbar();
        bar.Controls.Add(Caption("LOG"));
        _search.Width = 220;
        _search.PlaceholderText = "search text, event, object…";
        _search.BorderStyle = BorderStyle.FixedSingle;
        _search.BackColor = Theme.Paper;
        _search.Margin = new Padding(2, 1, 6, 0);
        _search.TextChanged += (_, _) => { _searchDebounce.Stop(); _searchDebounce.Start(); };
        bar.Controls.Add(_search);

        foreach (var cat in Enum.GetValues<EventCategory>())
        {
            var t = MakeToggle(cat.ToString(), true, Theme.Category(cat));
            t.CheckedChanged += (_, _) => Rebuild();
            _catToggles[cat] = t;
            bar.Controls.Add(t);
        }
        bar.Controls.Add(Divider());

        bar.Controls.Add(Caption("SORT"));
        _sortBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sortBox.FlatStyle = FlatStyle.Flat;
        _sortBox.BackColor = Theme.Paper;
        _sortBox.Width = 120;
        _sortBox.Margin = new Padding(2, 1, 2, 0);
        _sortBox.Items.AddRange(new object[] { "time", "lifetime", "event", "interaction", "category" });
        _sortBox.SelectedIndex = 0;
        _sortBox.SelectedIndexChanged += (_, _) => { _sort = (SortMode)_sortBox.SelectedIndex; Rebuild(); };
        bar.Controls.Add(_sortBox);

        _dirButton.Text = "ASC";
        _dirButton.Width = 56;
        _dirButton.FlatStyle = FlatStyle.Flat;
        _dirButton.BackColor = Theme.Paper;
        _dirButton.Height = 24;
        _dirButton.Margin = new Padding(2, 0, 2, 0);
        _dirButton.TabStop = false;
        _dirButton.FlatAppearance.BorderColor = Theme.Line;
        _dirButton.Click += (_, _) => { _descending = !_descending; Rebuild(); };
        bar.Controls.Add(_dirButton);

        var follow = MakeToggle("FOLLOW", true);
        follow.CheckedChanged += (_, _) => _follow.Checked = follow.Checked;
        _follow.Checked = true;
        _follow.CheckedChanged += (_, _) => { if (follow.Checked != _follow.Checked) follow.Checked = _follow.Checked; };
        bar.Controls.Add(follow);
        bar.Controls.Add(Divider());

        _count.AutoSize = true;
        _count.ForeColor = Theme.InkSoft;
        _count.Margin = new Padding(4, 6, 0, 0);
        bar.Controls.Add(_count);
        return bar;
    }

    // ── Simulation lifecycle ─────────────────────────────────

    private void NewFromBox()
    {
        if (int.TryParse(_seedBox.Text.Trim(), out int s)) NewSimulation(s);
        else NewSimulation(_seed + 1);
    }

    private void NewSimulation(int seed)
    {
        _seed = seed;
        var cfg = _baseConfig.Clone();
        cfg.Seed = seed;
        _sim = new Simulation(cfg);
        _map.SetSimulation(_sim);
        _inspector.Sim = _sim;
        _seedBox.Text = seed.ToString();
        _tickDebt = 0;
        _diedAt = DateTime.MaxValue;
        _lastSeenId = 0;
        Rebuild();
        _map.Invalidate();
        _inspector.Invalidate();
    }

    private void SetSpeed(int idx)
    {
        _speed = idx;
        if (idx > 0) _lastRunSpeed = idx;
        for (int i = 0; i < _speedButtons.Count; i++)
        {
            _speedButtons[i].BackColor = i == idx ? Theme.Select : Theme.Paper;
            _speedButtons[i].Font = i == idx ? Theme.MonoBold : Theme.Mono;
        }
    }

    private void Frame()
    {
        double dt = Math.Min(0.25, _frameClock.Elapsed.TotalSeconds);
        _frameClock.Restart();

        double rate = Speeds[_speed].Rate;
        _lagging = false;
        if (_sim.Organism.Alive && rate > 0)
        {
            _tickDebt += dt * rate;
            var budget = Stopwatch.StartNew();
            while (_tickDebt >= 1 && _sim.Organism.Alive)
            {
                _sim.Step();
                _tickDebt -= 1;
                if (budget.ElapsedMilliseconds > 20) { _tickDebt = 0; _lagging = true; break; }
            }
            if (!_sim.Organism.Alive) _diedAt = DateTime.Now;
        }

        if (!_sim.Organism.Alive && _autoNew.Checked && (DateTime.Now - _diedAt).TotalSeconds > 3)
        {
            NewSimulation(_seed + 1);
            return;
        }

        string state = !_sim.Organism.Alive ? $"DECEASED ({_sim.Organism.CauseOfDeath})"
            : _sim.Organism.Sleeping ? "SLEEPING" : "ALIVE";
        _map.StatusText = $"SEED {_seed}   T+{Names.Time(_sim.Tick)}   {Speeds[_speed].Label}{(_lagging ? " (lagging)" : "")}   {state}";
        _map.Invalidate();
        _inspector.Invalidate();

        if ((DateTime.Now - _lastPull).TotalMilliseconds > 100)
        {
            _lastPull = DateTime.Now;
            PullEvents();
        }
    }

    // ── Log: filter, sort, follow ────────────────────────────

    private bool Matches(SimEvent ev)
    {
        if (_catToggles.TryGetValue(ev.Category, out var t) && !t.Checked) return false;
        string q = _search.Text.Trim();
        if (q.Length == 0) return true;
        return ev.Text.Contains(q, StringComparison.OrdinalIgnoreCase)
               || ev.Type.Contains(q, StringComparison.OrdinalIgnoreCase)
               || ev.Subject.Contains(q, StringComparison.OrdinalIgnoreCase)
               || ev.Category.ToString().Equals(q, StringComparison.OrdinalIgnoreCase);
    }

    private bool Incremental => _sort == SortMode.Time && !_descending;

    private void PullEvents()
    {
        int added = 0;
        foreach (var ev in _sim.Log.Since(_lastSeenId))
        {
            _lastSeenId = ev.Id;
            if (!Matches(ev)) continue;
            if (Incremental) { _log.Rows.Add(ev); added++; }
            else _dirty = true;
        }

        if (_dirty && (DateTime.Now - _lastRebuild).TotalMilliseconds > 700) Rebuild();
        else if (added > 0)
        {
            _log.SyncSize();
            if (_follow.Checked && _log.Rows.Count > 0 && _log.IsHandleCreated) _log.EnsureVisible(_log.Rows.Count - 1);
        }
        UpdateCount();
    }

    private void Rebuild()
    {
        if (_sim == null) return;
        var rows = _sim.Log.Events.Where(Matches).ToList();
        Comparison<SimEvent> cmp = _sort switch
        {
            SortMode.Lifetime => (a, b) => a.Age != b.Age ? a.Age.CompareTo(b.Age) : a.Id.CompareTo(b.Id),
            SortMode.Event => (a, b) =>
            {
                int c = string.CompareOrdinal(a.Type, b.Type);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            },
            SortMode.Interaction => (a, b) =>
            {
                bool ea = a.Subject.Length == 0, eb = b.Subject.Length == 0;
                if (ea != eb) return ea ? 1 : -1;
                int c = string.Compare(a.Subject, b.Subject, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : a.Id.CompareTo(b.Id);
            },
            SortMode.Category => (a, b) => a.Category != b.Category ? a.Category.CompareTo(b.Category) : a.Id.CompareTo(b.Id),
            _ => (a, b) => a.Id.CompareTo(b.Id),
        };
        if (_descending)
        {
            var inner = cmp;
            cmp = (a, b) => inner(b, a);
        }
        rows.Sort(cmp);

        _log.SortColumn = _sort switch
        {
            SortMode.Lifetime => EventListView.ColLifetime,
            SortMode.Event => EventListView.ColEvent,
            SortMode.Interaction => EventListView.ColInteraction,
            SortMode.Category => EventListView.ColCategory,
            _ => -1,
        };
        _log.SortDescending = _descending;
        _dirButton.Text = _descending ? "DESC" : "ASC";
        _log.SetRows(rows);
        _lastSeenId = _sim.Log.LastId;
        _dirty = false;
        _lastRebuild = DateTime.Now;

        if (_follow.Checked && rows.Count > 0 && _log.IsHandleCreated)
            _log.EnsureVisible(_sort == SortMode.Time && !_descending ? rows.Count - 1 : 0);
        _log.Invalidate(true);
        UpdateCount();
    }

    private void UpdateCount()
    {
        string dropped = _sim.Log.Dropped > 0 ? $" ({_sim.Log.Dropped:N0} oldest dropped)" : "";
        _count.Text = $"{_log.Rows.Count:N0} / {_sim.Log.Events.Count:N0} events{dropped}";
    }

    private void OnColumnClick(int column)
    {
        SortMode mode = column switch
        {
            EventListView.ColLifetime => SortMode.Lifetime,
            EventListView.ColCategory => SortMode.Category,
            EventListView.ColEvent => SortMode.Event,
            EventListView.ColInteraction => SortMode.Interaction,
            _ => SortMode.Time,
        };
        if (mode == _sort) _descending = !_descending;
        else { _sort = mode; _descending = false; }
        _sortBox.SelectedIndex = (int)_sort; // triggers Rebuild
        Rebuild();
    }

    // ── Keyboard ─────────────────────────────────────────────

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_search.Focused || _seedBox.Focused) return;
        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.Space: SetSpeed(_speed == 0 ? _lastRunSpeed : 0); break;
            case Keys.D1: case Keys.NumPad1: SetSpeed(1); break;
            case Keys.D2: case Keys.NumPad2: SetSpeed(2); break;
            case Keys.D3: case Keys.NumPad3: SetSpeed(3); break;
            case Keys.F: _map.FitWorld(); break;
            case Keys.B: if (_beliefsToggle != null) _beliefsToggle.Checked = !_beliefsToggle.Checked; break;
            case Keys.E: if (_effortToggle != null) _effortToggle.Checked = !_effortToggle.Checked; break;
            case Keys.T: if (_trailToggle != null) _trailToggle.Checked = !_trailToggle.Checked; break;
            case Keys.N: NewSimulation(_seed + 1); break;
            default: handled = false; break;
        }
        if (handled)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        SetSpeed(1);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _timer.Stop();
        base.OnFormClosing(e);
    }
}
