using System.ComponentModel;
using System.Diagnostics;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class CsdkProcessService
{
    private static readonly HashSet<string> IgnoredProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "resourcecompiler",
        "CSDKCfgVPK",
    };

    public static bool IsRunning(DeadlimitPaths paths)
    {
        if (string.IsNullOrWhiteSpace(paths.CsdkRoot) || !Directory.Exists(paths.CsdkRoot))
        {
            return false;
        }

        var csdkRoot = NormalizeRoot(paths.CsdkRoot);
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
                    if (string.IsNullOrWhiteSpace(executablePath))
                    {
                        continue;
                    }

                    if (IsInsideRoot(executablePath, csdkRoot))
                    {
                        return true;
                    }
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

        return false;
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
}
