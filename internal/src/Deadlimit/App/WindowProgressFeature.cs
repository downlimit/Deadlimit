namespace Deadlimit.App;

internal static class WindowProgressFeature
{
    private const string AppTitle = UiText.ProductName;
    private const string ProgressTitlePrefix = AppTitle + " — ";
    private const string LegacyProgressTitlePrefix = "Deadlimit Manager — ";
    private static readonly Dictionary<MainForm, ToolStripStatusLabel> StatusLabels = [];

    public static void Attach(MainForm form)
    {
        var statusLabel = FindDescendants<StatusStrip>(form)
            .SelectMany(strip => strip.Items.OfType<ToolStripStatusLabel>())
            .FirstOrDefault(item => !item.Spring);
        if (statusLabel is not null)
        {
            StatusLabels[form] = statusLabel;
            form.FormClosed += (_, _) => StatusLabels.Remove(form);
        }

        form.Text = AppTitle;
        form.TextChanged += (_, _) =>
        {
            var title = form.Text;
            if (string.Equals(title, AppTitle, StringComparison.Ordinal))
            {
                return;
            }

            if (TryExtractProgressMessage(title, out var message))
            {
                ReportStatus(form, message);
            }

            // Runtime progress belongs in the bottom status area. Keep the native window
            // caption stable so the taskbar/title bar always identifies only the app.
            form.Text = AppTitle;
        };
    }

    public static void ReportStatus(MainForm form, string message)
    {
        if (form.IsDisposed)
        {
            return;
        }

        var statusLabel = StatusLabels.GetValueOrDefault(form)
            ?? FindDescendants<StatusStrip>(form)
                .SelectMany(strip => strip.Items.OfType<ToolStripStatusLabel>())
                .FirstOrDefault(item => !item.Spring);
        if (statusLabel is null)
        {
            return;
        }

        statusLabel.Text = UiText.NormalizeProductNames(message);
    }

    internal static int RunDetachedStatusSmoke()
    {
        using var form = new MainForm();
        var statusStrip = FindDescendants<StatusStrip>(form).FirstOrDefault();
        var statusLabel = statusStrip?.Items
            .OfType<ToolStripStatusLabel>()
            .FirstOrDefault(item => !item.Spring);
        if (statusStrip?.Parent is null || statusLabel is null)
        {
            return 1;
        }

        Attach(form);
        statusStrip.Parent.Controls.Remove(statusStrip);

        const string marker = "Detailed build progress";
        ReportStatus(form, marker);
        return string.Equals(statusLabel.Text, marker, StringComparison.Ordinal)
            ? 0
            : 2;
    }

    private static bool TryExtractProgressMessage(string title, out string message)
    {
        message = string.Empty;
        var prefixLength = title.StartsWith(ProgressTitlePrefix, StringComparison.Ordinal)
            ? ProgressTitlePrefix.Length
            : title.StartsWith(LegacyProgressTitlePrefix, StringComparison.Ordinal)
                ? LegacyProgressTitlePrefix.Length
                : 0;
        if (prefixLength == 0)
        {
            return false;
        }

        var progressText = title[prefixLength..].Trim();

        // BUILD FOR TEST historically wrote "[42% spinner] - message" into the title.
        // The percent already has its own label beside the bottom progress bar, so only
        // route the human-readable operation message into the status line.
        if (progressText.StartsWith("[", StringComparison.Ordinal))
        {
            var separator = progressText.IndexOf("] - ", StringComparison.Ordinal);
            if (separator >= 0)
            {
                progressText = progressText[(separator + 4)..].Trim();
            }
        }

        if (progressText.Length == 0)
        {
            return false;
        }

        message = UiText.NormalizeProductNames(progressText);
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
