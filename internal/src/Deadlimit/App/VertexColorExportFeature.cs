using Deadlimit.Core;

namespace Deadlimit.App;

internal static class VertexColorExportFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => FindDescendants<Button>(panel).Any(IsSettingsButton));

        if (topBar is null)
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
                "Copies the repository MaxScript fileIn command. The helper exports selected Vertex Color to an FBX beside the latest Wall Worm DMX. PREPARE transfers it and removes the FBX after verification.",
                "Копирует команду fileIn для MaxScript из репозитория. Скрипт экспортирует Vertex Color выделенных мешей в FBX рядом с последним DMX Wall Worm. PREPARE переносит цвет и удаляет FBX после проверки."));

        void RefreshEnabledState()
        {
            try
            {
                button.Enabled = File.Exists(VertexColorMaxScriptService.GetBundledScriptPath());
            }
            catch (DirectoryNotFoundException)
            {
                button.Enabled = false;
            }
        }

        button.Click += (_, _) =>
        {
            try
            {
                var scriptPath = VertexColorMaxScriptService.GetBundledScriptPath();
                Clipboard.SetText(VertexColorMaxScriptService.CreateFileInCommand(scriptPath));

                System.Windows.Forms.MessageBox.Show(
                    form,
                    UiText.T(
                        "The MAXScript fileIn command is in the clipboard.\n\nPaste it into MAXScript Listener once. For each update: export the normal DMX with Wall Worm, keep the same geometry selected, then export the Vertex Color FBX. Run PREPARE in Deadlimit to transfer it and remove the FBX. The script reads no Deadlimit project settings and contains no path to Deadlimit.exe.",
                        "Команда fileIn для MAXScript скопирована в буфер обмена.\n\nОдин раз вставьте её в MAXScript Listener. При каждом обновлении: экспортируйте обычный DMX через Wall Worm, оставьте ту же геометрию выделенной и экспортируйте Vertex Color FBX. Затем запустите PREPARE в Deadlimit — он перенесёт цвет и удалит FBX. Скрипт ничего не читает из настроек проекта Deadlimit и не содержит пути к Deadlimit.exe."),
                    UiText.T("Deadlimit Vertex Color", "Deadlimit Vertex Color"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException)
            {
                System.Windows.Forms.MessageBox.Show(
                    form,
                    ex.Message,
                    UiText.T("Vertex Color helper unavailable", "Скрипт Vertex Color недоступен"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        };

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
