using Deadlimit.Core;

namespace Deadlimit.App;

public sealed class MainForm : Form
{
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
        Text = UiText.T("Select or create a project.", "Выберите или создайте проект."),
    };
    private readonly Button _extractHeroButton = new()
    {
        Text = UiText.T("EXTRACT HERO SOURCE", "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ"),
        AutoSize = true,
    };

    private ProjectManifest? _loadedManifest;

    public MainForm()
    {
        Text = "Deadlimit";
        StartPosition = FormStartPosition.CenterScreen;
        Width = 980;
        Height = 660;
        MinimumSize = new Size(800, 540);

        BuildUi();
        Shown += (_, _) => RestoreLastProject();
    }

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var topBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 10),
        };

        var newButton = new Button
        {
            Text = UiText.T("NEW PROJECT", "НОВЫЙ ПРОЕКТ"),
            AutoSize = true,
        };
        newButton.Click += (_, _) => NewProject();

        var openButton = new Button
        {
            Text = UiText.T("OPEN PROJECT", "ОТКРЫТЬ ПРОЕКТ"),
            AutoSize = true,
        };
        openButton.Click += (_, _) => OpenProject();

        var rescanButton = new Button
        {
            Text = UiText.T("RESCAN", "ПЕРЕСКАНИРОВАТЬ"),
            AutoSize = true,
        };
        rescanButton.Click += (_, _) => RefreshScan(showStatus: true);

        var settingsButton = new Button
        {
            Text = UiText.T("SETTINGS", "НАСТРОЙКИ"),
            AutoSize = true,
        };
        settingsButton.Click += (_, _) => ShowSettings();
        _extractHeroButton.Click += async (_, _) => await ExtractHeroSourceAsync();

        topBar.Controls.Add(newButton);
        topBar.Controls.Add(openButton);
        topBar.Controls.Add(rescanButton);
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

        var folderButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Anchor = AnchorStyles.Left,
        };
        var browseButton = new Button
        {
            Text = UiText.T("BROWSE", "ОБЗОР"),
            AutoSize = true,
        };
        browseButton.Click += (_, _) => BrowseProjectFolder();
        var openFolderButton = new Button
        {
            Text = UiText.T("OPEN", "ОТКРЫТЬ"),
            AutoSize = true,
        };
        openFolderButton.Click += (_, _) => OpenProjectFolder();
        folderButtons.Controls.Add(browseButton);
        folderButtons.Controls.Add(openFolderButton);
        projectGrid.Controls.Add(folderButtons, 2, 0);

        AddField(projectGrid, 1, UiText.T("Project name", "Имя проекта"), _projectNameText);
        AddField(projectGrid, 2, UiText.T("Hero", "Герой"), _heroText);
        AddField(projectGrid, 3, "Release ID", _releaseTargetText);

        var saveButton = new Button
        {
            Text = UiText.T("CREATE / SAVE PROJECT", "СОЗДАТЬ / СОХРАНИТЬ ПРОЕКТ"),
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

        var statusStrip = new StatusStrip();
        statusStrip.Items.Add(_statusLabel);

        root.Controls.Add(topBar, 0, 0);
        root.Controls.Add(projectGroup, 0, 1);
        root.Controls.Add(assetsGroup, 0, 2);
        root.Controls.Add(statusStrip, 0, 3);
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

    private void RestoreLastProject()
    {
        try
        {
            var manifest = ProjectStore.TryLoadLastProject();
            if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
            {
                return;
            }

            LoadManifest(manifest);
            SetStatus(UiText.T(
                $"Opened last project: {manifest.ProjectName}",
                $"Открыт последний проект: {manifest.ProjectName}"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SetStatus(UiText.T("Could not reopen the last project.", "Не удалось открыть последний проект."));
        }
    }

    private void NewProject()
    {
        var folder = ChooseFolder(UiText.T(
            "Select the folder that contains your project's DMX and PNG files",
            "Выберите папку проекта с файлами DMX и PNG"));
        if (folder is null)
        {
            return;
        }

        var existing = ProjectStore.TryLoad(folder);
        if (existing is not null)
        {
            LoadManifest(existing);
            SetStatus(UiText.T(
                "This folder already contains a Deadlimit project; opened it instead.",
                "В этой папке уже есть проект Deadlimit; он был открыт."));
            return;
        }

        _loadedManifest = null;
        _projectFolderText.Text = folder;
        _projectNameText.Text = new DirectoryInfo(folder).Name;
        _heroText.Clear();
        _releaseTargetText.Clear();
        RefreshScan(showStatus: false);
        SetStatus(UiText.T(
            "New project folder selected. Enter the hero and save the project.",
            "Выбрана папка нового проекта. Укажите героя и сохраните проект."));
    }

    private void OpenProject()
    {
        var folder = ChooseFolder(UiText.T(
            "Select an existing Deadlimit project folder",
            "Выберите папку существующего проекта Deadlimit"));
        if (folder is null)
        {
            return;
        }

        var manifest = ProjectStore.TryLoad(folder);
        if (manifest is null)
        {
            MessageBox.Show(
                this,
                UiText.T(
                    "No Deadlimit project metadata was found in this folder. Use NEW PROJECT to initialize it.",
                    "В этой папке не найдены метаданные проекта Deadlimit. Используйте НОВЫЙ ПРОЕКТ для инициализации."),
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        LoadManifest(manifest);
        SetStatus(UiText.T(
            $"Opened project: {manifest.ProjectName}",
            $"Открыт проект: {manifest.ProjectName}"));
    }

    private void BrowseProjectFolder()
    {
        var folder = ChooseFolder(UiText.T("Select project folder", "Выберите папку проекта"));
        if (folder is null)
        {
            return;
        }

        var manifest = ProjectStore.TryLoad(folder);
        if (manifest is not null)
        {
            LoadManifest(manifest);
            SetStatus(UiText.T(
                $"Opened project: {manifest.ProjectName}",
                $"Открыт проект: {manifest.ProjectName}"));
            return;
        }

        _loadedManifest = null;
        _projectFolderText.Text = folder;
        if (string.IsNullOrWhiteSpace(_projectNameText.Text))
        {
            _projectNameText.Text = new DirectoryInfo(folder).Name;
        }

        RefreshScan(showStatus: true);
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
            if (dialog.LanguageChanged)
            {
                Application.Restart();
                Close();
                return;
            }

            SetStatus(UiText.T(
                "Tool paths saved. New actions will use the updated paths immediately.",
                "Пути к инструментам сохранены. Новые действия сразу используют обновлённые пути."));
        }
    }

    private void SaveProject()
    {
        if (TrySaveProject())
        {
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
        _projectFolderText.Clear();
        _projectNameText.Clear();
        _heroText.Clear();
        _releaseTargetText.Clear();
        _assetList.Items.Clear();
        _dmxCountLabel.Text = "DMX: 0";
        _pngCountLabel.Text = "PNG: 0";
        _sourceFolderLabel.Text = UiText.T(
            "Hero source destination: 0source (created on demand by hero extraction).",
            "Папка исходников героя: 0source (создаётся по запросу при извлечении)." );
    }

    private static string? ChooseFolder(string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
        };

        return dialog.ShowDialog() == DialogResult.OK ? dialog.SelectedPath : null;
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
}
