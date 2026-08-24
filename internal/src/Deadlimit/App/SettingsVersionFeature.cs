using System.Reflection;

namespace Deadlimit.App;

internal static class SettingsVersionFeature
{
    private const string VersionValueName = "DeadlimitVersionValue";

    public static void Attach()
    {
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var settingsForm in Application.OpenForms.OfType<SettingsForm>())
        {
            EnsureVersionRow(settingsForm);
        }
    }

    private static void EnsureVersionRow(SettingsForm form)
    {
        if (FindDescendants<Label>(form).Any(label =>
                string.Equals(label.Name, VersionValueName, StringComparison.Ordinal)))
        {
            return;
        }

        var grid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel =>
                panel.ColumnCount == 4
                && FindDescendants<TextBox>(panel).Count() >= 4);
        if (grid is null)
        {
            return;
        }

        var row = grid.RowCount;
        grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = UiText.T("Version", "Версия"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 12, 9),
        };
        var value = new Label
        {
            Name = VersionValueName,
            Text = GetDisplayVersion(),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 9, 8, 9),
        };

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(value, 1, row);
        grid.SetColumnSpan(value, 3);
    }

    private static string GetDisplayVersion()
    {
        var informationalVersion = typeof(SettingsVersionFeature).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return Application.ProductVersion;
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
