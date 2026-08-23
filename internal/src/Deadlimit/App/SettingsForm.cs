using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _csdkRootText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _deadlockToolsRootText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _retailDeadlockRootText = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _languageCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left,
        Width = 180,
    };
    private readonly ComboBox _themeCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left,
        Width = 180,
    };

    private readonly string _initialLanguage;
    private readonly string _initialTheme;

    public SettingsForm()
    {
        var paths = new DeadlimitPaths();
        var settings = ProjectStore.GetToolPathSettings();
        _initialLanguage = settings.UiLanguage;
        _initialTheme = settings.UiTheme;

        Text = UiText.T("Deadlimit Settings", "Настройки Deadlimit");
        StartPosition = FormStartPosition.CenterParent;
        Width = 840;
        Height = 400;
        MinimumSize = new Size(720, 380);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _csdkRootText.Text = paths.CsdkRoot;
        _deadlockToolsRootText.Text = paths.DeadlockToolsRoot;
        _retailDeadlockRootText.Text = paths.RetailDeadlockRoot;

        _languageCombo.Items.Add(new LanguageItem("en", "English"));
        _languageCombo.Items.Add(new LanguageItem("ru", "Русский"));
        _languageCombo.SelectedIndex = string.Equals(settings.UiLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        _themeCombo.Items.Add(new ThemeItem("system", UiText.T("System theme", "Системная тема")));
        _themeCombo.Items.Add(new ThemeItem("light", UiText.T("Light", "Светлая")));
        _themeCombo.Items.Add(new ThemeItem("gray", UiText.T("Gray", "Серая")));
        _themeCombo.Items.Add(new ThemeItem("dark", UiText.T("Dark", "Тёмная")));
        _themeCombo.SelectedIndex = settings.UiTheme switch
        {
            "light" => 1,
            "gray" => 2,
            "dark" => 3,
            _ => 0,
        };

        BuildUi();
        UiTheme.ApplyCustomPalette(this, settings.UiTheme);
    }

    public bool RestartRequired { get; private set; }

    public bool LanguageChanged => RestartRequired;

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
            Text = UiText.T(
                "Machine-local tool paths, interface language and theme. Language and theme changes are applied after Deadlimit restarts.",
                "Локальные пути к инструментам, язык и тема интерфейса. Смена языка и темы применяется после перезапуска Deadlimit."),
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(description, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            AutoSize = true,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddPathRow(grid, 0, "Reduced CSDK12", _csdkRootText, UiText.T("Select Reduced_CSDK_12 root", "Выберите корень Reduced_CSDK_12"));
        AddPathRow(grid, 1, "DeadlockTools", _deadlockToolsRootText, UiText.T("Select DeadlockTools root", "Выберите корень DeadlockTools"));
        AddPathRow(grid, 2, UiText.T("Retail Deadlock", "Retail Deadlock"), _retailDeadlockRootText, UiText.T("Select Steam Project8Staging root", "Выберите корень Steam Project8Staging"));
        AddLanguageRow(grid, 3);
        AddThemeRow(grid, 4);
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
            Text = UiText.T("CANCEL", "ОТМЕНА"),
            AutoSize = true,
            DialogResult = DialogResult.Cancel,
        };
        var saveButton = new Button
        {
            Text = UiText.T("SAVE", "СОХРАНИТЬ"),
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
            Text = UiText.T("BROWSE", "ОБЗОР"),
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

    private void AddLanguageRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = UiText.T("Interface language", "Язык интерфейса"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9),
        };

        _languageCombo.Margin = new Padding(0, 5, 8, 5);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(_languageCombo, 1, row);
    }

    private void AddThemeRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = UiText.T("Interface theme", "Тема интерфейса"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9),
        };

        _themeCombo.Margin = new Padding(0, 5, 8, 5);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(_themeCombo, 1, row);
    }

    private void SaveSettings()
    {
        var selectedLanguage = (_languageCombo.SelectedItem as LanguageItem)?.Code ?? "en";
        var selectedTheme = (_themeCombo.SelectedItem as ThemeItem)?.Code ?? "system";
        var candidate = new ToolPathSettings
        {
            CsdkRoot = _csdkRootText.Text.Trim(),
            DeadlockToolsRoot = _deadlockToolsRootText.Text.Trim(),
            RetailDeadlockRoot = _retailDeadlockRootText.Text.Trim(),
            UiLanguage = selectedLanguage,
            UiTheme = selectedTheme,
        };

        if (!ValidatePaths(candidate, out var error))
        {
            MessageBox.Show(
                this,
                error,
                UiText.T("Invalid settings", "Некорректные настройки"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            ProjectStore.SaveToolPathSettings(candidate);
            RestartRequired = !string.Equals(_initialLanguage, selectedLanguage, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_initialTheme, selectedTheme, StringComparison.OrdinalIgnoreCase);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T("Could not save settings", "Не удалось сохранить настройки"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static bool ValidatePaths(ToolPathSettings candidate, out string error)
    {
        if (!Directory.Exists(candidate.CsdkRoot))
        {
            error = UiText.T(
                $"Reduced CSDK12 folder does not exist:\n{candidate.CsdkRoot}",
                $"Папка Reduced CSDK12 не существует:\n{candidate.CsdkRoot}");
            return false;
        }

        var candidatePaths = new DeadlimitPaths(candidate);
        if (!File.Exists(candidatePaths.CsdkLauncherPath))
        {
            error = UiText.T(
                $"csdkcfg.exe was not found in the selected Reduced CSDK12 root:\n{candidatePaths.CsdkLauncherPath}",
                $"csdkcfg.exe не найден в выбранном корне Reduced CSDK12:\n{candidatePaths.CsdkLauncherPath}");
            return false;
        }

        if (!Directory.Exists(candidate.DeadlockToolsRoot))
        {
            error = UiText.T(
                $"DeadlockTools folder does not exist:\n{candidate.DeadlockToolsRoot}",
                $"Папка DeadlockTools не существует:\n{candidate.DeadlockToolsRoot}");
            return false;
        }

        if (!File.Exists(candidatePaths.DeadlockToolsExePath))
        {
            error = UiText.T(
                $"DeadlockTools.exe was not found at the expected build path:\n{candidatePaths.DeadlockToolsExePath}",
                $"DeadlockTools.exe не найден по ожидаемому пути:\n{candidatePaths.DeadlockToolsExePath}");
            return false;
        }

        if (!Directory.Exists(candidate.RetailDeadlockRoot))
        {
            error = UiText.T(
                $"Retail Deadlock folder does not exist:\n{candidate.RetailDeadlockRoot}",
                $"Папка retail Deadlock не существует:\n{candidate.RetailDeadlockRoot}");
            return false;
        }

        var retailCitadel = Path.Combine(candidate.RetailDeadlockRoot, "game", "citadel");
        if (!Directory.Exists(retailCitadel))
        {
            error = UiText.T(
                $"The selected retail folder does not contain game\\citadel:\n{candidate.RetailDeadlockRoot}",
                $"В выбранной retail-папке нет game\\citadel:\n{candidate.RetailDeadlockRoot}");
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

    private sealed record LanguageItem(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ThemeItem(string Code, string Label)
    {
        public override string ToString() => Label;
    }
}
