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

        var row = grid.GetRow(projectNameLabel);
        var projectNameText = grid.GetControlFromPosition(1, row) as TextBox;
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

        if (row >= 0 && row < grid.RowStyles.Count)
        {
            grid.RowStyles[row].SizeType = SizeType.Absolute;
            grid.RowStyles[row].Height = 0;
        }
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
