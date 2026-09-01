using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Deadlimit.App;

internal static class UiRenderingStabilityFeature
{
    private const int WmSetRedraw = 0x000B;

    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConditionalWeakTable<Control, PreparedControlMarker> PreparedControls = new();
    private static readonly HashSet<Form> PreparedForms = [];
    private static readonly HashSet<Form> TrackedModalForms = [];
    private static readonly Dictionary<Control, int> RedrawHolds = [];

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        // Register before Program.Main attaches feature-level Application.Idle handlers.
        // This lets us suppress intermediate paints while late UI augmentations run.
        Application.Idle += OnApplicationIdle;
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.Cast<Form>().ToArray())
        {
            if (form.IsDisposed)
            {
                continue;
            }

            var firstSeen = PreparedForms.Add(form);
            if (firstSeen)
            {
                // Non-main forms may still receive legacy Application.Idle enhancements
                // after Show()/ShowDialog(). Keep the first idle pass atomic so the user
                // never sees controls being inserted/reflowed one step at a time.
                var batchFirstIdle = form is not MainForm && form.IsHandleCreated;
                if (batchFirstIdle)
                {
                    HoldRedraw(form);
                }

                PrepareControlTree(form);
                form.FormClosed += OnTrackedFormClosed;

                if (batchFirstIdle)
                {
                    form.BeginInvoke((Action)(() => ReleaseRedraw(form, repaint: true)));
                }
            }
            else
            {
                PrepareControlTree(form);
            }

            TrackModalOwner(form);
        }
    }

    private static void PrepareControlTree(Control control)
    {
        if (!PreparedControls.TryGetValue(control, out _))
        {
            PreparedControls.Add(control, new PreparedControlMarker());
            control.ControlAdded += OnControlAdded;

            if (ShouldDoubleBuffer(control))
            {
                TryEnableDoubleBuffering(control);
            }
        }

        foreach (Control child in control.Controls)
        {
            PrepareControlTree(child);
        }
    }

    private static void OnControlAdded(object? sender, ControlEventArgs e)
    {
        PrepareControlTree(e.Control);
    }

    private static bool ShouldDoubleBuffer(Control control) =>
        control is Form
            or Panel
            or GroupBox
            or ListBox
            or UserControl;

    private static void TryEnableDoubleBuffering(Control control)
    {
        try
        {
            DoubleBufferedProperty?.SetValue(control, true);
        }
        catch (Exception exception) when (exception is ArgumentException
            or MemberAccessException
            or TargetInvocationException)
        {
            // Rendering hardening is best-effort. A platform/control that rejects the
            // protected DoubleBuffered property must not affect application behavior.
        }
    }

    private static void TrackModalOwner(Form form)
    {
        if (!form.Modal
            || form.Owner is not Form owner
            || owner.IsDisposed
            || !owner.IsHandleCreated
            || !TrackedModalForms.Add(form))
        {
            return;
        }

        // While a modal child owns the interaction, the parent has nothing useful to
        // repaint. Keep its last fully-composed frame (header artwork + vignette + list)
        // instead of exposing partial activation/deactivation paints behind the dialog.
        HoldRedraw(owner);
        form.FormClosed += (_, _) =>
        {
            TrackedModalForms.Remove(form);
            if (owner.IsDisposed || !owner.IsHandleCreated)
            {
                RedrawHolds.Remove(owner);
                return;
            }

            // Owner Activated handlers may rebuild/reorder/reload controls. Release on the
            // next message turn so only their final state is ever painted.
            owner.BeginInvoke((Action)(() => ReleaseRedraw(owner, repaint: true)));
        };
    }

    private static void OnTrackedFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form form)
        {
            return;
        }

        PreparedForms.Remove(form);
        RedrawHolds.Remove(form);
        TrackedModalForms.Remove(form);
        form.FormClosed -= OnTrackedFormClosed;
    }

    private static void HoldRedraw(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated)
        {
            return;
        }

        var depth = RedrawHolds.TryGetValue(control, out var currentDepth)
            ? currentDepth
            : 0;
        RedrawHolds[control] = depth + 1;
        if (depth != 0 || !OperatingSystem.IsWindows())
        {
            return;
        }

        SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    private static void ReleaseRedraw(Control control, bool repaint)
    {
        if (!RedrawHolds.TryGetValue(control, out var depth))
        {
            return;
        }

        if (depth > 1)
        {
            RedrawHolds[control] = depth - 1;
            return;
        }

        RedrawHolds.Remove(control);
        if (control.IsDisposed || !control.IsHandleCreated)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            SendMessage(control.Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        }

        if (!repaint)
        {
            return;
        }

        control.Invalidate(invalidateChildren: true);
        control.Update();
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    private sealed class PreparedControlMarker;
}
