param(
    [Parameter(Mandatory = $true)]
    [string]$Root
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

try {
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    $git = Get-Command git.exe -ErrorAction SilentlyContinue
    if ($null -eq $git) {
        throw "Git was not found. Deadlimit Updater cannot update this checkout."
    }

    & $git.Source -C $rootPath rev-parse --is-inside-work-tree *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "This folder is not a Git checkout: $rootPath"
    }

    $currentBranch = (& $git.Source -C $rootPath rev-parse --abbrev-ref HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $currentBranch -ne "main") {
        throw "Deadlimit Updater requires the local checkout to be on branch 'main'. Current branch: '$currentBranch'."
    }

    $trackedChanges = @(& $git.Source -C $rootPath status --porcelain --untracked-files=no)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect the local Git working tree."
    }
    if ($trackedChanges.Count -gt 0) {
        throw "The local checkout has tracked changes. Commit or revert them before running Deadlimit Updater."
    }

    # The Manager executable can otherwise stay locked while the updater rebuilds it.
    Get-Process -Name DeadlimitManager, DeadlimitAggregator, Deadlimit -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue

    Write-Host "Updating the Deadlimit repository from origin/main..."
    & $git.Source -C $rootPath fetch origin main
    if ($LASTEXITCODE -ne 0) {
        throw "Could not fetch origin/main."
    }

    & $git.Source -C $rootPath merge --ff-only origin/main
    if ($LASTEXITCODE -ne 0) {
        throw "The local checkout cannot be fast-forwarded safely. Resolve local Git state before running Deadlimit Updater again."
    }

    $launcher = Join-Path $rootPath "DeadlimitManager.cmd"
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw "DeadlimitManager.cmd was not found after update."
    }

    & $env:ComSpec /d /c "`"$launcher`" --refresh-only"
    if ($LASTEXITCODE -ne 0) {
        throw "Deadlimit Manager refresh failed with exit code $LASTEXITCODE."
    }

    $app = Join-Path $rootPath "internal\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.exe"
    if (Test-Path -LiteralPath $app -PathType Leaf) {
        Start-Process -FilePath $app -WorkingDirectory $rootPath
    }

    exit 0
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Read-Host "Press Enter to close"
    exit 1
}
