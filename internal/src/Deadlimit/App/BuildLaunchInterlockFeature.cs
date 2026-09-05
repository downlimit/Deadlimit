using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;

namespace Deadlimit.App;

internal static class BuildLaunchInterlockFeature
{
    private static readonly Color BuildGradientStart = Color.FromArgb(0x39, 0x9A, 0xED);
    private static readonly Color BuildGradientEnd = Color.FromArgb(0x24, 0x5E, 0xCF);
    private static readonly ConditionalWeakTable<MainForm, object> AttachedForms = new();

    [ModuleInitializer]
    internal static void Initialize()
    {
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.OfType<MainForm>())
        {
            if (AttachedForms.TryGetValue(form, out _))
            {
                continue;
            }

            if (TryAttach(form))
            {
                AttachedForms.Add(form, new object());
            }
        }
    }

    private static bool TryAttach(MainForm form)
    {
        var buildButton = FindDescendants<Button>(form).FirstOrDefault(button =>
            string.Equals(button.Text, "BUILD FOR TEST", StringComparison.Ordinal)
            || string.Equals(button.Text, "СОБРАТЬ ДЛЯ ТЕСТА", StringComparison.Ordinal));
        var launchGameButton = FindDescendants<Button>(form).FirstOrDefault(button =>
            button.Text.Contains("LAUNCH GAME", StringComparison.Ordinal)
            || button.Text.Contains("ЗАПУСК ИГРЫ", StringComparison.Ordinal));

        if (buildButton is null || launchGameButton?.Parent is not Control parent)
        {
            return false;
        }

        var overlay = new BuildLockOverlay
        {
            Font = launchGameButton.Font,
            Text = UiText.T("BUILDING...", "ИДЁТ СБОРКА"),
            Bounds = launchGameButton.Bounds,
            Visible = false,
        };
        parent.Controls.Add(overlay);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };
        toolTip.SetToolTip(
            overlay,
            UiText.T(
                "The mod is still being built. Deadlock launch is temporarily disabled until the VPK is ready.",
                "Сборка мода ещё идёт. Запуск Deadlock временно отключён, пока VPK не будет готов."));

        var interlocked = false;
        var launchGameEnabledBeforeInterlock = true;

        void SyncOverlayBounds()
        {
            if (!overlay.IsDisposed && !launchGameButton.IsDisposed)
            {
                overlay.Bounds = launchGameButton.Bounds;
            }
        }

        void SetInterlocked(bool value)
        {
            if (interlocked == value || overlay.IsDisposed || launchGameButton.IsDisposed)
            {
                return;
            }

            interlocked = value;
            if (value)
            {
                launchGameEnabledBeforeInterlock = launchGameButton.Enabled;
                launchGameButton.Enabled = false;
                overlay.Text = UiText.T("BUILDING...", "ИДЁТ СБОРКА");
                SyncOverlayBounds();
                overlay.Visible = true;
                overlay.BringToFront();
            }
            else
            {
                overlay.Visible = false;
                launchGameButton.Enabled = launchGameEnabledBeforeInterlock;
                launchGameButton.Invalidate();
            }
        }

        // BuildFeature owns the actual async build. Its handler is registered first and
        // disables the hidden BUILD FOR TEST button before its first await. If validation
        // or the close-Deadlock prompt cancels the build, the button stays enabled and no
        // interlock is shown.
        buildButton.Click += (_, _) =>
        {
            if (!buildButton.Enabled)
            {
                SetInterlocked(true);
            }
        };
        buildButton.EnabledChanged += (_, _) =>
        {
            if (interlocked && buildButton.Enabled)
            {
                SetInterlocked(false);
            }
        };

        launchGameButton.LocationChanged += (_, _) => SyncOverlayBounds();
        launchGameButton.SizeChanged += (_, _) => SyncOverlayBounds();
        parent.Resize += (_, _) => SyncOverlayBounds();

        form.FormClosed += (_, _) =>
        {
            toolTip.Dispose();
            overlay.Dispose();
        };

        return true;
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

    private sealed class BuildLockOverlay : Control
    {
        public BuildLockOverlay()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
                true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
            {
                return;
            }

            using var brush = new LinearGradientBrush(
                ClientRectangle,
                BuildGradientStart,
                BuildGradientEnd,
                LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, ClientRectangle);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.NoPrefix);
        }
    }
}
