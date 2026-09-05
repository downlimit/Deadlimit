using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Deadlimit.App;

internal static class UiRenderingStabilityFeature
{
    private const int WmSetRedraw = 0x000B;
    private const int WmShowWindow = 0x0018;
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const uint GwOwner = 4;

    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConditionalWeakTable<Control, PreparedControlMarker> PreparedControls = new();
    private static readonly ConditionalWeakTable<SettingsForm, PreparedSettingsMarker> PreparedSettingsForms = new();
    private static readonly HashSet<Form> PreparedForms = [];
    private static readonly HashSet<Form> TrackedModalForms = [];
    private static readonly Dictionary<Control, int> RedrawHolds = [];
    private static readonly Dictionary<Form, int> PendingModalOwnerReleases = [];
    private static readonly Dictionary<Form, int> ReadyModalOwnerReleases = [];
    private static readonly FirstPaintMessageFilter FirstPaintFilter = new();
    private static readonly HookProc CbtHookCallback = OnCbtHook;
    private static IntPtr _cbtHook;

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        // The CBT hook catches activation synchronously, before the new top-level window
        // can expose its first useful frame. IMessageFilter remains a second pre-paint path;
        // Application.Idle is only the defensive fallback.
        if (OperatingSystem.IsWindows())
        {
            _cbtHook = SetWindowsHookEx(
                WhCbt,
                CbtHookCallback,
                IntPtr.Zero,
                GetCurrentThreadId());
        }

        Application.AddMessageFilter(FirstPaintFilter);
        Application.Idle += OnApplicationIdle;
    }

    internal static void ApplyAtomically(Control root, Action action)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(action);

        var redrawHeld = root.IsHandleCreated && !root.IsDisposed;
        if (redrawHeld)
        {
            HoldRedraw(root);
        }

        root.SuspendLayout();
        try
        {
            action();
            PrepareControlTree(root);
        }
        finally
        {
            root.ResumeLayout(performLayout: true);
            if (redrawHeld)
            {
                ReleaseRedraw(root, repaint: true);
            }
        }
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
                // Settings is normally fully assembled before its first paint. Other legacy
                // dialogs may still receive late augmentations, so keep first-idle batching
                // only for those forms.
                var batchFirstIdle = form is not MainForm
                    && form is not SettingsForm
                    && form.IsHandleCreated;
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

            if (form is SettingsForm settingsForm)
            {
                // Native-hook failure fallback. In the normal path this is a no-op because
                // Settings was already prepared before its first visible paint.
                PrepareSettings(settingsForm, repaintOnRelease: true);
            }

            ErrorLogShortcutFeature.Prepare(form);
            TrackModalOwner(form, allowOwnedNonModal: false);
        }

        ReleaseReadyModalOwners();
        PromotePendingModalOwnerReleases();
    }

    private static void PrepareSettings(SettingsForm form, bool repaintOnRelease)
    {
        if (PreparedSettingsForms.TryGetValue(form, out _))
        {
            return;
        }

        var redrawHeld = form.IsHandleCreated && !form.IsDisposed;
        if (redrawHeld)
        {
            // The base SettingsForm has already applied the selected theme in its
            // constructor. Freeze the HWND only while the final compatibility features add
            // their controls/layout, so no partially assembled client area can paint.
            HoldRedraw(form);
        }

        try
        {
            SettingsVersionFeature.Prepare(form);
            SettingsToolchainProgressFeature.Prepare(form);
            SettingsFeedbackFeature.Prepare(form);
            PrepareControlTree(form);
            form.PerformLayout();
            PreparedSettingsForms.Add(form, new PreparedSettingsMarker());
        }
        catch (Exception exception) when (exception is InvalidOperationException
            or ArgumentException
            or TargetInvocationException)
        {
            // Rendering hardening must never prevent Settings from opening.
        }
        finally
        {
            if (redrawHeld)
            {
                // Re-enable drawing and always invalidate the completed control tree. The
                // CBT/WM_SHOWWINDOW path deliberately skips synchronous Update(), allowing
                // the native show sequence to paint the final state once. The Idle fallback
                // may still force the queued paint immediately.
                ReleaseRedraw(form, repaint: repaintOnRelease);
            }
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
        if (e.Control is not null)
        {
            PrepareControlTree(e.Control);
        }
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

    private static void TrackModalOwner(Form form, bool allowOwnedNonModal)
    {
        var owner = ResolveOwner(form);
        if ((!allowOwnedNonModal && !form.Modal)
            || owner is null
            || owner.IsDisposed
            || !owner.IsHandleCreated
            || !TrackedModalForms.Add(form))
        {
            return;
        }

        // Start the hold before the child gets its first visible frame whenever possible.
        // This preserves the owner's fully composed header/list frame under the dialog.
        HoldRedraw(owner);
        form.FormClosed += (_, _) =>
        {
            TrackedModalForms.Remove(form);
            QueueModalOwnerRelease(owner);
        };
    }

    private static Form? ResolveOwner(Form form)
    {
        if (form.Owner is Form managedOwner)
        {
            return managedOwner;
        }

        if (!OperatingSystem.IsWindows() || !form.IsHandleCreated)
        {
            return null;
        }

        var ownerHandle = GetWindow(form.Handle, GwOwner);
        return ownerHandle == IntPtr.Zero
            ? null
            : Control.FromHandle(ownerHandle) as Form;
    }

    private static void QueueModalOwnerRelease(Form owner)
    {
        if (owner.IsDisposed || !owner.IsHandleCreated)
        {
            RedrawHolds.Remove(owner);
            PendingModalOwnerReleases.Remove(owner);
            ReadyModalOwnerReleases.Remove(owner);
            return;
        }

        PendingModalOwnerReleases[owner] = PendingModalOwnerReleases.TryGetValue(owner, out var count)
            ? count + 1
            : 1;
    }

    private static void PromotePendingModalOwnerReleases()
    {
        if (PendingModalOwnerReleases.Count == 0)
        {
            return;
        }

        foreach (var pair in PendingModalOwnerReleases.ToArray())
        {
            var owner = pair.Key;
            var releaseCount = pair.Value;
            PendingModalOwnerReleases.Remove(owner);

            if (owner.IsDisposed || !owner.IsHandleCreated)
            {
                RedrawHolds.Remove(owner);
                continue;
            }

            ReadyModalOwnerReleases[owner] = ReadyModalOwnerReleases.TryGetValue(owner, out var readyCount)
                ? readyCount + releaseCount
                : releaseCount;

            // Force one message turn before the release. This guarantees another Idle
            // boundary even when the user simply closes Settings and does nothing else.
            try
            {
                owner.BeginInvoke((Action)(() => { }));
            }
            catch (InvalidOperationException)
            {
                RedrawHolds.Remove(owner);
                ReadyModalOwnerReleases.Remove(owner);
            }
        }
    }

    private static void ReleaseReadyModalOwners()
    {
        if (ReadyModalOwnerReleases.Count == 0)
        {
            return;
        }

        foreach (var pair in ReadyModalOwnerReleases.ToArray())
        {
            var owner = pair.Key;
            var releaseCount = pair.Value;
            ReadyModalOwnerReleases.Remove(owner);

            if (owner.IsDisposed || !owner.IsHandleCreated)
            {
                RedrawHolds.Remove(owner);
                continue;
            }

            // This is the second settled Idle after FormClosed. Activated handlers and any
            // MainForm replacement requested by interface changes have already run.
            for (var index = 0; index < releaseCount; index++)
            {
                ReleaseRedraw(owner, repaint: true);
            }
        }
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
        if (form is SettingsForm settingsForm)
        {
            PreparedSettingsForms.Remove(settingsForm);
        }
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

        // WM_SETREDRAW does not automatically invalidate the client area when drawing is
        // re-enabled. Always queue one paint for the now-complete tree; otherwise Settings
        // can remain blank until mouse movement invalidates individual child controls.
        control.Invalidate(invalidateChildren: true);

        if (repaint)
        {
            control.Update();
        }
    }

    private static IntPtr OnCbtHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcbtActivate
            && wParam != IntPtr.Zero
            && Control.FromHandle(wParam) is Form form
            && !form.IsDisposed)
        {
            if (form is SettingsForm settingsForm)
            {
                PrepareSettings(settingsForm, repaintOnRelease: false);
                TrackModalOwner(settingsForm, allowOwnedNonModal: true);
            }
            else
            {
                TrackModalOwner(form, allowOwnedNonModal: false);
            }

            ErrorLogShortcutFeature.Prepare(form);
            PrepareControlTree(form);
        }

        return CallNextHookEx(_cbtHook, code, wParam, lParam);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        HookProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hook,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr windowHandle, uint command);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private sealed class FirstPaintMessageFilter : IMessageFilter
    {
        public bool PreFilterMessage(ref Message message)
        {
            if (message.Msg != WmShowWindow || message.WParam == IntPtr.Zero)
            {
                return false;
            }

            if (Control.FromHandle(message.HWnd) is not Form form || form.IsDisposed)
            {
                return false;
            }

            if (form is SettingsForm settingsForm)
            {
                PrepareSettings(settingsForm, repaintOnRelease: false);
                TrackModalOwner(settingsForm, allowOwnedNonModal: true);
            }
            else
            {
                TrackModalOwner(form, allowOwnedNonModal: false);
            }

            ErrorLogShortcutFeature.Prepare(form);
            PrepareControlTree(form);
            return false;
        }
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    private sealed class PreparedControlMarker
    {
    }

    private sealed class PreparedSettingsMarker
    {
    }
}
