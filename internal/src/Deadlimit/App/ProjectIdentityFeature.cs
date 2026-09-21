using System.Drawing;

namespace Deadlimit.App;

internal static class ProjectIdentityFeature
{
    private const int ProjectActionIconWidth = 34;
    private const int ProjectActionHeight = 24;
    private const int ProjectActionTextWidth = 148;
    private const int ProjectActionGap = 6;

    public static void Attach(MainForm form)
    {
        var projectNameText = FindDescendants<TextBox>(form)
            .FirstOrDefault(textBox => textBox.Name == UiControlNames.ProjectName);
        if (projectNameText?.Parent is not TableLayoutPanel grid)
        {
            return;
        }

        var projectNameRow = grid.GetRow(projectNameText);
        if (grid.GetControlFromPosition(0, projectNameRow) is not Label projectNameLabel)
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
        MoveSaveButtonToReleaseRow(grid);
        ReplaceReleaseIdWithNumericControl(grid);
    }

    private static void ConfigureFolderAndExtractionActions(MainForm form, TableLayoutPanel grid)
    {
        var openFolderButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == UiControlNames.OpenProjectFolderButton);
        var extractButton = FindDescendants<Button>(form)
            .FirstOrDefault(button => string.Equals(
                button.Name,
                UiControlNames.ExtractHeroSourceButton,
                StringComparison.Ordinal));
        if (openFolderButton is null || extractButton is null)
        {
            return;
        }

        var folderText = FindProjectFolderText(grid);
        if (folderText is null)
        {
            return;
        }

        var folderRow = grid.GetRow(folderText);

        grid.Controls.Remove(openFolderButton);
        extractButton.Parent?.Controls.Remove(extractButton);

        openFolderButton.Text = "📂";
        openFolderButton.AutoSize = false;
        openFolderButton.Width = ProjectActionIconWidth;
        openFolderButton.Height = ProjectActionHeight;
        openFolderButton.Font = new Font("Segoe UI Emoji", 11F, FontStyle.Regular, GraphicsUnit.Point);
        openFolderButton.TextAlign = ContentAlignment.MiddleCenter;
        openFolderButton.Margin = new Padding(0, 4, ProjectActionGap, 4);
        openFolderButton.Anchor = AnchorStyles.Left;
        openFolderButton.TabStop = false;

        extractButton.Text = UiText.T("EXTRACT SOURCE…", "ИЗВЛЕЧЬ ИСХОДНИКИ…");
        extractButton.AutoSize = true;
        var preferredWidth = extractButton.PreferredSize.Width;
        extractButton.AutoSize = false;
        extractButton.Width = Math.Max(ProjectActionTextWidth, preferredWidth);
        extractButton.Height = ProjectActionHeight;
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
        grid.Controls.Add(actions, 2, folderRow);

        var toolTip = CreateToolTip();
        toolTip.SetToolTip(
            openFolderButton,
            UiText.T(
                "Open the selected project's root folder in Explorer.\n\nDouble-clicking the project in the Library opens the same folder.",
                "Открыть корневую папку выбранного проекта в Проводнике.\n\nДвойной клик по проекту в Библиотеке открывает ту же папку."));
        toolTip.SetToolTip(
            extractButton,
            UiText.T(
                "Open extraction options for the selected hero, then extract the chosen current retail resources into 0source.\n\nThe dialog lets you choose textures, abilities / VFX, portraits and UI, and whether an existing 0source refresh keeps a backup.",
                "Открыть параметры извлечения выбранного героя, затем извлечь выбранные актуальные retail-ресурсы в 0source.\n\nВ диалоге можно выбрать текстуры, способности / VFX, портреты и UI, а также сохранять ли backup при обновлении существующего 0source."));
    }

    private static void MoveSaveButtonToReleaseRow(TableLayoutPanel grid)
    {
        var saveButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button => button.Name == UiControlNames.SaveProjectButton);
        var releaseText = grid.Controls
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.Name == UiControlNames.ReleaseIdBacking);
        if (saveButton is null || releaseText is null)
        {
            return;
        }

        var oldRow = grid.GetRow(saveButton);
        var releaseRow = grid.GetRow(releaseText);
        grid.Controls.Remove(saveButton);
        saveButton.Anchor = AnchorStyles.Left;
        saveButton.Margin = new Padding(0, 4, 0, 4);
        grid.Controls.Add(saveButton, 2, releaseRow);
        HideRow(grid, oldRow);

        var toolTip = CreateToolTip();
        toolTip.SetToolTip(
            saveButton,
            UiText.T(
                "Save this project's metadata, hero, Release ID and current DMX/PNG file list.\n\nAfter a successful save, hero selection is locked again to protect the project from accidental changes.",
                "Сохранить метаданные проекта, героя, Release ID и текущий список DMX/PNG-файлов.\n\nПосле успешного сохранения выбор героя снова блокируется, чтобы защитить проект от случайной смены."));
    }

    private static void ReplaceReleaseIdWithNumericControl(TableLayoutPanel grid)
    {
        var backingReleaseText = grid.Controls
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.Name == UiControlNames.ReleaseIdBacking);
        if (backingReleaseText is null)
        {
            return;
        }

        var row = grid.GetRow(backingReleaseText);
        if (grid.GetControlFromPosition(0, row) is not Label releaseLabel)
        {
            return;
        }

        var releaseId = new ReleaseIdNumericUpDown
        {
            Name = UiControlNames.ReleaseId,
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
            "Game-client VPK release slot: 01-99. Type the number directly or change it with the arrows by ±1.\n\nThe slot becomes part of the deployed VPK filename, for example Release ID 07 → pak07_dir.vpk.",
            "Слот VPK игрового клиента Deadlock: 01-99. Число можно ввести вручную или менять стрелками на ±1.\n\nСлот входит в имя установленного VPK-файла, например Release ID 07 → pak07_dir.vpk.");

        var toolTip = CreateToolTip();
        toolTip.SetToolTip(releaseLabel, tipText);
        toolTip.SetToolTip(releaseId, tipText);
    }

    private static ToolTip CreateToolTip() => new()
    {
        ShowAlways = true,
        InitialDelay = 300,
        ReshowDelay = 100,
        AutoPopDelay = 10000,
    };

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
            if (control is TextBox textBox && textBox.Name == UiControlNames.ProjectFolder)
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
