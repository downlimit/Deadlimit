$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = Get-Content -LiteralPath $Path -Raw
    if (-not $text.Contains($Old)) {
        throw "Expected source block was not found in $Path"
    }
    Set-Content -LiteralPath $Path -Value ($text.Replace($Old, $New)) -Encoding utf8NoBOM -NoNewline
}

$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
$libraryPath = 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs'
$uiSmokePath = 'internal/tests/ui-localization-smoke.ps1'

Replace-Exact $headerPath @'
    private static string? _cachedSteamExecutable;
'@ @'
    private static string? _cachedSteamExecutable;

    internal static string GetHeaderImagePath(string projectFolder) =>
        Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), HeaderFileName);
'@

Replace-Exact $headerPath @'
        void OpenHeaderFolder()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder))
            {
                return;
            }

            try
            {
                var imagePath = EnsureHeaderImage(folder, header.ClientSize);
                var artworkFolder = Path.GetDirectoryName(imagePath);
                if (string.IsNullOrWhiteSpace(artworkFolder) || !Directory.Exists(artworkFolder))
                {
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{artworkFolder}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    form,
                    ex.Message,
                    UiText.T("Could not open artwork folder", "Не удалось открыть папку обложки"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
'@ @'
        void OpenHeaderImage()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder))
            {
                return;
            }

            try
            {
                var imagePath = EnsureHeaderImage(folder, header.ClientSize);
                if (!File.Exists(imagePath))
                {
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = imagePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    form,
                    ex.Message,
                    UiText.T("Could not open project cover", "Не удалось открыть обложку проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
'@

Replace-Exact $headerPath '                OpenHeaderFolder();' '                OpenHeaderImage();'
Replace-Exact $headerPath '        var path = Path.Combine(metadataFolder, HeaderFileName);' '        var path = GetHeaderImagePath(projectFolder);'

Replace-Exact $libraryPath @'
            var renameItem = new ToolStripMenuItem(UiText.T("Rename", "Переименовать"));
            var openItem = new ToolStripMenuItem(UiText.T("Open in File Explorer", "Открыть в проводнике"));
            var deleteItem = new ToolStripMenuItem(UiText.T("Delete...", "Удалить..."));

            renameItem.Click += (_, _) => RenameSelectedProject();
            openItem.Click += (_, _) => OpenSelectedProjectFolder();
            deleteItem.Click += (_, _) => DeleteSelectedProject();

            menu.Items.Add(renameItem);
            menu.Items.Add(openItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(deleteItem);
            menu.Opening += (sender, e) =>
            {
                var hasSelection = TryGetSelectedProject(out _, out _);
                renameItem.Enabled = hasSelection;
                openItem.Enabled = hasSelection;
                deleteItem.Enabled = hasSelection;
'@ @'
            var renameItem = new ToolStripMenuItem(UiText.T("Rename", "Переименовать"));
            var openItem = new ToolStripMenuItem(UiText.T("Open in File Explorer", "Открыть в проводнике"));
            var openCoverItem = new ToolStripMenuItem(UiText.T("Open Project Cover", "Открыть обложку проекта"));
            var deleteItem = new ToolStripMenuItem(UiText.T("Delete...", "Удалить..."));

            renameItem.Click += (_, _) => RenameSelectedProject();
            openItem.Click += (_, _) => OpenSelectedProjectFolder();
            openCoverItem.Click += (_, _) => OpenSelectedProjectCover();
            deleteItem.Click += (_, _) => DeleteSelectedProject();

            menu.Items.Add(renameItem);
            menu.Items.Add(openItem);
            menu.Items.Add(openCoverItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(deleteItem);
            menu.Opening += (sender, e) =>
            {
                var hasSelection = TryGetSelectedProject(out _, out _);
                renameItem.Enabled = hasSelection;
                openItem.Enabled = hasSelection;
                openCoverItem.Enabled = hasSelection && HasSelectedProjectCover();
                deleteItem.Enabled = hasSelection;
'@

Replace-Exact $libraryPath @'
        private void OpenSelectedProjectFolder()
'@ @'
        private bool HasSelectedProjectCover()
        {
            return TryGetSelectedProject(out _, out var folder)
                && File.Exists(ProjectHeaderFeature.GetHeaderImagePath(folder));
        }

        private void OpenSelectedProjectCover()
        {
            if (!TryGetSelectedProject(out _, out var folder))
            {
                return;
            }

            var imagePath = ProjectHeaderFeature.GetHeaderImagePath(folder);
            if (!File.Exists(imagePath))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = imagePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    _form,
                    ex.Message,
                    UiText.T("Could not open project cover", "Не удалось открыть обложку проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedProjectFolder()
'@

Replace-Exact $uiSmokePath @'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Text = "📂 CSDK Fast Startup Fix"'
'@ @'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Text = "📂 CSDK Fast Startup Fix"'
Assert-Contains 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs' 'UiText.T("Could not open project cover", "Не удалось открыть обложку проекта")'
Assert-Contains 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs' 'UiText.T("Open Project Cover", "Открыть обложку проекта")'
Assert-Contains 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs' 'ProjectHeaderFeature.GetHeaderImagePath(folder)'
Assert-NotContains 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs' 'OpenHeaderFolder()'
'@
