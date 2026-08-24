using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectSaveStateFeature
{
    private static readonly Dictionary<MainForm, Action> Updaters = [];

    public static void Attach(MainForm form)
    {
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
        var heroCombo = FindDescendants<ComboBox>(projectGroup).FirstOrDefault();
        var releaseId = FindDescendants<NumericUpDown>(projectGroup).FirstOrDefault();
        var saveButton = FindDescendants<Button>(projectGroup)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SAVE PROJECT", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal));
        if (folderText is null || saveButton is null)
        {
            return;
        }

        bool IsDirty()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder))
            {
                return false;
            }

            var hero = heroCombo?.SelectedItem is HeroCatalogEntry entry
                ? entry.LookupName.Trim()
                : heroCombo?.Text.Trim() ?? string.Empty;
            var release = NormalizeReleaseId(releaseId?.Text);
            var manifest = ProjectStore.TryLoad(folder);

            ProjectScanResult scan;
            try
            {
                scan = ProjectScanner.Scan(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return false;
            }

            if (manifest is null)
            {
                return hero.Length > 0
                    || release.Length > 0
                    || scan.DmxFiles.Count > 0
                    || scan.PngTextures.Count > 0;
            }

            if (!string.Equals(hero, manifest.Hero?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(release, NormalizeReleaseId(manifest.ReleaseTarget), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !SequenceEqualIgnoreCase(scan.DmxFiles, manifest.DmxFiles)
                || !SequenceEqualIgnoreCase(scan.PngTextures, manifest.PngTextures);
        }

        void UpdateSaveState()
        {
            saveButton.Enabled = IsDirty();
        }

        Updaters[form] = UpdateSaveState;

        folderText.TextChanged += (_, _) => UpdateSaveState();
        if (heroCombo is not null)
        {
            heroCombo.SelectedIndexChanged += (_, _) => UpdateSaveState();
            heroCombo.TextChanged += (_, _) => UpdateSaveState();
        }
        if (releaseId is not null)
        {
            releaseId.ValueChanged += (_, _) => UpdateSaveState();
            releaseId.TextChanged += (_, _) => UpdateSaveState();
            releaseId.Validated += (_, _) => UpdateSaveState();
        }

        saveButton.Click += (_, _) => form.BeginInvoke((Action)UpdateSaveState);
        form.Activated += (_, _) => UpdateSaveState();
        form.Shown += (_, _) => UpdateSaveState();
        form.FormClosed += (_, _) => Updaters.Remove(form);

        UpdateSaveState();
    }

    public static void Refresh(MainForm form)
    {
        if (Updaters.TryGetValue(form, out var update))
        {
            update();
        }
    }

    private static string NormalizeReleaseId(string? value)
    {
        if (!int.TryParse(value?.Trim(), out var parsed) || parsed is < 1 or > 99)
        {
            return string.Empty;
        }

        return parsed.ToString("00");
    }

    private static bool SequenceEqualIgnoreCase(
        IReadOnlyList<string> left,
        IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
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
