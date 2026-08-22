using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _csdkRootText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _deadlockToolsRootText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _retailDeadlockRootText = new() { Dock = DockStyle.Fill };

    public SettingsForm()
    {
        Text = "Deadlimit Settings";
        StartPosition = FormStartPosition.CenterParent;
        Width = 840;
        Height = 310;
        MinimumSize = new Size(720, 300);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        var paths = new DeadlimitPaths();
        _csdkRootText.Text = paths.CsdkRoot;
        _deadlockToolsRootText.Text = paths.DeadlockToolsRoot;
        _retailDeadlockRootText.Text = paths.RetailDeadlockRoot;

        BuildUi();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var description = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "These machine-local paths are used by extraction, authoring preparation, CSDK launch, and later release actions.",
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(description, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddPathRow(grid, 0, "Reduced CSDK12", _csdkRootText, "Select Reduced_CSDK_12 root");
        AddPathRow(grid, 1, "DeadlockTools", _deadlockToolsRootText, "Select DeadlockTools root");
        AddPathRow(grid, 2, "Retail Deadlock", _retailDeadlockRootText, "Select Steam Project8Staging root");
        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 12, 0, 0),
        };

        var cancelButton = new Button
        {
            Text = "CANCEL",
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        var saveButton = new Button
        {
            Text = "SAVE",
            AutoSize = true,
        };
        saveButton.Click += (_, _) => SaveSettings();

        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(saveButton);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.Add(root);
    }

    private void AddPathRow(
        TableLayoutPanel grid,
        int row,
        string label,
        TextBox textBox,
        string dialogDescription)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9),
        };

        textBox.Margin = new Padding(0, 5, 8, 5);

        var browseButton = new Button
        {
            Text = "BROWSE",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        browseButton.Click += (_, _) =>
        {
            var selected = ChooseFolder(dialogDescription, textBox.Text);
            if (selected is not null)
            {
                textBox.Text = selected;
            }
        };

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(textBox, 1, row);
        grid.Controls.Add(browseButton, 2, row);
    }

    private void SaveSettings()
    {
        var candidate = new ToolPathSettings
        {
            CsdkRoot = _csdkRootText.Text.Trim(),
            DeadlockToolsRoot = _deadlockToolsRootText.Text.Trim(),
            RetailDeadlockRoot = _retailDeadlockRootText.Text.Trim(),
        };

        if (!ValidatePaths(candidate, out var error))
        {
            MessageBox.Show(
                this,
                error,
                "Invalid tool paths",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            ProjectStore.SaveToolPathSettings(candidate);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Could not save settings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool ValidatePaths(ToolPathSettings candidate, out string error)
    {
        if (!Directory.Exists(candidate.CsdkRoot))
        {
            error = $"Reduced CSDK12 folder does not exist:\n{candidate.CsdkRoot}";
            return false;
        }

        var candidatePaths = new DeadlimitPaths(candidate);
        if (!File.Exists(candidatePaths.CsdkLauncherPath))
        {
            error = $"csdkcfg.exe was not found in the selected Reduced CSDK12 root:\n{candidatePaths.CsdkLauncherPath}";
            return false;
        }

        if (!Directory.Exists(candidate.DeadlockToolsRoot))
        {
            error = $"DeadlockTools folder does not exist:\n{candidate.DeadlockToolsRoot}";
            return false;
        }

        if (!File.Exists(candidatePaths.DeadlockToolsExePath))
        {
            error = $"DeadlockTools.exe was not found at the expected build path:\n{candidatePaths.DeadlockToolsExePath}";
            return false;
        }

        if (!Directory.Exists(candidate.RetailDeadlockRoot))
        {
            error = $"Retail Deadlock folder does not exist:\n{candidate.RetailDeadlockRoot}";
            return false;
        }

        var retailCitadel = Path.Combine(candidate.RetailDeadlockRoot, "game", "citadel");
        if (!Directory.Exists(retailCitadel))
        {
            error = $"The selected retail folder does not contain game\\citadel:\n{candidate.RetailDeadlockRoot}";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static string? ChooseFolder(string description, string currentPath)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = false,
            InitialDirectory = Directory.Exists(currentPath) ? currentPath : string.Empty,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
    }
}
