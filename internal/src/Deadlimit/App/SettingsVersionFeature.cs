using System.Diagnostics;
using System.Reflection;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsVersionFeature
{
    private const string VersionValueName = "DeadlimitAggregatorVersionValue";
    private const string ApplyButtonName = "DeadlimitSettingsApplyButton";
    private const string UpdateButtonName = "DeadlimitUpdateButton";

    public static void Attach()
    {
        // Normal preparation happens before first activation in UiRenderingStabilityFeature.
        // Keep Idle only as a defensive fallback if the native pre-show hook is unavailable.
        Application.Idle += OnApplicationIdle;
    }

    internal static void Prepare(SettingsForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        EnsureFooterEnhancements(form);
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var settingsForm in Application.OpenForms.OfType<SettingsForm>())
        {
            Prepare(settingsForm);
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

        var oldSaveButton = footer.Controls.OfType<Button>().FirstOrDefault(IsSaveButton);
        var oldCancelButton = footer.Controls.OfType<Button>().FirstOrDefault(IsCancelButton);
        if (oldSaveButton is null || oldCancelButton is null)
        {
            return;
        }

        var toolGrid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel => panel.ColumnCount == 7 && panel.RowCount >= 4);
        if (toolGrid is null)
        {
            return;
        }

        var csdkPath = toolGrid.GetControlFromPosition(4, 0) as TextBox;
        var deadlockToolsPath = toolGrid.GetControlFromPosition(4, 1) as TextBox;
        var deadlockClientPath = toolGrid.GetControlFromPosition(4, 2) as TextBox;
        var projectsPath = toolGrid.GetControlFromPosition(4, 3) as TextBox;
        if (csdkPath is null || deadlockToolsPath is null || deadlockClientPath is null || projectsPath is null)
        {
            return;
        }

        var comboBoxes = FindDescendants<ComboBox>(form).ToArray();
        var languageCombo = comboBoxes.FirstOrDefault(combo =>
            combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "English", StringComparison.Ordinal))
            && combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "Русский", StringComparison.Ordinal)));
        var themeCombo = comboBoxes.FirstOrDefault(combo => !ReferenceEquals(combo, languageCombo));
        if (languageCombo is null || themeCombo is null)
        {
            return;
        }

        var stored = ProjectStore.GetToolPathSettings();
        var initialLanguage = stored.UiLanguage;
        var initialTheme = stored.UiTheme;

        var applyButton = new Button
        {
            Name = ApplyButtonName,
            Text = UiText.T("APPLY", "ПРИМЕНИТЬ"),
            AutoSize = true,
            Enabled = false,
        };

        footer.Controls.Remove(oldCancelButton);
        footer.Controls.Remove(oldSaveButton);
        oldCancelButton.Dispose();
        oldSaveButton.Dispose();
        var updateButton = new Button
        {
            Name = UpdateButtonName,
            Text = UiText.T("UPDATE DEADLIMIT", "ОБНОВИТЬ DEADLIMIT"),
            AutoSize = true,
        };
        updateButton.Click += (_, _) => LaunchUpdater(form);
        footer.Controls.Add(updateButton);
        footer.Controls.Add(applyButton);
        form.AcceptButton = applyButton;
        form.CancelButton = null;

        var versionLabel = new Label
        {
            Name = VersionValueName,
            Text = $"{UiText.T("Version", "Версия")} {GetDisplayVersion()}",
            AutoSize = false,
            Height = applyButton.GetPreferredSize(Size.Empty).Height,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 0, 12, 0),
        };
        footer.Controls.Add(versionLabel);

        if (toolGrid.ColumnStyles.Count >= 5)
        {
            toolGrid.ColumnStyles[3].SizeType = SizeType.Absolute;
            toolGrid.ColumnStyles[3].Width = 118;
            toolGrid.ColumnStyles[4].SizeType = SizeType.Absolute;
            toolGrid.ColumnStyles[4].Width = 262;
        }

        var fineTuneButton = toolGrid.GetControlFromPosition(3, 0) as Button;
        if (fineTuneButton is not null)
        {
            fineTuneButton.Text = UiText.T("FINE-TUNE…", "ДОНАСТРОЙКА…");
            fineTuneButton.AutoSize = false;
            fineTuneButton.Width = 112;
        }

        // The base SettingsForm has already been themed in its constructor. Theme only
        // the controls inserted by this feature instead of repainting the whole dialog a
        // second time during native activation.
        UiTheme.ApplyCustomPalette(footer, stored.UiTheme);

        void UpdateVersionLayout()
        {
            var occupiedWidth = footer.Controls
                .Cast<Control>()
                .Where(control => !ReferenceEquals(control, versionLabel))
                .Sum(control => control.Width + control.Margin.Horizontal);
            var preferredWidth = versionLabel.GetPreferredSize(Size.Empty).Width;
            var availableWidth = Math.Max(
                preferredWidth,
                footer.DisplayRectangle.Width
                    - footer.Padding.Horizontal
                    - occupiedWidth
                    - versionLabel.Margin.Horizontal);

            if (versionLabel.Width != availableWidth)
            {
                versionLabel.Width = availableWidth;
            }
        }

        string SelectedLanguage() =>
            string.Equals(languageCombo.SelectedItem?.ToString(), "Русский", StringComparison.Ordinal)
                ? "ru"
                : "en";

        string SelectedTheme()
        {
            var label = themeCombo.SelectedItem?.ToString() ?? string.Empty;
            return label switch
            {
                "Light" or "Светлая" => "light",
                "Gray" or "Серая" => "gray",
                "Dark" or "Тёмная" => "dark",
                _ => "system",
            };
        }

        void UpdateApplyEnabled()
        {
            applyButton.Enabled = !string.Equals(initialLanguage, SelectedLanguage(), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(initialTheme, SelectedTheme(), StringComparison.OrdinalIgnoreCase);
        }

        void PersistPaths()
        {
            try
            {
                var current = ProjectStore.GetToolPathSettings();
                current.CsdkRoot = csdkPath.Text.Trim();
                current.DeadlockToolsRoot = deadlockToolsPath.Text.Trim();
                current.RetailDeadlockRoot = deadlockClientPath.Text.Trim();
                current.ProjectsRoot = projectsPath.Text.Trim();
                ProjectStore.SaveToolPathSettings(current);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(
                    form,
                    exception.Message,
                    UiText.T("Could not save tool path", "Не удалось сохранить путь инструмента"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        void ApplyInterfaceSettings()
        {
            try
            {
                PersistPaths();
                var current = ProjectStore.GetToolPathSettings();
                current.UiLanguage = SelectedLanguage();
                current.UiTheme = SelectedTheme();
                ProjectStore.SaveToolPathSettings(current);

                var interfaceChanged = typeof(SettingsForm).GetProperty(
                    nameof(SettingsForm.InterfaceChanged),
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                interfaceChanged?.SetValue(form, true);

                form.DialogResult = DialogResult.OK;
                UiSettingsChangeBus.NotifyChanged();
                form.Close();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(
                    form,
                    exception.Message,
                    UiText.T("Could not apply interface settings", "Не удалось применить настройки интерфейса"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        footer.Layout += (_, _) => UpdateVersionLayout();
        applyButton.Click += (_, _) => ApplyInterfaceSettings();
        languageCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
        themeCombo.SelectedIndexChanged += (_, _) => UpdateApplyEnabled();
        csdkPath.TextChanged += (_, _) => PersistPaths();
        deadlockToolsPath.TextChanged += (_, _) => PersistPaths();
        deadlockClientPath.TextChanged += (_, _) => PersistPaths();
        projectsPath.TextChanged += (_, _) => PersistPaths();

        UpdateVersionLayout();
        UpdateApplyEnabled();
    }

    private static bool IsSaveButton(Button button) =>
        string.Equals(button.Text, "SAVE", StringComparison.Ordinal)
        || string.Equals(button.Text, "СОХРАНИТЬ", StringComparison.Ordinal);

    private static bool IsCancelButton(Button button) =>
        string.Equals(button.Text, "CANCEL", StringComparison.Ordinal)
        || string.Equals(button.Text, "ОТМЕНА", StringComparison.Ordinal);

    private static void LaunchUpdater(IWin32Window owner)
    {
        var updater = ReleaseChannelPolicy.IsPortableRelease
            ? Path.Combine(AppContext.BaseDirectory, "Update Deadlimit.cmd")
            : Path.Combine(DeadlimitPaths.DefaultDeadlimitRoot, "DeadlimitUpdater.bat");
        if (!File.Exists(updater))
        {
            MessageBox.Show(
                owner,
                UiText.T(
                    "The updater entry point was not found for the current release channel.",
                    "Файл обновления не найден для текущего канала программы."),
                UiText.T("Updater unavailable", "Обновление недоступно"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                WorkingDirectory = Path.GetDirectoryName(updater) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                exception.Message,
                UiText.T("Could not start updater", "Не удалось запустить обновление"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
