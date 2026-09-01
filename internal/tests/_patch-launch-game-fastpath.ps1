$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = Get-Content -LiteralPath $Path -Raw
    if (-not $text.Contains($Old)) {
        throw "Expected source block was not found in $Path"
    }
    $text = $text.Replace($Old, $New)
    Set-Content -LiteralPath $Path -Value $text -Encoding utf8NoBOM -NoNewline
}

$onlinePath = 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs'
$onlineNeedle = @'
    private static async Task<bool> ToggleOnlinePreparationAsync()
'@
$onlineReplacement = @'
    internal static bool StopForGameLaunch()
    {
        if (_session is null)
        {
            return false;
        }

        StopSession();
        return true;
    }

    private static async Task<bool> ToggleOnlinePreparationAsync()
'@
Replace-Exact $onlinePath $onlineNeedle $onlineReplacement

$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
Replace-Exact $headerPath @'
using System.Drawing.Imaging;
using Deadlimit.Core;
'@ @'
using System.Drawing.Imaging;
using Microsoft.Win32;
using Deadlimit.Core;
'@

Replace-Exact $headerPath @'
    private static readonly Color GameGradientStart = Color.FromArgb(0x4C, 0xC7, 0x31);
    private static readonly Color GameGradientEnd = Color.FromArgb(0x13, 0xA5, 0x44);
'@ @'
    private static readonly Color GameGradientStart = Color.FromArgb(0x4C, 0xC7, 0x31);
    private static readonly Color GameGradientEnd = Color.FromArgb(0x13, 0xA5, 0x44);

    private static string? _cachedSteamExecutable;
'@

Replace-Exact $headerPath @'
            LaunchDeadlock(form);
        };
'@ @'
            if (LaunchDeadlock(form))
            {
                OnlinePreparationFeature.StopForGameLaunch();
            }
        };
'@

Replace-Exact $headerPath @'
        PositionControls();
    }
'@ @'
        PositionControls();

        // Warm the Steam path off the UI thread so LAUNCH GAME can dispatch immediately.
        _ = Task.Run(FindSteamExecutable);
    }
'@

Replace-Exact $headerPath @'
    private static void LaunchDeadlock(MainForm form)
    {
        if (TryLaunchDeadlockThroughSteamExecutable())
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeadlockSteamUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not launch Deadlock", "Не удалось запустить Deadlock"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
'@ @'
    private static bool LaunchDeadlock(MainForm form)
    {
        if (TryLaunchDeadlockThroughSteamExecutable())
        {
            return true;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeadlockSteamUri,
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not launch Deadlock", "Не удалось запустить Deadlock"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return false;
        }
    }
'@

$oldFind = @'
    private static string? FindSteamExecutable()
    {
        var steamProcesses = System.Diagnostics.Process.GetProcessesByName("steam");
        try
        {
            foreach (var process in steamProcesses)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in steamProcesses)
            {
                process.Dispose();
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }
'@
$newFind = @'
    private static string? FindSteamExecutable()
    {
        var cached = _cachedSteamExecutable;
        if (!string.IsNullOrWhiteSpace(cached) && File.Exists(cached))
        {
            return cached;
        }

        var resolved = FindSteamExecutableFromRegistry()
            ?? FindSteamExecutableFromKnownLocations()
            ?? FindSteamExecutableFromRunningProcess();
        if (!string.IsNullOrWhiteSpace(resolved))
        {
            _cachedSteamExecutable = resolved;
        }

        return resolved;
    }

    private static string? FindSteamExecutableFromRegistry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false);
            var configured = key?.GetValue("SteamExe") as string;
            if (string.IsNullOrWhiteSpace(configured))
            {
                return null;
            }

            var normalized = configured.Replace('/', Path.DirectorySeparatorChar);
            return File.Exists(normalized) ? normalized : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException
            or IOException
            or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static string? FindSteamExecutableFromKnownLocations()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static string? FindSteamExecutableFromRunningProcess()
    {
        var steamProcesses = System.Diagnostics.Process.GetProcessesByName("steam");
        try
        {
            foreach (var process in steamProcesses)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in steamProcesses)
            {
                process.Dispose();
            }
        }

        return null;
    }
'@
Replace-Exact $headerPath $oldFind $newFind

$smokePath = 'internal/tests/launch-game-fastpath-smoke.ps1'
$smoke = @'
$ErrorActionPreference = 'Stop'

$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
$onlinePath = 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs'
$header = Get-Content -LiteralPath $headerPath -Raw
$online = Get-Content -LiteralPath $onlinePath -Raw

$requiredHeader = @(
    'if (LaunchDeadlock(form))',
    'OnlinePreparationFeature.StopForGameLaunch();',
    'private static bool LaunchDeadlock(MainForm form)',
    'private static string? _cachedSteamExecutable;',
    '_ = Task.Run(FindSteamExecutable);',
    'Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false)',
    'Arguments = $"-applaunch {DeadlockSteamAppId}"'
)
foreach ($pattern in $requiredHeader) {
    if (-not $header.Contains($pattern)) {
        throw "Missing launch-game fastpath contract: $pattern"
    }
}

$requiredOnline = @(
    'internal static bool StopForGameLaunch()',
    'StopSession();',
    'return true;'
)
foreach ($pattern in $requiredOnline) {
    if (-not $online.Contains($pattern)) {
        throw "Missing online-stop contract: $pattern"
    }
}

$registryIndex = $header.IndexOf('Registry.CurrentUser.OpenSubKey', [StringComparison]::Ordinal)
$knownIndex = $header.IndexOf('FindSteamExecutableFromKnownLocations()', [StringComparison]::Ordinal)
$processIndex = $header.IndexOf('FindSteamExecutableFromRunningProcess()', [StringComparison]::Ordinal)
if ($registryIndex -lt 0 -or $knownIndex -lt 0 -or $processIndex -lt 0) {
    throw 'Steam resolution stages were not found.'
}
if ($processIndex -lt $registryIndex) {
    throw 'Running-process MainModule scan must remain the last-resort Steam path lookup.'
}

Write-Host 'Launch game fastpath smoke passed.'
'@
Set-Content -LiteralPath $smokePath -Value $smoke -Encoding utf8NoBOM -NoNewline
