using System.Diagnostics;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsAttentionPulseFeature
{
    private const int PulseIntervalMilliseconds = 33;
    private const double PulsePeriodSeconds = 1.4;

    private static readonly Color AlertColor = Color.FromArgb(244, 67, 54);
    private static readonly Color PulseBaseColor = Color.White;

    private static Button? _settingsButton;
    private static System.Windows.Forms.Timer? _pulseTimer;
    private static long _pulseStartedAt;
    private static bool _attentionActive;
    private static bool _settingsOpen;
    private static Color _restingColor;

    internal static void Attach(MainForm form)
    {
        var button = FindDescendants<Button>(form)
            .FirstOrDefault(candidate => candidate.Name == UiControlNames.SettingsButton);
        if (button is null)
        {
            return;
        }

        _settingsButton = button;
        _restingColor = button.ForeColor;
        _pulseTimer = new System.Windows.Forms.Timer
        {
            Interval = PulseIntervalMilliseconds,
        };
        _pulseTimer.Tick += (_, _) => PaintPulseFrame();

        form.FormClosed += (_, _) => Detach();
        Refresh();
    }

    internal static void SettingsOpened()
    {
        _settingsOpen = true;
        StopPulse();
    }

    internal static void SettingsClosed()
    {
        _settingsOpen = false;
        Refresh();
    }

    internal static void Refresh()
    {
        var button = _settingsButton;
        if (button is null || button.IsDisposed)
        {
            return;
        }

        var requiresAttention = !_settingsOpen
            && SettingsStartupFeature.RequiresSetup(ProjectStore.GetToolPathSettings());
        if (requiresAttention)
        {
            StartPulse();
        }
        else
        {
            StopPulse();
        }
    }

    private static void StartPulse()
    {
        var button = _settingsButton;
        if (button is null || button.IsDisposed)
        {
            return;
        }

        if (!_attentionActive)
        {
            _attentionActive = true;
            _pulseStartedAt = Stopwatch.GetTimestamp();
            button.ForeColor = PulseBaseColor;
            _pulseTimer?.Start();
        }

        button.Invalidate();
    }

    private static void StopPulse()
    {
        var button = _settingsButton;
        _attentionActive = false;
        _pulseTimer?.Stop();

        if (button is not null && !button.IsDisposed)
        {
            button.ForeColor = _restingColor;
            button.Invalidate();
        }
    }

    private static void PaintPulseFrame()
    {
        var button = _settingsButton;
        if (!_attentionActive || button is null || button.IsDisposed)
        {
            return;
        }

        var elapsedSeconds = Stopwatch.GetElapsedTime(_pulseStartedAt).TotalSeconds;
        var phase = 2.0 * Math.PI * elapsedSeconds / PulsePeriodSeconds;
        var mix = 0.5 - (0.5 * Math.Cos(phase));

        button.ForeColor = Blend(PulseBaseColor, AlertColor, mix);
        button.Invalidate();
    }

    private static Color Blend(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0.0, 1.0);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + ((to.R - from.R) * amount)),
            (int)Math.Round(from.G + ((to.G - from.G) * amount)),
            (int)Math.Round(from.B + ((to.B - from.B) * amount)));
    }

    private static void Detach()
    {
        StopPulse();
        _pulseTimer?.Dispose();
        _pulseTimer = null;
        _settingsButton = null;
        _settingsOpen = false;
        _restingColor = Color.Empty;
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
