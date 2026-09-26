using System.ComponentModel;
using System.Reflection;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectCreationChoiceFeature
{
    public static void Attach(MainForm form)
    {
        var libraryGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group => group.Name == UiControlNames.LibraryGroup);
        if (libraryGroup is null)
        {
            return;
        }

        var legacyAddButton = libraryGroup.Controls
            .OfType<Button>()
            .FirstOrDefault(IsLegacyAddButton);
        if (legacyAddButton is null)
        {
            return;
        }

        var createProjectHandlers = DetachClickHandlers(legacyAddButton);
        if (createProjectHandlers.Length == 0)
        {
            throw new InvalidOperationException("Deadlimit could not preserve the existing project creation action.");
        }

        var addButton = new Button
        {
            Text = string.Empty,
            Width = legacyAddButton.Width,
            Height = legacyAddButton.Height,
            Location = legacyAddButton.Location,
            Anchor = legacyAddButton.Anchor,
            Padding = legacyAddButton.Padding,
            Margin = legacyAddButton.Margin,
            TabStop = false,
        };
        addButton.Paint += (_, e) =>
        {
            TextRenderer.DrawText(
                e.Graphics,
                "+",
                addButton.Font,
                addButton.ClientRectangle,
                addButton.ForeColor,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix);
        };
        addButton.Click += (_, _) => ShowProjectEntryChoice(form, addButton, createProjectHandlers);

        libraryGroup.Controls.Remove(legacyAddButton);
        legacyAddButton.Visible = false;
        libraryGroup.Controls.Add(addButton);
        addButton.BringToFront();

        var toolTip = new RichToolTip();
        toolTip.SetToolTip(
            addButton,
            UiText.T(
                "Add a project to the Library.\n\nCreate a new project or import an existing Deadlock VPK.",
                "Добавить проект в Библиотеку.\n\nСоздайте новый проект или импортируйте существующий VPK Deadlock."));

        ImportedProjectModeFeature.Attach(form);

        form.Disposed += (_, _) =>
        {
            toolTip.Dispose();
            legacyAddButton.Dispose();
        };
    }

    private static bool IsLegacyAddButton(Button button) =>
        string.IsNullOrEmpty(button.Text)
        && button.Width == 26
        && button.Height == 23
        && button.Anchor.HasFlag(AnchorStyles.Right)
        && button.Anchor.HasFlag(AnchorStyles.Top);

    private static EventHandler[] DetachClickHandlers(Button button)
    {
        var eventsProperty = typeof(Component).GetProperty(
            "Events",
            BindingFlags.Instance | BindingFlags.NonPublic);
        var clickEventKeyField = typeof(Control).GetField(
            "s_clickEvent",
            BindingFlags.Static | BindingFlags.NonPublic);

        if (eventsProperty?.GetValue(button) is not EventHandlerList eventHandlers
            || clickEventKeyField?.GetValue(null) is not object clickEventKey
            || eventHandlers[clickEventKey] is not Delegate handlers)
        {
            return [];
        }

        var result = handlers.GetInvocationList().OfType<EventHandler>().ToArray();
        foreach (var handler in result)
        {
            button.Click -= handler;
        }
        return result;
    }

    private static void ShowProjectEntryChoice(
        MainForm form,
        Button sender,
        IReadOnlyList<EventHandler> createProjectHandlers)
    {
        if (ApplicationMutationCoordinator.IsBusy)
        {
            MessageBox.Show(
                form,
                UiText.T(
                    $"Cannot create or import a project while {ApplicationMutationCoordinator.ActiveOperation} is running.",
                    $"Нельзя создать или импортировать проект, пока выполняется операция: {ApplicationMutationCoordinator.ActiveOperation}."),
                UiText.T("Operation in progress", "Операция выполняется"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var settings = ProjectStore.GetToolPathSettings();
        using var dialog = new ProjectEntryChoiceDialog(settings.UiTheme);
        if (dialog.ShowDialog(form) != DialogResult.OK)
        {
            return;
        }

        void ContinueAfterChoice(Action continuation)
        {
            if (form.IsDisposed || !form.IsHandleCreated)
            {
                return;
            }

            form.BeginInvoke((Action)(() =>
            {
                if (form.IsDisposed)
                {
                    return;
                }

                form.Activate();
                continuation();
            }));
        }

        switch (dialog.Choice)
        {
            case ProjectEntryChoice.CreateProject:
                ContinueAfterChoice(() =>
                {
                    foreach (var handler in createProjectHandlers)
                    {
                        handler(sender, EventArgs.Empty);
                    }
                });
                break;

            case ProjectEntryChoice.ImportVpk:
                ContinueAfterChoice(() => _ = SelectVpkImportSourceAsync(form, sender));
                break;
        }
    }

    private static async Task SelectVpkImportSourceAsync(MainForm form, Button addButton)
    {
        var settings = ProjectStore.GetToolPathSettings();
        var retailAddons = Path.Combine(
            settings.RetailDeadlockRoot,
            "game",
            "citadel",
            "addons");

        using var dialog = new OpenFileDialog
        {
            Title = UiText.T("Import Deadlock VPK", "Импорт VPK Deadlock"),
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

        addButton.Enabled = false;
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
            addButton.Enabled = true;
            MessageBox.Show(
                form,
                exception.Message,
                UiText.T("Could not import VPK", "Не удалось импортировать VPK"),
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
            addButton.Enabled = true;
            WindowProgressFeature.ReportStatus(
                form,
                UiText.T("VPK import cancelled.", "Импорт VPK отменён."));
            return;
        }

        ImportedVpkProjectResult importedProject;
        using var cancellation = new CancellationTokenSource();
        void CancelWhenClosed(object? _, FormClosedEventArgs __) => cancellation.Cancel();
        form.FormClosed += CancelWhenClosed;
        try
        {
            using var operation = ApplicationMutationCoordinator.Begin("IMPORT VPK");
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
                UiText.T("VPK project import complete.", "Импорт проекта из VPK завершён."),
                100));
        }
        catch (OperationCanceledException)
        {
            if (!form.IsDisposed)
            {
                WindowProgressFeature.ReportStatus(
                    form,
                    UiText.T("VPK import cancelled.", "Импорт VPK отменён."));
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
                    UiText.T("Could not import VPK", "Не удалось импортировать VPK"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }
        finally
        {
            form.FormClosed -= CancelWhenClosed;
            if (!addButton.IsDisposed)
            {
                addButton.Enabled = true;
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
                $"Imported VPK project created.\n\nProject: {manifest.ProjectName}\nHero: {hero}\nRelease ID: {releaseId}\nPrimary model: {primaryModel}\nPreserved files: {manifest.ImportedVpk!.SourceEntryCount}\nWorking files: {importedProject.AuthoringFolder}\nSHA-256: {manifest.ImportedVpk.OriginalVpkSha256}\n\nThe original VPK entry manifest is stored in .deadlimit/original-vpk.json.",
                $"Проект из VPK создан.\n\nПроект: {manifest.ProjectName}\nГерой: {hero}\nRelease ID: {releaseId}\nОсновная модель: {primaryModel}\nСохранено файлов: {manifest.ImportedVpk!.SourceEntryCount}\nРабочие файлы: {importedProject.AuthoringFolder}\nSHA-256: {manifest.ImportedVpk.OriginalVpkSha256}\n\nСписок исходных VPK-файлов и их хэшей сохранён в .deadlimit/original-vpk.json."),
            UiText.T("VPK project created", "Проект из VPK создан"),
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

    private enum ProjectEntryChoice
    {
        None,
        CreateProject,
        ImportVpk,
    }

    private sealed class ProjectEntryChoiceDialog : Form
    {
        public ProjectEntryChoiceDialog(string theme)
        {
            Text = UiText.T("Add project", "Добавить проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(470, 128);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
        }

        public ProjectEntryChoice Choice { get; private set; }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var prompt = new Label
            {
                Text = UiText.T(
                    "How do you want to add the project?",
                    "Как добавить проект?"),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 14),
            };

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
            var importButton = new Button
            {
                Text = UiText.T("IMPORT VPK...", "ИМПОРТ VPK..."),
                AutoSize = true,
            };
            var createButton = new Button
            {
                Text = UiText.T("CREATE PROJECT", "СОЗДАТЬ ПРОЕКТ"),
                AutoSize = true,
            };

            createButton.Click += (_, _) => Complete(ProjectEntryChoice.CreateProject);
            importButton.Click += (_, _) => Complete(ProjectEntryChoice.ImportVpk);

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(importButton);
            buttons.Controls.Add(createButton);

            root.Controls.Add(prompt, 0, 0);
            root.Controls.Add(buttons, 0, 1);
            Controls.Add(root);

            AcceptButton = createButton;
            CancelButton = cancelButton;
        }

        private void Complete(ProjectEntryChoice choice)
        {
            Choice = choice;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private sealed class ImportedVpkProjectNameDialog : Form
    {
        private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };

        public ImportedVpkProjectNameDialog(string suggestedName, string theme)
        {
            _nameText.Text = suggestedName;

            Text = UiText.T("Import VPK project", "Импорт проекта из VPK");
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
            var importButton = new Button
            {
                Text = UiText.T("IMPORT", "ИМПОРТ"),
                AutoSize = true,
            };
            importButton.Click += (_, _) => CompleteImport();

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(importButton);
            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_nameText, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = importButton;
            CancelButton = cancelButton;
        }

        private void CompleteImport()
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
        }
    }
}
