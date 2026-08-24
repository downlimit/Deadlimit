using System.Drawing;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectLogsFeature
{
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

        var grid = projectGroup.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        if (grid is null)
        {
            return;
        }

        var folderText = grid.GetControlFromPosition(1, 0) as TextBox;
        var heroActions = grid.GetControlFromPosition(2, 2) as FlowLayoutPanel;
        var saveButton = grid.Controls
            .OfType<Button>()
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SAVE PROJECT", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОХРАНИТЬ ПРОЕКТ", StringComparison.Ordinal));
        if (folderText is null || heroActions is null || saveButton is null)
        {
            return;
        }

        var upperButtons = heroActions.Controls.OfType<Button>().ToArray();
        if (upperButtons.Length < 2)
        {
            return;
        }

        var lockButton = upperButtons[0];
        var refreshButton = upperButtons[1];
        var saveRow = grid.GetRow(saveButton);
        var saveColumn = grid.GetColumn(saveButton);

        var logsButton = new Button
        {
            AutoSize = false,
            Size = lockButton.Size,
            Anchor = AnchorStyles.Left,
            Margin = lockButton.Margin,
            Padding = Padding.Empty,
            TabStop = false,
            Text = string.Empty,
        };
        logsButton.Paint += (_, e) => DrawLogDocumentIcon(logsButton, e.Graphics);
        logsButton.Click += (_, _) => OpenLogsFolder(form, folderText.Text);

        saveButton.AutoSize = false;
        saveButton.Size = refreshButton.Size;
        saveButton.Margin = refreshButton.Margin;
        saveButton.Anchor = AnchorStyles.Left;

        var projectActions = new FlowLayoutPanel
        {
            AutoSize = false,
            Size = heroActions.Size,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Anchor = AnchorStyles.Left,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };

        grid.Controls.Remove(saveButton);
        projectActions.Controls.Add(logsButton);
        projectActions.Controls.Add(saveButton);
        grid.Controls.Add(projectActions, saveColumn, saveRow);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };
        toolTip.SetToolTip(
            logsButton,
            UiText.T(
                "Open this project's Deadlimit logs folder in Explorer.\n\nPREPARE FOR CSDK and BUILD FOR TEST write their diagnostic .log files here.",
                "Открыть папку логов этого проекта Deadlimit в Проводнике.\n\nПОДГОТОВИТЬ ДЛЯ CSDK и СОБРАТЬ ДЛЯ ТЕСТА сохраняют сюда диагностические .log-файлы."));
    }

    private static void OpenLogsFolder(Form form, string projectFolder)
    {
        var folder = projectFolder.Trim();
        if (!Directory.Exists(folder))
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "Select an existing project before opening its logs.",
                    "Сначала выберите существующий проект, чтобы открыть его логи."),
                UiText.T("Logs unavailable", "Логи недоступны"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        try
        {
            var metadataFolder = ProjectStore.GetMetadataFolder(folder);
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
            or System.ComponentModel.Win32Exception
            or InvalidOperationException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not open logs", "Не удалось открыть логи"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void DrawLogDocumentIcon(Button button, Graphics graphics)
    {
        const float width = 12F;
        const float height = 14F;
        const float fold = 4F;

        var x = (button.ClientSize.Width - width) / 2F;
        var y = (button.ClientSize.Height - height) / 2F;
        var color = button.Enabled ? button.ForeColor : SystemColors.GrayText;

        using var pen = new Pen(color, 1F);

        graphics.DrawLine(pen, x, y, x + width - fold, y);
        graphics.DrawLine(pen, x + width - fold, y, x + width, y + fold);
        graphics.DrawLine(pen, x + width, y + fold, x + width, y + height);
        graphics.DrawLine(pen, x + width, y + height, x, y + height);
        graphics.DrawLine(pen, x, y + height, x, y);
        graphics.DrawLine(pen, x + width - fold, y, x + width - fold, y + fold);
        graphics.DrawLine(pen, x + width - fold, y + fold, x + width, y + fold);

        var lineLeft = x + 2F;
        var lineRight = x + width - 2F;
        graphics.DrawLine(pen, lineLeft, y + 7F, lineRight, y + 7F);
        graphics.DrawLine(pen, lineLeft, y + 10F, lineRight, y + 10F);
        graphics.DrawLine(pen, lineLeft, y + 13F, lineRight - 2F, y + 13F);
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
