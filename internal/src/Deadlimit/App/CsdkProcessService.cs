using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class CsdkProcessService
{
    private const int SwRestore = 9;

    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "resourcecompiler",
        "CSDKCfgVPK",
    };

    public static bool IsRunning(DeadlimitPaths paths)
    {
        if (!TryGetCsdkRoot(paths, out var csdkRoot))
        {
            return false;
        }

        return GetRunningProcessIds(csdkRoot).Count > 0;
    }

    public static bool TryActivateRunningWindow(DeadlimitPaths paths)
    {
        if (!TryGetCsdkRoot(paths, out var csdkRoot))
        {
            return false;
        }

        var processIds = GetRunningProcessIds(csdkRoot);
        if (processIds.Count == 0)
        {
            return false;
        }

        nint targetWindow = 0;
        EnumWindows((window, _state) =>
        {
            if (!IsWindowVisible(window) || GetWindowTextLength(window) <= 0)
            {
                return true;
            }

            GetWindowThreadProcessId(window, out var processId);
            if (!processIds.Contains(unchecked((int)processId)))
            {
                return true;
            }

            // EnumWindows walks top-level windows in Z order, so the first visible CSDK
            // window is the closest approximation to the window the user last had active.
            targetWindow = window;
            return false;
        }, 0);

        if (targetWindow == 0)
        {
            return false;
        }

        if (IsIconic(targetWindow))
        {
            _ = ShowWindow(targetWindow, SwRestore);
        }

        var broughtToTop = BringWindowToTop(targetWindow);
        var foregrounded = SetForegroundWindow(targetWindow);
        return foregrounded || broughtToTop;
    }

    private static HashSet<int> GetRunningProcessIds(string csdkRoot)
    {
        var result = new HashSet<int>();
        var processes = Process.GetProcesses();
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited || IgnoredProcessNames.Contains(process.ProcessName))
                    {
                        continue;
                    }

                    var executablePath = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(executablePath) || !IsInsideRoot(executablePath, csdkRoot))
                    {
                        continue;
                    }

                    result.Add(process.Id);
                }
                catch (InvalidOperationException)
                {
                }
                catch (Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }

        return result;
    }

    private static bool TryGetCsdkRoot(DeadlimitPaths paths, out string csdkRoot)
    {
        csdkRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(paths.CsdkRoot) || !Directory.Exists(paths.CsdkRoot))
        {
            return false;
        }

        csdkRoot = NormalizeRoot(paths.CsdkRoot);
        return true;
    }

    private static string NormalizeRoot(string path) =>
        Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool IsInsideRoot(string path, string root)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private delegate bool EnumWindowsCallback(nint window, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, nint state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);
}
