using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectSaveStateFeature
{
    private static readonly Dictionary<MainForm, Action> Updaters = [];

    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group => group.Name == UiControlNames.ProjectGroup);
        if (projectGroup is null)
        {
            return;
        }

        var folderText = FindDescendants<TextBox>(projectGroup)
            .FirstOrDefault(textBox => textBox.Name == UiControlNames.ProjectFolder);
        var heroCombo = FindDescendants<ComboBox>(projectGroup).FirstOrDefault(combo => combo.Name == UiControlNames.Hero);
        var releaseId = FindDescendants<NumericUpDown>(projectGroup).FirstOrDefault(input => input.Name == UiControlNames.ReleaseId);
        var saveButton = FindDescendants<Button>(projectGroup)
            .FirstOrDefault(button => button.Name == UiControlNames.SaveProjectButton);
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

            if (manifest is null)
            {
                // The initial save establishes the project's hero and release slot.
                // Authoring files are discovered automatically by the project scanner
                // and must not turn SAVE PROJECT into a filesystem dirty indicator.
                return hero.Length > 0
                    || release.Length > 0;
            }

            return !string.Equals(hero, manifest.Hero?.Trim(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    release,
                    NormalizeReleaseId(manifest.ReleaseTarget),
                    StringComparison.OrdinalIgnoreCase);
        }

        void UpdateSaveState()
        {
            saveButton.Enabled = IsDirty();
        }

        void WarnIfReleaseIdIsShared()
        {
            if (IsDirty())
            {
                return;
            }

            var folder = folderText.Text.Trim();
            var manifest = Directory.Exists(folder) ? ProjectStore.TryLoad(folder) : null;
            var release = NormalizeReleaseId(manifest?.ReleaseTarget);
            if (manifest is null || release.Length == 0)
            {
                return;
            }

            var conflicts = FindReleaseIdConflicts(folder, release);
            if (conflicts.Count == 0)
            {
                return;
            }

            var projects = string.Join(", ", conflicts.Select(name => $"\"{name}\""));
            var vpkName = $"pak{release}_dir.vpk";
            MessageBox.Show(
                form,
                UiText.T(
                    $"Release ID {release} is already used by {projects}.\n\nBoth projects target the same game-client VPK: {vpkName}. The project was saved, but choose another ID if this overlap is not intentional.",
                    $"Release ID {release} уже занят проектом {projects}.\n\nОба проекта используют один и тот же VPK игрового клиента Deadlock: {vpkName}. Проект сохранён, но выберите другой ID, если это пересечение не было намеренным."),
                UiText.T("Release ID already in use", "Release ID уже занят"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
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

        saveButton.Click += (_, _) => form.BeginInvoke((Action)(() =>
        {
            UpdateSaveState();
            WarnIfReleaseIdIsShared();
        }));
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

    private static List<string> FindReleaseIdConflicts(string currentFolder, string release)
    {
        var projectsRoot = ProjectStore.GetToolPathSettings().ProjectsRoot;
        if (!Directory.Exists(projectsRoot))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(projectsRoot)
                .Where(folder => !PathsEqual(folder, currentFolder))
                .Select(folder => (Folder: folder, Manifest: ProjectStore.TryLoad(folder)))
                .Where(item => item.Manifest is not null
                    && string.Equals(
                        NormalizeReleaseId(item.Manifest.ReleaseTarget),
                        release,
                        StringComparison.OrdinalIgnoreCase))
                .Select(item => string.IsNullOrWhiteSpace(item.Manifest!.ProjectName)
                    ? Path.GetFileName(item.Folder)
                    : item.Manifest.ProjectName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [];
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
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
