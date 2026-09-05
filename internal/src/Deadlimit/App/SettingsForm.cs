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
    private readonly Button _applyButton = new() { AutoSize = true };
    private readonly Button _closeCancelButton = new() { AutoSize = true, DialogResult = DialogResult.Cancel };

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
    private readonly bool _allowUnverifiedToolchainAutomation = ReleaseChannelPolicy.AllowsUnverifiedToolchainAutomation;
    private readonly List<Button> _pathButtons = [];
    private readonly string _initialProjectsRoot;
    private readonly string _initialCsdkRoot;
    private readonly string _initialDeadlockToolsRoot;
    private readonly string _initialRetailDeadlockRoot;
    private readonly string _initialLanguage;
    private readonly string _initialTheme;

    private ToolchainStatus _csdkStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _deadlockToolsStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _retailDeadlockStatus = new(ToolchainStatusKind.NotSpecified);
    private ToolchainStatus _projectsStatus = new(ToolchainStatusKind.NotSpecified);
    private bool _busy;
    private bool _themePreviewApplied;
    private CancellationTokenSource? _csdkCheckCancellation;
    private CancellationTokenSource? _deadlockToolsCheckCancellation;
    private int _retailCheckGeneration;

    public SettingsForm()
    {
        var settings = ProjectStore.GetToolPathSettings();
        _initialProjectsRoot = settings.ProjectsRoot.Trim();
        _initialCsdkRoot = settings.CsdkRoot.Trim();
        _initialDeadlockToolsRoot = settings.DeadlockToolsRoot.Trim();
        _initialRetailDeadlockRoot = settings.RetailDeadlockRoot.Trim();
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

        _languageCombo.SelectedIndexChanged += (_, _) => UpdateSettingsActionState();
        _themeCombo.SelectedIndexChanged += (_, _) =>
        {
            PreviewTheme();
            UpdateSettingsActionState();
        };
        _projectsRootText.TextChanged += (_, _) => UpdateSettingsActionState();
        _csdkRootText.TextChanged += (_, _) => UpdateSettingsActionState();
        _deadlockToolsRootText.TextChanged += (_, _) => UpdateSettingsActionState();
        _retailDeadlockRootText.TextChanged += (_, _) => UpdateSettingsActionState();
        UpdateSettingsActionState();
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
            _csdkCheckCancellation?.Cancel();
            _csdkCheckCancellation?.Dispose();
            _deadlockToolsCheckCancellation?.Cancel();
            _deadlockToolsCheckCancellation?.Dispose();
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
            RowCount = 2,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));


        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Margin = Padding.Empty,
        };

        var toolsGrid = CreateToolsGrid();
        SettingsVersionFeature.AddManagerRow(toolsGrid, 0);
        AddDeadlockGameRow(toolsGrid, 1);
        AddCsdkRow(toolsGrid, 2);
        AddDeadlockToolsRow(toolsGrid, 3);
        AddProjectsRow(toolsGrid, 4);
        content.Controls.Add(toolsGrid);

        var preferencesGrid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2,
            RowCount = 4,
            Margin = new Padding(0, 10, 0, 0),
        };
        preferencesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        preferencesGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        AddLanguageRow(preferencesGrid, 0);
        AddThemeRow(preferencesGrid, 1);
        AddCsdkCacheToolRow(preferencesGrid, 2);
        AddScriptsFolderRow(preferencesGrid, 3);
        content.Controls.Add(preferencesGrid);
        root.Controls.Add(content, 0, 0);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 30,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 10, 0, 0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var friendlyVersionLabel = new Label
        {
            Text = $"v{GetFriendlyVersion()}",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 0, 0),
        };

        var footerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Right,
            Margin = Padding.Empty,
        };

        _closeCancelButton.Text = UiText.T("CLOSE", "ЗАКРЫТЬ");
        _applyButton.Text = UiText.T("APPLY", "ПРИМЕНИТЬ");
        _applyButton.Click += (_, _) => ApplySettings();

        _toolTip.SetToolTip(
            _applyButton,
            UiText.T(
                "Validate and apply the changed folders and interface settings.\n\nUnspecified external-tool paths are allowed.",
                "Проверить и применить изменённые папки и настройки интерфейса.\n\nПути к внешним инструментам можно оставить неуказанными."));

        _applyButton.Margin = new Padding(0, 0, 8, 0);
        _closeCancelButton.Margin = Padding.Empty;
        footerActions.Controls.Add(_applyButton);
        footerActions.Controls.Add(_closeCancelButton);
        footer.Controls.Add(friendlyVersionLabel, 0, 0);
        footer.Controls.Add(footerActions, 1, 0);
        root.Controls.Add(footer, 0, 1);

        AcceptButton = _applyButton;
        CancelButton = _closeCancelButton;
        Controls.Add(root);
    }

    private bool HasPendingSettingsChanges()
    {
        var selectedLanguage = (_languageCombo.SelectedItem as LanguageItem)?.Code ?? "en";
        var selectedTheme = SelectedThemeCode();
        return !SettingEquals(_initialProjectsRoot, _projectsRootText.Text)
            || !SettingEquals(_initialCsdkRoot, _csdkRootText.Text)
            || !SettingEquals(_initialDeadlockToolsRoot, _deadlockToolsRootText.Text)
            || !SettingEquals(_initialRetailDeadlockRoot, _retailDeadlockRootText.Text)
            || !string.Equals(_initialLanguage, selectedLanguage, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(_initialTheme, selectedTheme, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateSettingsActionState()
    {
        var hasPendingChanges = HasPendingSettingsChanges();
        _applyButton.Enabled = hasPendingChanges && !_busy;
        _closeCancelButton.Text = hasPendingChanges
            ? UiText.T("CANCEL", "ОТМЕНА")
            : UiText.T("CLOSE", "ЗАКРЫТЬ");
        _toolTip.SetToolTip(
            _closeCancelButton,
            hasPendingChanges
                ? UiText.T(
                    "Discard pending Settings changes and close the window.\n\nA theme preview is reverted automatically.",
                    "Отменить несохранённые изменения настроек и закрыть окно.\n\nПредпросмотр темы будет автоматически отменён.")
                : UiText.T(
                    "Close Settings. There are no pending changes.",
                    "Закрыть настройки. Несохранённых изменений нет."));
    }

    private static bool SettingEquals(string initialValue, string currentValue) =>
        string.Equals(initialValue.Trim(), currentValue.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static int RunFooterLayoutSmoke()
    {
        using var form = new SettingsForm();
        form.CreateControl();
        form.PerformLayout();

        var cases = new[]
        {
            (Apply: "APPLY", Close: "CLOSE"),
            (Apply: "APPLY", Close: "CANCEL"),
            (Apply: "ПРИМЕНИТЬ", Close: "ЗАКРЫТЬ"),
            (Apply: "ПРИМЕНИТЬ", Close: "ОТМЕНА"),
        };

        foreach (var item in cases)
        {
            form._applyButton.Text = item.Apply;
            form._closeCancelButton.Text = item.Close;
            form.PerformLayout();

            if (!form.ControlFitsClient(form._applyButton) || !form.ControlFitsClient(form._closeCancelButton))
            {
                Console.Error.WriteLine($"Settings footer overflow: '{item.Apply}' / '{item.Close}'.");
                return 1;
            }

            if (!ButtonTextFits(form._applyButton) || !ButtonTextFits(form._closeCancelButton))
            {
                Console.Error.WriteLine($"Settings footer text clipping: '{item.Apply}' / '{item.Close}'.");
                return 2;
            }
        }

        return 0;
    }

    private bool ControlFitsClient(Control control)
    {
        var point = control.Location;
        for (var parent = control.Parent; parent is not null && parent != this; parent = parent.Parent)
        {
            point.Offset(parent.Location);
        }

        return ClientRectangle.Contains(new Rectangle(point, control.Size));
    }

    private static bool ButtonTextFits(Button button)
    {
        var measured = TextRenderer.MeasureText(button.Text, button.Font);
        return measured.Width + 12 <= button.ClientSize.Width;
    }

    private static string GetFriendlyVersion()
    {
        var version = Application.ProductVersion?.Trim() ?? "0.0.0";
        var metadataSeparator = version.IndexOf('+');
        return metadataSeparator >= 0 ? version[..metadataSeparator] : version;
    }


    private static TableLayoutPanel CreateToolsGrid()
    {
        var grid = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 7,
            RowCount = 5,
            Margin = Padding.Empty,
            Width = 910,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 328));
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
            _allowUnverifiedToolchainAutomation
                ? UiText.T(
                    "**INSTALL…** selects an empty folder and downloads the current Reduced CSDK.\n\n**UPDATE…** overlays the current distribution onto the configured CSDK folder.\n\n**CHECK** validates the installation and checks the latest published CSDK generation.",
                    "**УСТАНОВИТЬ…** выбирает пустую папку и скачивает актуальный Reduced CSDK.\n\n**ОБНОВИТЬ…** накладывает актуальный дистрибутив поверх настроенной папки CSDK.\n\n**ПРОВЕРИТЬ** валидирует установку и проверяет последнее опубликованное поколение CSDK.")
                : PortableToolchainNotice());
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
            _allowUnverifiedToolchainAutomation
                ? UiText.T(
                    "**INSTALL…** downloads the latest official Windows x64 release from GitHub into an empty folder.\n\n**UPDATE…** updates a Deadlimit-managed release installation. Git checkouts are updated through Git and rebuilt.\n\n**CHECK** compares a managed install or Git checkout with the current upstream state.\n\nIf the version of a manually copied build cannot be identified, **INSTALL…** remains available instead of offering a meaningless CHECK.",
                    "**УСТАНОВИТЬ…** скачивает последний официальный Windows x64 release с GitHub в пустую папку.\n\n**ОБНОВИТЬ…** обновляет установку release, которой управляет Deadlimit. Git checkout обновляется через Git и пересобирается.\n\n**ПРОВЕРИТЬ** сравнивает управляемую установку или Git checkout с текущим upstream.\n\nЕсли версию вручную скопированной сборки определить нельзя, остаётся доступна кнопка **УСТАНОВИТЬ…**, а не бесполезная проверка.")
                : PortableToolchainNotice());
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
            if (_retailDeadlockStatus.Kind == ToolchainStatusKind.Checking)
            {
                _retailCheckGeneration++;
                SetRetailStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("Deadlock client check cancelled.", "Проверка Deadlock клиента отменена.")));
                return;
            }

            var generation = ++_retailCheckGeneration;
            SetRetailStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
            if (generation == _retailCheckGeneration)
            {
                RefreshDeadlockGameStatus();
            }
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

    private void AddScriptsFolderRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var openButton = new Button
        {
            Text = UiText.T("Open scripts section", "Открыть раздел скриптов"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 4),
        };
        openButton.Click += (_, _) => OpenScriptsFolder();
        _toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open the bundled Deadlimit Scripts section in File Explorer.\n\nIt contains DeadlimitPipelineScripts.ms and its README.",
                "Открыть раздел Deadlimit Scripts в Проводнике.\n\nВ нём находятся DeadlimitPipelineScripts.ms и README."));
        grid.Controls.Add(CreatePreferenceCaption("Deadlimit Scripts"), 0, row);
        grid.Controls.Add(openButton, 1, row);
    }

    private void AddCsdkCacheToolRow(TableLayoutPanel grid, int row)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var optimizeButton = new Button
        {
            Text = UiText.T("Run optimization", "Провести оптимизацию"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 4),
        };
        optimizeButton.Click += (_, _) => RunCsdkStartupOptimization();
        _toolTip.SetToolTip(
            optimizeButton,
            UiText.T(
                "Run the bundled CSDK startup optimization.\n\nUse it after a clean Reduced CSDK installation/update or whenever CSDK startup becomes slow.",
                "Запустить встроенную оптимизацию запуска CSDK.\n\nИспользуйте её после чистой установки/обновления Reduced CSDK или если CSDK снова долго открывается."));
        grid.Controls.Add(CreatePreferenceCaption(UiText.T("CSDK startup optimization", "Оптимизация запуска CSDK")), 0, row);
        grid.Controls.Add(optimizeButton, 1, row);
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
        _csdkCheckCancellation?.Cancel();
        _csdkCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _csdkCheckCancellation = cancellation;

        if (!skipCheckingState)
        {
            SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            var status = await _toolchain.CheckCsdkAsync(_csdkRootText.Text.Trim(), cancellation.Token);
            if (ReferenceEquals(_csdkCheckCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetCsdkStatus(status);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                SetCsdkStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("Reduced CSDK check cancelled.", "Проверка Reduced CSDK отменена.")));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                SetCsdkStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
            }
        }
        finally
        {
            if (ReferenceEquals(_csdkCheckCancellation, cancellation))
            {
                _csdkCheckCancellation = null;
                cancellation.Dispose();
                UpdateActionAvailability();
            }
        }
    }

    private bool CancelCsdkCheck()
    {
        if (_csdkCheckCancellation is null)
        {
            return false;
        }

        _csdkCheckCancellation.Cancel();
        SetCsdkStatus(new ToolchainStatus(
            ToolchainStatusKind.Cancelled,
            UiText.T("Reduced CSDK check cancelled.", "Проверка Reduced CSDK отменена.")));
        return true;
    }
    private async Task RefreshDeadlockToolsStatusAsync(bool skipCheckingState = false)
    {
        _deadlockToolsCheckCancellation?.Cancel();
        _deadlockToolsCheckCancellation?.Dispose();
        var cancellation = new CancellationTokenSource();
        _deadlockToolsCheckCancellation = cancellation;

        if (!skipCheckingState)
        {
            SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.Checking));
            await Task.Yield();
        }

        try
        {
            var status = await _toolchain.CheckDeadlockToolsAsync(_deadlockToolsRootText.Text.Trim(), cancellation.Token);
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation) && !cancellation.IsCancellationRequested)
            {
                SetDeadlockToolsStatus(status);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                SetDeadlockToolsStatus(new ToolchainStatus(
                    ToolchainStatusKind.Cancelled,
                    UiText.T("DeadlockTools check cancelled.", "Проверка DeadlockTools отменена.")));
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                SetDeadlockToolsStatus(new ToolchainStatus(ToolchainStatusKind.NetworkIssue, exception.Message));
            }
        }
        finally
        {
            if (ReferenceEquals(_deadlockToolsCheckCancellation, cancellation))
            {
                _deadlockToolsCheckCancellation = null;
                cancellation.Dispose();
                UpdateActionAvailability();
            }
        }
    }

    private bool CancelDeadlockToolsCheck()
    {
        if (_deadlockToolsCheckCancellation is null)
        {
            return false;
        }

        _deadlockToolsCheckCancellation.Cancel();
        SetDeadlockToolsStatus(new ToolchainStatus(
            ToolchainStatusKind.Cancelled,
            UiText.T("DeadlockTools check cancelled.", "Проверка DeadlockTools отменена.")));
        return true;
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
        _csdkPrimaryButton.Text = CsdkPrimaryActionText(_csdkStatus.Kind, _allowUnverifiedToolchainAutomation);
        _deadlockToolsPrimaryButton.Text = DeadlockToolsPrimaryActionText(_deadlockToolsStatus.Kind, _allowUnverifiedToolchainAutomation);
        _retailDeadlockCheckButton.Text = _retailDeadlockStatus.Kind == ToolchainStatusKind.Checking
            ? UiText.T("CHECKING…", "ПРОВЕРКА…")
            : UiText.T("CHECK", "ПРОВЕРИТЬ");

        _csdkPrimaryButton.Enabled = !_busy && _csdkStatus.Kind is not ToolchainStatusKind.Working;
        _deadlockToolsPrimaryButton.Enabled = !_busy && _deadlockToolsStatus.Kind is not ToolchainStatusKind.Working;
        _retailDeadlockCheckButton.Enabled = !_busy;
        _retailDeadlockFindButton.Enabled = !_busy;

        var csdkValid = Directory.Exists(_csdkRootText.Text.Trim())
            && File.Exists(Path.Combine(_csdkRootText.Text.Trim(), "csdkcfg.exe"));
        var gameClientValid = _retailDeadlockStatus.Kind == ToolchainStatusKind.Ready;
        _csdkSetupButton.Enabled = !_busy
            && _allowUnverifiedToolchainAutomation
            && csdkValid
            && gameClientValid
            && _csdkStatus.NetworkAvailable
            && _csdkStatus.Kind is not ToolchainStatusKind.NetworkIssue;
        UpdateSettingsActionState();

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

    private static string CsdkPrimaryActionText(ToolchainStatusKind kind, bool allowAutomation) => !allowAutomation
        ? UiText.T("CHECK", "ПРОВЕРИТЬ")
        : kind switch
    {
        ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath => UiText.T("INSTALL…", "УСТАНОВИТЬ…"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
        ToolchainStatusKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
        ToolchainStatusKind.Working => UiText.T("WORKING…", "ВЫПОЛНЕНИЕ…"),
        _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
    };

    private static string DeadlockToolsPrimaryActionText(ToolchainStatusKind kind, bool allowAutomation) => !allowAutomation
        ? UiText.T("CHECK", "ПРОВЕРИТЬ")
        : kind switch
    {
        ToolchainStatusKind.NotSpecified or ToolchainStatusKind.InvalidPath or ToolchainStatusKind.Installed => UiText.T("INSTALL…", "УСТАНОВИТЬ…"),
        ToolchainStatusKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
        ToolchainStatusKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
        ToolchainStatusKind.Working => UiText.T("WORKING…", "ВЫПОЛНЕНИЕ…"),
        _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
    };

    private async Task HandleCsdkPrimaryActionAsync()
    {
        if (_csdkStatus.Kind == ToolchainStatusKind.Checking)
        {
            CancelCsdkCheck();
            return;
        }

        if (!_allowUnverifiedToolchainAutomation)
        {
            await RefreshCsdkStatusAsync();
            return;
        }

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

        await RefreshCsdkStatusAsync();
    }

    private async Task HandleDeadlockToolsPrimaryActionAsync()
    {
        if (_deadlockToolsStatus.Kind == ToolchainStatusKind.Checking)
        {
            CancelDeadlockToolsCheck();
            return;
        }

        if (!_allowUnverifiedToolchainAutomation)
        {
            await RefreshDeadlockToolsStatusAsync();
            return;
        }

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

        await RefreshDeadlockToolsStatusAsync();
    }

    private async Task SetupCsdkAsync()
    {
        if (!_allowUnverifiedToolchainAutomation)
        {
            MessageBox.Show(
                this,
                PortableToolchainNotice(),
                UiText.T("Portable release safety", "Безопасность portable-релиза"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

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

    private static string PortableToolchainNotice() => UiText.T(
        "Portable releases require an existing Reduced CSDK/DeadlockTools installation selected with BROWSE. Automatic install, update, and full CSDK setup stay disabled until upstream archives have release-pinned trusted checksums.",
        "Portable-релиз требует существующую установку Reduced CSDK/DeadlockTools, выбранную через ОБЗОР. Автоустановка, обновление и полная настройка CSDK отключены, пока для upstream-архивов нет привязанных к релизу доверенных SHA-256.");

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
            ToolchainStatusKind.Cancelled => $"○ {UiText.T("Check cancelled", "Проверка отменена")}",
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
                _ => FormatStatus(status, context),
            };
        }

        if (context == StatusContext.Projects)
        {
            return status.Kind switch
            {
                ToolchainStatusKind.NotSpecified => UiText.T(
                    "No projects folder has been selected yet.",
                    "Папка проектов пока не указана."),
                ToolchainStatusKind.Ready => UiText.T(
                    "The projects workspace folder is available.",
                    "Рабочая папка проектов доступна."),
                ToolchainStatusKind.InvalidPath => UiText.T(
                    "The selected projects folder does not exist.",
                    "Выбранная папка проектов не существует."),
                _ => FormatStatus(status, context),
            };
        }

        if (context == StatusContext.Csdk)
        {
            return status.Kind switch
            {
                ToolchainStatusKind.NotSpecified => UiText.T(
                    "Reduced CSDK folder is not specified.",
                    "Папка Reduced CSDK не указана."),
                ToolchainStatusKind.Installed when status.AvailableGeneration is int available => UiText.T(
                    $"Reduced CSDK is valid. Latest published generation is {available}, but the local generation could not be identified.",
                    $"Reduced CSDK установлен корректно. Последнее опубликованное поколение: {available}; локальное поколение определить не удалось."),
                ToolchainStatusKind.Installed => UiText.T(
                    "Reduced CSDK is installed, but its local generation could not be identified.",
                    "Reduced CSDK установлен, но локальное поколение определить не удалось."),
                ToolchainStatusKind.UpToDate when status.InstalledGeneration is int installed => UiText.T(
                    $"Installed CSDK generation: {installed}.",
                    $"Установленное поколение CSDK: {installed}."),
                ToolchainStatusKind.UpdateAvailable when status.InstalledGeneration is int installed && status.AvailableGeneration is int available => UiText.T(
                    $"Installed CSDK {installed}; CSDK {available} is available.",
                    $"Установлен CSDK {installed}; доступен CSDK {available}."),
                ToolchainStatusKind.InvalidPath => UiText.T(
                    "csdkcfg.exe was not found in the selected Reduced CSDK folder.",
                    "В выбранной папке Reduced CSDK не найден csdkcfg.exe."),
                ToolchainStatusKind.NetworkIssue => UiText.T(
                    "CSDK is installed, but freshness could not be checked because the update source is unavailable.",
                    "CSDK установлен, но проверить актуальность не удалось: источник обновлений недоступен."),
                ToolchainStatusKind.Checking => UiText.T("Checking Reduced CSDK…", "Проверка Reduced CSDK…"),
                ToolchainStatusKind.Working => UiText.T("Working with Reduced CSDK…", "Выполняется операция с Reduced CSDK…"),
                _ => FormatStatus(status, context),
            };
        }

        if (context == StatusContext.DeadlockTools)
        {
            return status.Kind switch
            {
                ToolchainStatusKind.NotSpecified => UiText.T(
                    "DeadlockTools folder is not specified.",
                    "Папка DeadlockTools не указана."),
                ToolchainStatusKind.Installed when !string.IsNullOrWhiteSpace(status.AvailableVersion) => UiText.T(
                    $"DeadlockTools is installed, but its local version could not be identified. Latest official release: {status.AvailableVersion}.",
                    $"DeadlockTools установлен, но локальную версию определить не удалось. Последний официальный релиз: {status.AvailableVersion}."),
                ToolchainStatusKind.Installed => UiText.T(
                    "DeadlockTools is installed, but its local version could not be identified.",
                    "DeadlockTools установлен, но локальную версию определить не удалось."),
                ToolchainStatusKind.UpToDate when !string.IsNullOrWhiteSpace(status.InstalledVersion) => UiText.T(
                    $"Installed DeadlockTools release: {status.InstalledVersion}.",
                    $"Установленный релиз DeadlockTools: {status.InstalledVersion}."),
                ToolchainStatusKind.UpdateAvailable when !string.IsNullOrWhiteSpace(status.InstalledVersion) && !string.IsNullOrWhiteSpace(status.AvailableVersion) => UiText.T(
                    $"Installed DeadlockTools {status.InstalledVersion}; {status.AvailableVersion} is available.",
                    $"Установлен DeadlockTools {status.InstalledVersion}; доступен {status.AvailableVersion}."),
                ToolchainStatusKind.InvalidPath => UiText.T(
                    "DeadlockTools.exe was not found in the selected DeadlockTools folder.",
                    "В выбранной папке DeadlockTools не найден DeadlockTools.exe."),
                ToolchainStatusKind.NetworkIssue => UiText.T(
                    "DeadlockTools is installed, but freshness could not be checked because GitHub is unavailable.",
                    "DeadlockTools установлен, но проверить актуальность не удалось: GitHub недоступен."),
                ToolchainStatusKind.Checking => UiText.T("Checking DeadlockTools…", "Проверка DeadlockTools…"),
                ToolchainStatusKind.Working => UiText.T("Working with DeadlockTools…", "Выполняется операция с DeadlockTools…"),
                _ => FormatStatus(status, context),
            };
        }

        return FormatStatus(status, context);
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

    private void OpenScriptsFolder()
    {
        try
        {
            OpenConfiguredFolder(DeadlimitScriptsService.GetBundledScriptFolder());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, exception.Message, UiText.T("Deadlimit Scripts folder unavailable", "Папка Deadlimit Scripts недоступна"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void RunCsdkStartupOptimization()
    {
        try
        {
            var commandPath = CsdkAssetCacheToolService.GetBundledCommandPath();
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = commandPath,
                WorkingDirectory = Path.GetDirectoryName(commandPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, exception.Message, UiText.T("CSDK optimization unavailable", "Оптимизация CSDK недоступна"), MessageBoxButtons.OK, MessageBoxIcon.Error);
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

    private void ApplySettings()
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
