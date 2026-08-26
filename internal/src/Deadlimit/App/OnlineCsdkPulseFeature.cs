using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace Deadlimit.App;

internal static class OnlineCsdkPulseFeature
{
    private const int PulseIntervalMilliseconds = 33;
    private const double PulsePeriodSeconds = 1.4;
    private const string IndicatorReserve = "     ";
    private const string IndicatorGap = "  ";

    private static readonly Color IndicatorColor = Color.FromArgb(244, 67, 54);

    private static Button? _launchButton;
    private static System.Windows.Forms.Timer? _pulseTimer;
    private static long _pulseStartedAt;
    private static bool _onlineActive;
    private static bool _normalizingText;

    public static void Attach(MainForm form)
    {
        var launchButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                button.Text.Contains("LAUNCH CSDK", StringComparison.OrdinalIgnoreCase)
                || button.Text.Contains("ЗАПУСК CSDK", StringComparison.OrdinalIgnoreCase));
        if (launchButton is null)
        {
            return;
        }

        _launchButton = launchButton;
        _pulseTimer = new System.Windows.Forms.Timer
        {
            Interval = PulseIntervalMilliseconds,
        };
        _pulseTimer.Tick += (_, _) =>
        {
            if (_onlineActive && _launchButton is not null && !_launchButton.IsDisposed)
            {
                _launchButton.Invalidate();
            }
        };

        launchButton.TextChanged += OnLaunchButtonTextChanged;
        form.Shown += (_, _) =>
        {
            if (_launchButton is null || _launchButton.IsDisposed)
            {
                return;
            }

            // ProjectHeaderFeature installs the gradient renderer from its own Shown handler.
            // This feature is attached afterwards, so its Paint handler runs last and only
            // overlays the status indicator without owning the button background or text.
            _launchButton.Paint += PaintOnlineIndicator;
            SyncStateFromButtonText();
        };
        form.FormClosed += (_, _) => Detach();

        SyncStateFromButtonText();
    }

    private static void OnLaunchButtonTextChanged(object? sender, EventArgs e)
    {
        if (!_normalizingText)
        {
            SyncStateFromButtonText();
        }
    }

    private static void SyncStateFromButtonText()
    {
        var button = _launchButton;
        if (button is null || button.IsDisposed)
        {
            return;
        }

        var online = IsOnlineText(button.Text);
        if (online)
        {
            var normalized = UiText.T(
                IndicatorReserve + "ONLINE CSDK",
                IndicatorReserve + "CSDK ONLINE");
            if (!string.Equals(button.Text, normalized, StringComparison.Ordinal))
            {
                _normalizingText = true;
                try
                {
                    button.Text = normalized;
                }
                finally
                {
                    _normalizingText = false;
                }
            }

            if (!_onlineActive)
            {
                _onlineActive = true;
                _pulseStartedAt = Stopwatch.GetTimestamp();
                _pulseTimer?.Start();
            }
        }
        else if (_onlineActive)
        {
            _onlineActive = false;
            _pulseTimer?.Stop();
        }

        button.Invalidate();
    }

    private static bool IsOnlineText(string text) =>
        text.Contains("ONLINE CSDK", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CSDK ONLINE", StringComparison.OrdinalIgnoreCase);

    private static void PaintOnlineIndicator(object? sender, PaintEventArgs e)
    {
        if (!_onlineActive || sender is not Button button || button.IsDisposed)
        {
            return;
        }

        var flags = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
        var textSize = TextRenderer.MeasureText(
            e.Graphics,
            button.Text,
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            flags);
        var reserveSize = TextRenderer.MeasureText(
            e.Graphics,
            IndicatorReserve,
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            flags);
        var gapSize = TextRenderer.MeasureText(
            e.Graphics,
            IndicatorGap,
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            flags);
        var playSize = TextRenderer.MeasureText(
            e.Graphics,
            "▶",
            button.Font,
            new Size(int.MaxValue, int.MaxValue),
            flags);

        var diameter = Math.Max(6F, Math.Min(playSize.Width, playSize.Height) - 1F);
        var contentLeft = (button.ClientSize.Width - textSize.Width) / 2F;
        var x = contentLeft + Math.Max(0F, reserveSize.Width - gapSize.Width - diameter);
        var y = (button.ClientSize.Height - diameter) / 2F;

        var elapsedSeconds = Stopwatch.GetElapsedTime(_pulseStartedAt).TotalSeconds;
        var phase = (2.0 * Math.PI * elapsedSeconds / PulsePeriodSeconds) + (Math.PI / 2.0);
        var opacity = 0.5 + (0.5 * Math.Sin(phase));
        var alpha = Math.Clamp((int)Math.Round(255.0 * opacity), 0, 255);

        var previousSmoothingMode = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        try
        {
            using var brush = new SolidBrush(Color.FromArgb(alpha, IndicatorColor));
            e.Graphics.FillEllipse(brush, x, y, diameter, diameter);
        }
        finally
        {
            e.Graphics.SmoothingMode = previousSmoothingMode;
        }
    }

    private static void Detach()
    {
        if (_launchButton is not null && !_launchButton.IsDisposed)
        {
            _launchButton.TextChanged -= OnLaunchButtonTextChanged;
            _launchButton.Paint -= PaintOnlineIndicator;
        }

        _pulseTimer?.Stop();
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _launchButton = null;
        _onlineActive = false;
        _normalizingText = false;
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

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
