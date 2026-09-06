$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$updaterWorkerSource = Join-Path $repositoryRoot 'internal\DeadlimitUpdater.ps1'
$updaterBootstrapSource = Join-Path $repositoryRoot 'DeadlimitUpdater.bat'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-updater-dirty-' + [Guid]::NewGuid().ToString('N'))
$remote = Join-Path $testRoot 'remote.git'
$seed = Join-Path $testRoot 'seed'
$work = Join-Path $testRoot 'work'

function Run-Git([string]$workingDirectory, [Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments) {
    & git.exe -C $workingDirectory @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git failed in '$workingDirectory': $($Arguments -join ' ')"
    }
}

function Run-Updater([string]$workingDirectory) {
    $bootstrap = Join-Path $workingDirectory 'DeadlimitUpdater.bat'
    $output = & cmd.exe /d /c "`"$bootstrap`"" 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host $line
    }
    return $exitCode
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    & git.exe init --bare $remote
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize updater smoke remote.' }

    & git.exe init -b main $seed
    if ($LASTEXITCODE -ne 0) { throw 'Could not initialize updater smoke seed.' }
    Run-Git $seed config user.email 'deadlimit-ci@example.invalid'
    Run-Git $seed config user.name 'Deadlimit CI'

    New-Item -ItemType Directory -Path (Join-Path $seed 'internal') | Out-Null
    Copy-Item -LiteralPath $updaterWorkerSource -Destination (Join-Path $seed 'internal\DeadlimitUpdater.ps1')
    Copy-Item -LiteralPath $updaterBootstrapSource -Destination (Join-Path $seed 'DeadlimitUpdater.bat')
    Set-Content -LiteralPath (Join-Path $seed 'DeadlimitManager.cmd') -Value "@echo off`r`nexit /b 0`r`n" -Encoding ascii
    $managerBin = Join-Path $seed 'internal\src\Deadlimit\bin\Release\net10.0-windows'
    New-Item -ItemType Directory -Path $managerBin -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\notepad.exe') -Destination (Join-Path $managerBin 'DeadlimitManager.exe')
    Set-Content -LiteralPath (Join-Path $seed 'local.txt') -Value 'base local' -Encoding ascii
    Set-Content -LiteralPath (Join-Path $seed 'incoming.txt') -Value 'base incoming' -Encoding ascii
    Run-Git $seed add .
    Run-Git $seed commit -m 'seed'
    Run-Git $seed remote add origin $remote
    Run-Git $seed push -u origin main

    & git.exe clone --branch main $remote $work
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone updater smoke worktree.' }

    # Simulate the exact failure mode this bootstrap is meant to prevent: the
    # checked-out updater worker is stale/broken while origin/main has a valid one.
    $staleWorker = "Write-Host 'STALE LOCAL UPDATER MUST NOT RUN'`r`nexit 91`r`n"
    Set-Content -LiteralPath (Join-Path $work 'internal\DeadlimitUpdater.ps1') -Value $staleWorker -Encoding ascii
    Set-Content -LiteralPath (Join-Path $work 'local.txt') -Value 'parallel local edit' -Encoding ascii

    Set-Content -LiteralPath (Join-Path $seed 'incoming.txt') -Value 'remote unrelated update' -Encoding ascii
    Run-Git $seed add incoming.txt
    Run-Git $seed commit -m 'unrelated remote update'
    Run-Git $seed push origin main

    # The in-app update case starts while DeadlimitManager.exe is still running.
    # The bootstrap must therefore force the worker into no-wait mode and restart
    # the freshly refreshed Manager after a successful update.
    $managerExe = Join-Path $work 'internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.exe'
    $initialManager = Start-Process -FilePath $managerExe -PassThru
    Start-Sleep -Milliseconds 400
    if ($initialManager.HasExited) {
        throw 'Updater smoke could not start the simulated Deadlimit Manager process.'
    }

    $exitCode = Run-Updater $work
    if ($exitCode -ne 0) {
        throw "Bootstrapped updater rejected unrelated local tracked changes with exit code $exitCode."
    }

    Start-Sleep -Milliseconds 500
    $restartedManagers = @(Get-Process -Name DeadlimitManager -ErrorAction SilentlyContinue)
    if ($restartedManagers.Count -eq 0) {
        throw 'Updater did not relaunch Deadlimit Manager after updating while Manager was running.'
    }
    foreach ($process in $restartedManagers) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }

    $localContent = (Get-Content -LiteralPath (Join-Path $work 'local.txt') -Raw).Trim()
    if ($localContent -ne 'parallel local edit') {
        throw 'Updater did not preserve the unrelated local tracked edit.'
    }

    $staleWorkerAfterUpdate = (Get-Content -LiteralPath (Join-Path $work 'internal\DeadlimitUpdater.ps1') -Raw).Trim()
    if ($staleWorkerAfterUpdate -notmatch 'STALE LOCAL UPDATER MUST NOT RUN') {
        throw 'Bootstrap unexpectedly replaced the local stale worker instead of executing the fresh origin/main copy from TEMP.'
    }

    $workHead = (& git.exe -C $work rev-parse HEAD).Trim()
    $remoteHead = (& git.exe -C $seed rev-parse HEAD).Trim()
    if ($workHead -ne $remoteHead) {
        throw 'Updater did not fast-forward when local tracked edits were unrelated.'
    }

    Set-Content -LiteralPath (Join-Path $seed 'local.txt') -Value 'remote conflicting update' -Encoding ascii
    Run-Git $seed add local.txt
    Run-Git $seed commit -m 'overlapping remote update'
    Run-Git $seed push origin main

    $headBeforeOverlap = (& git.exe -C $work rev-parse HEAD).Trim()
    $bootstrap = Join-Path $work 'DeadlimitUpdater.bat'
    $overlapOutput = & cmd.exe /d /c "`"$bootstrap`" -NoWait" 2>&1
    if ($LASTEXITCODE -eq 0) {
        throw 'Updater accepted an incoming update that overlaps local tracked work.'
    }
    if (-not ($overlapOutput -match 'origin/main also changes files you are editing locally')) {
        throw 'Updater overlap failure did not explain the exact reason.'
    }
    if (-not ($overlapOutput -match 'local.txt')) {
        throw 'Updater overlap failure did not list the conflicting file.'
    }

    $headAfterOverlap = (& git.exe -C $work rev-parse HEAD).Trim()
    if ($headBeforeOverlap -ne $headAfterOverlap) {
        throw 'Updater moved HEAD despite an overlapping local tracked edit.'
    }
    $localContentAfterOverlap = (Get-Content -LiteralPath (Join-Path $work 'local.txt') -Raw).Trim()
    if ($localContentAfterOverlap -ne 'parallel local edit') {
        throw 'Updater modified local work during overlap rejection.'
    }

    Write-Host 'Updater self-bootstrap, Manager relaunch, and dirty-worktree smoke passed.'
}
finally {
    Get-Process -Name DeadlimitManager -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testRoot -Recurse -Force -ErrorAction SilentlyContinue
}
