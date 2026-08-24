namespace Deadlimit.App;

internal static class ExtractionProgressFeature
{
    private const string DecompilingPrefix = "Decompiling ";

    public static void Attach(MainForm form)
    {
        var statusStrip = FindDescendants<StatusStrip>(form).FirstOrDefault();
        if (statusStrip is null)
        {
            return;
        }

        var statusLabel = statusStrip.Items
            .OfType<ToolStripStatusLabel>()
            .FirstOrDefault(item => !item.Spring);
        var progressBar = statusStrip.Items
            .OfType<ToolStripProgressBar>()
            .FirstOrDefault();
        if (statusLabel is null || progressBar is null)
        {
            return;
        }

        var extractionActive = false;

        void SetProgress(int value)
        {
            progressBar.Value = Math.Clamp(value, progressBar.Minimum, progressBar.Maximum);
            progressBar.Visible = true;
        }

        void FinishProgress()
        {
            extractionActive = false;
            progressBar.Visible = false;
            progressBar.Value = progressBar.Minimum;
        }

        void UpdateFromStatus()
        {
            var text = statusLabel.Text?.Trim() ?? string.Empty;

            if (text.StartsWith("Locating current retail hero model", StringComparison.Ordinal))
            {
                extractionActive = true;
                SetProgress(1);
                return;
            }

            if (!extractionActive)
            {
                return;
            }

            if (text.StartsWith("Scanning ", StringComparison.Ordinal))
            {
                SetProgress(4);
                return;
            }

            if (text.StartsWith(DecompilingPrefix, StringComparison.Ordinal))
            {
                if (TryParseDecompileFraction(text, out var current, out var total))
                {
                    var fraction = total == 0 ? 0d : (double)current / total;
                    SetProgress(8 + (int)Math.Round(88d * Math.Clamp(fraction, 0d, 1d)));
                }
                else
                {
                    SetProgress(8);
                }

                return;
            }

            if (text.StartsWith("Publishing refreshed 0source", StringComparison.Ordinal))
            {
                SetProgress(98);
                return;
            }

            if (text.StartsWith("Hero source extraction complete", StringComparison.Ordinal))
            {
                SetProgress(100);
                return;
            }

            if (text.StartsWith("Hero source ready:", StringComparison.Ordinal)
                || text.StartsWith("Исходники героя готовы:", StringComparison.Ordinal)
                || text.StartsWith("Hero source extraction failed", StringComparison.Ordinal)
                || text.StartsWith("Не удалось извлечь исходники героя", StringComparison.Ordinal)
                || text.StartsWith("Hero source extraction cancelled", StringComparison.Ordinal)
                || text.StartsWith("Извлечение исходников героя отменено", StringComparison.Ordinal))
            {
                FinishProgress();
            }
        }

        statusLabel.TextChanged += (_, _) => UpdateFromStatus();
        form.FormClosed += (_, _) => FinishProgress();
    }

    private static bool TryParseDecompileFraction(string text, out int current, out int total)
    {
        current = 0;
        total = 0;

        var remainder = text[DecompilingPrefix.Length..];
        var colon = remainder.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var fraction = remainder[..colon];
        var slash = fraction.IndexOf('/');
        if (slash <= 0 || slash >= fraction.Length - 1)
        {
            return false;
        }

        return int.TryParse(fraction[..slash], out current)
            && int.TryParse(fraction[(slash + 1)..], out total)
            && current >= 0
            && total > 0;
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
