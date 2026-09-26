using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ImportedProjectModeFeature
{
    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group => group.Name == UiControlNames.ProjectGroup);
        var grid = projectGroup?.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var folderText = grid?.Controls
            .OfType<TextBox>()
            .FirstOrDefault(textBox => textBox.Name == UiControlNames.ProjectFolder);
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
        return button.Name is UiControlNames.SaveProjectButton
            or UiControlNames.ExtractHeroSourceButton
            or UiControlNames.PrepareButton;
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
