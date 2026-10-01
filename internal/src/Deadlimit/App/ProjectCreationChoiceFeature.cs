using System.Reflection;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectCreationChoiceFeature
{
    public static async Task ExtractVpkAsProjectAsync(MainForm form, Button triggerButton)
    {
        if (ApplicationMutationCoordinator.IsBusy)
        {
            MessageBox.Show(
                form,
                UiText.T(
                    $"Cannot extract a VPK while {ApplicationMutationCoordinator.ActiveOperation} is running.",
                    $"Нельзя извлечь VPK, пока выполняется операция: {ApplicationMutationCoordinator.ActiveOperation}."),
                UiText.T("Operation in progress", "Операция выполняется"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var settings = ProjectStore.GetToolPathSettings();
        var retailAddons = Path.Combine(
            settings.RetailDeadlockRoot,
            "game",
            "citadel",
            "addons");

        using var dialog = new OpenFileDialog
        {
            Title = UiText.T("Extract Deadlock VPK", "Извлечь VPK Deadlock"),
            Filter = UiText.T(
                "VPK directory archives (*_dir.vpk)|*_dir.vpk",
                "Архивы VPK directory (*_dir.vpk)|*_dir.vpk"),
            FilterIndex = 1,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            DereferenceLinks = true,
            SupportMultiDottedExtensions = true,
        };
        if (Directory.Exists(retailAddons))
        {
            dialog.InitialDirectory = retailAddons;
        }

        if (dialog.ShowDialog(form) != DialogResult.OK)
        {
            return;
        }

        triggerButton.Enabled = false;
        VpkImportCandidate candidate;
        VpkImportIdentity identity;
        try
        {
            using var inspectionProgress = new VpkImportProgressPresenter(form);
            inspectionProgress.Update(new ImportedVpkImportProgress(
                UiText.T("Inspecting the selected VPK...", "Проверка выбранного VPK..."),
                2));
            (candidate, identity) = await Task.Run(() =>
            {
                var validated = VpkImportSourceValidator.Validate(dialog.FileName);
                return (validated, VpkImportIdentityService.Infer(validated));
            });
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            triggerButton.Enabled = true;
            MessageBox.Show(
                form,
                exception.Message,
                UiText.T("Could not extract VPK", "Не удалось извлечь VPK"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var nameDialog = new ImportedVpkProjectNameDialog(
            identity.SuggestedFolderName,
            settings.UiTheme);
        WindowProgressFeature.ReportStatus(
            form,
            UiText.T(
                "VPK inspected. Enter the project name.",
                "VPK проверен. Введите название проекта."));
        if (nameDialog.ShowDialog(form) != DialogResult.OK)
        {
            triggerButton.Enabled = true;
            WindowProgressFeature.ReportStatus(
                form,
                UiText.T("VPK extraction cancelled.", "Извлечение VPK отменено."));
            return;
        }

        ImportedVpkProjectResult importedProject;
        using var cancellation = new CancellationTokenSource();
        void CancelWhenClosed(object? _, FormClosedEventArgs __) => cancellation.Cancel();
        form.FormClosed += CancelWhenClosed;
        try
        {
            using var operation = ApplicationMutationCoordinator.Begin("EXTRACT VPK");
            using var importProgress = new VpkImportProgressPresenter(form);
            var progress = new Progress<ImportedVpkImportProgress>(importProgress.Update);
            importedProject = await Task.Run(
                () => ImportedVpkProjectService.Create(
                    candidate,
                    identity,
                    settings.ProjectsRoot,
                    nameDialog.ProjectName,
                    progress,
                    cancellation.Token),
                cancellation.Token);
            importProgress.Update(new ImportedVpkImportProgress(
                UiText.T("VPK extraction into the project is complete.", "Извлечение VPK в проект завершено."),
                100));
        }
        catch (OperationCanceledException)
        {
            if (!form.IsDisposed)
            {
                WindowProgressFeature.ReportStatus(
                    form,
                    UiText.T("VPK extraction cancelled.", "Извлечение VPK отменено."));
            }
            return;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!form.IsDisposed)
            {
                MessageBox.Show(
                    form,
                    exception.Message,
                    UiText.T("Could not extract VPK", "Не удалось извлечь VPK"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }
        finally
        {
            form.FormClosed -= CancelWhenClosed;
            if (!triggerButton.IsDisposed)
            {
                triggerButton.Enabled = true;
            }
        }

        if (form.IsDisposed)
        {
            return;
        }

        TrySelectImportedProject(form, importedProject.ProjectFolder);
        ShowImportedProjectCreated(form, importedProject, identity);
    }

    private static void TrySelectImportedProject(MainForm form, string projectFolder)
    {
        try
        {
            var refreshMethod = typeof(MainForm)
                .GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(method =>
                {
                    if (!string.Equals(method.Name, "RefreshProjectLibrary", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    var parameters = method.GetParameters();
                    return parameters.Length == 3
                        && parameters[0].ParameterType == typeof(bool)
                        && parameters[1].ParameterType == typeof(bool)
                        && parameters[2].ParameterType == typeof(string);
                });

            refreshMethod?.Invoke(form, [false, false, projectFolder]);
        }
        catch (Exception exception) when (exception is TargetInvocationException
            or ArgumentException
            or MethodAccessException
            or InvalidOperationException)
        {
            // ProjectStore already remembers the imported project as last selected.
            // A normal Library refresh/activation will pick it up if this best-effort
            // immediate refresh cannot be invoked on the current UI implementation.
        }
    }

    private static void ShowImportedProjectCreated(
        MainForm form,
        ImportedVpkProjectResult importedProject,
        VpkImportIdentity identity)
    {
        var manifest = importedProject.Manifest;
        var releaseId = manifest.ReleaseTarget
            ?? UiText.T("not derived from filename", "не определён по имени файла");
        var hero = identity.HeroDisplayName
            ?? UiText.T("not identified confidently", "не определён с достаточной уверенностью");
        var primaryModel = identity.PrimaryModelResources.Count > 0
            ? identity.PrimaryModelResources[0]
            : UiText.T("not uniquely identified", "не определена однозначно");

        MessageBox.Show(
            form,
            UiText.T(
                $"VPK extracted into a new project.\n\nProject: {manifest.ProjectName}\nHero: {hero}\nRelease ID: {releaseId}\nPrimary model: {primaryModel}\nPreserved files: {manifest.ImportedVpk!.SourceEntryCount}\nWorking files: {importedProject.AuthoringFolder}\nSHA-256: {manifest.ImportedVpk.OriginalVpkSha256}\n\nThe original VPK entry manifest is stored in .deadlimit/original-vpk.json.",
                $"VPK извлечён в новый проект.\n\nПроект: {manifest.ProjectName}\nГерой: {hero}\nRelease ID: {releaseId}\nОсновная модель: {primaryModel}\nСохранено файлов: {manifest.ImportedVpk!.SourceEntryCount}\nРабочие файлы: {importedProject.AuthoringFolder}\nSHA-256: {manifest.ImportedVpk.OriginalVpkSha256}\n\nСписок исходных VPK-файлов и их хэшей сохранён в .deadlimit/original-vpk.json."),
            UiText.T("VPK extracted", "VPK извлечён"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class ImportedVpkProjectNameDialog : Form
    {
        private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };

        public ImportedVpkProjectNameDialog(string suggestedName, string theme)
        {
            _nameText.Text = suggestedName;

            Text = UiText.T("Extract VPK into project", "Извлечь VPK в проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(470, 150);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
            Shown += (_, _) =>
            {
                _nameText.Focus();
                _nameText.SelectAll();
            };
        }

        public string ProjectName { get; private set; } = string.Empty;

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
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Text = UiText.T(
                    "Project name. This will also be the mod's working folder name.",
                    "Название проекта. Оно также станет именем рабочей папки мода."),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            _nameText.Margin = new Padding(0, 0, 0, 12);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };
            var cancelButton = new Button
            {
                Text = UiText.T("CANCEL", "ОТМЕНА"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
            };
            var extractButton = new Button
            {
                Text = UiText.T("EXTRACT", "ИЗВЛЕЧЬ"),
                AutoSize = true,
            };
            extractButton.Click += (_, _) => CompleteExtraction();

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(extractButton);
            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_nameText, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = extractButton;
            CancelButton = cancelButton;
        }

        private void CompleteExtraction()
        {
            var name = _nameText.Text.Trim();
            if (name.Length == 0
                || Path.IsPathRooted(name)
                || name.Contains(Path.DirectorySeparatorChar)
                || name.Contains(Path.AltDirectorySeparatorChar)
                || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || name.EndsWith(' ')
                || name.EndsWith('.'))
            {
                MessageBox.Show(
                    this,
                    UiText.T(
                        "Enter a valid project name without path separators or reserved filename characters.",
                        "Введите корректное название проекта без разделителей пути и запрещённых символов имени файла."),
                    UiText.T("Invalid project name", "Некорректное название проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            ProjectName = name;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private sealed class VpkImportProgressPresenter : IDisposable
    {
        private readonly MainForm _form;
        private readonly ToolStripProgressBar? _progressBar;
        private bool _disposed;

        public VpkImportProgressPresenter(MainForm form)
        {
            _form = form;
            _progressBar = FindDescendants<StatusStrip>(form)
                .FirstOrDefault()?
                .Items
                .OfType<ToolStripProgressBar>()
                .FirstOrDefault();
        }

        public void Update(ImportedVpkImportProgress update)
        {
            if (_disposed || _form.IsDisposed)
            {
                return;
            }

            if (_progressBar is not null)
            {
                _progressBar.Value = Math.Clamp(update.Percent, 0, 100);
                _progressBar.Visible = true;
            }
            SteamStatusFeature.ReportProgress(_form, update.Percent);
            WindowProgressFeature.ReportStatus(_form, update.Message);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_progressBar is not null && !_progressBar.IsDisposed)
            {
                _progressBar.Visible = false;
                _progressBar.Value = 0;
            }
            if (!_form.IsDisposed)
            {
                SteamStatusFeature.ReportProgress(_form, null);
            }
        }
    }
}
