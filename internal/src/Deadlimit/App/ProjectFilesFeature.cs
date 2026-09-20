using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectFilesFeature
{
    public static void Attach(MainForm form)
    {
        var assetsGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Detected in 1authoring", StringComparison.Ordinal)
                || string.Equals(group.Text, "Найдено в 1authoring", StringComparison.Ordinal)
                || string.Equals(group.Text, "Project files", StringComparison.Ordinal)
                || string.Equals(group.Text, "Файлы проекта", StringComparison.Ordinal));
        if (assetsGroup is null)
        {
            return;
        }

        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        if (projectGroup is null)
        {
            return;
        }

        var folderText = FindDescendants<TextBox>(projectGroup)
            .FirstOrDefault(textBox => textBox.ReadOnly);
        if (folderText is null)
        {
            return;
        }

        assetsGroup.Text = UiText.T("Project files", "Файлы проекта");
        assetsGroup.Controls.Clear();
        assetsGroup.Padding = new Padding(3, 8, 3, 3);

        var authoringModelsLabel = CreateSummaryLabel();
        var authoringTexturesLabel = CreateSummaryLabel();
        var mainFileLabel = CreateSummaryLabel();
        var sourceFilesLabel = CreateSummaryLabel();

        var summaryRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(4, 1, 4, 1),
        };
        summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        summaryRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        summaryRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
        summaryRow.Controls.Add(authoringModelsLabel, 0, 0);
        summaryRow.Controls.Add(mainFileLabel, 1, 0);
        summaryRow.Controls.Add(authoringTexturesLabel, 0, 1);
        summaryRow.Controls.Add(sourceFilesLabel, 1, 1);
        var dmxList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        var textureList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // FlowLayoutPanel gives this section a wheel-scrollable overflow path if more
        // file-format columns are added later (animations, Source 2 authoring files, etc.).
        var fileColumns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0),
            Padding = Padding.Empty,
        };

        var dmxColumn = CreateFileColumn("DMX / FBX / glTF", dmxList, new Padding(0, 0, 5, 0));
        var textureColumn = CreateFileColumn("PNG / TGA / PSD", textureList, new Padding(5, 0, 0, 0));
        fileColumns.Controls.Add(dmxColumn);
        fileColumns.Controls.Add(textureColumn);

        void ClearFileSelection()
        {
            dmxList.ClearSelected();
            textureList.ClearSelected();
        }

        dmxList.MouseDown += (_, eventArgs) =>
        {
            if (dmxList.IndexFromPoint(eventArgs.Location) < 0)
            {
                ClearFileSelection();
            }
        };
        textureList.MouseDown += (_, eventArgs) =>
        {
            if (textureList.IndexFromPoint(eventArgs.Location) < 0)
            {
                ClearFileSelection();
            }
        };
        dmxList.MouseDoubleClick += (_, eventArgs) =>
            OpenClickedAuthoringFile(form, folderText.Text, dmxList, eventArgs.Location);
        textureList.MouseDoubleClick += (_, eventArgs) =>
            OpenClickedAuthoringFile(form, folderText.Text, textureList, eventArgs.Location);
        AttachBackgroundSelectionClear(form, ClearFileSelection);

        void ResizeColumns()
        {
            var width = Math.Max(120, (fileColumns.ClientSize.Width - 12) / 2);
            var height = Math.Max(70, fileColumns.ClientSize.Height - 4);
            dmxColumn.Size = new Size(width, height);
            textureColumn.Size = new Size(width, height);
        }
        fileColumns.SizeChanged += (_, _) => ResizeColumns();

        root.Controls.Add(summaryRow, 0, 0);
        root.Controls.Add(fileColumns, 0, 1);
        assetsGroup.Controls.Add(root);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };

        void Refresh()
        {
            dmxList.BeginUpdate();
            textureList.BeginUpdate();
            try
            {
                dmxList.Items.Clear();
                textureList.Items.Clear();

                var folder = folderText.Text.Trim();
                if (!Directory.Exists(folder))
                {
                    authoringModelsLabel.Text = UiText.T("AUTHORING MODELS: 0", "АВТОРСКИЕ МОДЕЛИ: 0");
                    authoringTexturesLabel.Text = UiText.T("AUTHORING TEXTURES: 0", "АВТОРСКИЕ ТЕКСТУРЫ: 0");
                    mainFileLabel.Text = UiText.T("MAIN FILE: —", "ГЛАВНЫЙ ФАЙЛ: —");
                    sourceFilesLabel.Text = UiText.T("SOURCE FILES: —", "ИСХОДНЫЕ ФАЙЛЫ: —");
                    toolTip.SetToolTip(mainFileLabel, string.Empty);
                    return;
                }

                var scan = ProjectScanner.Scan(folder);
                var modelCount = scan.DmxFiles.Count + scan.FbxFiles.Count + scan.GltfFiles.Count;
                authoringModelsLabel.Text = UiText.T(
                    $"AUTHORING MODELS: {modelCount}",
                    $"АВТОРСКИЕ МОДЕЛИ: {modelCount}");
                authoringTexturesLabel.Text = UiText.T(
                    $"AUTHORING TEXTURES: {scan.PngTextures.Count}",
                    $"АВТОРСКИЕ ТЕКСТУРЫ: {scan.PngTextures.Count}");

                foreach (var file in scan.DmxFiles)
                {
                    dmxList.Items.Add(ToAuthoringDisplayPath(file));
                }

                foreach (var file in scan.FbxFiles)
                {
                    dmxList.Items.Add(ToAuthoringDisplayPath(file));
                }

                foreach (var file in scan.GltfFiles)
                {
                    dmxList.Items.Add(ToAuthoringDisplayPath(file));
                }

                foreach (var file in scan.PngTextures)
                {
                    textureList.Items.Add(ToAuthoringDisplayPath(file));
                }

                var manifest = ProjectStore.TryLoad(folder);
                var extractedCount = manifest?.ExtractedSourceFileCount;

                var retailMainModel = manifest?.RetailMainModel;
                var mainModel = string.IsNullOrWhiteSpace(retailMainModel)
                    ? null
                    : retailMainModel.Trim();
                mainFileLabel.Text = UiText.T(
                    $"MAIN FILE: {mainModel ?? "—"}",
                    $"ГЛАВНЫЙ ФАЙЛ: {mainModel ?? "—"}");
                sourceFilesLabel.Text = UiText.T(
                    $"SOURCE FILES: {extractedCount?.ToString() ?? "—"}",
                    $"ИСХОДНЫЕ ФАЙЛЫ: {extractedCount?.ToString() ?? "—"}");
                toolTip.SetToolTip(mainFileLabel, mainModel ?? string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                authoringModelsLabel.Text = UiText.T("SCAN FAILED", "ОШИБКА СКАНИРОВАНИЯ");
                authoringTexturesLabel.Text = string.Empty;
                mainFileLabel.Text = ex.Message;
                sourceFilesLabel.Text = string.Empty;
                toolTip.SetToolTip(mainFileLabel, ex.Message);
            }
            finally
            {
                dmxList.EndUpdate();
                textureList.EndUpdate();
            }
        }

        folderText.TextChanged += (_, _) => Refresh();
        form.Activated += (_, _) => Refresh();

        var saveButton = FindDescendants<Button>(projectGroup)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SAVE PROJECT", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal));
        if (saveButton is not null)
        {
            saveButton.Click += (_, _) => form.BeginInvoke((Action)Refresh);
        }

        var extractButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "EXTRACT SOURCE", StringComparison.Ordinal)
                || string.Equals(button.Text, "ИЗВЛЕЧЬ ИСХОДНИКИ", StringComparison.Ordinal));
        if (extractButton is not null)
        {
            extractButton.EnabledChanged += (_, _) =>
            {
                if (extractButton.Enabled)
                {
                    Refresh();
                }
            };
        }

        ResizeColumns();
        Refresh();
    }

    private static Label CreateSummaryLabel() => new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    private static string ToAuthoringDisplayPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        var prefix = ProjectAuthoringLayout.AuthoringFolderName + "/";
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[prefix.Length..]
            : normalized;
    }

    private static void OpenClickedAuthoringFile(
        Form owner,
        string projectFolder,
        ListBox list,
        Point location)
    {
        var itemIndex = list.IndexFromPoint(location);
        if (itemIndex < 0 || itemIndex >= list.Items.Count)
        {
            return;
        }

        var displayPath = list.Items[itemIndex]?.ToString();
        if (string.IsNullOrWhiteSpace(displayPath))
        {
            return;
        }

        try
        {
            var filePath = ResolveAuthoringFilePath(projectFolder, displayPath);
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(
                    UiText.T("The selected project file no longer exists.", "Выбранный файл проекта больше не существует."),
                    filePath);
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{filePath}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                ex.Message,
                UiText.T("Could not show project file", "Не удалось показать файл проекта"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static string ResolveAuthoringFilePath(string projectFolder, string displayPath)
    {
        var authoringRoot = Path.Combine(
            Path.GetFullPath(projectFolder.Trim()),
            ProjectAuthoringLayout.AuthoringFolderName);
        return SafePath.ResolveUnderRoot(
            authoringRoot,
            displayPath.Replace('/', Path.DirectorySeparatorChar),
            "Project file selection");
    }

    private static void AttachBackgroundSelectionClear(Control root, Action clearSelection)
    {
        if (root is Form or Panel or GroupBox or Label)
        {
            root.MouseDown += (_, _) => clearSelection();
        }

        foreach (Control child in root.Controls)
        {
            if (child is ListBox)
            {
                continue;
            }

            AttachBackgroundSelectionClear(child, clearSelection);
        }
    }

    private static Control CreateFileColumn(string title, ListBox list, Padding margin)
    {
        var panel = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Margin = margin,
            Padding = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var label = new Label
        {
            Text = title,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 2),
        };

        panel.Controls.Add(label, 0, 0);
        panel.Controls.Add(list, 0, 1);
        return panel;
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
}
