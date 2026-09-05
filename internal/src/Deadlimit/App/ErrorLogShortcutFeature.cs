using System.Runtime.CompilerServices;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ErrorLogShortcutFeature
{
    private static readonly ConditionalWeakTable<Form, object> PreparedDialogs = new();

    internal static void Prepare(Form dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        if (!IsSupportedErrorDialog(dialog) || PreparedDialogs.TryGetValue(dialog, out _))
        {
            return;
        }

        var projectFolder = ProjectStore.GetLastProjectFolder();
        if (string.IsNullOrWhiteSpace(projectFolder) || !Directory.Exists(projectFolder))
        {
            return;
        }

        var buttonRow = FindDescendants<FlowLayoutPanel>(dialog)
            .FirstOrDefault(panel => panel.Controls.OfType<Button>().Any(button =>
                string.Equals(button.Text, "OK", StringComparison.OrdinalIgnoreCase)));
        if (buttonRow is null
            || buttonRow.Controls.OfType<Button>().Any(button =>
                string.Equals(button.Text, "OPEN LOGS", StringComparison.OrdinalIgnoreCase)
                || string.Equals(button.Text, "ОТКРЫТЬ ЛОГИ", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var openLogsButton = new Button
        {
            Text = UiText.T("OPEN LOGS", "ОТКРЫТЬ ЛОГИ"),
            AutoSize = true,
            MinimumSize = new Size(92, 0),
            Margin = new Padding(6, 0, 0, 0),
        };
        openLogsButton.Click += (_, _) => OpenCurrentProjectLogs(dialog, projectFolder);
        buttonRow.Controls.Add(openLogsButton);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };
        toolTip.SetToolTip(
            openLogsButton,
            UiText.T(
                "Open the current project's **logs folder**.\n\nIf you report this error, the latest log can help explain what happened.",
                "Открыть **папку логов** текущего проекта.\n\nЕсли вы будете сообщать об этой ошибке, последний лог поможет понять, что произошло."));
        dialog.FormClosed += (_, _) => toolTip.Dispose();
        PreparedDialogs.Add(dialog, new object());
    }

    private static bool IsSupportedErrorDialog(Form dialog)
    {
        if (dialog is MainForm or SettingsForm || dialog.IsDisposed)
        {
            return false;
        }

        var title = dialog.Text?.Trim() ?? string.Empty;
        if (title.Length == 0
            || title.Contains("logs", StringComparison.OrdinalIgnoreCase)
            || title.Contains("логи", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return title.Contains("error", StringComparison.OrdinalIgnoreCase)
            || title.Contains("failed", StringComparison.OrdinalIgnoreCase)
            || title.Contains("could not", StringComparison.OrdinalIgnoreCase)
            || title.Contains("ошибка", StringComparison.OrdinalIgnoreCase)
            || title.Contains("не удалось", StringComparison.OrdinalIgnoreCase);
    }

    private static void OpenCurrentProjectLogs(Form owner, string projectFolder)
    {
        try
        {
            var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
            Directory.CreateDirectory(metadataFolder);
            if (OperatingSystem.IsWindows())
            {
                var attributes = File.GetAttributes(metadataFolder);
                File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
            }

            var logsFolder = Path.Combine(metadataFolder, "logs");
            Directory.CreateDirectory(logsFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{logsFolder}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                ex.Message,
                UiText.T("Could not open logs", "Не удалось открыть логи"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static IEnumerable<T> FindDescendants<T>(Control root)
        where T : Control
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
