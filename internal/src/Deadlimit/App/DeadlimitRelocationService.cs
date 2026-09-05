using System.Diagnostics;
using System.Text;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class DeadlimitRelocationService
{
    public static async Task PrepareRelocationAsync(string targetRoot)
    {
        var sourceRoot = NormalizeRoot(DeadlimitPaths.DefaultDeadlimitRoot);
        var destinationRoot = NormalizeRoot(targetRoot);
        ValidateRoots(sourceRoot, destinationRoot);

        var destinationExisted = Directory.Exists(destinationRoot);
        if (destinationExisted && Directory.EnumerateFileSystemEntries(destinationRoot).Any())
        {
            throw new InvalidOperationException(
                UiText.T(
                    "The selected Deadlimit folder must be empty.",
                    "Выбранная папка Deadlimit должна быть пустой."));
        }

        Directory.CreateDirectory(destinationRoot);
        try
        {
            await Task.Run(() => CopyTree(sourceRoot, destinationRoot)).ConfigureAwait(true);
        }
        catch
        {
            if (!destinationExisted)
            {
                TryDeleteDirectory(destinationRoot);
            }
            throw;
        }

        var currentExecutable = Environment.ProcessPath
            ?? throw new InvalidOperationException("The current Deadlimit Manager executable path is unavailable.");
        var executableRelative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(currentExecutable));
        if (EscapesRoot(executableRelative))
        {
            throw new InvalidOperationException("Deadlimit Manager is not running from the detected Deadlimit root.");
        }

        var relocatedExecutable = Path.Combine(destinationRoot, executableRelative);
        if (!File.Exists(relocatedExecutable))
        {
            throw new FileNotFoundException("The relocated Deadlimit Manager executable was not copied.", relocatedExecutable);
        }

        var helperPath = Path.Combine(
            Path.GetTempPath(),
            $"deadlimit-relocate-{Guid.NewGuid():N}.ps1");
        await File.WriteAllTextAsync(helperPath, RelocationWorkerScript, new UTF8Encoding(false)).ConfigureAwait(true);

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("-ProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-OldRoot");
        startInfo.ArgumentList.Add(sourceRoot);
        startInfo.ArgumentList.Add("-NewRoot");
        startInfo.ArgumentList.Add(destinationRoot);
        startInfo.ArgumentList.Add("-NewExecutable");
        startInfo.ArgumentList.Add(relocatedExecutable);

        _ = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The Deadlimit relocation worker could not be started.");
    }

    internal static void ValidateRoots(string sourceRoot, string destinationRoot)
    {
        if (string.Equals(sourceRoot, destinationRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(UiText.T("Deadlimit is already in this folder.", "Deadlimit уже находится в этой папке."));
        }

        if (IsWithin(destinationRoot, sourceRoot) || IsWithin(sourceRoot, destinationRoot))
        {
            throw new InvalidOperationException(
                UiText.T(
                    "The new Deadlimit folder cannot be inside the current folder or contain the current folder.",
                    "Новая папка Deadlimit не может находиться внутри текущей папки или содержать текущую папку."));
        }
    }

    private static string NormalizeRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(UiText.T("The Deadlimit folder is empty.", "Папка Deadlimit не указана."), nameof(path));
        }

        return Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithin(string candidate, string root)
    {
        var relative = Path.GetRelativePath(root, candidate);
        return !EscapesRoot(relative)
            && !string.Equals(relative, ".", StringComparison.Ordinal);
    }

    private static bool EscapesRoot(string relative) =>
        string.Equals(relative, "..", StringComparison.Ordinal)
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || Path.IsPathRooted(relative);

    private static void CopyTree(string sourceRoot, string destinationRoot)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relative));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, sourceFile);
            var destinationFile = Path.Combine(destinationRoot, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
            try
            {
                File.SetLastWriteTimeUtc(destinationFile, File.GetLastWriteTimeUtc(sourceFile));
                File.SetAttributes(destinationFile, File.GetAttributes(sourceFile));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private const string RelocationWorkerScript = """
param(
    [Parameter(Mandatory=$true)][int]$ProcessId,
    [Parameter(Mandatory=$true)][string]$OldRoot,
    [Parameter(Mandatory=$true)][string]$NewRoot,
    [Parameter(Mandatory=$true)][string]$NewExecutable
)
$ErrorActionPreference = 'Stop'

function Replace-RootPrefix([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $Value }
    if ($Value.StartsWith($OldRoot, [StringComparison]::OrdinalIgnoreCase)) {
        return $NewRoot + $Value.Substring($OldRoot.Length)
    }
    return $Value
}

function Rewrite-Shortcut([object]$Shell, [string]$Path) {
    try {
        $shortcut = $Shell.CreateShortcut($Path)
        $changed = $false
        $target = Replace-RootPrefix $shortcut.TargetPath
        if ($target -ne $shortcut.TargetPath) { $shortcut.TargetPath = $target; $changed = $true }
        $working = Replace-RootPrefix $shortcut.WorkingDirectory
        if ($working -ne $shortcut.WorkingDirectory) { $shortcut.WorkingDirectory = $working; $changed = $true }
        $icon = Replace-RootPrefix $shortcut.IconLocation
        if ($icon -ne $shortcut.IconLocation) { $shortcut.IconLocation = $icon; $changed = $true }
        if ($changed) { $shortcut.Save() }
    } catch {}
}

try { Wait-Process -Id $ProcessId -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 250

$shell = New-Object -ComObject WScript.Shell
$locations = @(
    $NewRoot,
    [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory),
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Deadlimit')
) | Select-Object -Unique
foreach ($location in $locations) {
    if (-not [string]::IsNullOrWhiteSpace($location) -and (Test-Path -LiteralPath $location)) {
        Get-ChildItem -LiteralPath $location -Filter '*.lnk' -File -ErrorAction SilentlyContinue |
            ForEach-Object { Rewrite-Shortcut $shell $_.FullName }
    }
}

$deleteError = $null
try {
    if (Test-Path -LiteralPath $OldRoot) {
        Remove-Item -LiteralPath $OldRoot -Recurse -Force
    }
} catch {
    $deleteError = $_.Exception.Message
}

Start-Process -FilePath $NewExecutable -WorkingDirectory $NewRoot
if ($deleteError) {
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show(
        "Deadlimit was copied to the new folder and restarted, but the old folder could not be removed:`n$OldRoot`n`n$deleteError",
        'Deadlimit relocation',
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Warning) | Out-Null
}

try { Remove-Item -LiteralPath $PSCommandPath -Force } catch {}
""";
}
