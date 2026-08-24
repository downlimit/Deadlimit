namespace Deadlimit.App;

internal sealed class BuildTestSuccessDialog : Form
{
    public BuildTestSuccessDialog(string vpkPath, string summary)
    {
        Text = UiText.T("Build for test complete", "Сборка для теста готова");
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
            RowCount = 2,
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = $"{summary}\n\nVPK:\n{vpkPath}",
            Margin = new Padding(0, 0, 0, 16),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };

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

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };
        toolTip.SetToolTip(
            okButton,
            UiText.T(
                "Close this build summary.\n\nThe VPK has already been deployed; launch Deadlock separately when you are ready to test it.",
                "Закрыть сводку сборки.\n\nVPK уже установлен; запустите Deadlock отдельно, когда будете готовы к тесту."));

        buttons.Controls.Add(okButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        AcceptButton = okButton;
        CancelButton = okButton;
    }
}
