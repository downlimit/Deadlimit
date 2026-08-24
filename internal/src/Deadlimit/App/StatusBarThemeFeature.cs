namespace Deadlimit.App;

internal static class StatusBarThemeFeature
{
    public static void Attach(MainForm form, string theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        var dark = normalized == "dark"
            || (normalized == "system" && Application.IsDarkModeEnabled)
            || normalized is not ("light" or "gray" or "dark" or "system") && Application.IsDarkModeEnabled;

        var barColor = dark
            ? Color.FromArgb(33, 33, 33)
            : normalized == "gray"
                ? Color.FromArgb(65, 65, 65)
                : Color.FromArgb(248, 248, 248);
        var separatorColor = dark
            ? Color.FromArgb(63, 63, 63)
            : normalized == "gray"
                ? Color.FromArgb(96, 96, 96)
                : Color.FromArgb(190, 190, 190);
        var textColor = dark
            ? Color.FromArgb(160, 160, 160)
            : normalized == "gray"
                ? Color.FromArgb(184, 184, 184)
                : Color.FromArgb(86, 86, 86);
        var strongTextColor = dark
            ? Color.FromArgb(210, 210, 210)
            : normalized == "gray"
                ? Color.FromArgb(226, 226, 226)
                : Color.FromArgb(34, 34, 34);

        var statusBar = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel =>
                panel.ColumnCount == 5
                && panel.RowCount == 1
                && panel.Parent is TableLayoutPanel
                && panel.Height <= 50);
        if (statusBar is null)
        {
            return;
        }

        ApplyBackground(statusBar, barColor);

        foreach (Control child in statusBar.Controls)
        {
            if (child.Width == 1)
            {
                child.BackColor = separatorColor;
            }
        }

        var labels = FindDescendants<Label>(statusBar).ToList();
        foreach (var label in labels)
        {
            label.ForeColor = textColor;
        }
        if (labels.Count > 0)
        {
            labels[0].ForeColor = strongTextColor;
        }
    }

    private static void ApplyBackground(Control control, Color color)
    {
        if (control is Panel or TableLayoutPanel)
        {
            control.BackColor = color;
        }

        foreach (Control child in control.Controls)
        {
            ApplyBackground(child, color);
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
