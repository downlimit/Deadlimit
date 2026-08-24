using System.Drawing;

namespace Deadlimit.App;

internal static class ProjectIdentityFeature
{
    public static void Attach(MainForm form)
    {
        var projectNameLabel = FindDescendants<Label>(form)
            .FirstOrDefault(label =>
                string.Equals(label.Text, "Project name", StringComparison.Ordinal)
                || string.Equals(label.Text, "Имя проекта", StringComparison.Ordinal));
        if (projectNameLabel?.Parent is not TableLayoutPanel grid)
        {
            return;
        }

        var projectNameRow = grid.GetRow(projectNameLabel);
        var projectNameText = grid.GetControlFromPosition(1, projectNameRow) as TextBox;
        if (projectNameText is null)
        {
            return;
        }

        var folderText = FindProjectFolderText(grid);
        if (folderText is null)
        {
            return;
        }

        void SyncProjectName()
        {
            projectNameText.Text = GetFolderName(folderText.Text);
        }

        folderText.TextChanged += (_, _) => SyncProjectName();
        SyncProjectName();

        grid.Controls.Remove(projectNameLabel);
        grid.Controls.Remove(projectNameText);
        projectNameLabel.Dispose();

        HideRow(grid, projectNameRow);
        ConfigureFolderAndExtractionActions(form, grid);
        MoveSaveButtonUnderHeroRefresh(grid);
        ReplaceReleaseIdWithNumericControl(grid);
    }

    private static void ConfigureFolderAndExtractionActions(MainForm form, TableLayoutPanel grid)
    {
        var openFolderButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Text, "OPEN FOLDER", StringComparison.Ordinal)
                || string.Equals(button.Text, "ОТКРЫТЬ ПАПКУ", StringComparison.Ordinal));
        var extractButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "EXTRACT HERO SOURCE", StringComparison.Ordinal)
                || string.Equals(button.Text, "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ", StringComparison.Ordinal));
        if (openFolderButton is null || extractButton is null)
        {
            return;
        }

        grid.Controls.Remove(openFolderButton);
        extractButton.Parent?.Controls.Remove(extractButton);

        openFolderButton.Text = "📂";
        openFolderButton.AutoSize = false;
        openFolderButton.Width = 34;
        openFolderButton.Height = 24;
        openFolderButton.Font = new Font("Segoe UI Emoji", 11F, FontStyle.Regular, GraphicsUnit.Point);
        openFolderButton.TextAlign = ContentAlignment.MiddleCenter;
        openFolderButton.Margin = new Padding(0, 4, 6, 4);
        openFolderButton.Anchor = AnchorStyles.Left;
        openFolderButton.TabStop = false;

        extractButton.Text = UiText.T("EXTRACT SOURCE", "ИЗВЛЕЧЬ ИСХОДНИКИ");
        extractButton.AutoSize = true;
        extractButton.Margin = new Padding(0, 4, 0, 4);
        extractButton.Anchor = AnchorStyles.Left;

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Anchor = AnchorStyles.Left,
        };
        actions.Controls.Add(openFolderButton);
        actions.Controls.Add(extractButton);
        grid.Controls.Add(actions, 2, 0);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 300,
            AutoPopDelay = 6000,
        };
        toolTip.SetToolTip(
            openFolderButton,
            UiText.T("Open project folder", "Открыть папку проекта"));
    }

    private static void MoveSaveButtonUnderHeroRefresh(TableLayoutPanel grid)
    {
        var saveButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SAVE PROJECT", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal));
        if (saveButton is null)
        {
            return;
        }

        var oldRow = grid.GetRow(saveButton);
        grid.Controls.Remove(saveButton);
        saveButton.Anchor = AnchorStyles.Left;
        saveButton.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(saveButton, 2, 3);
        HideRow(grid, oldRow);
    }

    private static void ReplaceReleaseIdWithNumericControl(TableLayoutPanel grid)
    {
        var releaseLabel = grid.Controls
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, "Release ID", StringComparison.Ordinal));
        if (releaseLabel is null)
        {
            return;
        }

        var row = grid.GetRow(releaseLabel);
        if (grid.GetControlFromPosition(1, row) is not TextBox backingReleaseText)
        {
            return;
        }

        var releaseId = new ReleaseIdNumericUpDown
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 4, 8, 4),
        };

        var syncing = false;

        void SyncFromBacking()
        {
            syncing = true;
            try
            {
                releaseId.SetReleaseText(backingReleaseText.Text);
            }
            finally
            {
                syncing = false;
            }
        }

        void SyncToBacking()
        {
            if (!syncing)
            {
                backingReleaseText.Text = releaseId.ReleaseText;
            }
        }

        backingReleaseText.TextChanged += (_, _) => SyncFromBacking();
        releaseId.ValueChanged += (_, _) => SyncToBacking();
        releaseId.Validated += (_, _) =>
        {
            releaseId.CommitTypedValue();
            SyncToBacking();
        };
        releaseId.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            releaseId.CommitTypedValue();
            SyncToBacking();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        grid.Controls.Remove(backingReleaseText);
        grid.Controls.Add(releaseId, 1, row);
        SyncFromBacking();

        var tipText = UiText.T(
            "Release slot 01-99. Type a number or use the arrows to change it by one. It becomes the retail VPK file name: pak##_dir.vpk.",
            "Слот релиза 01-99. Введите число вручную или меняйте его стрелками на единицу. Он входит в имя retail VPK-файла: pak##_dir.vpk.");

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 300,
            AutoPopDelay = 8000,
        };
        toolTip.SetToolTip(releaseLabel, tipText);
        toolTip.SetToolTip(releaseId, tipText);
    }

    private static void HideRow(TableLayoutPanel grid, int row)
    {
        if (row < 0 || row >= grid.RowStyles.Count)
        {
            return;
        }

        grid.RowStyles[row].SizeType = SizeType.Absolute;
        grid.RowStyles[row].Height = 0;
    }

    private static TextBox? FindProjectFolderText(TableLayoutPanel grid)
    {
        foreach (Control control in grid.Controls)
        {
            if (control is TextBox textBox && textBox.ReadOnly)
            {
                return textBox;
            }
        }

        return null;
    }

    private static string GetFolderName(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return string.Empty;
        }

        try
        {
            var normalized = Path.GetFullPath(folder.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(normalized);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
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

    private sealed class ReleaseIdNumericUpDown : NumericUpDown
    {
        public ReleaseIdNumericUpDown()
        {
            Minimum = 0;
            Maximum = 99;
            Increment = 1;
            ReadOnly = false;
            ThousandsSeparator = false;
            TextAlign = HorizontalAlignment.Left;
            Value = 0;
            UpdateEditText();
        }

        public string ReleaseText => Value > 0
            ? ((int)Value).ToString("00")
            : string.Empty;

        public void SetReleaseText(string? value)
        {
            if (int.TryParse(value?.Trim(), out var parsed) && parsed is >= 1 and <= 99)
            {
                Value = parsed;
                UpdateEditText();
                return;
            }

            Value = 0;
            UpdateEditText();
        }

        public void CommitTypedValue()
        {
            base.ValidateEditText();
            UpdateEditText();
        }

        protected override void UpdateEditText()
        {
            Text = Value > 0
                ? ((int)Value).ToString("00")
                : string.Empty;
        }
    }
}
