using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Deadlimit.App;

/// <summary>
/// Repairs the Manager's visible frame at the native activation boundary before any
/// Activated handlers perform project/library refresh work. This also neutralizes a
/// stale top-level WM_SETREDRAW hold left by older modal-rendering hardening.
/// </summary>
internal static class WindowActivationRecoveryFeature
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;
    private const int WmSetRedraw = 0x000B;

    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;
    private const uint RdwFrame = 0x0400;

    private static readonly HookProc CbtHookCallback = OnCbtHook;
    private static IntPtr _cbtHook;

    [ModuleInitializer]
    internal static void Bootstrap()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        _cbtHook = SetWindowsHookEx(
            WhCbt,
            CbtHookCallback,
            IntPtr.Zero,
            GetCurrentThreadId());
    }

    private static IntPtr OnCbtHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code == HcbtActivate
            && wParam != IntPtr.Zero
            && Control.FromHandle(wParam) is MainForm form
            && !form.IsDisposed)
        {
            // WM_SETREDRAW(FALSE) on a top-level HWND can suppress its visible client
            // frame. Always restore drawing before the Manager becomes foreground.
            SendMessage(wParam, WmSetRedraw, new IntPtr(1), IntPtr.Zero);

            // Commit one complete current frame before Activated subscribers start any
            // filesystem scans or list refreshes. The shell can therefore raise the
            // Manager immediately instead of exposing an empty/black client area.
            RedrawWindow(
                wParam,
                IntPtr.Zero,
                IntPtr.Zero,
                RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow | RdwFrame);
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
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr windowHandle,
        int message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(
        IntPtr windowHandle,
        IntPtr updateRectangle,
        IntPtr updateRegion,
        uint flags);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);
}
