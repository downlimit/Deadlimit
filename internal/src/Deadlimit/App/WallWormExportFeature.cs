using Deadlimit.Core;

namespace Deadlimit.App;

internal static class WallWormExportFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => FindDescendants<Button>(panel).Any(button =>
                string.Equals(button.Text, "SETTINGS", StringComparison.Ordinal)
                || string.Equals(button.Text, "НАСТРОЙКИ", StringComparison.Ordinal)));
        if (topBar is null)
        {
            return;
        }

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
        if (folderText is null)
        {
            return;
        }

        var exportButton = new Button
        {
            Text = UiText.T("MAX EXPORT", "ЭКСПОРТ ИЗ MAX"),
            AutoSize = true,
            Enabled = false,
        };

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 12000,
        };
        toolTip.SetToolTip(
            exportButton,
            UiText.T(
                "Creates the project Wall Worm DMX22 exporter and copies a MAXScript fileIn command. The exporter preserves Max vertex color channel 0 through a temporary ChannelMod bridge.",
                "Создаёт проектный экспортёр Wall Worm DMX22 и копирует команду fileIn для MAXScript. Экспортёр сохраняет Vertex Color channel 0 через временный ChannelMod."));

        void RefreshEnabledState()
        {
            var folder = folderText.Text.Trim();
            exportButton.Enabled = Directory.Exists(folder) && ProjectStore.TryLoad(folder) is not null;
        }

        exportButton.Click += (_, _) =>
        {
            try
            {
                var projectFolder = folderText.Text.Trim();
                var manifest = ProjectStore.TryLoad(projectFolder)
                    ?? throw new InvalidOperationException(
                        UiText.T(
                            "Save the project before preparing the Max exporter.",
                            "Сохраните проект перед подготовкой экспортёра для Max."));

                var scriptPath = WallWormExportScriptService.WriteProjectScript(manifest);
                var command = WallWormExportScriptService.CreateFileInCommand(scriptPath);
                Clipboard.SetText(command);

                System.Windows.Forms.MessageBox.Show(
                    form,
                    UiText.T(
                        "MAXScript export command copied to the clipboard.\n\nIn 3ds Max, select the geometry node(s), open MAXScript Listener, paste the command and press Enter.\n\nDMX22 files are written directly to the project root. The artist nodes are not modified.",
                        "Команда экспорта MAXScript скопирована в буфер обмена.\n\nВ 3ds Max выделите нужные геометрические узлы, откройте MAXScript Listener, вставьте команду и нажмите Enter.\n\nDMX22-файлы будут записаны прямо в корень проекта. Исходные узлы сцены не изменяются."),
                    UiText.T("Deadlimit Max export", "Deadlimit: экспорт из Max"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
            {
                System.Windows.Forms.MessageBox.Show(
                    form,
                    ex.Message,
                    UiText.T("Max export unavailable", "Экспорт из Max недоступен"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        folderText.TextChanged += (_, _) => RefreshEnabledState();
        form.Activated += (_, _) => RefreshEnabledState();

        var settingsButtonIndex = topBar.Controls
            .Cast<Control>()
            .Select((control, index) => (control, index))
            .FirstOrDefault(item => item.control is Button button
                && (string.Equals(button.Text, "SETTINGS", StringComparison.Ordinal)
                    || string.Equals(button.Text, "НАСТРОЙКИ", StringComparison.Ordinal)))
            .index;

        topBar.Controls.Add(exportButton);
        if (settingsButtonIndex >= 0)
        {
            topBar.Controls.SetChildIndex(exportButton, settingsButtonIndex);
        }

        RefreshEnabledState();
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
