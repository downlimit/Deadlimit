using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _projectsRootText = new() { Dock = DockStyle.Fill };
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
        Height = 440;
        MinimumSize = new Size(720, 420);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _projectsRootText.Text = settings.ProjectsRoot;
        _csdkRootText.Text = paths.CsdkRoot;
        _deadlockToolsRootText.Text = paths.DeadlockToolsRoot;
        _retailDeadlockRootText.Text = paths.RetailDeadlockRoot;

        _languageCombo.Items.Add(new LanguageItem("en", "English"));
        _languageCombo.Items.Add(new LanguageItem("ru", "Русский"));
        _languageCombo.SelectedIndex = string.Equals(settings.UiLanguage, "ru", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        _themeCombo.Items.Add(new ThemeItem("system", UiText.T("System theme", "Системная тема")));
        _themeCombo.Items.Add(new ThemeItem("light", UiText.T("Light", "Светлая")));
        _themeCombo.Items.Add(new ThemeItem("gray", UiText.T("Gray", "Серая")));
        _themeCombo.Items.Add(new ThemeItem("dark", UiText.T("Original theme", "Исходная тема")));
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
                "Projects folder, machine-local tool paths, interface language and theme. Language and theme changes are applied after Deadlimit restarts.",
                "Папка проектов, локальные пути к инструментам, язык и тема интерфейса. Смена языка и темы применяется после перезапуска Deadlimit."),
            Margin = new Padding(0, 0, 0, 12),
        };
        root.Controls.Add(description, 0, 0);

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 4,
            RowCount = 6,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddPathRow(grid, 0, UiText.T("Projects folder", "Папка проектов"), _projectsRootText, UiText.T("Select projects folder", "Выберите папку проектов"));
        AddPathRow(grid, 1, "Reduced CSDK12", _csdkRootText, UiText.T("Select Reduced_CSDK_12 root", "Выберите корень Reduced_CSDK_12"));
        AddPathRow(grid, 2, "DeadlockTools", _deadlockToolsRootText, UiText.T("Select DeadlockTools root", "Выберите корень DeadlockTools"));
        AddPathRow(grid, 3, UiText.T("Retail Deadlock", "Retail Deadlock"), _retailDeadlockRootText, UiText.T("Select Steam Project8Staging root", "Выберите корень Steam Project8Staging"));
        AddLanguageRow(grid, 4);
        AddThemeRow(grid, 5);
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

        var toolTip = CreateToolTip();
        toolTip.SetToolTip(
            saveButton,
            UiText.T(
                "Validate and save all paths and interface settings.\n\nLanguage or theme changes restart Deadlimit before they take effect.",
                "Проверить и сохранить все пути и настройки интерфейса.\n\nПосле смены языка или темы Deadlimit перезапустится, чтобы применить изменения."));
        toolTip.SetToolTip(
            cancelButton,
            UiText.T(
                "Close Settings without saving changes.",
                "Закрыть настройки без сохранения изменений."));

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

        var openButton = new Button
        {
            Text = "📂",
            AutoSize = false,
            Width = 30,
            Height = 24,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 6, 5),
            Padding = Padding.Empty,
            TabStop = false,
            Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        openButton.Click += (_, _) => OpenConfiguredFolder(textBox.Text);

        var browseButton = new Button
        {
            Text = UiText.T("BROWSE", "ОБЗОР"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 0, 5),
        };
        browseButton.Click += (_, _) =>
        {
            var selected = ChooseFolder(dialogDescription, textBox.Text);
            if (selected is not null)
            {
                textBox.Text = selected;
            }
        };

        var toolTip = CreateToolTip();
        toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open the folder currently entered in this row.\n\nThis does not change the configured path.",
                "Открыть папку, которая сейчас указана в этой строке.\n\nЭто не изменяет сохранённый путь."));
        toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Choose a different folder for this setting.\n\nThe new path is applied only after you press SAVE.",
                "Выбрать другую папку для этой настройки.\n\nНовый путь применится только после нажатия СОХРАНИТЬ."));

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(textBox, 1, row);
        grid.Controls.Add(openButton, 2, row);
        grid.Controls.Add(browseButton, 3, row);
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
        grid.SetColumnSpan(_languageCombo, 3);
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
        grid.SetColumnSpan(_themeCombo, 3);
    }

    private void OpenConfiguredFolder(string path)
    {
        var folder = path.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                UiText.T(
                    $"The configured folder does not exist:\n{folder}",
                    $"Указанная папка не существует:\n{folder}"),
                UiText.T("Folder unavailable", "Папка недоступна"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{folder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T("Could not open folder", "Не удалось открыть папку"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void SaveSettings()
    {
        var selectedLanguage = (_languageCombo.SelectedItem as LanguageItem)?.Code ?? "en";
        var selectedTheme = (_themeCombo.SelectedItem as ThemeItem)?.Code ?? "system";
        var candidate = new ToolPathSettings
        {
            ProjectsRoot = _projectsRootText.Text.Trim(),
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
        if (!Directory.Exists(candidate.ProjectsRoot))
        {
            error = UiText.T(
                $"Projects folder does not exist:\n{candidate.ProjectsRoot}",
                $"Папка проектов не существует:\n{candidate.ProjectsRoot}");
            return false;
        }

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

    private static ToolTip CreateToolTip() => new()
    {
        ShowAlways = true,
        InitialDelay = 350,
        ReshowDelay = 100,
        AutoPopDelay = 10000,
    };

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
