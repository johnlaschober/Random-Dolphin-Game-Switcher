namespace DolphinRoulette;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private TextBox _dolphinPathBox = null!;
    private NumericUpDown _slotNum = null!;
    private NumericUpDown _minNum = null!;
    private NumericUpDown _maxNum = null!;
    private NumericUpDown _graceNum = null!;
    private CheckBox _showTimerChk = null!;
    private Button _browseBtn = null!;
    private Button _okBtn = null!;
    private Button _cancelBtn = null!;

    public SettingsForm(AppSettings settings)
    {
        _settings = settings;
        BuildUI();
        LoadValues();
    }

    private void BuildUI()
    {
        Text = "Settings";
        Size = new Size(480, 370);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Segoe UI", 9.5f);

        var tip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400, ReshowDelay = 200 };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 7,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        // Dolphin path
        var dolphinLbl = Label("Dolphin.exe path");
        tip.SetToolTip(dolphinLbl, "Full path to Dolphin.exe on your machine.");
        layout.Controls.Add(dolphinLbl, 0, 0);
        _dolphinPathBox = DarkTextBox();
        tip.SetToolTip(_dolphinPathBox, "Full path to Dolphin.exe on your machine.");
        layout.Controls.Add(_dolphinPathBox, 1, 0);
        _browseBtn = SmallButton("Browse…");
        _browseBtn.Click += BrowseDolphin;
        layout.Controls.Add(_browseBtn, 2, 0);

        // Savestate slot
        var slotLbl = Label("Savestate slot (1–8)");
        tip.SetToolTip(slotLbl, "Which savestate slot to load from and save to.\nMust match the slot you saved manually before starting.");
        layout.Controls.Add(slotLbl, 0, 1);
        _slotNum = DarkNumeric(1, 8);
        tip.SetToolTip(_slotNum, "Which savestate slot to load from and save to.\nMust match the slot you saved manually before starting.");
        layout.Controls.Add(_slotNum, 1, 1);
        layout.SetColumnSpan(_slotNum, 2);

        // Min play time
        var minLbl = Label("Min play time (seconds)");
        tip.SetToolTip(minLbl, "Minimum time (seconds) spent on each game before switching.");
        layout.Controls.Add(minLbl, 0, 2);
        _minNum = DarkNumeric(1, 9999);
        tip.SetToolTip(_minNum, "Minimum time (seconds) spent on each game before switching.");
        layout.Controls.Add(_minNum, 1, 2);
        layout.SetColumnSpan(_minNum, 2);

        // Max play time
        var maxLbl = Label("Max play time (seconds)");
        tip.SetToolTip(maxLbl, "Maximum time (seconds) spent on each game before switching.");
        layout.Controls.Add(maxLbl, 0, 3);
        _maxNum = DarkNumeric(1, 9999);
        tip.SetToolTip(_maxNum, "Maximum time (seconds) spent on each game before switching.");
        layout.Controls.Add(_maxNum, 1, 3);
        layout.SetColumnSpan(_maxNum, 2);

        // Grace period
        var graceLbl = Label("Dolphin boot wait (ms)");
        tip.SetToolTip(graceLbl, "Milliseconds to wait after launching Dolphin before sending the load-state hotkey.\nIncrease if Dolphin hasn't finished loading the game by the time the hotkey fires (default 4500).");
        layout.Controls.Add(graceLbl, 0, 4);
        _graceNum = DarkNumeric(0, 30000);
        tip.SetToolTip(_graceNum, "Milliseconds to wait after launching Dolphin before sending the load-state hotkey.\nIncrease if Dolphin hasn't finished loading the game by the time the hotkey fires (default 4500).");
        layout.Controls.Add(_graceNum, 1, 4);
        layout.SetColumnSpan(_graceNum, 2);

        // Show timer
        _showTimerChk = new CheckBox
        {
            Text = "Show next switch time in GUI",
            AutoSize = true,
            ForeColor = Color.FromArgb(180, 180, 180),
            BackColor = Color.Transparent,
            Margin = new Padding(0, 8, 0, 0),
        };
        tip.SetToolTip(_showTimerChk, "Show countdown timer and progress bar in the header while a game is running.");
        layout.Controls.Add(_showTimerChk, 0, 5);
        layout.SetColumnSpan(_showTimerChk, 3);

        // Buttons
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 44,
            Padding = new Padding(8, 6, 8, 6),
            BackColor = Color.FromArgb(30, 30, 30),
        };
        _okBtn = AccentButton("Save");
        _okBtn.Click += Save;
        _cancelBtn = SmallButton("Cancel");
        _cancelBtn.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(_cancelBtn);
        btnPanel.Controls.Add(_okBtn);

        Controls.Add(layout);
        Controls.Add(btnPanel);
    }

    private void LoadValues()
    {
        _dolphinPathBox.Text = _settings.DolphinPath;
        _slotNum.Value = _settings.SavestateSlot;
        _minNum.Value = _settings.MinPlaySeconds;
        _maxNum.Value = _settings.MaxPlaySeconds;
        _graceNum.Value = _settings.GracePeriodMs;
        _showTimerChk.Checked = _settings.ShowTimerInGui;
    }

    private void Save(object? s, EventArgs e)
    {
        if ((int)_minNum.Value >= (int)_maxNum.Value)
        {
            MessageBox.Show("Min play time must be less than max play time.",
                "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _settings.DolphinPath = _dolphinPathBox.Text.Trim();
        _settings.SavestateSlot = (int)_slotNum.Value;
        _settings.MinPlaySeconds = (int)_minNum.Value;
        _settings.MaxPlaySeconds = (int)_maxNum.Value;
        _settings.GracePeriodMs = (int)_graceNum.Value;
        _settings.ShowTimerInGui = _showTimerChk.Checked;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseDolphin(object? s, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title = "Select Dolphin.exe",
            Filter = "Dolphin executable|Dolphin.exe|All executables|*.exe",
        };
        if (dlg.ShowDialog() == DialogResult.OK)
            _dolphinPathBox.Text = dlg.FileName;
    }

    // ── Helpers ────────────────────────────────────────────────────────
    private static Label Label(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Top,
        Padding = new Padding(0, 6, 0, 0),
        ForeColor = Color.FromArgb(180, 180, 180),
    };

    private static TextBox DarkTextBox() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(45, 45, 45),
        ForeColor = Color.FromArgb(220, 220, 220),
        BorderStyle = BorderStyle.FixedSingle,
        Margin = new Padding(2, 4, 2, 4),
    };

    private static NumericUpDown DarkNumeric(int min, int max) => new()
    {
        Minimum = min,
        Maximum = max,
        BackColor = Color.FromArgb(45, 45, 45),
        ForeColor = Color.FromArgb(220, 220, 220),
        Margin = new Padding(2, 4, 2, 4),
        Width = 80,
    };

    private static Button SmallButton(string text) => new()
    {
        Text = text,
        Width = 72,
        Height = 28,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(55, 55, 55),
        ForeColor = Color.FromArgb(220, 220, 220),
        FlatAppearance = { BorderColor = Color.FromArgb(80, 80, 80) },
        Margin = new Padding(4, 0, 0, 0),
        Cursor = Cursors.Hand,
    };

    private static Button AccentButton(string text) => new()
    {
        Text = text,
        Width = 72,
        Height = 28,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(0, 120, 215),
        ForeColor = Color.White,
        FlatAppearance = { BorderSize = 0 },
        Margin = new Padding(4, 0, 0, 0),
        Cursor = Cursors.Hand,
    };
}
