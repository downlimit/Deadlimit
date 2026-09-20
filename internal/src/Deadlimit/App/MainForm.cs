using Deadlimit.Core;

namespace Deadlimit.App;

public sealed class MainForm : Form
{
    private readonly ListBox _projectLibrary = new()
    {
        Name = UiControlNames.ProjectLibrary,
        Dock = DockStyle.Fill,
        IntegralHeight = false,
    };
    private readonly TextBox _projectFolderText = new() { Name = UiControlNames.ProjectFolder, Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _projectNameText = new() { Name = UiControlNames.ProjectName, Dock = DockStyle.Fill };
    private readonly TextBox _heroText = new() { Name = UiControlNames.Hero, Dock = DockStyle.Fill };
    private readonly TextBox _releaseTargetText = new() { Name = UiControlNames.ReleaseIdBacking, Dock = DockStyle.Fill };
    private readonly Label _dmxCountLabel = new() { AutoSize = true };
    private readonly Label _pngCountLabel = new() { AutoSize = true };
    private readonly Label _sourceFolderLabel = new() { AutoSize = true };
    private readonly ListBox _assetList = new() { Name = UiControlNames.AssetList, Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Text = UiText.T("Select a project from the library.", "Выберите проект в библиотеке."),
    };
    private readonly Button _extractHeroButton = new()
    {
        Name = UiControlNames.ExtractHeroSourceButton,
        Text = UiText.T("EXTRACT SOURCE…", "ИЗВЛЕЧЬ ИСХОДНИКИ…"),
        AutoSize = true,
    };

    private ProjectManifest? _loadedManifest;
    private bool _refreshingProjectLibrary;
    private bool _libraryInitialized;
    private CancellationTokenSource? _heroExtractionCancellation;
    private bool _closeAfterHeroExtraction;

    public MainForm()
    {
        Text = "Deadlimit Manager";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 980;
        Height = 660;
        MinimumSize = new Size(800, 540);

        BuildUi();
        FormClosing += (_, args) =>
        {
            if (_heroExtractionCancellation is null)
            {
                return;
            }

            _closeAfterHeroExtraction = true;
            args.Cancel = true;
            if (!_heroExtractionCancellation.IsCancellationRequested)
            {
                _heroExtractionCancellation.Cancel();
                _extractHeroButton.Enabled = false;
                SetStatus(UiText.T(
                    "Cancelling hero source extraction before closing...",
                    "Отмена извлечения исходников перед закрытием..."));
            }
        };
        Shown += (_, _) =>
        {
            _libraryInitialized = true;
            InitializeProjectLibrary();
        };
    }

    internal static int RunFrameAlignmentSmoke()
    {
        using var form = new MainForm
        {
            Size = new Size(972, 672),
        };
        form.CreateControl();
        PerformLayoutRecursively(form);

        var groups = FindControls<GroupBox>(form).ToArray();
        var library = groups.FirstOrDefault(group => group.Name == UiControlNames.LibraryGroup);
        var projectFiles = groups.FirstOrDefault(group => group.Name == UiControlNames.ProjectFilesGroup);
        if (library is null || projectFiles is null)
        {
            return 1;
        }

        if (GetBottomRelativeToForm(library) != GetBottomRelativeToForm(projectFiles))
        {
            return 2;
        }

        var leftInset = GetLeftRelativeToForm(library);
        var rightInset = form.ClientSize.Width - GetRightRelativeToForm(projectFiles);
        return leftInset == rightInset ? 0 : 3;
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(14),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var libraryGroup = new GroupBox
        {
            Name = UiControlNames.LibraryGroup,
            Text = UiText.T("Projects", "Проекты"),
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 10, 0),
        };
        _projectLibrary.SelectedIndexChanged += (_, _) =>
        {
            if (_refreshingProjectLibrary || _projectLibrary.SelectedItem is not ProjectLibraryItem item)
            {
                return;
            }

            if (ApplicationMutationCoordinator.IsBusy
                && _loadedManifest is not null
                && !string.Equals(
                    Path.GetFullPath(item.Folder),
                    Path.GetFullPath(_loadedManifest.ProjectFolder),
                    StringComparison.OrdinalIgnoreCase))
            {
                _refreshingProjectLibrary = true;
                try
                {
                    for (var index = 0; index < _projectLibrary.Items.Count; index++)
                    {
                        if (_projectLibrary.Items[index] is ProjectLibraryItem candidate
                            && string.Equals(
                                Path.GetFullPath(candidate.Folder),
                                Path.GetFullPath(_loadedManifest.ProjectFolder),
                                StringComparison.OrdinalIgnoreCase))
                        {
                            _projectLibrary.SelectedIndex = index;
                            break;
                        }
                    }
                }
                finally
                {
                    _refreshingProjectLibrary = false;
                }

                SetStatus(UiText.T(
                    $"Project switching is locked while {ApplicationMutationCoordinator.ActiveOperation} is running.",
                    $"Переключение проекта заблокировано, пока выполняется операция: {ApplicationMutationCoordinator.ActiveOperation}."));
                return;
            }

            SelectProjectFolder(item.Folder, rememberSelection: true, showStatus: true);
        };
        libraryGroup.Controls.Add(_projectLibrary);

        var workspace = new TableLayoutPanel
        {
            Name = UiControlNames.Workspace,
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(3, 3, 0, 3),
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel
        {
            Name = UiControlNames.PrimaryActions,
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 10),
        };

        var settingsButton = new Button
        {
            Name = UiControlNames.SettingsButton,
            Text = UiText.T("SETTINGS", "НАСТРОЙКИ"),
            AutoSize = true,
        };
        settingsButton.Click += (_, _) => ShowSettings();
        _extractHeroButton.Click += async (_, _) => await ExtractHeroSourceAsync();

        topBar.Controls.Add(_extractHeroButton);
        topBar.Controls.Add(settingsButton);

        var projectGroup = new GroupBox
        {
            Name = UiControlNames.ProjectGroup,
            Text = UiText.T("Project", "Проект"),
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12),
        };

        var projectGrid = new TableLayoutPanel
        {
            Name = UiControlNames.ProjectGrid,
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 5,
            AutoSize = true,
        };
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        projectGrid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        AddField(projectGrid, 0, UiText.T("Folder", "Папка"), _projectFolderText);

        var openFolderButton = new Button
        {
            Name = UiControlNames.OpenProjectFolderButton,
            Text = UiText.T("OPEN FOLDER", "ОТКРЫТЬ ПАПКУ"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        };
        openFolderButton.Click += (_, _) => OpenProjectFolder();
        projectGrid.Controls.Add(openFolderButton, 2, 0);

        AddField(projectGrid, 1, UiText.T("Project name", "Имя проекта"), _projectNameText);
        AddField(projectGrid, 2, UiText.T("Hero", "Герой"), _heroText);
        AddField(projectGrid, 3, "Release ID", _releaseTargetText);

        var saveButton = new Button
        {
            Name = UiControlNames.SaveProjectButton,
            Text = UiText.T("SAVE PROJECT", "СОХРАНИТЬ ПРОЕКТ"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 10, 0, 0),
        };
        saveButton.Click += (_, _) => SaveProject();
        projectGrid.Controls.Add(saveButton, 1, 4);

        projectGroup.Controls.Add(projectGrid);

        var assetsGroup = new GroupBox
        {
            Name = UiControlNames.ProjectFilesGroup,
            Text = UiText.T("Detected in 1authoring", "Найдено в 1authoring"),
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            Margin = new Padding(3, 3, 0, 3),
        };

        // The project-files frame is nested inside workspace, so its lower edge includes
        // both margins. Match that accumulated inset on the adjacent Library frame.
        libraryGroup.Margin = new Padding(
            libraryGroup.Margin.Left,
            libraryGroup.Margin.Top,
            libraryGroup.Margin.Right,
            workspace.Margin.Bottom + assetsGroup.Margin.Bottom);

        var assetsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        assetsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        assetsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        assetsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var counts = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
        };
        counts.Controls.Add(_dmxCountLabel);
        counts.Controls.Add(new Label { Text = "    ", AutoSize = true });
        counts.Controls.Add(_pngCountLabel);

        _sourceFolderLabel.Margin = new Padding(3, 7, 3, 7);
        assetsLayout.Controls.Add(counts, 0, 0);
        assetsLayout.Controls.Add(_sourceFolderLabel, 0, 1);
        assetsLayout.Controls.Add(_assetList, 0, 2);
        assetsGroup.Controls.Add(assetsLayout);

        workspace.Controls.Add(topBar, 0, 0);
        workspace.Controls.Add(projectGroup, 0, 1);
        workspace.Controls.Add(assetsGroup, 0, 2);

        var statusStrip = new StatusStrip { Name = UiControlNames.StatusStrip };
        statusStrip.Items.Add(_statusLabel);

        root.Controls.Add(libraryGroup, 0, 0);
        root.Controls.Add(workspace, 1, 0);
        root.Controls.Add(statusStrip, 0, 1);
        root.SetColumnSpan(statusStrip, 2);
        Controls.Add(root);

        ClearProjectView();
    }

    private static void AddField(TableLayoutPanel grid, int row, string label, Control control)
    {
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 7, 12, 7),
        };
        control.Margin = new Padding(0, 4, 8, 4);
        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(control, 1, row);
    }

    private static void PerformLayoutRecursively(Control root)
    {
        root.PerformLayout();
        foreach (Control child in root.Controls)
        {
            PerformLayoutRecursively(child);
        }
    }

    private static int GetBottomRelativeToForm(Control control)
    {
        var bottom = control.Bottom;
        for (var parent = control.Parent; parent is not null and not Form; parent = parent.Parent)
        {
            bottom += parent.Top;
        }

        return bottom;
    }

    private static int GetLeftRelativeToForm(Control control)
    {
        var left = control.Left;
        for (var parent = control.Parent; parent is not null and not Form; parent = parent.Parent)
        {
            left += parent.Left;
        }

        return left;
    }

    private static int GetRightRelativeToForm(Control control)
    {
        var right = control.Right;
        for (var parent = control.Parent; parent is not null and not Form; parent = parent.Parent)
        {
            right += parent.Left;
        }

        return right;
    }

    private static IEnumerable<T> FindControls<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindControls<T>(child))
            {
                yield return nested;
            }
        }
    }

    private void InitializeProjectLibrary()
    {
        RefreshProjectLibrary(
            preserveSelection: false,
            rescanSelected: false,
            preferredFolder: ProjectStore.GetLastProjectFolder());
    }

    internal void RefreshExternalProjectState(bool projectLibraryChanged)
    {
        if (!_libraryInitialized || IsDisposed)
        {
            return;
        }

        if (projectLibraryChanged)
        {
            RefreshProjectLibrary(preserveSelection: true, rescanSelected: true);
            return;
        }

        RefreshScan(showStatus: false);
    }

    private void RefreshProjectLibrary(
        bool preserveSelection,
        bool rescanSelected,
        string? preferredFolder = null)
    {
        var settings = ProjectStore.GetToolPathSettings();
        var projectsRoot = settings.ProjectsRoot;

        if (!Directory.Exists(projectsRoot))
        {
            _refreshingProjectLibrary = true;
            _projectLibrary.Items.Clear();
            _refreshingProjectLibrary = false;
            ClearProjectView();
            SetStatus(UiText.T(
                "Projects folder is unavailable. Set it in Settings.",
                "Папка проектов недоступна. Укажите её в настройках."));
            return;
        }

        var previousFolder = preserveSelection
            ? (_projectLibrary.SelectedItem as ProjectLibraryItem)?.Folder ?? _projectFolderText.Text.Trim()
            : preferredFolder;

        try
        {
            var paths = new DeadlimitPaths(settings);
            var folders = Directory.EnumerateDirectories(projectsRoot)
                .Where(folder => !ShouldHideLibraryFolder(folder, projectsRoot, paths))
                .OrderBy(folder => Path.GetFileName(folder), StringComparer.OrdinalIgnoreCase)
                .Select(folder => new ProjectLibraryItem(Path.GetFileName(folder), Path.GetFullPath(folder)))
                .ToList();

            var targetIndex = -1;
            if (!string.IsNullOrWhiteSpace(previousFolder))
            {
                targetIndex = folders.FindIndex(item => PathsEqual(item.Folder, previousFolder));
            }

            if (targetIndex < 0 && folders.Count > 0)
            {
                targetIndex = 0;
            }

            _refreshingProjectLibrary = true;
            _projectLibrary.BeginUpdate();
            try
            {
                _projectLibrary.Items.Clear();
                foreach (var item in folders)
                {
                    _projectLibrary.Items.Add(item);
                }

                _projectLibrary.SelectedIndex = targetIndex;
            }
            finally
            {
                _projectLibrary.EndUpdate();
                _refreshingProjectLibrary = false;
            }

            if (targetIndex < 0)
            {
                ClearProjectView();
                SetStatus(UiText.T(
                    "No project folders found.",
                    "Папки проектов не найдены."));
                return;
            }

            var selected = folders[targetIndex];
            if (PathsEqual(_projectFolderText.Text, selected.Folder))
            {
                if (rescanSelected)
                {
                    RefreshScan(showStatus: false);
                }
                return;
            }

            SelectProjectFolder(selected.Folder, rememberSelection: true, showStatus: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus(UiText.T(
                $"Could not refresh project library: {ex.Message}",
                $"Не удалось обновить библиотеку проектов: {ex.Message}"));
        }
    }

    private static bool ShouldHideLibraryFolder(
        string folder,
        string projectsRoot,
        DeadlimitPaths paths)
    {
        var name = Path.GetFileName(folder);
        if (string.Equals(name, "Deadlimit", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var excludedPaths = new[]
        {
            paths.CsdkRoot,
            paths.DeadlockToolsRoot,
            paths.RetailDeadlockRoot,
            DeadlimitPaths.DefaultDeadlimitRoot,
            AppContext.BaseDirectory,
        };

        return excludedPaths.Any(path =>
            !string.IsNullOrWhiteSpace(path)
            && IsSameOrDescendant(path, projectsRoot)
            && IsSameOrDescendant(path, folder));
    }

    private static bool IsSameOrDescendant(string path, string parent)
    {
        var fullPath = NormalizeComparablePath(path);
        var fullParent = NormalizeComparablePath(parent);
        if (string.Equals(fullPath, fullParent, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return fullPath.StartsWith(
            fullParent + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            NormalizeComparablePath(left),
            NormalizeComparablePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeComparablePath(string path) =>
        Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private void SelectProjectFolder(string folder, bool rememberSelection, bool showStatus)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        var manifest = ProjectStore.TryLoad(folder);
        if (manifest is not null)
        {
            LoadManifest(manifest);
            if (rememberSelection)
            {
                ProjectStore.RememberLastProject(folder);
            }

            if (showStatus)
            {
                SetStatus(UiText.T(
                    $"Selected project: {manifest.ProjectName}",
                    $"Выбран проект: {manifest.ProjectName}"));
            }
            return;
        }

        _loadedManifest = null;
        _projectFolderText.Text = Path.GetFullPath(folder);
        _projectNameText.Text = new DirectoryInfo(folder).Name;
        _heroText.Clear();
        _releaseTargetText.Clear();
        RefreshScan(showStatus: false);

        if (rememberSelection)
        {
            ProjectStore.RememberLastProject(folder);
        }

        if (showStatus)
        {
            SetStatus(UiText.T(
                "Project folder selected. Enter the hero and save project metadata.",
                "Папка проекта выбрана. Укажите героя и сохраните метаданные проекта."));
        }
    }

    private void OpenProjectFolder()
    {
        var folder = _projectFolderText.Text.Trim();
        if (!Directory.Exists(folder))
        {
            ShowValidation(UiText.T(
                "Select an existing project folder first.",
                "Сначала выберите существующую папку проекта."));
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
            SetStatus(UiText.T("Opened project folder.", "Папка проекта открыта."));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T("Could not open project folder", "Не удалось открыть папку проекта"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ShowSettings()
    {
        if (ApplicationMutationCoordinator.IsBusy)
        {
            MessageBox.Show(
                this,
                UiText.T(
                    $"Wait for the active operation to finish or cancel it first: {ApplicationMutationCoordinator.ActiveOperation}",
                    $"Сначала дождитесь завершения активной операции или отмените её: {ApplicationMutationCoordinator.ActiveOperation}"),
                UiText.T("Operation in progress", "Операция выполняется"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SettingsForm();
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            if (dialog.RestartRequired)
            {
                Application.Restart();
                Close();
                return;
            }

            RefreshProjectLibrary(preserveSelection: true, rescanSelected: true);
            SetStatus(UiText.T(
                "Settings saved. Project library and tool paths refreshed.",
                "Настройки сохранены. Библиотека проектов и пути к инструментам обновлены."));
        }
    }

    private void SaveProject()
    {
        if (TrySaveProject())
        {
            RefreshProjectLibrary(preserveSelection: true, rescanSelected: true);
            SetStatus(UiText.T(
                $"Saved. Models: {_loadedManifest!.DmxFiles.Count + _loadedManifest.FbxFiles.Count + _loadedManifest.GltfFiles.Count}; textures: {_loadedManifest.PngTextures.Count}.",
                $"Сохранено. Моделей: {_loadedManifest!.DmxFiles.Count + _loadedManifest.FbxFiles.Count + _loadedManifest.GltfFiles.Count}; текстур: {_loadedManifest.PngTextures.Count}."));
        }
    }

    private bool TrySaveProject()
    {
        if (ApplicationMutationCoordinator.IsBusy)
        {
            ShowValidation(UiText.T(
                $"Project settings cannot be changed while {ApplicationMutationCoordinator.ActiveOperation} is running.",
                $"Нельзя менять настройки проекта, пока выполняется операция: {ApplicationMutationCoordinator.ActiveOperation}."));
            return false;
        }

        var folder = _projectFolderText.Text.Trim();
        var projectName = _projectNameText.Text.Trim();
        var hero = _heroText.Text.Trim();
        var releaseTarget = NullIfWhiteSpace(_releaseTargetText.Text);

        if (!Directory.Exists(folder))
        {
            ShowValidation(UiText.T("Select an existing project folder.", "Выберите существующую папку проекта."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(projectName))
        {
            ShowValidation(UiText.T("Enter a project name.", "Введите имя проекта."));
            return false;
        }

        if (string.IsNullOrWhiteSpace(hero))
        {
            ShowValidation(UiText.T(
                "Enter the Deadlock hero for this project.",
                "Укажите героя Deadlock для этого проекта."));
            return false;
        }

        try
        {
            var fullFolder = Path.GetFullPath(folder);
            if (!EnsureLegacyRootAuthoringMigrated(fullFolder))
            {
                return false;
            }

            ProjectAuthoringLayout.EnsureStructure(fullFolder);
            var scan = ProjectScanner.Scan(fullFolder);
            var existing = ProjectStore.TryLoad(fullFolder);
            if (existing is null
                && _loadedManifest is not null
                && string.Equals(
                    Path.GetFullPath(_loadedManifest.ProjectFolder),
                    fullFolder,
                    StringComparison.OrdinalIgnoreCase))
            {
                existing = _loadedManifest;
            }

            var canonicalProjectName = Path.GetFileName(fullFolder);

            var manifest = new ProjectManifest
            {
                SchemaVersion = Math.Max(existing?.SchemaVersion ?? 1, 3),
                ProjectId = string.IsNullOrWhiteSpace(existing?.ProjectId)
                    ? AddonIdentityService.CreateProjectId()
                    : existing.ProjectId,
                AddonId = AddonIdentityService.ResolveInitialAddonId(existing, canonicalProjectName),
                ProjectName = projectName,
                ProjectFolder = fullFolder,
                Hero = hero,
                ReleaseTarget = releaseTarget,
                SourceDumpFolderName = existing?.SourceDumpFolderName ?? "0source",
                DmxFiles = [.. scan.DmxFiles],
                FbxFiles = [.. scan.FbxFiles],
                GltfFiles = [.. scan.GltfFiles],
                PngTextures = [.. scan.PngTextures],
                TextureTargetBindings = existing?.TextureTargetBindings
                    ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
                RetailMainModel = existing?.RetailMainModel,
                RetailSourceVpk = existing?.RetailSourceVpk,
                LastSourceExtractionUtc = existing?.LastSourceExtractionUtc,
                Source2ViewerVersion = existing?.Source2ViewerVersion,
                ExtractedSourceFileCount = existing?.ExtractedSourceFileCount,
                LastSourceExtractionIncludedTextures = existing?.LastSourceExtractionIncludedTextures ?? false,
                LastSourceExtractionIncludedAbilities = existing?.LastSourceExtractionIncludedAbilities ?? false,
                SourceVmdl = existing?.SourceVmdl,
                CompiledVmdl = existing?.CompiledVmdl,
                AnimGraph2Refs = existing?.AnimGraph2Refs ?? [],
                NmSkeletonRef = existing?.NmSkeletonRef,
            };

            ProjectStore.Save(manifest);
            _loadedManifest = manifest;
            RefreshScan(showStatus: false);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T("Could not save project", "Не удалось сохранить проект"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }

    private async Task ExtractHeroSourceAsync()
    {
        if (!TrySaveProject() || _loadedManifest is null)
        {
            return;
        }

        var outputFolder = Path.Combine(_loadedManifest.ProjectFolder, _loadedManifest.SourceDumpFolderName);
        var gltfOutputFolder = Path.Combine(outputFolder, ExtractedSourceLayout.GltfPipelineFolderName);
        var legacyGltfOutputFolder = Path.Combine(outputFolder, ExtractedSourceLayout.LegacyGltfPipelineFolderName);
        var hasExistingDmxSource = Directory.Exists(outputFolder)
            && Directory.EnumerateFileSystemEntries(outputFolder)
                .Any(path => !new[] { gltfOutputFolder, legacyGltfOutputFolder }.Any(gltfFolder => string.Equals(
                    Path.GetFullPath(path),
                    Path.GetFullPath(gltfFolder),
                    StringComparison.OrdinalIgnoreCase)));
        var hasExistingGltfSource = new[] { gltfOutputFolder, legacyGltfOutputFolder }
            .Any(folder => Directory.Exists(folder)
                && Directory.EnumerateFileSystemEntries(folder).Any());
        var dialogResult = HeroExtractionOptionsDialog.Show(
            this,
            hasExistingDmxSource,
            hasExistingGltfSource);
        if (!dialogResult.Accepted)
        {
            SetStatus(UiText.T("Hero source extraction cancelled.", "Извлечение исходников героя отменено."));
            return;
        }

        _extractHeroButton.Enabled = false;
        try
        {
            using var mutation = ApplicationMutationCoordinator.Begin("EXTRACT HERO SOURCE");
            using var cancellation = new CancellationTokenSource();
            _heroExtractionCancellation = cancellation;

            var progress = new Progress<HeroExtractionProgress>(update => SetStatus(update.Message));
            var service = new HeroExtractionService(new DeadlimitPaths());
            var result = await service.ExtractAsync(
                _loadedManifest,
                dialogResult.Options,
                progress,
                cancellation.Token);

            var backupCleanupWarning = dialogResult.RemoveBackupAfterSuccess
                ? TryRemovePreviousHeroSourceBackup(
                    _loadedManifest.ProjectFolder,
                    dialogResult.Options.Format)
                : null;

            RefreshScan(showStatus: false);
            SetStatus(UiText.T(
                $"Hero source ready: {result.ExtractedFileCount} files.",
                $"Исходники героя готовы: {result.ExtractedFileCount} файлов."));

            var successMessage = UiText.T(
                $"Hero source refreshed successfully.\n\nMain model: {result.MainModelResourcePath}\nFiles: {result.ExtractedFileCount}\nOutput: {result.OutputFolder}",
                $"Исходники героя успешно обновлены.\n\nОсновная модель: {result.MainModelResourcePath}\nФайлов: {result.ExtractedFileCount}\nПапка: {result.OutputFolder}");
            if (!string.IsNullOrWhiteSpace(backupCleanupWarning))
            {
                successMessage += UiText.T(
                    $"\n\nBackup cleanup warning:\n{backupCleanupWarning}",
                    $"\n\nНе удалось удалить предыдущую резервную копию:\n{backupCleanupWarning}");
            }

            MessageBox.Show(
                this,
                successMessage,
                "Deadlimit Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (OperationCanceledException) when (_heroExtractionCancellation?.IsCancellationRequested == true)
        {
            SetStatus(UiText.T(
                "Hero source extraction cancelled.",
                "Извлечение исходников героя отменено."));
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or InvalidDataException
            or ArgumentException
            or NotSupportedException)
        {
            SetStatus(UiText.T("Hero source extraction failed.", "Не удалось извлечь исходники героя."));
            MessageBox.Show(
                this,
                ex.Message,
                UiText.T("Hero source extraction failed", "Ошибка извлечения исходников героя"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _heroExtractionCancellation = null;
            _extractHeroButton.Enabled = true;
            if (_closeAfterHeroExtraction && !IsDisposed && IsHandleCreated)
            {
                BeginInvoke((Action)Close);
            }
        }
    }

    private static string? TryRemovePreviousHeroSourceBackup(
        string projectFolder,
        HeroExtractionFormat format)
    {
        try
        {
            var backupFolderName = format == HeroExtractionFormat.Gltf
                ? "glTFpipeline.previous"
                : "0source.previous";
            var previousFolder = Path.Combine(
                ProjectStore.GetMetadataFolder(projectFolder),
                backupFolderName);
            if (Directory.Exists(previousFolder))
            {
                Directory.Delete(previousFolder, recursive: true);
            }

            return null;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            return ex.Message;
        }
    }

    private void LoadManifest(ProjectManifest manifest)
    {
        _loadedManifest = manifest;
        _projectFolderText.Text = manifest.ProjectFolder;
        _projectNameText.Text = manifest.ProjectName;
        _heroText.Text = manifest.Hero;
        _releaseTargetText.Text = manifest.ReleaseTarget ?? string.Empty;
        RefreshScan(showStatus: false);
    }

    private void RefreshScan(bool showStatus)
    {
        var folder = _projectFolderText.Text.Trim();
        _assetList.Items.Clear();

        if (!Directory.Exists(folder))
        {
            _dmxCountLabel.Text = UiText.T("MODELS: 0", "МОДЕЛИ: 0");
            _pngCountLabel.Text = UiText.T("TEXTURES: 0", "ТЕКСТУРЫ: 0");
            _sourceFolderLabel.Text = UiText.T(
                "Hero source destination: 0source (created on demand by hero extraction).",
                "Папка исходников героя: 0source (создаётся по запросу при извлечении)." );
            return;
        }

        try
        {
            var scan = ProjectScanner.Scan(folder);
            _dmxCountLabel.Text = UiText.T(
                $"MODELS: {scan.DmxFiles.Count + scan.FbxFiles.Count + scan.GltfFiles.Count}",
                $"МОДЕЛИ: {scan.DmxFiles.Count + scan.FbxFiles.Count + scan.GltfFiles.Count}");
            _pngCountLabel.Text = UiText.T(
                $"TEXTURES: {scan.PngTextures.Count}",
                $"ТЕКСТУРЫ: {scan.PngTextures.Count}");

            var sourcePath = Path.Combine(folder, _loadedManifest?.SourceDumpFolderName ?? "0source");
            if (_loadedManifest?.LastSourceExtractionUtc is not null)
            {
                var unknownMainModel = UiText.T("unknown", "неизвестно");
                _sourceFolderLabel.Text = UiText.T(
                    $"Hero source: {sourcePath} | {_loadedManifest.ExtractedSourceFileCount ?? 0} files | main: {_loadedManifest.RetailMainModel ?? unknownMainModel}",
                    $"Исходники героя: {sourcePath} | файлов: {_loadedManifest.ExtractedSourceFileCount ?? 0} | main: {_loadedManifest.RetailMainModel ?? unknownMainModel}");
            }
            else
            {
                _sourceFolderLabel.Text = UiText.T(
                    $"Hero source destination: {sourcePath} (created only when extraction is requested).",
                    $"Папка исходников героя: {sourcePath} (создаётся только при запуске извлечения)." );
            }

            foreach (var file in scan.DmxFiles)
            {
                _assetList.Items.Add($"[DMX] {file}");
            }

            foreach (var file in scan.FbxFiles)
            {
                _assetList.Items.Add($"[FBX] {file}");
            }

            foreach (var file in scan.GltfFiles)
            {
                _assetList.Items.Add($"[{Path.GetExtension(file).TrimStart('.').ToUpperInvariant()}] {file}");
            }

            foreach (var file in scan.PngTextures)
            {
                _assetList.Items.Add($"[{Path.GetExtension(file).TrimStart('.').ToUpperInvariant()}] {file}");
            }

            if (showStatus)
            {
                SetStatus(UiText.T(
                    $"Scan complete. Models: {scan.DmxFiles.Count + scan.FbxFiles.Count + scan.GltfFiles.Count}; textures: {scan.PngTextures.Count}.",
                    $"Сканирование завершено. Моделей: {scan.DmxFiles.Count + scan.FbxFiles.Count + scan.GltfFiles.Count}; текстур: {scan.PngTextures.Count}."));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetStatus(UiText.T($"Scan failed: {ex.Message}", $"Ошибка сканирования: {ex.Message}"));
        }
    }

    private void ClearProjectView()
    {
        _loadedManifest = null;
        _projectFolderText.Clear();
        _projectNameText.Clear();
        _heroText.Clear();
        _releaseTargetText.Clear();
        _assetList.Items.Clear();
        _dmxCountLabel.Text = UiText.T("MODELS: 0", "МОДЕЛИ: 0");
        _pngCountLabel.Text = UiText.T("TEXTURES: 0", "ТЕКСТУРЫ: 0");
        _sourceFolderLabel.Text = UiText.T(
            "Select a project folder from the library.",
            "Выберите папку проекта в библиотеке.");
    }

    private bool EnsureLegacyRootAuthoringMigrated(string projectFolder)
    {
        var authoringRoot = Path.Combine(projectFolder, ProjectAuthoringLayout.AuthoringFolderName);
        var authoringAlreadyPopulated = Directory.Exists(authoringRoot)
            && Directory.EnumerateFiles(authoringRoot, "*", SearchOption.AllDirectories)
                .Any(IsLegacyAuthoringExtension);
        if (authoringAlreadyPopulated)
        {
            return true;
        }

        var legacyFiles = Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsLegacyAuthoringExtension)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (legacyFiles.Length == 0)
        {
            return true;
        }

        var names = string.Join(
            Environment.NewLine,
            legacyFiles.Take(12).Select(path => $"  • {Path.GetFileName(path)}"));
        if (legacyFiles.Length > 12)
        {
            names += Environment.NewLine + $"  … +{legacyFiles.Length - 12}";
        }

        var migrate = MessageBox.Show(
            this,
            UiText.T(
                $"This project still contains authoring files in the legacy project root. Current Deadlimit reads authoring inputs only from 1authoring.\n\nCopy these files into 1authoring now? The originals will be left unchanged.\n\n{names}",
                $"В корне проекта остались authoring-файлы старого формата. Текущий Deadlimit читает входные файлы только из 1authoring.\n\nСкопировать эти файлы в 1authoring? Оригиналы останутся без изменений.\n\n{names}"),
            UiText.T("Migrate legacy authoring files", "Перенести старые authoring-файлы"),
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (migrate != DialogResult.Yes)
        {
            return false;
        }

        Directory.CreateDirectory(authoringRoot);
        foreach (var source in legacyFiles)
        {
            var destination = Path.Combine(authoringRoot, Path.GetFileName(source));
            if (File.Exists(destination))
            {
                throw new IOException(
                    $"Legacy authoring migration cannot copy '{Path.GetFileName(source)}' because that file already exists in 1authoring.");
            }

            File.Copy(source, destination, overwrite: false);
        }

        return true;
    }

    private static bool IsLegacyAuthoringExtension(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".psd", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private void ShowValidation(string message)
    {
        MessageBox.Show(this, message, "Deadlimit Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
    }

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record ProjectLibraryItem(string Name, string Folder)
    {
        public override string ToString() => Name;
    }
}
