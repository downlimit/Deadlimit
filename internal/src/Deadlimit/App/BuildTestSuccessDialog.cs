namespace Deadlimit.App;

internal sealed class BuildTestSuccessDialog : Form
{
    private const string DeadlockSteamUri = "steam://rungameid/1422450";

    public BuildTestSuccessDialog(string vpkPath, string summary)
    {
        var deadlockRunning = DeadlockProcessService.IsRunning();

        Text = UiText.T("Build & Test complete", "Сборка и тест готовы");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 3,
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = $"{summary}\n\nVPK:\n{vpkPath}",
            Margin = new Padding(0, 0, 0, 14),
        };

        var hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = deadlockRunning
                ? UiText.T(
                    "Deadlock is already running now. The new VPK was deployed successfully before this dialog appeared.",
                    "Deadlock уже запущен. Новый VPK был успешно установлен до появления этого окна.")
                : UiText.T(
                    "Deadlock is not running. Launch it when you are ready to test the new VPK.",
                    "Deadlock не запущен. Запустите игру, когда будете готовы проверить новый VPK."),
            Margin = new Padding(0, 0, 0, 16),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };

        var launchButton = new Button
        {
            Text = deadlockRunning
                ? UiText.T("DEADLOCK IS RUNNING", "DEADLOCK УЖЕ ЗАПУЩЕН")
                : UiText.T("LAUNCH DEADLOCK GAME", "ЗАПУСТИТЬ DEADLOCK"),
            AutoSize = true,
            Enabled = !deadlockRunning,
            Margin = new Padding(8, 0, 0, 0),
        };
        launchButton.Click += (_, _) => LaunchDeadlock();

        var okButton = new Button
        {
            Text = "OK",
            AutoSize = true,
        };
        okButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        buttons.Controls.Add(launchButton);
        buttons.Controls.Add(okButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(hint, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        AcceptButton = deadlockRunning ? okButton : launchButton;
        CancelButton = okButton;
    }

    private void LaunchDeadlock()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeadlockSteamUri,
                UseShellExecute = true,
            });

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T(
                    "Could not launch Deadlock through Steam",
                    "Не удалось запустить Deadlock через Steam"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
