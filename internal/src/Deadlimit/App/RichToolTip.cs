namespace Deadlimit.App;

internal sealed class RichToolTip : IDisposable
{
    private const int HorizontalPadding = 10;
    private const int VerticalPadding = 8;
    private const int ParagraphGap = 6;

    private readonly ToolTip _toolTip;
    private readonly Dictionary<Control, string> _texts = [];

    public RichToolTip(int autoPopDelay = 16000)
    {
        _toolTip = new ToolTip
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

    public void SetToolTip(Control control, string text)
    {
        var normalized = Normalize(text);
        _texts[control] = normalized;
        _toolTip.SetToolTip(control, normalized);
    }

    public void Dispose()
    {
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
        var regularFont = e.AssociatedControl.Font;
        var width = 0;
        var height = VerticalPadding * 2;
        var lines = text.Split('\n');

        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                height += ParagraphGap;
                continue;
            }

            var lineWidth = 0;
            var lineHeight = 0;
            foreach (var run in ParseRuns(line))
            {
                var font = run.Bold ? boldFont : regularFont;
                var size = TextRenderer.MeasureText(
                    graphics,
                    run.Text,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                lineWidth += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
            }

            width = Math.Max(width, lineWidth);
            height += Math.Max(lineHeight, regularFont.Height);
        }

        e.ToolTipSize = new Size(width + (HorizontalPadding * 2), height);
    }

    private void DrawPopup(object? sender, DrawToolTipEventArgs e)
    {
        e.DrawBackground();
        e.DrawBorder();

        var text = e.AssociatedControl is not null && _texts.TryGetValue(e.AssociatedControl, out var stored)
            ? stored
            : e.ToolTipText;
        Font regularFont = e.Font ?? e.AssociatedControl?.Font ?? Control.DefaultFont;
        using var boldFont = new Font(regularFont, FontStyle.Bold);
        var textColor = SystemColors.InfoText;
        var x = e.Bounds.Left + HorizontalPadding;
        var y = e.Bounds.Top + VerticalPadding;

        foreach (var line in text.Split('\n'))
        {
            if (line.Length == 0)
            {
                y += ParagraphGap;
                continue;
            }

            var cursorX = x;
            var lineHeight = regularFont.Height;
            foreach (var run in ParseRuns(line))
            {
                var font = run.Bold ? boldFont : regularFont;
                var size = TextRenderer.MeasureText(
                    e.Graphics,
                    run.Text,
                    font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                TextRenderer.DrawText(
                    e.Graphics,
                    run.Text,
                    font,
                    new Point(cursorX, y),
                    textColor,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                cursorX += size.Width;
                lineHeight = Math.Max(lineHeight, size.Height);
            }

            y += lineHeight;
        }
    }

    private static IEnumerable<TextRun> ParseRuns(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var marker = line.IndexOf("**", index, StringComparison.Ordinal);
            if (marker < 0)
            {
                yield return new TextRun(line[index..], false);
                yield break;
            }

            if (marker > index)
            {
                yield return new TextRun(line[index..marker], false);
            }

            var end = line.IndexOf("**", marker + 2, StringComparison.Ordinal);
            if (end < 0)
            {
                yield return new TextRun(line[marker..], false);
                yield break;
            }

            if (end > marker + 2)
            {
                yield return new TextRun(line[(marker + 2)..end], true);
            }
            index = end + 2;
        }
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();

    private sealed record TextRun(string Text, bool Bold);
}
