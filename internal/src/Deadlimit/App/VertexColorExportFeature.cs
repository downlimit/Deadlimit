using Deadlimit.Core;

namespace Deadlimit.App;

internal static class VertexColorExportFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => FindDescendants<Button>(panel).Any(IsSettingsButton));
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        var folderText = projectGroup is null
            ? null
            : FindDescendants<TextBox>(projectGroup).FirstOrDefault(textBox => textBox.ReadOnly);

        if (topBar is null || folderText is null)
        {
            return;
        }

        var button = new Button
        {
            Text = UiText.T("MAX VERTEX COLOR", "VERTEX COLOR ИЗ MAX"),
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
            button,
            UiText.T(
                "Copies the universal one-button Max helper and its fileIn command. The helper exports selected geometry beside the latest Wall Worm DMX as _vertexcolor.fbx.",
                "Копирует универсальный однокнопочный Max-скрипт и команду fileIn. Скрипт экспортирует выделенную геометрию рядом с последним DMX Wall Worm в _vertexcolor.fbx."));

        void RefreshEnabledState()
        {
            var folder = folderText.Text.Trim();
            button.Enabled = Directory.Exists(folder) && ProjectStore.TryLoad(folder) is not null;
        }

        button.Click += (_, _) =>
        {
            try
            {
                var projectFolder = folderText.Text.Trim();
                var manifest = ProjectStore.TryLoad(projectFolder)
                    ?? throw new InvalidOperationException(
                        UiText.T(
                            "Save the project before preparing the Max helper.",
                            "Сохраните проект перед подготовкой Max-скрипта."));

                var scriptPath = VertexColorMaxScriptService.WriteProjectScript(manifest);
                Clipboard.SetText(VertexColorMaxScriptService.CreateFileInCommand(scriptPath));

                System.Windows.Forms.MessageBox.Show(
                    form,
                    UiText.T(
                        "The MAXScript fileIn command is in the clipboard.\n\nPaste it into MAXScript Listener once. A small Vertex Color FBX window will open.\n\nFor each update: export the normal DMX with Wall Worm, keep the same geometry selected, then press EXPORT SELECTED VERTEX COLOR. The helper uses Wall Worm's latest export folder; it reads no Deadlimit project settings.",
                        "Команда fileIn для MAXScript скопирована в буфер обмена.\n\nОдин раз вставьте её в MAXScript Listener. Откроется маленькое окно Vertex Color FBX.\n\nПри каждом обновлении: экспортируйте обычный DMX через Wall Worm, оставьте ту же геометрию выделенной и нажмите EXPORT SELECTED VERTEX COLOR. Скрипт использует папку последнего экспорта Wall Worm и ничего не читает из настроек проекта Deadlimit."),
                    UiText.T("Deadlimit Vertex Color", "Deadlimit Vertex Color"),
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
                    UiText.T("Vertex Color helper unavailable", "Скрипт Vertex Color недоступен"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

        folderText.TextChanged += (_, _) => RefreshEnabledState();
        form.Activated += (_, _) => RefreshEnabledState();

        var settingsButton = topBar.Controls.Cast<Control>().OfType<Button>().FirstOrDefault(IsSettingsButton);
        topBar.Controls.Add(button);
        if (settingsButton is not null)
        {
            topBar.Controls.SetChildIndex(button, topBar.Controls.GetChildIndex(settingsButton));
        }

        RefreshEnabledState();
    }

    private static bool IsSettingsButton(Button button) =>
        string.Equals(button.Text, "SETTINGS", StringComparison.Ordinal)
        || string.Equals(button.Text, "НАСТРОЙКИ", StringComparison.Ordinal);

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
