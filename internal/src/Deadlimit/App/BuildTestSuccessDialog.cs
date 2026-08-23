namespace Deadlimit.App;

internal sealed class BuildTestSuccessDialog : Form
{
    private const string DeadlockSteamUri = "steam://rungameid/1422450";

    public BuildTestSuccessDialog(string vpkPath, string summary)
    {
        Text = "Build & Test complete";
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
            Text = "If Deadlock is already running, restart it so the new VPK is loaded.",
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
            Text = "LAUNCH DEADLOCK GAME",
            AutoSize = true,
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

        AcceptButton = launchButton;
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
                "Could not launch Deadlock through Steam",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
