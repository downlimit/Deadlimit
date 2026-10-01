using System.Reflection;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectCreationChoiceFeature
{
    public static async Task ExtractVpkIntoCurrentProjectAsync(MainForm form, Button triggerButton)
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

        var currentProject = form.LoadedManifest;
        if (currentProject is null)
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "Save the current project before extracting a VPK into 1authoring.",
                    "Сохраните текущий проект перед извлечением VPK в 1authoring."),
                UiText.T("Project is not saved", "Проект не сохранён"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (ProjectSaveStateFeature.IsDirty(form))
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "Save the changed project folder, hero, or Release ID before extracting a VPK.",
                    "Сохраните изменённую папку проекта, героя или Release ID перед извлечением VPK."),
                UiText.T("Save the project", "Сохраните проект"),
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
                () => ImportedVpkProjectService.ExtractIntoExisting(
                    currentProject,
                    candidate,
                    identity,
                    new DeadlimitPaths(),
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
        ShowVpkExtracted(form, importedProject);
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

    private static void ShowVpkExtracted(
        MainForm form,
        ImportedVpkProjectResult importedProject)
    {
        var manifest = importedProject.Manifest;

        MessageBox.Show(
            form,
            UiText.T(
                $"VPK extracted into the current project's 1authoring folder.\n\nProject: {manifest.ProjectName}\nFiles: {manifest.ImportedVpk!.SourceEntryCount}\nOutput: {importedProject.AuthoringFolder}",
                $"VPK извлечён в папку 1authoring текущего проекта.\n\nПроект: {manifest.ProjectName}\nФайлов: {manifest.ImportedVpk!.SourceEntryCount}\nПапка: {importedProject.AuthoringFolder}"),
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
