using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class BuildTestSuccessDialog : Form
{
    public BuildTestSuccessDialog(string vpkPath, string summary)
        : this(
            UiText.T("Build for test complete", "Сборка для теста готова"),
            $"{summary}\n\nVPK:\n{vpkPath}",
            UiText.T(
                "Close this build summary.\n\nThe VPK has already been deployed; launch Deadlock separately when you are ready to test it.",
                "Закрыть сводку сборки.\n\nVPK уже установлен; запустите Deadlock отдельно, когда будете готовы к тесту."))
    {
    }

    public static BuildTestSuccessDialog CreatePrepareSummary(string message) => new(
        UiText.T("Prepare for CSDK complete", "Подготовка для CSDK готова"),
        message,
        UiText.T(
            "Close this preparation summary.\n\nThe authoring content is already prepared; launch CSDK separately when you are ready to work with it.",
            "Закрыть сводку подготовки.\n\nAuthoring-контент уже подготовлен; запустите CSDK отдельно, когда будете готовы с ним работать."));

    private BuildTestSuccessDialog(string title, string body, string okToolTip)
    {
        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = Padding.Empty;

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(18),
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = body,
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
        toolTip.SetToolTip(okButton, okToolTip);

        buttons.Controls.Add(okButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

        UiTheme.ApplyCustomPalette(this, ProjectStore.GetToolPathSettings().UiTheme);

        AcceptButton = okButton;
        CancelButton = okButton;
    }
}
