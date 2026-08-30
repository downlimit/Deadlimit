param(
    [string]$CsdkRootOverride = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Keep the legacy settings path so existing Deadlimit Manager installations retain their configuration.
$settingsPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) "Deadlimit\settings.json"
$generatorProcess = $null
$backupRoot = $null
$cachePaths = @()
$originalCachePaths = @{}
$startedAt = [DateTime]::UtcNow

function Stop-GeneratedProcessTree {
    param([int]$RootProcessId)

    $allProcesses = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
    $ids = [System.Collections.Generic.List[int]]::new()
    $pending = [System.Collections.Generic.Queue[int]]::new()
    $pending.Enqueue($RootProcessId)

    while ($pending.Count -gt 0) {
        $parentId = $pending.Dequeue()
        if (-not $ids.Contains($parentId)) {
            $ids.Add($parentId)
        }

        foreach ($child in $allProcesses | Where-Object { $_.ParentProcessId -eq $parentId }) {
            $pending.Enqueue([int]$child.ProcessId)
        }
    }

    for ($index = $ids.Count - 1; $index -ge 0; $index--) {
        Stop-Process -Id $ids[$index] -Force -ErrorAction SilentlyContinue
    }
}

function Restore-PreviousCaches {
    foreach ($cachePath in $cachePaths) {
        if ($originalCachePaths.ContainsKey($cachePath)) {
            Copy-Item -LiteralPath $originalCachePaths[$cachePath] -Destination $cachePath -Force
        }
        elseif (Test-Path -LiteralPath $cachePath) {
            Remove-Item -LiteralPath $cachePath -Force
        }
    }
}

try {
    Write-Host "Deadlimit Manager CSDK Fast Startup Cache Repair" -ForegroundColor Cyan
    Write-Host ""

    $configuredRoot = $CsdkRootOverride
    if ([string]::IsNullOrWhiteSpace($configuredRoot)) {
        if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
            throw "Deadlimit Manager settings were not found at '$settingsPath'. Open Deadlimit Manager Settings, configure Reduced CSDK12, and press SAVE first."
        }

        $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
        $configuredRoot = [string]$settings.CsdkRoot
        if ([string]::IsNullOrWhiteSpace($configuredRoot)) {
            throw "Reduced CSDK12 is not configured. Open Deadlimit Manager Settings, select its folder, and press SAVE first."
        }
    }

    $csdkRoot = [IO.Path]::GetFullPath($configuredRoot.Trim()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $deadlockExe = Join-Path $csdkRoot "game\bin\win64\deadlock.exe"
    $gameInfoPath = Join-Path $csdkRoot "game\citadel\gameinfo.gi"
    $cacheFolder = Join-Path $csdkRoot "game\citadel\addons\luaunlocker"
    $consoleLogPath = Join-Path $csdkRoot "game\citadel\console.log"
    $readonlyCachePath = Join-Path $cacheFolder "readonly_tools_asset_info.bin"
    $writableCachePath = Join-Path $cacheFolder "tools_asset_info.bin"
    $cachePaths = @($readonlyCachePath, $writableCachePath)

    if (-not (Test-Path -LiteralPath $deadlockExe -PathType Leaf)) {
        throw "CSDK executable was not found: '$deadlockExe'. Check the Reduced CSDK12 path in Deadlimit Manager Settings."
    }
    if (-not (Test-Path -LiteralPath $gameInfoPath -PathType Leaf)) {
        throw "CSDK gameinfo.gi was not found: '$gameInfoPath'. The Reduced CSDK12 installation is incomplete."
    }
    if (-not (Test-Path -LiteralPath $cacheFolder -PathType Container)) {
        throw "CSDK luaunlocker folder was not found: '$cacheFolder'. The Reduced CSDK12 installation is incomplete."
    }

    $rootPrefix = $csdkRoot + [IO.Path]::DirectorySeparatorChar
    $runningCsdk = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
        $executablePath = [string]$_.ExecutablePath
        -not [string]::IsNullOrWhiteSpace($executablePath) -and
            $executablePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -and
            $_.Name -in @("deadlock.exe", "vconsole2.exe", "csdkcfg.exe")
    })
    if ($runningCsdk.Count -gt 0) {
        $processNames = ($runningCsdk | ForEach-Object { "$($_.Name) (PID $($_.ProcessId))" }) -join ", "
        throw "Close every CSDK12 window and run this tool again. Running: $processNames"
    }

    $backupRoot = Join-Path ([IO.Path]::GetTempPath()) ("DeadlimitManager-CsdkAssetCache-" + [Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $backupRoot | Out-Null
    foreach ($cachePath in $cachePaths) {
        if (Test-Path -LiteralPath $cachePath -PathType Leaf) {
            $backupPath = Join-Path $backupRoot ([IO.Path]::GetFileName($cachePath))
            Copy-Item -LiteralPath $cachePath -Destination $backupPath
            $originalCachePaths[$cachePath] = $backupPath
        }
    }

    $arguments = @(
        "-game", "citadel",
        "-allowmultiple",
        "-insecure",
        "-condebug",
        "-toconsole",
        "-clientonly",
        "-danger_mode_ignore_schema_mismatches",
        "-tools",
        "-multiple_tools_instances",
        "-vconsole",
        "-savereadonlyassets"
    )

    Write-Host "CSDK root: $csdkRoot"
    Write-Host "Generating the full read-only asset cache. The first pass may take several minutes..."
    $startedAt = [DateTime]::UtcNow
    $startedAtLocal = $startedAt.ToLocalTime()
    $generatorProcess = Start-Process `
        -FilePath $deadlockExe `
        -ArgumentList $arguments `
        -WorkingDirectory ([IO.Path]::GetDirectoryName($deadlockExe)) `
        -PassThru

    $deadline = [DateTime]::UtcNow.AddMinutes(30)
    $initialized = $false
    $lastProgressLine = ""

    while ([DateTime]::UtcNow -lt $deadline) {
        $generatorProcess.Refresh()
        if ($generatorProcess.HasExited) {
            throw "CSDK exited before the asset cache was generated. Exit code: $($generatorProcess.ExitCode)."
        }

        if (Test-Path -LiteralPath $consoleLogPath -PathType Leaf) {
            $tailLines = @(Get-Content -LiteralPath $consoleLogPath -Tail 2000 -ErrorAction SilentlyContinue)
            $currentRunLines = @($tailLines | Where-Object {
                if ($_ -notmatch '^(\d{2}/\d{2}) (\d{2}:\d{2}:\d{2})') {
                    return $false
                }

                $lineTimestamp = [DateTime]::ParseExact(
                    "$($startedAtLocal.Year)/$($matches[1]) $($matches[2])",
                    "yyyy/MM/dd HH:mm:ss",
                    [Globalization.CultureInfo]::InvariantCulture)
                return $lineTimestamp -ge $startedAtLocal.AddSeconds(-2)
            })
            $currentRunLog = $currentRunLines -join [Environment]::NewLine

            if ($currentRunLog -match "AssetSystem initialized in") {
                $initialized = $true
            }

            $progressMatches = @([regex]::Matches($currentRunLog, "Asset System - Updating dependencies:.*"))
            if ($progressMatches.Count -gt 0) {
                $progressLine = $progressMatches[-1].Value.Trim()
                if ($progressLine -ne $lastProgressLine) {
                    Write-Host $progressLine
                    $lastProgressLine = $progressLine
                }
            }
        }

        $cacheReady = (Test-Path -LiteralPath $readonlyCachePath -PathType Leaf) -and
            ((Get-Item -LiteralPath $readonlyCachePath).Length -gt 1MB) -and
            ((Get-Item -LiteralPath $readonlyCachePath).LastWriteTimeUtc -ge $startedAt.AddSeconds(-2))

        if ($initialized -and $cacheReady) {
            break
        }

        Start-Sleep -Seconds 1
    }

    $cacheInfo = if (Test-Path -LiteralPath $readonlyCachePath -PathType Leaf) {
        Get-Item -LiteralPath $readonlyCachePath
    }
    else {
        $null
    }
    if (-not $initialized -or $null -eq $cacheInfo -or $cacheInfo.Length -le 1MB) {
        throw "Timed out before CSDK produced a valid read-only asset cache."
    }

    Stop-GeneratedProcessTree -RootProcessId $generatorProcess.Id
    $generatorProcess = $null

    $elapsed = [DateTime]::UtcNow - $startedAt
    Write-Host ""
    Write-Host "SUCCESS" -ForegroundColor Green
    Write-Host ("Cache: {0}" -f $readonlyCachePath)
    Write-Host ("Size: {0:N1} MB" -f ($cacheInfo.Length / 1MB))
    Write-Host ("Generation time: {0:mm\:ss}" -f $elapsed)
    Write-Host "Future CSDK starts should be significantly faster."
    exit 0
}
catch {
    if ($null -ne $generatorProcess) {
        Stop-GeneratedProcessTree -RootProcessId $generatorProcess.Id
        Start-Sleep -Milliseconds 500
    }

    if ($cachePaths.Count -gt 0 -and $null -ne $backupRoot) {
        Restore-PreviousCaches
    }

    Write-Host ""
    Write-Host "ERROR" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
finally {
    if ($null -ne $backupRoot -and (Test-Path -LiteralPath $backupRoot)) {
        Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
