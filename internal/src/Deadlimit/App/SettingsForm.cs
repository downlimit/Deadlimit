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
    private readonly Button _csdkSetupButton = CreateUtilityActionButton("\uE713");
    private readonly Button _deadlockToolsPrimaryButton = CreateActionButton();
    private readonly Button _retailDeadlockCheckButton = CreateActionButton();
    private readonly Button _retailDeadlockFindButton = CreateUtilityActionButton("\uE721");

    private readonly ComboBox _languageCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left,
        Width = 170,
    };

    private readonly ComboBox _themeCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left,
        Width = 170,
    };

    private readonly RichToolTip _toolTip = new();
    private readonly ToolchainDependencyService _toolchain = new();
    private readonly List<Button> _pathButtons = [];
    private readonly string _initialLanguage;
    private readonly string _initialTheme;

    private ToolchainStatus _csdkStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _deadlockToolsStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _retailDeadlockStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _projectsStatus = new(ToolchainStatusKind.NotSpecified);
    private bool _busy;
    private bool _themePreviewApplied;

    public SettingsForm()
    {
        var settings = ProjectStore.GetToolPathSettings();
        _initialLanguage = settings.UiLanguage;
        _initialTheme = settings.UiTheme;

        Text = UiText.T("Deadlimit Manager Settings", "Настройки Deadlimit Manager");
        Icon = LoadAppIcon();
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ClientSize = new Size(940, 510);
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

        _themeCombo.Items.Add(new ThemeItem("system", UiText.T("System", "Системная")));
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
        ApplyInitialStatuses();
        UiTheme.ApplyCustomPalette(this, settings.UiTheme);
        ReapplySemanticStatusColors();

        _themeCombo.SelectedIndexChanged += (_, _) => PreviewTheme();
        Shown += async (_, _) => await RefreshAllStatusesAsync();
    }

    public bool RestartRequired => false;

    public bool LanguageChanged => InterfaceChanged;

    public bool InterfaceChanged { get; private set; }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (DialogResult != DialogResult.OK && _themePreviewApplied)
        {
            RestoreThemePreview();
        }
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = UiText.T(
                "Tool status is checked when this window opens. Theme changes preview immediately; Save applies language and theme without restarting Deadlimit Manager.",
                "Состояние инструментов проверяется при открытии окна. Тема меняется сразу; после сохранения язык и тема применяются без перезапуска Deadlimit Manager."),
            Margin = new Padding(0, 0, 0, 10),
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
        AddDeadlockGameRow(toolsGrid, 0);
        AddCsdkRow(toolsGrid, 1);
        AddDeadlockToolsRow(toolsGrid, 2);
        AddProjectsRow(toolsGrid, 3);
        content.Controls.Add(toolsGrid);

        var preferencesGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 10, 0, 0),
        };
        preferencesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
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
            Margin = new Padding(0, 10, 0, 0),
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
                "Validate and save the selected folders and interface settings.\n\nUnspecified external-tool paths are allowed.",
                "Проверить и сохранить выбранные папки и настройки интерфейса.\n\nПути к внешним инструментам можно оставить неуказанными."));
        _toolTip.SetToolTip(
            cancelButton,
            UiText.T(
                "Close Settings without saving changes.\n\nA theme preview is reverted automatically.",
                "Закрыть настройки без сохранения изменений.\n\nПредпросмотр темы будет автоматически отменён."));

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
            Width = 910,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 33));
        return grid;
    }

    private void AddCsdkRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_csdkRootText);
        var browseButton = CreateBrowseButton(
            _csdkRootText,
            UiText.T("Select an existing Reduced CSDK folder", "Выберите существующую папку Reduced CSDK"),
            () => RefreshCsdkStatusAsync());

        _csdkPrimaryButton.Click += async (_, _) => await HandleCsdkPrimaryActionAsync();
        _csdkSetupButton.AccessibleName = UiText.T("Full CSDK setup", "Полная настройка CSDK");
        _csdkSetupButton.Click += async (_, _) => await SetupCsdkAsync();

        _toolTip.SetToolTip(
            _csdkPrimaryButton,
            UiText.T(
                "**INSTALL…** selects an empty folder and downloads the current Reduced CSDK.\n\n**UPDATE…** overlays the current distribution onto the configured CSDK folder.\n\n**CHECK** validates the installation and checks the latest published CSDK generation.",
                "**УСТАНОВИТЬ…** выбирает пустую папку и скачивает актуальный Reduced CSDK.\n\n**ОБНОВИТЬ…** накладывает актуальный дистрибутив поверх настроенной папки CSDK.\n\n**ПРОВЕРИТЬ** валидирует установку и проверяет последнее опубликованное поколение CSDK."));
        _toolTip.SetToolTip(
            _csdkSetupButton,
            UiText.T(
                "Run the optional full CSDK setup from the current installation guide.\n\nDeadlimit downloads the required Deadlock depots, extracts the downloaded VPK as-is, removes the temporary pak01 VPK set, then re-applies Reduced CSDK.\n\nDepotDownloader may open a console for Steam QR authentication.\n\nThe configured Deadlock client folder is only validated and is **never modified**.",
                "Выполнить дополнительную полную настройку CSDK по актуальной инструкции.\n\nDeadlimit скачивает нужные депо Deadlock, извлекает скачанный VPK без декомпиляции, удаляет временный набор pak01 VPK и повторно накладывает Reduced CSDK.\n\nDepotDownloader может открыть консоль для Steam-авторизации по QR.\n\nПапка Deadlock клиента только проверяется и **никогда не изменяется**."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select a Reduced CSDK installation that already exists on this PC.\n\nDeadlimit validates it and immediately checks freshness.",
                "Выбрать уже существующую на этом ПК установку Reduced CSDK.\n\nDeadlimit проверит её структуру и сразу запустит проверку актуальности."));

        AddToolRow(grid, row, "Reduced CSDK", _csdkStatusLabel, _csdkPrimaryButton, _csdkSetupButton, _csdkRootText, openButton, browseButton);
    }

    private void AddDeadlockToolsRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_deadlockToolsRootText);
        var browseButton = CreateBrowseButton(
            _deadlockToolsRootText,
            UiText.T("Select an existing DeadlockTools folder", "Выберите существующую папку DeadlockTools"),
            () => RefreshDeadlockToolsStatusAsync());

        _deadlockToolsPrimaryButton.Click += async (_, _) => await HandleDeadlockToolsPrimaryActionAsync();
        _toolTip.SetToolTip(
            _deadlockToolsPrimaryButton,
            UiText.T(
                "**INSTALL…** downloads the latest official Windows x64 release from GitHub into an empty folder.\n\n**UPDATE…** updates a Deadlimit-managed release installation. Git checkouts are updated through Git and rebuilt.\n\n**CHECK** compares a managed install or Git checkout with the current upstream state.\n\nIf the version of a manually copied build cannot be identified, **INSTALL…** remains available instead of offering a meaningless CHECK.",
                "**УСТАНОВИТЬ…** скачивает последний официальный Windows x64 release с GitHub в пустую папку.\n\n**ОБНОВИТЬ…** обновляет установку release, которой управляет Deadlimit. Git checkout обновляется через Git и пересобирается.\n\n**ПРОВЕРИТЬ** сравнивает управляемую установку или Git checkout с текущим upstream.\n\nЕсли версию вручную скопированной сборки определить нельзя, остаётся доступна кнопка **УСТАНОВИТЬ…**, а не бесполезная проверка."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select an existing DeadlockTools folder.\n\nDeadlimit can fully track installations it installed itself and Git checkouts. A manually copied build may show **Version unknown**.",
                "Выбрать существующую папку DeadlockTools.\n\nDeadlimit полностью отслеживает установки, которые установил сам, и Git checkout. Вручную скопированная сборка может показывать **Версия неизвестна**."));

        AddToolRow(grid, row, "DeadlockTools", _deadlockToolsStatusLabel, _deadlockToolsPrimaryButton, CreateActionSpacer(), _deadlockToolsRootText, openButton, browseButton);
    }

    private void AddDeadlockGameRow(TableLayoutPanel grid, int row)
    {
        var openButton = CreateOpenFolderButton(_retailDeadlockRootText);
        var browseButton = CreateBrowseButton(
            _retailDeadlockRootText,
            UiText.T("Select the installed Deadlock folder (Project8Staging)", "Выберите папку установленного Deadlock (Project8Staging)"),
            () =>
            {
                RefreshDeadlockGameStatus();
                return Task.CompletedTask;
            });

        _retailDeadlockCheckButton.Text = UiText.T("CHECK", "ПРОВЕРИТЬ");
        _retailDeadlockCheckButton.Click += async (_, _) =>
        {
            SetRetailStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
            RefreshDeadlockGameStatus();
        };

        _toolTip.SetToolTip(
            _retailDeadlockCheckButton,
            UiText.T(
                "Validate the installed **Deadlock client** used by Steam.\n\nDeadlimit checks that the selected Project8Staging folder contains the expected game files.\n\nDeadlimit does not install or update the Steam game from Settings.",
                "Проверить установленный **Deadlock клиент**, который запускается через Steam.\n\nDeadlimit проверит, что выбранная папка Project8Staging содержит ожидаемые игровые файлы.\n\nDeadlimit не устанавливает и не обновляет игру через Steam из окна настроек."));
        _toolTip.SetToolTip(
            browseButton,
            UiText.T(
                "Select the folder where the **Deadlock client** is installed.\n\nFor a standard Steam installation this folder is named Project8Staging.",
                "Выбрать папку, в которой установлен **Deadlock клиент**.\n\nВ стандартной установке Steam эта папка называется Project8Staging."));
        _retailDeadlockFindButton.AccessibleName = UiText.T("Find Deadlock automatically", "Найти Deadlock автоматически");
        _retailDeadlockFindButton.Click += async (_, _) => await AutoFindDeadlockAsync();
        _toolTip.SetToolTip(
            _retailDeadlockFindButton,
            UiText.T(
                "Try to find the installed **Deadlock client** automatically.\n\nDeadlimit checks Steam library folders first, then common Steam locations on local drives. If Deadlock is found, its folder is filled in automatically. Nothing is modified.",
                "Попытаться автоматически найти установленный **Deadlock клиент**.\n\nDeadlimit сначала проверит библиотеки Steam, затем типичные папки Steam на локальных дисках. Если Deadlock найден, путь подставится автоматически. Никакие файлы не изменяются."));

        AddToolRow(
            grid,
            row,
            UiText.T("Deadlock client", "Deadlock клиент"),
            _retailDeadlockStatusLabel,
            _retailDeadlockCheckButton,
            _retailDeadlockFindButton,
            _retailDeadlockRootText,
            openButton,
            browseButton);
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
                "Select the root folder used to store Deadlimit projects.\n\nThis is a workspace folder, so it has no install or update lifecycle.",
                "Выбрать корневую папку для проектов Deadlimit.\n\nЭто рабочая папка, поэтому у неё нет установки или обновления."));

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
            Margin = new Padding(0, 8, 10, 8),
        }, 0, row);
        grid.Controls.Add(status, 1, row);
        grid.Controls.Add(path, 2, row);
        grid.Controls.Add(open, 3, row);
        grid.Controls.Add(browse, 4, row);
        grid.Controls.Add(primaryAction, 5, row);
        grid.Controls.Add(secondaryAction, 6, row);
    }

    private void AddLanguageRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(CreatePreferenceCaption(UiText.T("Interface language", "Язык интерфейса")), 0, row);
        _languageCombo.Margin = new Padding(0, 4, 8, 4);
        grid.Controls.Add(_languageCombo, 1, row);
    }

    private void AddThemeRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(CreatePreferenceCaption(UiText.T("Interface theme", "Тема интерфейса")), 0, row);
        _themeCombo.Margin = new Padding(0, 4, 8, 4);
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
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };
        openButton.Click += (_, _) => OpenMaxScriptFolder();
        _toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open the bundled Deadlimit Max Script folder.\n\nIt contains DeadlimitPipelineScripts.ms and its README.",
                "Открыть встроенную папку Deadlimit Max Script.\n\nВ ней находятся DeadlimitPipelineScripts.ms и README."));
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
            Margin = new Padding(0, 4, 0, 4),
            Font = new Font("Segoe UI Emoji", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };
        openButton.Click += (_, _) => OpenCsdkCacheToolFolder();
        _toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open File Explorer and select the bundled CSDK cache repair CMD.\n\nRun it after a clean Reduced CSDK installation/update or whenever CSDK startup becomes slow.",
                "Открыть Проводник и выделить встроенный CMD для восстановления кеша CSDK.\n\nЗапускайте его после чистой установки/обновления Reduced CSDK или если CSDK снова долго открывается."));
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
        RefreshDeadlockGameStatus();
        RefreshProjectsStatus();
    }

    private async Task RefreshAllStatusesAsync()
    {
        if (_busy)
        {
            return;
        }

        RefreshDeadlockGameStatus();
        RefreshProjectsStatus();
        SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
        SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
        await Task.Yield();
        await Task.WhenAll(RefreshCsdkStatusAsync(skipCheckingState: true), RefreshDeadlockToolsStatusAsync(skipCheckingState: true));
    }

    private async Task RefreshCsdkStatusAsync(bool skipCheckingState = false)
    {
        if (!skipCheckingState)
        {
            SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            SetCsdkStatus(await _toolchain.CheckCsdkAsync(_csdkRootText.Text.Trim()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
        }
    }

    private async Task RefreshDeadlockToolsStatusAsync(bool skipCheckingState = false)
    {
        if (!skipCheckingState)
        {
            SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            SetDeadlockToolsStatus(await _toolchain.CheckDeadlockToolsAsync(_deadlockToolsRootText.Text.Trim()));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
        }
    }

    private void RefreshDeadlockGameStatus()
    {
        SetRetailStatus(_toolchain.CheckRetailDeadlock(_retailDeadlockRootText.Text.Trim()));
    }

    private async Task AutoFindDeadlockAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        UseWaitCursor = true;
        SetRetailStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
        await Task.Yield();

        try
        {
            var found = await Task.Run(DeadlockInstallLocator.FindInstallation);
            if (string.IsNullOrWhiteSpace(found))
            {
                RefreshDeadlockGameStatus();
                MessageBox.Show(
                    this,
                    UiText.T(
                        "Deadlock was not found automatically. Use BROWSE… to select the Project8Staging folder manually.",
                        "Deadlock не удалось найти автоматически. Нажмите ОБЗОР… и выберите папку Project8Staging вручную."),
                    UiText.T("Deadlock not found", "Deadlock не найден"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _retailDeadlockRootText.Text = found;
            RefreshDeadlockGameStatus();
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or InvalidOperationException)
        {
            RefreshDeadlockGameStatus();
            MessageBox.Show(
                this,
                exception.Message,
                UiText.T("Could not search for Deadlock", "Не удалось найти Deadlock"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _busy = false;
            UpdateActionAvailability();
        }
    }

    private void RefreshProjectsStatus()
    {
        _projectsStatus = _toolchain.CheckProjectsRoot(_projectsRootText.Text.Trim());
        ApplyStatus(_projectsStatusLabel, _projectsStatus, StatusContext.Projects);
        UpdateActionAvailability();
    }

    private void SetCsdkStatus(ToolchainStatus status)
    {
        _csdkStatus = status;
        ApplyStatus(_csdkStatusLabel, status, StatusContext.Csdk);
        UpdateActionAvailability();
    }

    private void SetDeadlockToolsStatus(ToolchainStatus status)
    {
        _deadlockToolsStatus = status;
        ApplyStatus(_deadlockToolsStatusLabel, status, StatusContext.DeadlockTools);
        UpdateActionAvailability();
    }

    private void SetRetailStatus(ToolchainStatus status)
    {
        _retailDeadlockStatus = status;
        ApplyStatus(_retailDeadlockStatusLabel, status, StatusContext.DeadlockGame);
        UpdateActionAvailability();
    }

    private void UpdateActionAvailability()
    {
        _csdkPrimaryButton.Text = CsdkPrimaryActionText(_csdkStatus.Kind);
        _deadlockToolsPrimaryButton.Text = DeadlockToolsPrimaryActionText(_deadlockToolsStatus.Kind);
        _retailDeadlockCheckButton.Text = _retailDeadlockStatus.Kind == ToolchainStatusKind.Checking
            ? UiText.T("CHECKING…", "ПРОВЕРКА…")
            : UiText.T("CHECK", "ПРОВЕРИТЬ");

        _csdkPrimaryButton.Enabled = !_busy && _csdkStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;
        _deadlockToolsPrimaryButton.Enabled = !_busy && _deadlockToolsStatus.Kind is not ToolchainStatusKind.Checking and not ToolchainStatusKind.Working;
        _retailDeadlockCheckButton.Enabled = !_busy && _retailDeadlockStatus.Kind != ToolchainStatusKind.Checking;
        _retailDeadlockFindButton.Enabled = !_busy;

        var csdkValid = Directory.Exists(_csdkRootText.Text.Trim())
            && File.Exists(Path.Combine(_csdkRootText.Text.Trim(), "csdkcfg.exe"));
        var gameClientValid = _retailDeadlockStatus.Kind == ToolchainStatusKind.Ready;
        _csdkSetupButton.Enabled = !_busy
            && csdkValid
            && gameClientValid
            && _csdkStatus.NetworkAvailable
            && _csdkStatus.Kind is not ToolchainStatusKind.NetworkIssue;

        foreach (var button in _pathButtons)
        {
            button.Enabled = !_busy;
        }
        _languageCombo.Enabled = !_busy;
        _themeCombo.Enabled = !_busy;

        _csdkPrimaryButton.Refresh();
        _deadlockToolsPrimaryButton.Refresh();
        _retailDeadlockCheckButton.Refresh();
        _retailDeadlockFindButton.Refresh();
    }

    private static string CsdkPrimaryActionText(ToolchainStatusKind kind) => kind switch
    {
        ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath => UiText.T("INSTALL…", "УСТАНОВИТЬ…"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
        ToolchainStatusKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
        ToolchainStatusKind.Working => UiText.T("WORKING…", "ВЫПОЛНЕНИЕ…"),
        _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
    };

    private static string DeadlockToolsPrimaryActionText(ToolchainStatusKind kind) => kind switch
    {
        ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath or ToolchainStatusKind.Installed => UiText.T("INSTALL…", "УСТАНОВИТЬ…"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
        ToolchainStatusKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
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

        if (_deadlockToolsStatus.Kind is ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath or ToolchainStatusKind.Installed)
        {
            var current = _deadlockToolsRootText.Text.Trim();
            var initialDirectory = Directory.Exists(current)
                && string.Equals(new DirectoryInfo(current).Name, "DeadlockTools", StringComparison.OrdinalIgnoreCase)
                    ? Directory.GetParent(current)?.FullName ?? current
                    : current;
            var destination = ChooseFolder(
                UiText.T("Choose an empty folder for DeadlockTools", "Выберите пустую папку для DeadlockTools"),
                initialDirectory,
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
            if (_csdkStatus.Kind == ToolchainStatusKind.Working)
            {
                await RefreshCsdkStatusAsync();
            }
            if (_deadlockToolsStatus.Kind == ToolchainStatusKind.Working)
            {
                await RefreshDeadlockToolsStatusAsync();
            }
        }
        finally
        {
            Text = baseTitle;
            UseWaitCursor = false;
            _busy = false;
            UpdateActionAvailability();
        }
    }

    private void ApplyStatus(Label label, ToolchainStatus status, StatusContext context)
    {
        label.Text = FormatStatus(status, context);
        label.ForeColor = StatusColor(status.Kind);
        if (!label.Font.Bold)
        {
            label.Font = new Font(label.Font, FontStyle.Bold);
        }
        label.Refresh();
        _toolTip.SetToolTip(label, StatusDetail(status, context));
    }

    private static string FormatStatus(ToolchainStatus status, StatusContext context)
    {
        return status.Kind switch
        {
            ToolchainStatusKind.NotSpecified => UiText.T("○ Not specified", "○ Не указано"),
            ToolchainStatusKind.Installed when context == StatusContext.DeadlockTools => UiText.T("● Version unknown", "● Версия неизвестна"),
            ToolchainStatusKind.Installed => UiText.T("● Version unknown", "● Версия неизвестна"),
            ToolchainStatusKind.UpToDate when context == StatusContext.Csdk && status.InstalledGeneration is int generation => $"✓ {UiText.T("Up to date", "Актуально")} · CSDK {generation}",
            ToolchainStatusKind.UpToDate when context == StatusContext.DeadlockTools && !string.IsNullOrWhiteSpace(status.InstalledVersion) => $"✓ {UiText.T("Up to date", "Актуально")} · {status.InstalledVersion}",
            ToolchainStatusKind.UpToDate => $"✓ {UiText.T("Up to date", "Актуально")}",
            ToolchainStatusKind.UpdateAvailable when context == StatusContext.Csdk && status.AvailableGeneration is int availableGeneration => $"↑ {UiText.T("Update", "Обновление")} · CSDK {availableGeneration}",
            ToolchainStatusKind.UpdateAvailable when context == StatusContext.DeadlockTools && !string.IsNullOrWhiteSpace(status.AvailableVersion) => $"↑ {UiText.T("Update", "Обновление")} · {status.AvailableVersion}",
            ToolchainStatusKind.UpdateAvailable => $"↑ {UiText.T("Update available", "Есть обновление")}",
            ToolchainStatusKind.InvalidPath => $"× {UiText.T("Invalid path", "Неверный путь")}",
            ToolchainStatusKind.NetworkIssue => $"! {UiText.T("Network issue", "Ошибка сети")}",
            ToolchainStatusKind.Checking => $"↻ {UiText.T("Checking…", "Проверка…")}",
            ToolchainStatusKind.Working => $"↻ {UiText.T("Working…", "Выполнение…")}",
            ToolchainStatusKind.Ready when context == StatusContext.DeadlockGame => $"✓ {UiText.T("Client ready", "Клиент готов")}",
            ToolchainStatusKind.Ready when context == StatusContext.Projects => $"✓ {UiText.T("Folder ready", "Папка готова")}",
            ToolchainStatusKind.Ready => $"✓ {UiText.T("Ready", "Готово")}",
            _ => UiText.T("? Unknown", "? Неизвестно"),
        };
    }

    private static string StatusDetail(ToolchainStatus status, StatusContext context)
    {
        if (context == StatusContext.DeadlockGame)
        {
            return status.Kind switch
            {
                ToolchainStatusKind.NotSpecified => UiText.T(
                    "No Deadlock client folder has been selected yet.\n\nUse **BROWSE…** to select the installed Project8Staging folder.",
                    "Папка Deadlock клиента пока не указана.\n\nНажмите **ОБЗОР…** и выберите установленную папку Project8Staging."),
                ToolchainStatusKind.Ready => UiText.T(
                    "The selected folder is a valid **Deadlock client** installation.\n\nDeadlimit found the expected game\\citadel structure.",
                    "Выбранная папка является валидной установкой **Deadlock клиента**.\n\nDeadlimit нашёл ожидаемую структуру game\\citadel."),
                ToolchainStatusKind.InvalidPath => UiText.T(
                    "The selected folder is not a valid Deadlock client installation.\n\nChoose the Project8Staging folder that contains game\\citadel.",
                    "Выбранная папка не является валидной установкой Deadlock клиента.\n\nВыберите папку Project8Staging, внутри которой находится game\\citadel."),
                ToolchainStatusKind.Checking => UiText.T("Checking the Deadlock client folder…", "Проверка папки Deadlock клиента…"),
                _ => status.Detail,
            };
        }

        if (context == StatusContext.Projects)
        {
            return string.IsNullOrWhiteSpace(status.Detail)
                ? UiText.T("Workspace folder status.", "Состояние рабочей папки.")
                : status.Detail;
        }

        return string.IsNullOrWhiteSpace(status.Detail)
            ? FormatStatus(status, context)
            : status.Detail;
    }

    private Color StatusColor(ToolchainStatusKind kind)
    {
        var theme = SelectedThemeCode();
        var dark = theme == "dark" || (theme == "system" && Application.IsDarkModeEnabled);
        return kind switch
        {
            ToolchainStatusKind.UpToDate or ToolchainStatusKind.Ready => dark ? Color.FromArgb(113, 214, 137) : Color.FromArgb(25, 125, 55),
            ToolchainStatusKind.UpdateAvailable => dark ? Color.FromArgb(255, 194, 92) : Color.FromArgb(173, 103, 0),
            ToolchainStatusKind.InvalidPath or ToolchainStatusKind.NetworkIssue => dark ? Color.FromArgb(255, 118, 118) : Color.FromArgb(184, 40, 40),
            ToolchainStatusKind.Checking or ToolchainStatusKind.Working => dark ? Color.FromArgb(117, 190, 255) : Color.FromArgb(30, 105, 175),
            _ => dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(85, 85, 85),
        };
    }

    private void ReapplySemanticStatusColors()
    {
        ApplyStatus(_csdkStatusLabel, _csdkStatus, StatusContext.Csdk);
        ApplyStatus(_deadlockToolsStatusLabel, _deadlockToolsStatus, StatusContext.DeadlockTools);
        ApplyStatus(_retailDeadlockStatusLabel, _retailDeadlockStatus, StatusContext.DeadlockGame);
        ApplyStatus(_projectsStatusLabel, _projectsStatus, StatusContext.Projects);
    }

    private void PreviewTheme()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var theme = SelectedThemeCode();
        UiTheme.ConfigureApplication(theme);
        UiTheme.ApplyCustomPalette(this, theme);
        if (Owner is not null && !Owner.IsDisposed)
        {
            UiTheme.ApplyCustomPalette(Owner, theme);
            Owner.Invalidate(true);
        }
        _themePreviewApplied = true;
        ReapplySemanticStatusColors();
        Invalidate(true);
    }

    private void RestoreThemePreview()
    {
        UiTheme.ConfigureApplication(_initialTheme);
        if (Owner is not null && !Owner.IsDisposed)
        {
            UiTheme.ApplyCustomPalette(Owner, _initialTheme);
            Owner.Invalidate(true);
        }
    }

    private Button CreateOpenFolderButton(TextBox textBox)
    {
        var button = new Button
        {
            Text = "📂",
            AutoSize = false,
            Width = 28,
            Height = 24,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 4, 4),
            Padding = Padding.Empty,
            TabStop = false,
            Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        button.Click += (_, _) => OpenConfiguredFolder(textBox.Text);
        _toolTip.SetToolTip(
            button,
            UiText.T(
                "Open the currently selected folder in File Explorer.\n\nThis does not change the configured path.",
                "Открыть выбранную папку в Проводнике.\n\nЭто не изменяет настроенный путь."));
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
            Margin = new Padding(0, 4, 0, 4),
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
        var selectedTheme = SelectedThemeCode();
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
            InterfaceChanged = !string.Equals(_initialLanguage, selectedLanguage, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(_initialTheme, selectedTheme, StringComparison.OrdinalIgnoreCase);
            DialogResult = DialogResult.OK;
            if (InterfaceChanged)
            {
                UiSettingsChangeBus.NotifyChanged();
            }
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
            var flatExecutable = Path.Combine(candidate.DeadlockToolsRoot, "DeadlockTools.exe");
            var legacyExecutable = Path.Combine(candidate.DeadlockToolsRoot, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");
            var executable = File.Exists(flatExecutable) ? flatExecutable : legacyExecutable;
            if (!File.Exists(executable))
            {
                error = UiText.T(
                    $"DeadlockTools.exe was not found in the selected DeadlockTools folder:\n{candidate.DeadlockToolsRoot}",
                    $"DeadlockTools.exe не найден в выбранной папке DeadlockTools:\n{candidate.DeadlockToolsRoot}");
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(candidate.RetailDeadlockRoot))
        {
            if (!Directory.Exists(candidate.RetailDeadlockRoot))
            {
                error = UiText.T($"Deadlock client folder does not exist:\n{candidate.RetailDeadlockRoot}", $"Папка Deadlock клиента не существует:\n{candidate.RetailDeadlockRoot}");
                return false;
            }
            if (!Directory.Exists(Path.Combine(candidate.RetailDeadlockRoot, "game", "citadel")))
            {
                error = UiText.T($"The selected Deadlock client folder does not contain game\\citadel:\n{candidate.RetailDeadlockRoot}", $"В выбранной папке Deadlock клиента нет game\\citadel:\n{candidate.RetailDeadlockRoot}");
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

    private string SelectedThemeCode() => (_themeCombo.SelectedItem as ThemeItem)?.Code ?? "system";

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
        Margin = new Padding(0, 4, 6, 4),
    };

    private static Label CreateStatusLabel() => new()
    {
        AutoSize = false,
        Width = 137,
        Height = 24,
        TextAlign = ContentAlignment.MiddleLeft,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 4, 6, 4),
    };

    private static Button CreateActionButton() => new()
    {
        AutoSize = false,
        Width = 94,
        Height = 26,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 5, 3),
    };

    private static Button CreateUtilityActionButton(string glyph) => new()
    {
        Text = glyph,
        AutoSize = false,
        Width = 28,
        Height = 26,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 3, 4, 3),
        Padding = Padding.Empty,
        TabStop = false,
        Font = new Font("Segoe MDL2 Assets", 10F, FontStyle.Regular, GraphicsUnit.Point),
        TextAlign = ContentAlignment.MiddleCenter,
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
        Margin = new Padding(0, 8, 10, 8),
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

    private enum StatusContext
    {
        Csdk,
        DeadlockTools,
        DeadlockGame,
        Projects,
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
