using System.ComponentModel;
using System.Reflection;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectCreationChoiceFeature
{
    public static void Attach(MainForm form)
    {
        var libraryGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Projects", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проекты", StringComparison.Ordinal)
                || string.Equals(group.Text, "Library", StringComparison.Ordinal)
                || string.Equals(group.Text, "Библиотека", StringComparison.Ordinal));
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
        var settings = ProjectStore.GetToolPathSettings();
        using var dialog = new ProjectEntryChoiceDialog(settings.UiTheme);
        if (dialog.ShowDialog(form) != DialogResult.OK)
        {
            return;
        }

        switch (dialog.Choice)
        {
            case ProjectEntryChoice.CreateProject:
                foreach (var handler in createProjectHandlers)
                {
                    handler(sender, EventArgs.Empty);
                }
                break;

            case ProjectEntryChoice.ImportVpk:
                SelectVpkImportSource(form);
                break;
        }
    }

    private static void SelectVpkImportSource(MainForm form)
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

        VpkImportCandidate candidate;
        try
        {
            candidate = VpkImportSourceValidator.Validate(dialog.FileName);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            MessageBox.Show(
                form,
                exception.Message,
                UiText.T("Could not import VPK", "Не удалось импортировать VPK"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var releaseId = candidate.ReleaseTarget
            ?? UiText.T("not derived from filename", "не определён по имени файла");
        MessageBox.Show(
            form,
            UiText.T(
                $"VPK source is valid.\n\nRelease ID: {releaseId}\nFiles: {candidate.EntryCount}\nSHA-256: {candidate.SourceVpkSha256}\n\nProject identity and folder creation are the next import stage.",
                $"VPK успешно проверен.\n\nRelease ID: {releaseId}\nФайлов: {candidate.EntryCount}\nSHA-256: {candidate.SourceVpkSha256}\n\nОпределение проекта и создание его папки выполняются на следующем этапе импорта."),
            UiText.T("VPK ready to import", "VPK готов к импорту"),
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
            ShowInTaskbar = false;
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
}
