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
            EnsureFooterEnhancements(settingsForm);
        }
    }

    private static void EnsureFooterEnhancements(SettingsForm form)
    {
        if (FindDescendants<Label>(form).Any(label =>
                string.Equals(label.Name, VersionValueName, StringComparison.Ordinal)))
        {
            return;
        }

        var footer = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel =>
                panel.Controls.OfType<Button>().Any(IsSaveButton)
                && panel.Controls.OfType<Button>().Any(IsCancelButton));
        if (footer is null)
        {
            return;
        }

        var saveButton = footer.Controls.OfType<Button>().FirstOrDefault(IsSaveButton);
        if (saveButton is null)
        {
            return;
        }

        var versionLabel = new Label
        {
            Name = VersionValueName,
            Text = $"{UiText.T("Version", "Версия")} {GetDisplayVersion()}",
            AutoSize = true,
            Margin = new Padding(12, 7, 12, 0),
        };
        footer.Controls.Add(versionLabel);

        var textBoxes = FindDescendants<TextBox>(form).ToArray();
        var comboBoxes = FindDescendants<ComboBox>(form).ToArray();
        var initialText = textBoxes.ToDictionary(
            textBox => textBox,
            textBox => textBox.Text.Trim());
        var initialSelection = comboBoxes.ToDictionary(
            comboBox => comboBox,
            comboBox => comboBox.SelectedIndex);

        void UpdateSaveEnabled()
        {
            var textChanged = initialText.Any(pair =>
                !string.Equals(
                    pair.Key.Text.Trim(),
                    pair.Value,
                    StringComparison.OrdinalIgnoreCase));
            var selectionChanged = initialSelection.Any(pair =>
                pair.Key.SelectedIndex != pair.Value);

            saveButton.Enabled = textChanged || selectionChanged;
        }

        foreach (var textBox in textBoxes)
        {
            textBox.TextChanged += (_, _) => UpdateSaveEnabled();
        }

        foreach (var comboBox in comboBoxes)
        {
            comboBox.SelectedIndexChanged += (_, _) => UpdateSaveEnabled();
        }

        UpdateSaveEnabled();
    }

    private static bool IsSaveButton(Button button) =>
        string.Equals(button.Text, "SAVE", StringComparison.Ordinal)
        || string.Equals(button.Text, "СОХРАНИТЬ", StringComparison.Ordinal);

    private static bool IsCancelButton(Button button) =>
        string.Equals(button.Text, "CANCEL", StringComparison.Ordinal)
        || string.Equals(button.Text, "ОТМЕНА", StringComparison.Ordinal);

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
