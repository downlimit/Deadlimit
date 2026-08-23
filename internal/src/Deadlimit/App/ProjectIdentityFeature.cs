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
        MoveSaveButtonUnderHeroRefresh(grid);
        AddReleaseIdTip(grid);
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

    private static void AddReleaseIdTip(TableLayoutPanel grid)
    {
        var releaseLabel = grid.Controls
            .OfType<Label>()
            .FirstOrDefault(label => string.Equals(label.Text, "Release ID", StringComparison.Ordinal));
        if (releaseLabel is null)
        {
            return;
        }

        var row = grid.GetRow(releaseLabel);
        var releaseText = grid.GetControlFromPosition(1, row);
        var tipText = UiText.T(
            "Release slot 01-99. It becomes the retail VPK file name: pak##_dir.vpk.",
            "Слот релиза 01-99. Он входит в имя retail VPK-файла: pak##_dir.vpk.");

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 300,
            AutoPopDelay = 8000,
        };
        toolTip.SetToolTip(releaseLabel, tipText);
        if (releaseText is not null)
        {
            toolTip.SetToolTip(releaseText, tipText);
        }
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
}
