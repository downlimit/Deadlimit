using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class SettingsForm : Form
{
    private readonly TextBox _csdkRootText = CreatePathTextBox();
    private readonly TextBox _deadlockToolsRootText = CreatePathTextBox();
    private readonly TextBox _retailDeadlockRootText = CreatePathTextBox();
    private readonly TextBox _projectsRootText = CreatePathTextBox();

    private readonly Label _csdkStatusLabel = CreateStatusLabel();
    private readonly Label _deadlockToolsStatusLabel = CreateStatusLabel();
    private readonly Label _retailDeadlockStatusLabel = CreateStatusLabel();
    private readonly Label _projectsStatusLabel = CreateStatusLabel();

    private readonly Button _csdkPrimaryButton = CreateActionButton();
    private readonly Button _csdkSetupButton = CreateActionButton();
    private readonly Button _deadlockToolsPrimaryButton = CreateActionButton();
    private readonly Button _retailDeadlockCheckButton = CreateActionButton();

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

    private readonly ToolTip _toolTip = CreateToolTip();
    private readonly ToolchainDependencyService _toolchain = new();
    private readonly List<Button> _pathButtons = [];
    private readonly string _initialLanguage;
    private readonly string _initialTheme;

    private ToolchainStatus _csdkStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _deadlockToolsStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _retailDeadlockStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _projectsStatus = new(ToolchainStatusKind.NotSpecified);
    private bool _busy;

    public SettingsForm()
    {
        var settings = ProjectStore.GetToolPathSettings();
        _initialLanguage = settings.UiLanguage;
        _initialTheme = settings.UiTheme;

        Text = UiText.T("Deadlimit Manager Settings", "Настройки Deadlimit Manager");
        Icon = LoadAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        Width = 1180;
        Height = 560;
        MinimumSize = new Size(1040, 520);
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        _csdkRootText.Text = settings.CsdkRoot;
        _deadlockToolsRootText.Text = settings.DeadlockToolsRoot;
        _retailDeadlockRootText.Text = settings.RetailDeadlockRoot;
        _projectsRootText.Text = settings.ProjectsRoot;

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
        ApplyInitialStatuses();
        UiTheme.ApplyCustomPalette(this, settings.UiTheme);

        Shown += async (_, _) => await RefreshAllStatusesAsync();
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

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = UiText.T(
                "Manage external tools and machine-local folders. Tool status is checked when this window opens. Language and theme changes are applied after Deadlimit Manager restarts.",
                "Управление внешними инструментами и локальными папками. Состояние инструментов проверяется при открытии окна. Смена языка и темы применяется после перезапуска Deadlimit Manager."),
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 0);

        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty,
        };

        var toolsGrid = CreateToolsGrid();
        AddCsdkRow(toolsGrid, 0);
        AddDeadlockToolsRow(toolsGrid, 1);
        AddRetailDeadlockRow(toolsGrid, 2);
        AddProjectsRow(toolsGrid, 3);
        content.Controls.Add(toolsGrid);

        var preferencesGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 14, 0, 0),
        };
        preferencesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        preferencesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddLanguageRow(preferencesGrid, 0);
        AddThemeRow(preferencesGrid, 1);
        AddMaxScriptFolderRow(preferencesGrid, 2);
        AddCsdkCacheToolRow(preferencesGrid, 3);
        content.Controls.Add(preferencesGrid);
        root.Controls.Add(content, 0, 1);

        var footer = new FlowLayoutPanel
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
        _toolTip.SetToolTip(
            saveButton,
            UiText.T(
                "Validate and save the selected folders and interface settings. Unspecified external-tool paths are allowed.",
                "Проверить и сохранить выбранные папки и настройки интерфейса. Пути к внешним инструментам можно оставить неуказанными."));
        _toolTip.SetToolTip(cancelButton, UiText.T("Close Settings without saving changes.", "Закрыть настройки без сохранения изменений."));

        footer.Controls.Add(cancelButton);
        footer.Controls.Add(saveButton);
        root.Controls.Add(footer, 0, 2);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        Controls.Add(root);
    }

    private static TableLayoutPanel CreateToolsGrid()
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 7,
            RowCount = 4,
            Margin = Padding.Empty,
            Width = 1120,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 135));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 105));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 470));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
        return grid;
    }

    private void AddCsdkRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_csdkRootText);
        var browseButton = CreateBrowseButton(
            _csdkRootText,
            UiText.T("Select an existing Reduced CSDK folder", "Выберите существующую папку Reduced CSDK"),
            RefreshCsdkStatusAsync);

        _csdkPrimaryButton.Click += async (_, _) => await HandleCsdkPrimaryActionAsync();
        _csdkSetupButton.Text = "SETUP";
        _csdkSetupButton.Click += async (_, _) => await SetupCsdkAsync();

        _toolTip.SetToolTip(
            _csdkPrimaryButton,
            UiText.T(
                "State-dependent Reduced CSDK action. INSTALL asks for an empty destination and downloads the current distribution. UPDATE overlays the current distribution over the configured installation. CHECK validates the installation and checks the latest published CSDK generation.",
                "Действие Reduced CSDK зависит от состояния. УСТАНОВИТЬ просит выбрать пустую папку и скачивает актуальный дистрибутив. ОБНОВИТЬ накладывает актуальный дистрибутив поверх настроенной установки. ПРОВЕРИТЬ валидирует установку и сверяет её с последним опубликованным поколением CSDK."));
        _toolTip.SetToolTip(
            _csdkSetupButton,
            UiText.T(
                "Run the optional full CSDK setup from the current installation guide. Deadlimit downloads the required Deadlock depots with DepotDownloader, extracts the downloaded VPK as-is, removes the temporary pak01 VPK set, then re-applies the current Reduced CSDK files. DepotDownloader may open a console for Steam QR authentication. The configured Retail Deadlock folder is only validated and is never modified.",
                "Выполнить дополнительную полную настройку CSDK по актуальной инструкции. Deadlimit скачивает требуемые депо Deadlock через DepotDownloader, извлекает скачанный VPK без декомпиляции, удаляет временный набор pak01 VPK и повторно накладывает актуальные файлы Reduced CSDK. DepotDownloader может открыть консоль для Steam-авторизации по QR. Указанная папка Retail Deadlock только проверяется и никогда не изменяется."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select an existing Reduced CSDK installation that was installed manually. Deadlimit validates it and immediately checks freshness.",
                "Выбрать существующую установку Reduced CSDK, установленную вручную. Deadlimit проверит её и сразу проверит актуальность."));

        AddToolRow(grid, row, "Reduced CSDK", _csdkStatusLabel, _csdkPrimaryButton, _csdkSetupButton, _csdkRootText, openButton, browseButton);
    }

    private void AddDeadlockToolsRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_deadlockToolsRootText);
        var browseButton = CreateBrowseButton(
            _deadlockToolsRootText,
            UiText.T("Select an existing DeadlockTools repository folder", "Выберите существующую папку репозитория DeadlockTools"),
            RefreshDeadlockToolsStatusAsync);

        _deadlockToolsPrimaryButton.Click += async (_, _) => await HandleDeadlockToolsPrimaryActionAsync();
        _toolTip.SetToolTip(
            _deadlockToolsPrimaryButton,
            UiText.T(
                "State-dependent DeadlockTools action. INSTALL clones the upstream repository into an empty folder and builds Release. UPDATE fast-forwards the Git checkout and rebuilds it. CHECK compares the local commit with the current upstream commit.",
                "Действие DeadlockTools зависит от состояния. УСТАНОВИТЬ клонирует upstream-репозиторий в пустую папку и собирает Release. ОБНОВИТЬ выполняет fast-forward Git checkout и повторную сборку. ПРОВЕРИТЬ сравнивает локальный коммит с текущим upstream-коммитом."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select an existing DeadlockTools folder. A Git checkout can be freshness-checked and updated automatically; a manually copied valid build can only be validated.",
                "Выбрать существующую папку DeadlockTools. Git checkout можно автоматически проверять на актуальность и обновлять; вручную скопированную валидную сборку можно только проверить."));

        AddToolRow(grid, row, "DeadlockTools", _deadlockToolsStatusLabel, _deadlockToolsPrimaryButton, CreateActionSpacer(), _deadlockToolsRootText, openButton, browseButton);
    }

    private void AddRetailDeadlockRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_retailDeadlockRootText);
        var browseButton = CreateBrowseButton(
            _retailDeadlockRootText,
            UiText.T("Select the Steam Project8Staging folder", "Выберите папку Steam Project8Staging"),
            () =>
            {
                RefreshRetailDeadlockStatus();
                return Task.CompletedTask;
            });

        _retailDeadlockCheckButton.Text = UiText.T("CHECK", "ПРОВЕРИТЬ");
        _retailDeadlockCheckButton.Click += (_, _) => RefreshRetailDeadlockStatus();
        _toolTip.SetToolTip(
            _retailDeadlockCheckButton,
            UiText.T(
                "Validate the selected Retail Deadlock installation. It must contain game\\citadel. Deadlimit does not install or update the Steam game from Settings.",
                "Проверить выбранную установку Retail Deadlock. В ней должен находиться game\\citadel. Deadlimit не устанавливает и не обновляет Steam-игру из настроек."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select the existing Steam Deadlock installation folder (Project8Staging).",
                "Выбрать существующую папку Steam-установки Deadlock (Project8Staging)."));

        AddToolRow(grid, row, "Retail Deadlock", _retailDeadlockStatusLabel, _retailDeadlockCheckButton, CreateActionSpacer(), _retailDeadlockRootText, openButton, browseButton);
    }

    private void AddProjectsRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_projectsRootText);
        var browseButton = CreateBrowseButton(
            _projectsRootText,
            UiText.T("Select the projects folder", "Выберите папку проектов"),
            () =>
            {
                RefreshProjectsStatus();
                return Task.CompletedTask;
            });
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select the root folder used to store Deadlimit projects. This is a workspace location and has no install/update lifecycle.",
                "Выбрать корневую папку для проектов Deadlimit. Это рабочая папка без установки или обновления."));

        AddToolRow(grid, row, UiText.T("Projects folder", "Папка проектов"), _projectsStatusLabel, CreateActionSpacer(), CreateActionSpacer(), _projectsRootText, openButton, browseButton);
    }

    private static void AddToolRow(
        TableLayoutPanel grid,
        int row,
        string name,
        Control status,
        Control primaryAction,
        Control secondaryAction,
        Control path,
        Control open,
        Control browse)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = name + ":",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9),
        }, 0, row);
        grid.Controls.Add(status, 1, row);
        grid.Controls.Add(primaryAction, 2, row);
        grid.Controls.Add(secondaryAction, 3, row);
        grid.Controls.Add(path, 4, row);
        grid.Controls.Add(open, 5, row);
        grid.Controls.Add(browse, 6, row);
    }

    private void AddLanguageRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(CreatePreferenceCaption(UiText.T("Interface language", "Язык интерфейса")), 0, row);
        _languageCombo.Margin = new Padding(0, 5, 8, 5);
        grid.Controls.Add(_languageCombo, 1, row);
    }

    private void AddThemeRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(CreatePreferenceCaption(UiText.T("Interface theme", "Тема интерфейса")), 0, row);
        _themeCombo.Margin = new Padding(0, 5, 8, 5);
        grid.Controls.Add(_themeCombo, 1, row);
    }

    private void AddMaxScriptFolderRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var openButton = new Button
        {
            Text = "📂 Deadlimit Max Script",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 0, 5),
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };
        openButton.Click += (_, _) => OpenMaxScriptFolder();
        _toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open the bundled Deadlimit Max Script folder containing DeadlimitPipelineScripts.ms and its README.",
                "Открыть встроенную папку Deadlimit Max Script с DeadlimitPipelineScripts.ms и README."));
        grid.Controls.Add(CreatePreferenceCaption("3ds Max"), 0, row);
        grid.Controls.Add(openButton, 1, row);
    }

    private void AddCsdkCacheToolRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var openButton = new Button
        {
            Text = "📂 CSDK Fast Startup Fix",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 0, 5),
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };
        openButton.Click += (_, _) => OpenCsdkCacheToolFolder();
        _toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open File Explorer and select the bundled CSDK cache repair CMD. Run it after a clean Reduced CSDK installation/update or whenever CSDK startup becomes slow.",
                "Открыть Проводник и выделить встроенный CMD для восстановления кеша CSDK. Запускайте его после чистой установки/обновления Reduced CSDK или если CSDK снова долго открывается."));
        grid.Controls.Add(CreatePreferenceCaption("CSDK"), 0, row);
        grid.Controls.Add(openButton, 1, row);
    }

    private void ApplyInitialStatuses()
    {
        SetCsdkStatus(string.IsNullOrWhiteSpace(_csdkRootText.Text)
            ? new ToolchainStatus(ToolchainStatusKind.NotSpecified)
            : new ToolchainStatus(ToolchainStatusKind.Installed));
        SetDeadlockToolsStatus(string.IsNullOrWhiteSpace(_deadlockToolsRootText.Text)
            ? new ToolchainStatus(ToolchainStatusKind.NotSpecified)
            : new ToolchainStatus(ToolchainStatusKind.Installed));
        RefreshRetailDeadlockStatus();
        RefreshProjectsStatus();
    }

    private async Task RefreshAllStatusesAsync()
    {
        if (_busy)
        {
            return;
        }

        RefreshRetailDeadlockStatus();
        RefreshProjectsStatus();
        await Task.WhenAll(RefreshCsdkStatusAsync(), RefreshDeadlockToolsStatusAsync());
    }

    private async Task RefreshCsdkStatusAsync()
    {
        SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
        try
        {
            SetCsdkStatus(await _toolchain.CheckCsdkAsync(_csdkRootText.Text.Trim()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
        }
    }

    private async Task RefreshDeadlockToolsStatusAsync()
    {
        SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
        try
        {
            SetDeadlockToolsStatus(await _toolchain.CheckDeadlockToolsAsync(_deadlockToolsRootText.Text.Trim()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
        }
    }

    private void RefreshRetailDeadlockStatus()
    {
        _retailDeadlockStatus = _toolchain.CheckRetailDeadlock(_retailDeadlockRootText.Text.Trim());
        ApplyStatus(_retailDeadlockStatusLabel, _retailDeadlockStatus);
        UpdateActionAvailability();
    }

    private void RefreshProjectsStatus()
    {
        _projectsStatus = _toolchain.CheckProjectsRoot(_projectsRootText.Text.Trim());
        ApplyStatus(_projectsStatusLabel, _projectsStatus);
        UpdateActionAvailability();
    }

    private void SetCsdkStatus(ToolchainStatus status)
    {
        _csdkStatus = status;
        ApplyStatus(_csdkStatusLabel, status);
        UpdateActionAvailability();
    }

    private void SetDeadlockToolsStatus(ToolchainStatus status)
    {
        _deadlockToolsStatus = status;
        ApplyStatus(_deadlockToolsStatusLabel, status);
        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        _csdkPrimaryButton.Text = PrimaryActionText(_csdkStatus.Kind);
        _deadlockToolsPrimaryButton.Text = PrimaryActionText(_deadlockToolsStatus.Kind);

        _csdkPrimaryButton.Enabled = !_busy && _csdkStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;
        _deadlockToolsPrimaryButton.Enabled = !_busy && _deadlockToolsStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;
        _retailDeadlockCheckButton.Enabled = !_busy;

        var csdkValid = Directory.Exists(_csdkRootText.Text.Trim())
            && File.Exists(Path.Combine(_csdkRootText.Text.Trim(), "csdkcfg.exe"));
        var retailValid = _retailDeadlockStatus.Kind == ToolchainStatusKind.Ready;
        _csdkSetupButton.Enabled = !_busy
            && csdkValid
            && retailValid
            && _csdkStatus.NetworkAvailable
            && _csdkStatus.Kind is not ToolchainStatusKind.NetworkIssue;

        foreach (var button in _pathButtons)
        {
            button.Enabled = !_busy;
        }
        _languageCombo.Enabled = !_busy;
        _themeCombo.Enabled = !_busy;
    }

    private static string PrimaryActionText(ToolchainStatusKind kind) => kind switch
    {
        ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath => UiText.T("INSTALL…", "УСТАНОВИТЬ…"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
        ToolchainStatusKind.Working => UiText.T("WORKING…", "ВЫПОЛНЕНИЕ…"),
        _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
    };

    private async Task HandleCsdkPrimaryActionAsync()
    {
        if (_csdkStatus.Kind == ToolchainStatusKind.UpdateAvailable)
        {
            await RunBusyOperationAsync(
                async progress =>
                {
                    SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Working));
                    var result = await _toolchain.UpdateCsdkAsync(_csdkRootText.Text.Trim(), progress);
                    _csdkRootText.Text = result.RootPath;
                    SetCsdkStatus(result.Status);
                },
                UiText.T("Could not update Reduced CSDK", "Не удалось обновить Reduced CSDK"));
            return;
        }

        if (_csdkStatus.Kind is ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath)
        {
            var destination = ChooseFolder(
                UiText.T("Choose the folder that will become the Reduced CSDK root", "Выберите папку, которая станет корнем Reduced CSDK"),
                _csdkRootText.Text,
                showNewFolderButton: true,
                fallbackInitialDirectory: DeadlimitPaths.DefaultWorkspaceRoot);
            if (destination is null)
            {
                return;
            }

            await RunBusyOperationAsync(
                async progress =>
                {
                    SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Working));
                    var result = await _toolchain.InstallCsdkAsync(destination, progress);
                    _csdkRootText.Text = result.RootPath;
                    SetCsdkStatus(result.Status);
                },
                UiText.T("Could not install Reduced CSDK", "Не удалось установить Reduced CSDK"));
            return;
        }

        await RunBusyOperationAsync(
            async _ => await RefreshCsdkStatusAsync(),
            UiText.T("Could not check Reduced CSDK", "Не удалось проверить Reduced CSDK"));
    }

    private async Task HandleDeadlockToolsPrimaryActionAsync()
    {
        if (_deadlockToolsStatus.Kind == ToolchainStatusKind.UpdateAvailable)
        {
            await RunBusyOperationAsync(
                async progress =>
                {
                    SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Working));
                    SetDeadlockToolsStatus(await _toolchain.UpdateDeadlockToolsAsync(_deadlockToolsRootText.Text.Trim(), progress));
                },
                UiText.T("Could not update DeadlockTools", "Не удалось обновить DeadlockTools"));
            return;
        }

        if (_deadlockToolsStatus.Kind is ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath)
        {
            var destination = ChooseFolder(
                UiText.T("Choose the folder that will become the DeadlockTools repository root", "Выберите папку, которая станет корнем репозитория DeadlockTools"),
                _deadlockToolsRootText.Text,
                showNewFolderButton: true,
                fallbackInitialDirectory: DeadlimitPaths.DefaultWorkspaceRoot);
            if (destination is null)
            {
                return;
            }

            await RunBusyOperationAsync(
                async progress =>
                {
                    SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Working));
                    var result = await _toolchain.InstallDeadlockToolsAsync(destination, progress);
                    _deadlockToolsRootText.Text = result.RootPath;
                    SetDeadlockToolsStatus(result.Status);
                },
                UiText.T("Could not install DeadlockTools", "Не удалось установить DeadlockTools"));
            return;
        }

        await RunBusyOperationAsync(
            async _ => await RefreshDeadlockToolsStatusAsync(),
            UiText.T("Could not check DeadlockTools", "Не удалось проверить DeadlockTools"));
    }

    private async Task SetupCsdkAsync()
    {
        await RunBusyOperationAsync(
            async progress =>
            {
                await _toolchain.SetupCsdkAsync(
                    _csdkRootText.Text.Trim(),
                    _retailDeadlockRootText.Text.Trim(),
                    progress);
                await RefreshCsdkStatusAsync();
            },
            UiText.T("Could not complete CSDK setup", "Не удалось выполнить настройку CSDK"));
    }

    private async Task RunBusyOperationAsync(Func<IProgress<string>, Task> operation, string errorTitle)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UseWaitCursor = true;
        UpdateActionAvailability();
        var baseTitle = UiText.T("Deadlimit Manager Settings", "Настройки Deadlimit Manager");
        var progress = new Progress<string>(message =>
        {
            if (!IsDisposed && !string.IsNullOrWhiteSpace(message))
            {
                Text = $"{baseTitle} — {message}";
            }
        });

        try
        {
            await operation(progress);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            MessageBox.Show(this, exception.Message, errorTitle, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            Text = baseTitle;
            UseWaitCursor = false;
            _busy = false;
            UpdateActionAvailability();
        }
    }

    private void ApplyStatus(Label label, ToolchainStatus status)
    {
        label.Text = StatusText(status.Kind);
        _toolTip.SetToolTip(label, string.IsNullOrWhiteSpace(status.Detail) ? label.Text : status.Detail);
    }

    private static string StatusText(ToolchainStatusKind kind) => kind switch
    {
        ToolchainStatusKind.NotSpecified => UiText.T("Not specified", "Не указано"),
        ToolchainStatusKind.Installed => UiText.T("Installed", "Установлено"),
        ToolchainStatusKind.UpToDate => UiText.T("Up to date", "Актуально"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("Update available", "Есть обновление"),
        ToolchainStatusKind.InvalidPath => UiText.T("Invalid path", "Неверный путь"),
        ToolchainStatusKind.NetworkIssue => UiText.T("Network issue", "Ошибка сети"),
        ToolchainStatusKind.Checking => UiText.T("Checking…", "Проверка…"),
        ToolchainStatusKind.Working => UiText.T("Working…", "Выполнение…"),
        ToolchainStatusKind.Ready => UiText.T("Ready", "Готово"),
        _ => UiText.T("Unknown", "Неизвестно"),
    };

    private Button CreateOpenFolderButton(TextBox textBox)
    {
        var button = new Button
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
        button.Click += (_, _) => OpenConfiguredFolder(textBox.Text);
        _toolTip.SetToolTip(
            button,
            UiText.T(
                "Open the currently selected folder in File Explorer. This does not change the configured path.",
                "Открыть выбранную папку в Проводнике. Это не изменяет настроенный путь."));
        _pathButtons.Add(button);
        return button;
    }

    private Button CreateBrowseButton(TextBox textBox, string description, Func<Task> afterSelection)
    {
        var button = new Button
        {
            Text = UiText.T("BROWSE…", "ОБЗОР…"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 5, 0, 5),
        };
        button.Click += async (_, _) =>
        {
            var selected = ChooseFolder(description, textBox.Text, showNewFolderButton: false);
            if (selected is null)
            {
                return;
            }

            textBox.Text = selected;
            try
            {
                await afterSelection();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    UiText.T("Could not check selected folder", "Не удалось проверить выбранную папку"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
        _pathButtons.Add(button);
        return button;
    }

    private void OpenMaxScriptFolder()
    {
        try
        {
            OpenConfiguredFolder(VertexColorMaxScriptService.GetBundledScriptFolder());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, UiText.T("MaxScript folder unavailable", "Папка MaxScript недоступна"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenCsdkCacheToolFolder()
    {
        try
        {
            var commandPath = CsdkAssetCacheToolService.GetBundledCommandPath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{commandPath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, UiText.T("CSDK cache tool unavailable", "Инструмент кеша CSDK недоступен"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenConfiguredFolder(string path)
    {
        var folder = path.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(
                this,
                UiText.T(
                    string.IsNullOrWhiteSpace(folder) ? "No folder is currently selected." : $"The selected folder does not exist:\n{folder}",
                    string.IsNullOrWhiteSpace(folder) ? "Папка пока не выбрана." : $"Выбранная папка не существует:\n{folder}"),
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
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, UiText.T("Could not open folder", "Не удалось открыть папку"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show(this, error, UiText.T("Invalid settings", "Некорректные настройки"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MessageBox.Show(this, exception.Message, UiText.T("Could not save settings", "Не удалось сохранить настройки"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static bool ValidatePaths(ToolPathSettings candidate, out string error)
    {
        if (!ValidateOptionalDirectory(candidate.ProjectsRoot, UiText.T("Projects folder", "Папка проектов"), out error))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(candidate.CsdkRoot))
        {
            if (!Directory.Exists(candidate.CsdkRoot))
            {
                error = UiText.T($"Reduced CSDK folder does not exist:\n{candidate.CsdkRoot}", $"Папка Reduced CSDK не существует:\n{candidate.CsdkRoot}");
                return false;
            }
            if (!File.Exists(Path.Combine(candidate.CsdkRoot, "csdkcfg.exe")))
            {
                error = UiText.T($"csdkcfg.exe was not found in the selected Reduced CSDK root:\n{candidate.CsdkRoot}", $"csdkcfg.exe не найден в выбранном корне Reduced CSDK:\n{candidate.CsdkRoot}");
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.DeadlockToolsRoot))
        {
            if (!Directory.Exists(candidate.DeadlockToolsRoot))
            {
                error = UiText.T($"DeadlockTools folder does not exist:\n{candidate.DeadlockToolsRoot}", $"Папка DeadlockTools не существует:\n{candidate.DeadlockToolsRoot}");
                return false;
            }
            var executable = Path.Combine(candidate.DeadlockToolsRoot, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");
            if (!File.Exists(executable))
            {
                error = UiText.T($"DeadlockTools.exe was not found at the expected Release build path:\n{executable}", $"DeadlockTools.exe не найден по ожидаемому пути Release:\n{executable}");
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.RetailDeadlockRoot))
        {
            if (!Directory.Exists(candidate.RetailDeadlockRoot))
            {
                error = UiText.T($"Retail Deadlock folder does not exist:\n{candidate.RetailDeadlockRoot}", $"Папка Retail Deadlock не существует:\n{candidate.RetailDeadlockRoot}");
                return false;
            }
            if (!Directory.Exists(Path.Combine(candidate.RetailDeadlockRoot, "game", "citadel")))
            {
                error = UiText.T($"The selected Retail Deadlock folder does not contain game\\citadel:\n{candidate.RetailDeadlockRoot}", $"В выбранной папке Retail Deadlock нет game\\citadel:\n{candidate.RetailDeadlockRoot}");
                return false;
            }
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateOptionalDirectory(string path, string label, out string error)
    {
        if (string.IsNullOrWhiteSpace(path) || Directory.Exists(path))
        {
            error = string.Empty;
            return true;
        }

        error = UiText.T($"{label} does not exist:\n{path}", $"{label} не существует:\n{path}");
        return false;
    }

    private static Icon LoadAppIcon()
    {
        using var stream = typeof(SettingsForm).Assembly.GetManifestResourceStream("DeadlimitManager.AppIcon.ico")
            ?? throw new InvalidOperationException("Embedded Deadlimit Manager application icon was not found.");
        using var source = new Icon(stream);
        return (Icon)source.Clone();
    }

    private static TextBox CreatePathTextBox() => new()
    {
        ReadOnly = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(0, 5, 8, 5),
    };

    private static Label CreateStatusLabel() => new()
    {
        AutoSize = false,
        Width = 125,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 5, 8, 5),
    };

    private static Button CreateActionButton() => new()
    {
        AutoSize = false,
        Width = 96,
        Height = 26,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 7, 4),
    };

    private static Control CreateActionSpacer() => new Panel
    {
        Width = 1,
        Height = 26,
        Margin = Padding.Empty,
    };

    private static Label CreatePreferenceCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 9, 12, 9),
    };

    private static ToolTip CreateToolTip() => new()
    {
        ShowAlways = true,
        InitialDelay = 350,
        ReshowDelay = 100,
        AutoPopDelay = 16000,
    };

    private static string? ChooseFolder(
        string description,
        string currentPath,
        bool showNewFolderButton,
        string? fallbackInitialDirectory = null)
    {
        var initialDirectory = Directory.Exists(currentPath)
            ? currentPath
            : Directory.Exists(fallbackInitialDirectory)
                ? fallbackInitialDirectory!
                : string.Empty;
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = showNewFolderButton,
            InitialDirectory = initialDirectory,
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
