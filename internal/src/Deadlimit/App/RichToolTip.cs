namespace Deadlimit.App;

internal sealed class RichToolTip : IDisposable
{
    private const int HorizontalPadding = 10;
    private const int VerticalPadding = 8;
    private const int ParagraphGapPixels = 6;
    private const int MaxContentWidth = 520;

    private static readonly Color BackgroundColor = Color.White;
    private static readonly Color BorderColor = Color.FromArgb(118, 118, 118);
    private static readonly Color TextColor = Color.Black;
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, RichToolTip> KeepAlive = new();

    private static readonly string[] AutoBoldKeywords =
    [
        "SHIFT+LMB",
        "SHIFT+click",
        "Shift-click",
        "PREPARE FOR CSDK",
        "BUILD FOR TEST",
        "LAUNCH CSDK",
        "ONLINE PREPARATION",
        "FINE-TUNE…",
        "ДОНАСТРОЙКА…",
        "INSTALL…",
        "УСТАНОВИТЬ…",
        "UPDATE…",
        "ОБНОВИТЬ…",
        "BROWSE…",
        "ОБЗОР…",
        "APPLY",
        "ПРИМЕНИТЬ",
        "Release ID",
        "DeadlockTools",
        "Reduced CSDK",
        "Deadlock client",
        "Deadlock клиент",
        "Vertex Color FBX",
        "Fixed Gamma",
        "0source",
        "SHIFT",
    ];

    private readonly System.Windows.Forms.ToolTip _toolTip;
    private readonly Dictionary<Control, string> _texts = [];

    public RichToolTip(int autoPopDelay = 16000)
    {
        _toolTip = new System.Windows.Forms.ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = autoPopDelay,
            OwnerDraw = true,
        };
        _toolTip.Popup += MeasurePopup;
        _toolTip.Draw += DrawPopup;
    }

    public bool ShowAlways
    {
        get => _toolTip.ShowAlways;
        set => _toolTip.ShowAlways = value;
    }

    public int InitialDelay
    {
        get => _toolTip.InitialDelay;
        set => _toolTip.InitialDelay = value;
    }

    public int ReshowDelay
    {
        get => _toolTip.ReshowDelay;
        set => _toolTip.ReshowDelay = value;
    }

    public int AutoPopDelay
    {
        get => _toolTip.AutoPopDelay;
        set => _toolTip.AutoPopDelay = value;
    }

    public void SetToolTip(Control control, string text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            KeepAlive.Remove(control);
            _texts.Remove(control);
            _toolTip.SetToolTip(control, string.Empty);
            return;
        }

        _texts[control] = normalized;
        KeepAlive.Remove(control);
        KeepAlive.Add(control, this);
        _toolTip.SetToolTip(control, normalized);
    }

    public void Dispose()
    {
        foreach (var control in _texts.Keys.ToArray())
        {
            KeepAlive.Remove(control);
        }
        _toolTip.Popup -= MeasurePopup;
        _toolTip.Draw -= DrawPopup;
        _toolTip.Dispose();
        _texts.Clear();
    }

    private void MeasurePopup(object? sender, PopupEventArgs e)
    {
        if (e.AssociatedControl is null || !_texts.TryGetValue(e.AssociatedControl, out var text))
        {
            return;
        }

        using var graphics = e.AssociatedControl.CreateGraphics();
        using var boldFont = new Font(e.AssociatedControl.Font, FontStyle.Bold);
        var layout = BuildLayout(graphics, e.AssociatedControl.Font, boldFont, text);
        e.ToolTipSize = new Size(
            layout.Width + (HorizontalPadding * 2),
            layout.Height + (VerticalPadding * 2));
    }

    private void DrawPopup(object? sender, DrawToolTipEventArgs e)
    {
        using (var background = new SolidBrush(BackgroundColor))
        {
            e.Graphics.FillRectangle(background, e.Bounds);
        }
        using (var border = new Pen(BorderColor))
        {
            var borderBounds = new Rectangle(
                e.Bounds.Left,
                e.Bounds.Top,
                Math.Max(0, e.Bounds.Width - 1),
                Math.Max(0, e.Bounds.Height - 1));
            e.Graphics.DrawRectangle(border, borderBounds);
        }

        var text = (e.AssociatedControl is not null && _texts.TryGetValue(e.AssociatedControl, out var stored)
            ? stored
            : e.ToolTipText) ?? string.Empty;
        Font regularFont = e.Font ?? e.AssociatedControl?.Font ?? Control.DefaultFont;
        using var boldFont = new Font(regularFont, FontStyle.Bold);
        var layout = BuildLayout(e.Graphics, regularFont, boldFont, text);
        var originX = e.Bounds.Left + HorizontalPadding;
        var cursorY = e.Bounds.Top + VerticalPadding;

        foreach (var row in layout.Rows)
        {
            if (row.IsParagraphGap)
            {
                cursorY += ParagraphGapPixels;
                continue;
            }

            var cursorX = originX;
            foreach (var run in row.Runs)
            {
                var font = run.Bold ? boldFont : regularFont;
                TextRenderer.DrawText(
                    e.Graphics,
                    run.Text,
                    font,
                    new Point(cursorX, cursorY),
                    TextColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                cursorX += run.Width;
            }
            cursorY += row.Height;
        }
    }

    private static RichLayout BuildLayout(Graphics graphics, Font regularFont, Font boldFont, string text)
    {
        var rows = new List<LayoutRow>();
        var maxWidth = 0;
        var totalHeight = 0;

        foreach (var sourceLine in text.Split('\n'))
        {
            if (sourceLine.Length == 0)
            {
                rows.Add(LayoutRow.ParagraphGap());
                totalHeight += ParagraphGapPixels;
                continue;
            }

            var currentRuns = new List<LayoutRun>();
            var currentWidth = 0;
            var currentHeight = Math.Max(regularFont.Height, boldFont.Height);

            void FlushLine()
            {
                if (currentRuns.Count == 0)
                {
                    return;
                }

                rows.Add(new LayoutRow(currentRuns.ToArray(), currentWidth, currentHeight, false));
                maxWidth = Math.Max(maxWidth, currentWidth);
                totalHeight += currentHeight;
                currentRuns = [];
                currentWidth = 0;
                currentHeight = Math.Max(regularFont.Height, boldFont.Height);
            }

            foreach (var run in ParseRuns(sourceLine))
            {
                var font = run.Bold ? boldFont : regularFont;
                foreach (var rawToken in Tokenize(run.Text))
                {
                    if (rawToken.Length == 0)
                    {
                        continue;
                    }

                    var isWhitespace = char.IsWhiteSpace(rawToken[0]);
                    if (isWhitespace)
                    {
                        if (currentRuns.Count == 0)
                        {
                            continue;
                        }

                        var whitespaceSize = MeasureToken(graphics, rawToken, font);
                        if (currentWidth + whitespaceSize.Width > MaxContentWidth)
                        {
                            FlushLine();
                            continue;
                        }

                        currentRuns.Add(new LayoutRun(rawToken, run.Bold, whitespaceSize.Width));
                        currentWidth += whitespaceSize.Width;
                        currentHeight = Math.Max(currentHeight, whitespaceSize.Height);
                        continue;
                    }

                    foreach (var token in SplitTokenToFit(graphics, rawToken, font))
                    {
                        var size = MeasureToken(graphics, token, font);
                        if (currentRuns.Count > 0 && currentWidth + size.Width > MaxContentWidth)
                        {
                            FlushLine();
                        }

                        currentRuns.Add(new LayoutRun(token, run.Bold, size.Width));
                        currentWidth += size.Width;
                        currentHeight = Math.Max(currentHeight, size.Height);
                    }
                }
            }

            FlushLine();
        }

        return new RichLayout(rows, Math.Max(1, maxWidth), Math.Max(1, totalHeight));
    }

    private static IEnumerable<string> SplitTokenToFit(Graphics graphics, string token, Font font)
    {
        if (MeasureToken(graphics, token, font).Width <= MaxContentWidth)
        {
            yield return token;
            yield break;
        }

        var start = 0;
        while (start < token.Length)
        {
            var low = 1;
            var high = token.Length - start;
            var best = 1;
            while (low <= high)
            {
                var length = low + ((high - low) / 2);
                var candidate = token.Substring(start, length);
                if (MeasureToken(graphics, candidate, font).Width <= MaxContentWidth)
                {
                    best = length;
                    low = length + 1;
                }
                else
                {
                    high = length - 1;
                }
            }

            yield return token.Substring(start, best);
            start += best;
        }
    }

    private static Size MeasureToken(Graphics graphics, string text, Font font) =>
        TextRenderer.MeasureText(
            graphics,
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    private static IEnumerable<string> Tokenize(string text)
    {
        if (text.Length == 0)
        {
            yield break;
        }

        var start = 0;
        var whitespace = char.IsWhiteSpace(text[0]);
        for (var index = 1; index < text.Length; index++)
        {
            var currentWhitespace = char.IsWhiteSpace(text[index]);
            if (currentWhitespace == whitespace)
            {
                continue;
            }

            yield return text[start..index];
            start = index;
            whitespace = currentWhitespace;
        }

        yield return text[start..];
    }

    private static IEnumerable<TextRun> ParseRuns(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var marker = line.IndexOf("**", index, StringComparison.Ordinal);
            if (marker < 0)
            {
                foreach (var run in AutoEmphasize(line[index..]))
                {
                    yield return run;
                }
                yield break;
            }

            if (marker > index)
            {
                foreach (var run in AutoEmphasize(line[index..marker]))
                {
                    yield return run;
                }
            }

            var end = line.IndexOf("**", marker + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                foreach (var run in AutoEmphasize(line[marker..]))
                {
                    yield return run;
                }
                yield break;
            }

            if (end > marker + 2)
            {
                yield return new TextRun(line[(marker + 2)..end], true);
            }
            index = end + 2;
        }
    }

    private static IEnumerable<TextRun> AutoEmphasize(string text)
    {
        var cursor = 0;
        while (cursor < text.Length)
        {
            var bestIndex = -1;
            string? bestKeyword = null;
            foreach (var keyword in AutoBoldKeywords)
            {
                var index = text.IndexOf(keyword, cursor, StringComparison.OrdinalIgnoreCase);
                if (index < 0 || (bestIndex >= 0 && index >= bestIndex))
                {
                    continue;
                }

                bestIndex = index;
                bestKeyword = keyword;
            }

            if (bestIndex < 0 || bestKeyword is null)
            {
                yield return new TextRun(text[cursor..], false);
                yield break;
            }

            if (bestIndex > cursor)
            {
                yield return new TextRun(text[cursor..bestIndex], false);
            }

            yield return new TextRun(text.Substring(bestIndex, bestKeyword.Length), true);
            cursor = bestIndex + bestKeyword.Length;
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private sealed record TextRun(string Text, bool Bold);
    private sealed record LayoutRun(string Text, bool Bold, int Width);
    private sealed record LayoutRow(IReadOnlyList<LayoutRun> Runs, int Width, int Height, bool IsParagraphGap)
    {
        public static LayoutRow ParagraphGap() => new([], 0, ParagraphGapPixels, true);
    }
    private sealed record RichLayout(IReadOnlyList<LayoutRow> Rows, int Width, int Height);
}
