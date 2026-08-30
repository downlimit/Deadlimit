param(
    [switch]$ResolveRootOnly
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Wait-ForAnyKey {
    Write-Host ""
    Write-Host "Press any key to close . . ."

    try {
        if (-not [Console]::IsInputRedirected) {
            [void][Console]::ReadKey($true)
            return
        }
    }
    catch {
        # Fall back to line input when no interactive console is available.
    }

    [void](Read-Host "Press Enter to close")
}

try {
    # Resolve the checkout from this tracked worker instead of accepting a path
    # from cmd.exe. A quoted batch argument ending in '\' can reach PowerShell
    # with a stray quote and make GetFullPath reject an otherwise valid path.
    $rootPath = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
    $rootPath = $rootPath.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

    if ($ResolveRootOnly) {
        Write-Output $rootPath
        exit 0
    }

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

    $oldHead = (& $git.Source -C $rootPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($oldHead)) {
        throw "Could not read the current Deadlimit revision."
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

    $newHead = (& $git.Source -C $rootPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($newHead)) {
        throw "Could not read the updated Deadlimit revision."
    }

    $launcher = Join-Path $rootPath "DeadlimitManager.cmd"
    if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) {
        throw "DeadlimitManager.cmd was not found after update."
    }

    # Keep the Manager executable and the two root shortcuts current, but do not
    # launch the Manager. The updater is a repository maintenance action only.
    & $env:ComSpec /d /c "`"$launcher`" --refresh-only"
    if ($LASTEXITCODE -ne 0) {
        throw "Deadlimit Manager refresh failed with exit code $LASTEXITCODE."
    }

    Write-Host ""
    if ([string]::Equals($oldHead, $newHead, [StringComparison]::OrdinalIgnoreCase)) {
        Write-Host "Deadlimit is already up to date." -ForegroundColor Green
    }
    else {
        Write-Host "Updated Deadlimit:" -ForegroundColor Green
        $commitLines = @(& $git.Source -C $rootPath log --reverse --pretty=format:"%h`t%s" "$oldHead..$newHead")
        if ($LASTEXITCODE -ne 0) {
            throw "Could not build the update summary."
        }

        foreach ($line in $commitLines) {
            if (-not [string]::IsNullOrWhiteSpace($line)) {
                Write-Host "  $line"
            }
        }

        Write-Host ""
        Write-Host "Changed files:"
        & $git.Source -C $rootPath diff --stat $oldHead $newHead
        if ($LASTEXITCODE -ne 0) {
            throw "Could not build the changed-files summary."
        }
    }

    Write-Host ""
    Write-Host "Update complete."
    Wait-ForAnyKey
    exit 0
}
catch {
    Write-Host ""
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Wait-ForAnyKey
    exit 1
}
