using System.Runtime.CompilerServices;

namespace Deadlimit.App;

internal static class SettingsFeedbackFeature
{
    private const string FeedbackUrl = "https://github.com/downlimit/Deadlimit/issues/new/choose";
    private static readonly ConditionalWeakTable<SettingsForm, object> PreparedForms = new();

    internal static void Prepare(SettingsForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (PreparedForms.TryGetValue(form, out _))
        {
            return;
        }

        var grid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel =>
                panel.ColumnCount == 2
                && FindDescendants<Label>(panel).Any(label =>
                    string.Equals(label.Text, "Deadlimit Scripts", StringComparison.Ordinal)));
        if (grid is null)
        {
            return;
        }

        var scriptsLabel = FindDescendants<Label>(grid)
            .FirstOrDefault(label => string.Equals(label.Text, "Deadlimit Scripts", StringComparison.Ordinal));
        if (scriptsLabel is null)
        {
            return;
        }

        var row = grid.GetRow(scriptsLabel) + 1;
        grid.RowCount = Math.Max(grid.RowCount, row + 1);
        while (grid.RowStyles.Count <= row)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var caption = new Label
        {
            Text = UiText.T("Feedback", "Обратная связь"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 8),
        };
        var openButton = new Button
        {
            Text = UiText.T("Open", "Перейти"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 4),
        };
        openButton.Click += (_, _) => OpenFeedback(form);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 12000,
        };
        toolTip.SetToolTip(
            openButton,
            UiText.T(
                "Open the **Deadlimit feedback** page on GitHub. Choose Bug report to describe a problem or Feature request to suggest an improvement.\n\nIf an unusual error keeps happening and you cannot solve it, attach the latest **log file**. A log is a text report of what Deadlimit was doing.\n\nTo open the logs folder, return to the main project window and click the document icon next to **SAVE PROJECT**.",
                "Открыть страницу **обратной связи Deadlimit** на GitHub. Выберите Bug report, чтобы описать ошибку, или Feature request, чтобы предложить улучшение.\n\nЕсли необычная ошибка повторяется и справиться с ней не получается, приложите последний **файл лога**. Лог — это текстовый отчёт о том, что делал Deadlimit.\n\nЧтобы открыть папку с логами, вернитесь в главное окно проекта и нажмите кнопку с иконкой документа рядом с **СОХРАНИТЬ ПРОЕКТ**."));

        grid.Controls.Add(caption, 0, row);
        grid.Controls.Add(openButton, 1, row);
        form.FormClosed += (_, _) => toolTip.Dispose();
        PreparedForms.Add(form, new object());
    }

    private static void OpenFeedback(Form owner)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = FeedbackUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                ex.Message,
                UiText.T("Could not open feedback", "Не удалось открыть обратную связь"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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
