namespace DolphinRoulette;

public class MainForm : Form
{
    private readonly AppSettings _settings;
    private RouletteEngine? _engine;

    // ── Controls ──────────────────────────────────────────────────────
    private Label _nowPlayingLabel = null!;
    private Label _timerLabel = null!;
    private ProgressBar _timerBar = null!;
    private ListBox _gameList = null!;
    private RichTextBox _logBox = null!;
    private Button _startBtn = null!;
    private Button _stopBtn = null!;
    private Button _skipBtn = null!;
    private Button _doneBtn = null!;
    private Button _addGameBtn = null!;
    private Button _removeGameBtn = null!;
    private Button _settingsBtn = null!;

    private System.Windows.Forms.Timer _countdownTimer = null!;
    private int _remainingSeconds = 0;
    private int _totalSeconds = 0;

    public MainForm()
    {
        _settings = AppSettings.Load();
        BuildUI();
        RefreshGameList();
        UpdateButtonStates(running: false);
    }

    // ── UI Construction ───────────────────────────────────────────────

    private void BuildUI()
    {
        Text = "🎮 Dolphin Roulette";
        Size = new Size(820, 620);
        MinimumSize = new Size(700, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(22, 22, 26);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Segoe UI", 9.5f);

        // ── Header ─────────────────────────────────────────────────────
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 90,
            BackColor = Color.FromArgb(15, 15, 18),
            Padding = new Padding(16, 10, 16, 10),
        };

        _nowPlayingLabel = new Label
        {
            Text = "No game running",
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = Color.FromArgb(100, 180, 255),
            AutoSize = true,
            Location = new Point(16, 10),
        };

        _timerLabel = new Label
        {
            Text = "",
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(160, 160, 160),
            AutoSize = true,
            Location = new Point(16, 44),
        };

        _timerBar = new ProgressBar
        {
            Location = new Point(16, 68),
            Width = 780,
            Height = 6,
            Style = ProgressBarStyle.Continuous,
            ForeColor = Color.FromArgb(0, 140, 255),
            BackColor = Color.FromArgb(40, 40, 50),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };

        header.Controls.AddRange(new Control[] { _nowPlayingLabel, _timerLabel, _timerBar });
        Controls.Add(header);

        // ── Main area ──────────────────────────────────────────────────
        var mainPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12),
        };
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260));
        mainPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // ── Left: game list panel ──────────────────────────────────────
        var leftPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 8, 0) };

        var listLabel = SectionLabel("GAME ROTATION");
        listLabel.Dock = DockStyle.Top;

        _gameList = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(30, 30, 36),
            ForeColor = Color.FromArgb(210, 210, 210),
            BorderStyle = BorderStyle.None,
            SelectionMode = SelectionMode.One,
            ItemHeight = 22,
            Font = new Font("Segoe UI", 9.5f),
        };
        _gameList.DrawMode = DrawMode.OwnerDrawFixed;
        _gameList.DrawItem += GameList_DrawItem;

        var listButtons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 4, 0, 0),
        };
        _addGameBtn = SmallBtn("+ Add");
        _addGameBtn.Click += AddGame;
        _removeGameBtn = SmallBtn("− Remove");
        _removeGameBtn.Click += RemoveGame;
        listButtons.Controls.AddRange(new Control[] { _addGameBtn, _removeGameBtn });

        leftPanel.Controls.Add(_gameList);
        leftPanel.Controls.Add(listLabel);
        leftPanel.Controls.Add(listButtons);

        // ── Right: log + controls ──────────────────────────────────────
        var rightPanel = new Panel { Dock = DockStyle.Fill };

        var logLabel = SectionLabel("LOG");
        logLabel.Dock = DockStyle.Top;

        _logBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(18, 18, 22),
            ForeColor = Color.FromArgb(180, 210, 180),
            BorderStyle = BorderStyle.None,
            ReadOnly = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Font = new Font("Cascadia Mono", 8.5f),
        };

        var controlBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 44,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 6, 0, 0),
        };

        _startBtn    = AccentBtn("▶  Start",   Color.FromArgb(0, 140, 80));
        _stopBtn     = AccentBtn("■  Stop",    Color.FromArgb(160, 40, 40));
        _skipBtn     = AccentBtn("⏭  Skip",    Color.FromArgb(60, 90, 140));
        _doneBtn     = AccentBtn("✓  Done",    Color.FromArgb(120, 80, 0));
        _settingsBtn = AccentBtn("⚙  Settings", Color.FromArgb(55, 55, 65));

        _startBtn.Click    += StartRoulette;
        _stopBtn.Click     += StopRoulette;
        _skipBtn.Click     += (_, _) => _engine?.ForceSkip();
        _doneBtn.Click     += (_, _) => _engine?.MarkCurrentDone();
        _settingsBtn.Click += OpenSettings;

        controlBar.Controls.AddRange(new Control[]
            { _startBtn, _stopBtn, _skipBtn, _doneBtn, _settingsBtn });

        rightPanel.Controls.Add(_logBox);
        rightPanel.Controls.Add(logLabel);
        rightPanel.Controls.Add(controlBar);

        mainPanel.Controls.Add(leftPanel, 0, 0);
        mainPanel.Controls.Add(rightPanel, 1, 0);
        Controls.Add(mainPanel);

        // ── Countdown timer ────────────────────────────────────────────
        _countdownTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _countdownTimer.Tick += CountdownTick;
    }

    // ── Event handlers ────────────────────────────────────────────────

    private void StartRoulette(object? s, EventArgs e)
    {
        var active = _settings.Games.Where(g => !g.Finished).ToList();
        if (active.Count == 0)
        {
            MessageBox.Show("No games in rotation. Add some games first.",
                "Nothing to play", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _engine = new RouletteEngine(_settings, AppendLog, OnEngineStateChanged);
        _engine.Start();
        UpdateButtonStates(running: true);
        Log("▶  Roulette started.");
    }

    private async void StopRoulette(object? s, EventArgs e)
    {
        if (_engine == null) return;
        Log("■  Stopping after saving current state…");
        _stopBtn.Enabled = false;
        await _engine.StopAsync();
        _engine = null;
        _countdownTimer.Stop();
        UpdateButtonStates(running: false);
        Log("  Stopped.");
    }

    private void OnEngineStateChanged(GameEntry? game, int totalSeconds, RouletteState state)
    {
        if (InvokeRequired) { Invoke(() => OnEngineStateChanged(game, totalSeconds, state)); return; }

        _totalSeconds = totalSeconds;
        _remainingSeconds = totalSeconds;

        if (game != null && state == RouletteState.Running)
        {
            _nowPlayingLabel.Text = game.Name;
            _timerBar.Value = 100;
            _countdownTimer.Start();
            RefreshGameList();
        }
        else
        {
            _nowPlayingLabel.Text = "No game running";
            _timerLabel.Text = "";
            _timerBar.Value = 0;
            _countdownTimer.Stop();
            UpdateButtonStates(running: false);
            RefreshGameList();
        }
    }

    private void CountdownTick(object? s, EventArgs e)
    {
        if (_remainingSeconds > 0) _remainingSeconds--;
        int m = _remainingSeconds / 60, sec = _remainingSeconds % 60;
        _timerLabel.Text = $"Next switch in {m:D2}:{sec:D2}";
        _timerBar.Value = _totalSeconds > 0
            ? (int)(100.0 * _remainingSeconds / _totalSeconds)
            : 0;
    }

    private void AddGame(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select game ISO / RVZ / GCZ",
            Filter = "Game files|*.iso;*.rvz;*.gcz;*.wbfs;*.ciso|All files|*.*",
            Multiselect = true,
        };
        if (dlg.ShowDialog() != DialogResult.OK) return;

        foreach (var file in dlg.FileNames)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (_settings.Games.Any(g => g.Path == file)) continue;
            _settings.Games.Add(new GameEntry { Name = name, Path = file });
            Log($"  + Added: {name}");
        }
        _settings.Save();
        RefreshGameList();
    }

    private void RemoveGame(object? s, EventArgs e)
    {
        if (_gameList.SelectedIndex < 0) return;
        var entry = (GameEntry)_gameList.SelectedItem!;
        var r = MessageBox.Show($"Remove '{entry.Name}' from the list?",
            "Confirm remove", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (r != DialogResult.Yes) return;
        _settings.Games.Remove(entry);
        _settings.Save();
        RefreshGameList();
        Log($"  − Removed: {entry.Name}");
    }

    private void OpenSettings(object? s, EventArgs e)
    {
        using var dlg = new SettingsForm(_settings);
        dlg.ShowDialog(this);
    }

    // ── Owner-draw game list ──────────────────────────────────────────

    private void GameList_DrawItem(object? s, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var entry = (GameEntry)_gameList.Items[e.Index];
        bool selected = (e.State & DrawItemState.Selected) != 0;
        bool isCurrent = _engine?.CurrentGame?.Name == entry.Name;

        var bg = selected
            ? Color.FromArgb(40, 80, 140)
            : (isCurrent ? Color.FromArgb(20, 60, 20) : Color.FromArgb(30, 30, 36));

        e.Graphics.FillRectangle(new SolidBrush(bg), e.Bounds);

        var fg = entry.Finished
            ? Color.FromArgb(90, 90, 90)
            : (isCurrent ? Color.FromArgb(100, 220, 100) : Color.FromArgb(210, 210, 210));

        string prefix = entry.Finished ? "✓ " : (isCurrent ? "▶ " : "  ");
        string label = $"{prefix}{entry.Name}  ({entry.PlayCount} plays)";

        using var brush = new SolidBrush(fg);
        e.Graphics.DrawString(label, _gameList.Font, brush,
            new RectangleF(e.Bounds.X + 4, e.Bounds.Y + 2, e.Bounds.Width - 8, e.Bounds.Height));
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private void RefreshGameList()
    {
        _gameList.BeginUpdate();
        _gameList.Items.Clear();
        foreach (var g in _settings.Games)
            _gameList.Items.Add(g);
        _gameList.EndUpdate();
        _gameList.Invalidate();
    }

    private void AppendLog(string msg)
    {
        if (InvokeRequired) { Invoke(() => AppendLog(msg)); return; }
        Log(msg);
    }

    private void Log(string msg)
    {
        _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        _logBox.ScrollToCaret();
    }

    private void UpdateButtonStates(bool running)
    {
        _startBtn.Enabled    = !running;
        _stopBtn.Enabled     = running;
        _skipBtn.Enabled     = running;
        _doneBtn.Enabled     = running;
        _addGameBtn.Enabled  = !running;
        _removeGameBtn.Enabled = !running;
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", 7.5f, FontStyle.Bold),
        ForeColor = Color.FromArgb(100, 100, 120),
        AutoSize = false,
        Height = 22,
        Padding = new Padding(0, 6, 0, 0),
        BackColor = Color.Transparent,
    };

    private static Button SmallBtn(string text) => new()
    {
        Text = text,
        Width = 76,
        Height = 28,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(40, 40, 50),
        ForeColor = Color.FromArgb(200, 200, 200),
        FlatAppearance = { BorderColor = Color.FromArgb(65, 65, 80) },
        Margin = new Padding(0, 0, 4, 0),
        Cursor = Cursors.Hand,
    };

    private static Button AccentBtn(string text, Color bg) => new()
    {
        Text = text,
        Width = 100,
        Height = 30,
        FlatStyle = FlatStyle.Flat,
        BackColor = bg,
        ForeColor = Color.White,
        FlatAppearance = { BorderSize = 0 },
        Margin = new Padding(0, 0, 6, 0),
        Cursor = Cursors.Hand,
    };
}
