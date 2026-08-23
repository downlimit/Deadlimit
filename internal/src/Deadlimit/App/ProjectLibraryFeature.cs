using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectLibraryFeature
{
    public static void Attach(MainForm form)
    {
        ProjectIdentityFeature.Attach(form);

        var libraryGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Projects", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проекты", StringComparison.Ordinal));
        if (libraryGroup is null)
        {
            return;
        }

        var library = libraryGroup.Controls.OfType<ListBox>().FirstOrDefault();
        if (library is null)
        {
            return;
        }

        WidenLibraryColumn(libraryGroup);
        ConfigureLibraryFormatting(library);
        ConfigureLibraryDoubleClick(form, library);
        AddCreateProjectButton(form, libraryGroup, library);
    }

    private static void WidenLibraryColumn(GroupBox libraryGroup)
    {
        if (libraryGroup.Parent is not TableLayoutPanel root || root.ColumnStyles.Count == 0)
        {
            return;
        }

        if (root.ColumnStyles[0].SizeType == SizeType.Absolute)
        {
            root.ColumnStyles[0].Width = 260;
        }
    }

    private static void ConfigureLibraryFormatting(ListBox library)
    {
        library.FormattingEnabled = true;
        library.Format += (_, e) =>
        {
            var folderName = e.ListItem?.ToString();
            if (string.IsNullOrWhiteSpace(folderName))
            {
                return;
            }

            var settings = ProjectStore.GetToolPathSettings();
            if (string.IsNullOrWhiteSpace(settings.ProjectsRoot))
            {
                return;
            }

            var folder = Path.Combine(settings.ProjectsRoot, folderName);
            var manifest = ProjectStore.TryLoad(folder);
            var hasMetadataFile = File.Exists(ProjectStore.GetManifestPath(folder));

            var id = string.IsNullOrWhiteSpace(manifest?.ReleaseTarget)
                ? "—"
                : manifest.ReleaseTarget.Trim();

            var marker = manifest is not null ? "◆" : hasMetadataFile ? "!" : "◇";
            var errorSuffix = hasMetadataFile && manifest is null
                ? $"   · {UiText.T("JSON ERROR", "ОШИБКА JSON")}"
                : string.Empty;

            e.Value = $"{marker}  ID {id}   {folderName}{errorSuffix}";
        };
    }

    private static void ConfigureLibraryDoubleClick(MainForm form, ListBox library)
    {
        library.MouseDoubleClick += (_, e) =>
        {
            var index = library.IndexFromPoint(e.Location);
            if (index < 0)
            {
                return;
            }

            var folderName = library.Items[index]?.ToString();
            var projectsRoot = ProjectStore.GetToolPathSettings().ProjectsRoot;
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(projectsRoot))
            {
                return;
            }

            var folder = Path.Combine(projectsRoot, folderName);
            if (!Directory.Exists(folder))
            {
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
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    form,
                    ex.Message,
                    UiText.T("Could not open project folder", "Не удалось открыть папку проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };
    }

    private static void AddCreateProjectButton(MainForm form, GroupBox libraryGroup, ListBox library)
    {
        libraryGroup.Padding = new Padding(10, 30, 10, 10);

        var addButton = new Button
        {
            Text = "+",
            Width = 26,
            Height = 23,
            TabStop = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        void PositionButton()
        {
            addButton.Location = new Point(
                Math.Max(0, libraryGroup.ClientSize.Width - addButton.Width - 9),
                1);
        }

        PositionButton();
        libraryGroup.Resize += (_, _) => PositionButton();

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
        };
        toolTip.SetToolTip(addButton, UiText.T("Create project folder", "Создать папку проекта"));

        addButton.Click += (_, _) => CreateProjectFolder(form, library);
        libraryGroup.Controls.Add(addButton);
        addButton.BringToFront();
    }

    private static void CreateProjectFolder(MainForm form, ListBox library)
    {
        var settings = ProjectStore.GetToolPathSettings();
        if (!Directory.Exists(settings.ProjectsRoot))
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "Projects folder is unavailable. Set it in Settings first.",
                    "Папка проектов недоступна. Сначала укажите её в настройках."),
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new NewProjectFolderDialog(settings.ProjectsRoot, settings.UiTheme);
        if (dialog.ShowDialog(form) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.CreatedFolder))
        {
            return;
        }

        var createdFolder = dialog.CreatedFolder;
        form.BeginInvoke((Action)(() =>
        {
            var createdName = Path.GetFileName(createdFolder);
            for (var index = 0; index < library.Items.Count; index++)
            {
                if (!string.Equals(library.Items[index]?.ToString(), createdName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                library.SelectedIndex = index;
                library.Focus();
                break;
            }
        }));
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

    private sealed class NewProjectFolderDialog : Form
    {
        private readonly string _projectsRoot;
        private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };

        public NewProjectFolderDialog(string projectsRoot, string theme)
        {
            _projectsRoot = projectsRoot;

            Text = UiText.T("New project", "Новый проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(430, 132);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
            Shown += (_, _) => _nameText.Focus();
        }

        public string? CreatedFolder { get; private set; }

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
                Text = UiText.T("Project name", "Название проекта"),
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
            var createButton = new Button
            {
                Text = UiText.T("CREATE", "СОЗДАТЬ"),
                AutoSize = true,
            };
            createButton.Click += (_, _) => TryCreate();

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(createButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_nameText, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = createButton;
            CancelButton = cancelButton;
        }

        private void TryCreate()
        {
            var name = _nameText.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowError(UiText.T("Enter a project name.", "Введите название проекта."));
                return;
            }

            if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                ShowError(UiText.T(
                    "The project name contains characters that cannot be used in a folder name.",
                    "Название проекта содержит символы, которые нельзя использовать в имени папки."));
                return;
            }

            var folder = Path.Combine(_projectsRoot, name);
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                ShowError(UiText.T(
                    "A folder or file with this name already exists in the projects folder.",
                    "В папке проектов уже существует папка или файл с таким именем."));
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                CreatedFolder = Path.GetFullPath(folder);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ShowError(ex.Message);
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                this,
                message,
                UiText.T("Could not create project", "Не удалось создать проект"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }
}
