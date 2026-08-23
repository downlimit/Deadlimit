using Deadlimit.Core;

namespace Deadlimit.App;

public sealed class MainForm : Form
{
    private readonly ListBox _projectLibrary = new()
    {
        Dock = DockStyle.Fill,
        IntegralHeight = false,
    };
    private readonly TextBox _projectFolderText = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox _projectNameText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _heroText = new() { Dock = DockStyle.Fill };
    private readonly TextBox _releaseTargetText = new() { Dock = DockStyle.Fill };
    private readonly Label _dmxCountLabel = new() { AutoSize = true };
    private readonly Label _pngCountLabel = new() { AutoSize = true };
    private readonly Label _sourceFolderLabel = new() { AutoSize = true };
    private readonly ListBox _assetList = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _statusLabel = new()
    {
        Text = UiText.T("Select a project from the library.", "Выберите проект в библиотеке."),
    };
    private readonly Button _extractHeroButton = new()
    {
        Text = UiText.T("EXTRACT HERO SOURCE", "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ"),
        AutoSize = true,
    };

    private ProjectManifest? _loadedManifest;
    private bool _refreshingProjectLibrary;
    private bool _libraryInitialized;

    public MainForm()
    {
        Text = "Deadlimit";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 980;
        Height = 660;
        MinimumSize = new Size(800, 540);

        BuildUi();
        Shown += (_, _) =>
        {
            _libraryInitialized = true;
            InitializeProjectLibrary();
        };
        Activated += (_, _) =>
        {
            if (_libraryInitialized)
            {
                RefreshProjectLibrary(preserveSelection: true, rescanSelected: true);
            }
        };
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

            SelectProjectFolder(item.Folder, rememberSelection: true, showStatus: true);
        };
        libraryGroup.Controls.Add(_projectLibrary);

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
        };
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 10),
        };

        var settingsButton = new Button
        {
            Text = UiText.T("SETTINGS", "НАСТРОЙКИ"),
            AutoSize = true,
        };
        settingsButton.Click += (_, _) => ShowSettings();
        _extractHeroButton.Click += async (_, _) => await ExtractHeroSourceAsync();

        topBar.Controls.Add(_extractHeroButton);
        topBar.Controls.Add(settingsButton);

        var projectGroup = new GroupBox
        {
            Text = UiText.T("Project", "Проект"),
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(12),
        };

        var projectGrid = new TableLayoutPanel
        {
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
            Text = UiText.T("Detected in project root", "Найдено в корне проекта"),
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
        };

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

        var statusStrip = new StatusStrip();
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

    private void InitializeProjectLibrary()
    {
        RefreshProjectLibrary(
            preserveSelection: false,
            rescanSelected: false,
            preferredFolder: ProjectStore.GetLastProjectFolder());
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
                $"Saved. DMX: {_loadedManifest!.DmxFiles.Count}; PNG: {_loadedManifest.PngTextures.Count}.",
                $"Сохранено. DMX: {_loadedManifest!.DmxFiles.Count}; PNG: {_loadedManifest.PngTextures.Count}."));
        }
    }

    private bool TrySaveProject()
    {
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
            var scan = ProjectScanner.Scan(folder);
            var existing = ProjectStore.TryLoad(folder) ?? _loadedManifest;

            var manifest = new ProjectManifest
            {
                SchemaVersion = Math.Max(existing?.SchemaVersion ?? 1, 2),
                ProjectName = projectName,
                ProjectFolder = Path.GetFullPath(folder),
                Hero = hero,
                ReleaseTarget = releaseTarget,
                SourceDumpFolderName = existing?.SourceDumpFolderName ?? "0source",
                DmxFiles = [.. scan.DmxFiles],
                PngTextures = [.. scan.PngTextures],
                CreatedUtc = existing?.CreatedUtc ?? DateTimeOffset.UtcNow,
                RetailMainModel = existing?.RetailMainModel,
                RetailSourceVpk = existing?.RetailSourceVpk,
                LastSourceExtractionUtc = existing?.LastSourceExtractionUtc,
                Source2ViewerVersion = existing?.Source2ViewerVersion,
                ExtractedSourceFileCount = existing?.ExtractedSourceFileCount,
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
        if (Directory.Exists(outputFolder) && Directory.EnumerateFileSystemEntries(outputFolder).Any())
        {
            var answer = MessageBox.Show(
                this,
                UiText.T(
                    "0source already contains files. Refresh it from the current retail Deadlock build?\n\nThe previous 0source will be preserved as a hidden backup until the new extraction succeeds.",
                    "0source уже содержит файлы. Обновить его из текущей retail-сборки Deadlock?\n\nПредыдущий 0source будет сохранён как скрытый backup до успешного завершения нового извлечения."),
                UiText.T("Refresh hero source", "Обновить исходники героя"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (answer != DialogResult.Yes)
            {
                SetStatus(UiText.T("Hero source extraction cancelled.", "Извлечение исходников героя отменено."));
                return;
            }
        }

        _extractHeroButton.Enabled = false;
        try
        {
            var progress = new Progress<HeroExtractionProgress>(update => SetStatus(update.Message));
            var service = new HeroExtractionService(new DeadlimitPaths());
            var result = await service.ExtractAsync(_loadedManifest, progress);

            RefreshScan(showStatus: false);
            SetStatus(UiText.T(
                $"Hero source ready: {result.ExtractedFileCount} files.",
                $"Исходники героя готовы: {result.ExtractedFileCount} файлов."));

            MessageBox.Show(
                this,
                UiText.T(
                    $"Hero source refreshed successfully.\n\nMain model: {result.MainModelResourcePath}\nFiles: {result.ExtractedFileCount}\nOutput: {result.OutputFolder}",
                    $"Исходники героя успешно обновлены.\n\nОсновная модель: {result.MainModelResourcePath}\nФайлов: {result.ExtractedFileCount}\nПапка: {result.OutputFolder}"),
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
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
            _extractHeroButton.Enabled = true;
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
            _dmxCountLabel.Text = "DMX: 0";
            _pngCountLabel.Text = "PNG: 0";
            _sourceFolderLabel.Text = UiText.T(
                "Hero source destination: 0source (created on demand by hero extraction).",
                "Папка исходников героя: 0source (создаётся по запросу при извлечении)." );
            return;
        }

        try
        {
            var scan = ProjectScanner.Scan(folder);
            _dmxCountLabel.Text = $"DMX: {scan.DmxFiles.Count}";
            _pngCountLabel.Text = $"PNG: {scan.PngTextures.Count}";

            var sourcePath = Path.Combine(folder, _loadedManifest?.SourceDumpFolderName ?? "0source");
            if (_loadedManifest?.LastSourceExtractionUtc is not null)
            {
                _sourceFolderLabel.Text = UiText.T(
                    $"Hero source: {sourcePath} | {_loadedManifest.ExtractedSourceFileCount ?? 0} files | main: {_loadedManifest.RetailMainModel ?? "unknown"}",
                    $"Исходники героя: {sourcePath} | файлов: {_loadedManifest.ExtractedSourceFileCount ?? 0} | main: {_loadedManifest.RetailMainModel ?? "неизвестно"}");
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

            foreach (var file in scan.PngTextures)
            {
                _assetList.Items.Add($"[PNG] {file}");
            }

            if (showStatus)
            {
                SetStatus(UiText.T(
                    $"Scan complete. DMX: {scan.DmxFiles.Count}; PNG: {scan.PngTextures.Count}.",
                    $"Сканирование завершено. DMX: {scan.DmxFiles.Count}; PNG: {scan.PngTextures.Count}."));
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
        _dmxCountLabel.Text = "DMX: 0";
        _pngCountLabel.Text = "PNG: 0";
        _sourceFolderLabel.Text = UiText.T(
            "Select a project folder from the library.",
            "Выберите папку проекта в библиотеке.");
    }

    private void ShowValidation(string message)
    {
        MessageBox.Show(this, message, "Deadlimit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
