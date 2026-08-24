namespace Deadlimit.App;

internal static class WindowProgressFeature
{
    private const string AppTitle = "Deadlimit";
    private const string ProgressTitlePrefix = AppTitle + " — ";

    public static void Attach(MainForm form)
    {
        var statusLabel = FindDescendants<StatusStrip>(form)
            .SelectMany(strip => strip.Items.OfType<ToolStripStatusLabel>())
            .FirstOrDefault(item => !item.Spring);

        form.Text = AppTitle;
        form.TextChanged += (_, _) =>
        {
            var title = form.Text;
            if (string.Equals(title, AppTitle, StringComparison.Ordinal))
            {
                return;
            }

            if (statusLabel is not null && TryExtractProgressMessage(title, out var message))
            {
                statusLabel.Text = message;
            }

            // Runtime progress belongs in the bottom status area. Keep the native window
            // caption stable so the taskbar/title bar always identifies only the app.
            form.Text = AppTitle;
        };
    }

    private static bool TryExtractProgressMessage(string title, out string message)
    {
        message = string.Empty;
        if (!title.StartsWith(ProgressTitlePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var progressText = title[ProgressTitlePrefix.Length..].Trim();

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

        message = progressText;
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
