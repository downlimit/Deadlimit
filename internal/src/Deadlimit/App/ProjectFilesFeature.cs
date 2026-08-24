using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectFilesFeature
{
    public static void Attach(MainForm form)
    {
        var assetsGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Detected in project root", StringComparison.Ordinal)
                || string.Equals(group.Text, "Найдено в корне проекта", StringComparison.Ordinal)
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

        var summaryLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5),
        };
        var sourceCountLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 3),
        };
        var mainModelLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Height = 20,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 0, 7),
        };
        var dmxList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };
        var pngList = new ListBox
        {
            Dock = DockStyle.Fill,
            IntegralHeight = false,
            HorizontalScrollbar = true,
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // FlowLayoutPanel gives this section a wheel-scrollable overflow path if more
        // file-format columns are added later (animations, Source 2 authoring files, etc.).
        var fileColumns = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        var dmxColumn = CreateFileColumn("DMX", dmxList, new Padding(0, 0, 5, 0));
        var pngColumn = CreateFileColumn("PNG", pngList, new Padding(5, 0, 0, 0));
        fileColumns.Controls.Add(dmxColumn);
        fileColumns.Controls.Add(pngColumn);

        void ResizeColumns()
        {
            var width = Math.Max(120, (fileColumns.ClientSize.Width - 12) / 2);
            var height = Math.Max(70, fileColumns.ClientSize.Height - 4);
            dmxColumn.Size = new Size(width, height);
            pngColumn.Size = new Size(width, height);
        }
        fileColumns.SizeChanged += (_, _) => ResizeColumns();

        root.Controls.Add(summaryLabel, 0, 0);
        root.Controls.Add(sourceCountLabel, 0, 1);
        root.Controls.Add(mainModelLabel, 0, 2);
        root.Controls.Add(fileColumns, 0, 3);
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
            pngList.BeginUpdate();
            try
            {
                dmxList.Items.Clear();
                pngList.Items.Clear();

                var folder = folderText.Text.Trim();
                if (!Directory.Exists(folder))
                {
                    summaryLabel.Text = "DMX: 0     PNG: 0";
                    sourceCountLabel.Text = UiText.T("Hero source: not extracted", "Исходники героя: не извлечены");
                    mainModelLabel.Text = UiText.T("Main file: —", "Основной файл: —");
                    toolTip.SetToolTip(mainModelLabel, string.Empty);
                    return;
                }

                var scan = ProjectScanner.Scan(folder);
                summaryLabel.Text = $"DMX: {scan.DmxFiles.Count}     PNG: {scan.PngTextures.Count}";

                foreach (var file in scan.DmxFiles)
                {
                    dmxList.Items.Add(file);
                }

                foreach (var file in scan.PngTextures)
                {
                    pngList.Items.Add(file);
                }

                var manifest = ProjectStore.TryLoad(folder);
                var extractedCount = manifest?.ExtractedSourceFileCount;
                sourceCountLabel.Text = extractedCount is not null
                    ? UiText.T($"Hero source: {extractedCount} files", $"Исходники героя: {extractedCount} файлов")
                    : UiText.T("Hero source: not extracted", "Исходники героя: не извлечены");

                var retailMainModel = manifest?.RetailMainModel;
                var mainModel = string.IsNullOrWhiteSpace(retailMainModel)
                    ? null
                    : retailMainModel.Trim();
                mainModelLabel.Text = UiText.T(
                    $"Main file: {mainModel ?? "—"}",
                    $"Основной файл: {mainModel ?? "—"}");
                toolTip.SetToolTip(mainModelLabel, mainModel ?? string.Empty);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                summaryLabel.Text = UiText.T("Could not scan project files.", "Не удалось просканировать файлы проекта.");
                sourceCountLabel.Text = ex.Message;
                mainModelLabel.Text = string.Empty;
            }
            finally
            {
                dmxList.EndUpdate();
                pngList.EndUpdate();
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
            Margin = new Padding(0, 0, 0, 4),
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
