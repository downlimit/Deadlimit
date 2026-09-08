using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ImportedProjectModeFeature
{
    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        var grid = projectGroup?.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var folderText = grid?.Controls
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.ReadOnly);
        if (folderText is null)
        {
            return;
        }

        var guardedButtons = FindDescendants<Button>(form)
            .Where(IsAuthoringOnlyButton)
            .Distinct()
            .ToArray();

        void ApplyModeGuard()
        {
            var folder = folderText.Text.Trim();
            var manifest = Directory.Exists(folder)
                ? ProjectStore.TryLoad(folder)
                : null;
            var imported = manifest?.Mode == ProjectMode.ImportedVpk;

            foreach (var button in guardedButtons)
            {
                button.Enabled = !imported;
            }
        }

        folderText.TextChanged += (_, _) => ApplyModeGuard();
        form.Activated += (_, _) => ApplyModeGuard();
        form.Shown += (_, _) => ApplyModeGuard();
        ApplyModeGuard();
    }

    private static bool IsAuthoringOnlyButton(Button button)
    {
        var text = button.Text.Trim();
        return string.Equals(text, "SAVE PROJECT", StringComparison.Ordinal)
            || string.Equals(text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal)
            || string.Equals(text, "EXTRACT SOURCE", StringComparison.Ordinal)
            || string.Equals(text, "ИЗВЛЕЧЬ ИСХОДНИКИ", StringComparison.Ordinal)
            || string.Equals(text, "EXTRACT HERO SOURCE", StringComparison.Ordinal)
            || string.Equals(text, "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ", StringComparison.Ordinal)
            || string.Equals(text, "PREPARE FOR CSDK", StringComparison.Ordinal)
            || string.Equals(text, "ПОДГОТОВИТЬ ДЛЯ CSDK", StringComparison.Ordinal)
            || string.Equals(text, "BUILD FOR TEST", StringComparison.Ordinal)
            || string.Equals(text, "СОБРАТЬ ДЛЯ ТЕСТА", StringComparison.Ordinal);
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
