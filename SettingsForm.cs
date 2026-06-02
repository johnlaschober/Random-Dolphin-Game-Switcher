namespace DolphinRoulette;

public class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private TextBox _dolphinPathBox = null!;
    private NumericUpDown _slotNum = null!;
    private NumericUpDown _minNum = null!;
    private NumericUpDown _maxNum = null!;
    private NumericUpDown _graceNum = null!;
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
        Size = new Size(480, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.FromArgb(220, 220, 220);
        Font = new Font("Segoe UI", 9.5f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 3,
            RowCount = 6,
            AutoSize = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        // Dolphin path
        layout.Controls.Add(Label("Dolphin.exe path"), 0, 0);
        _dolphinPathBox = DarkTextBox(); layout.Controls.Add(_dolphinPathBox, 1, 0);
        _browseBtn = SmallButton("Browse…");
        _browseBtn.Click += BrowseDolphin;
        layout.Controls.Add(_browseBtn, 2, 0);

        // Savestate slot
        layout.Controls.Add(Label("Savestate slot (1–8)"), 0, 1);
        _slotNum = DarkNumeric(1, 8);
        layout.Controls.Add(_slotNum, 1, 1);
        layout.SetColumnSpan(_slotNum, 2);

        // Min play time
        layout.Controls.Add(Label("Min play time (seconds)"), 0, 2);
        _minNum = DarkNumeric(1, 9999);
        layout.Controls.Add(_minNum, 1, 2);
        layout.SetColumnSpan(_minNum, 2);

        // Max play time
        layout.Controls.Add(Label("Max play time (seconds)"), 0, 3);
        _maxNum = DarkNumeric(1, 9999);
        layout.Controls.Add(_maxNum, 1, 3);
        layout.SetColumnSpan(_maxNum, 2);

        // Grace period
        layout.Controls.Add(Label("Grace period (seconds)"), 0, 4);
        _graceNum = DarkNumeric(1, 30);
        layout.Controls.Add(_graceNum, 1, 4);
        layout.SetColumnSpan(_graceNum, 2);

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
        _graceNum.Value = _settings.GracePeriodSeconds;
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
        _settings.GracePeriodSeconds = (int)_graceNum.Value;
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
