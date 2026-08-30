namespace Deadlimit.App;

internal sealed class StartupProgressForm : Form
{
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Spring = true,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private readonly ToolStripProgressBar _progressBar = new()
    {
        Minimum = 0,
        Maximum = 100,
        Value = 0,
        Width = 220,
        Style = ProgressBarStyle.Blocks,
    };

    public StartupProgressForm(Icon icon, string theme)
    {
        Text = UiText.ProductName;
        Icon = icon;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(500, 68);
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        TopMost = true;

        var heading = new Label
        {
            Text = UiText.T("Starting Deadlimit Manager...", "Запуск Deadlimit Manager..."),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(12, 4, 12, 4),
            AutoEllipsis = true,
        };

        var statusStrip = new StatusStrip
        {
            SizingGrip = false,
        };
        statusStrip.Items.Add(_statusLabel);
        statusStrip.Items.Add(_progressBar);

        Controls.Add(heading);
        Controls.Add(statusStrip);
        UiTheme.ApplyCustomPalette(this, theme);
    }

    public void UpdateProgress(int value, string message)
    {
        _progressBar.Value = Math.Clamp(value, _progressBar.Minimum, _progressBar.Maximum);
        _statusLabel.Text = message;
        Refresh();
    }
}
