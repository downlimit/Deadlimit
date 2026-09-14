using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Deadlimit.App;

internal static class UiRenderingStabilityFeature
{
    private const int WmShowWindow = 0x0018;
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;

    private static readonly PropertyInfo? DoubleBufferedProperty = typeof(Control).GetProperty(
        "DoubleBuffered",
        BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConditionalWeakTable<Control, PreparedControlMarker> PreparedControls = new();
    private static readonly ConditionalWeakTable<SettingsForm, PreparedSettingsMarker> PreparedSettingsForms = new();
    private static readonly HashSet<Form> PreparedForms = [];
    private static readonly FirstPaintMessageFilter FirstPaintFilter = new();
    private static readonly HookProc CbtHookCallback = OnCbtHook;
    private static IntPtr _cbtHook;

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        // The native hook exists only because Settings still has compatibility controls
        // that must be assembled before its first visible frame. Manager activation itself
        // is deliberately left to the normal WinForms/Windows message flow.
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

        root.SuspendLayout();
        try
        {
            action();
            PrepareControlTree(root);
        }
        finally
        {
            root.ResumeLayout(performLayout: true);
        }

        // SuspendLayout/ResumeLayout is the standard WinForms batching primitive here.
        // Never suppress drawing on a top-level HWND: doing that changes native visibility
        // semantics and makes task switching depend on manual redraw recovery.
        if (!root.IsDisposed && root.Visible)
        {
            root.Invalidate(invalidateChildren: true);
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

            if (PreparedForms.Add(form))
            {
                PrepareControlTree(form);
                ErrorLogShortcutFeature.Prepare(form);
                form.FormClosed += OnPreparedFormClosed;
            }

            if (form is SettingsForm settingsForm)
            {
                // Defensive fallback for environments where the native pre-show hook was
                // unavailable. Preparation is idempotent and does not suppress painting.
                PrepareSettings(settingsForm, invalidateAfterPrepare: true);
            }
        }
    }

    private static void PrepareSettings(SettingsForm form, bool invalidateAfterPrepare)
    {
        if (PreparedSettingsForms.TryGetValue(form, out _))
        {
            return;
        }

        form.SuspendLayout();
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
            // Rendering hardening is best-effort. Settings must remain usable if one
            // compatibility feature cannot be prepared on a particular machine.
        }
        finally
        {
            form.ResumeLayout(performLayout: true);
        }

        if (invalidateAfterPrepare && !form.IsDisposed && form.Visible)
        {
            form.Invalidate(invalidateChildren: true);
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
            // A platform/control that rejects the protected DoubleBuffered property must
            // not affect application behavior.
        }
    }

    private static void OnPreparedFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (sender is not Form form)
        {
            return;
        }

        PreparedForms.Remove(form);
        if (form is SettingsForm settingsForm)
        {
            PreparedSettingsForms.Remove(settingsForm);
        }
        form.FormClosed -= OnPreparedFormClosed;
    }

    private static IntPtr OnCbtHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcbtActivate
            && wParam != IntPtr.Zero
            && Control.FromHandle(wParam) is SettingsForm settingsForm
            && !settingsForm.IsDisposed)
        {
            PrepareSettings(settingsForm, invalidateAfterPrepare: false);
            ErrorLogShortcutFeature.Prepare(settingsForm);
            PrepareControlTree(settingsForm);
        }

        return CallNextHookEx(_cbtHook, code, wParam, lParam);
    }

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
                PrepareSettings(settingsForm, invalidateAfterPrepare: false);
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
