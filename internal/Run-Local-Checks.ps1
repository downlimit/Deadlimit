# One documented Windows check entry point; run from any working directory.
[CmdletBinding()]
param(
    [ValidateSet('Fast', 'Full')][string]$Scope = 'Fast',
    [string]$LogDirectory = (Join-Path ([System.IO.Path]::GetTempPath()) 'Deadlimit-local-checks')
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exitCode = 0
$transcriptStarted = $false
$step = ''
try {
    New-Item -ItemType Directory -Path $LogDirectory -Force | Out-Null
    $log = Join-Path $LogDirectory ("checks-{0}-{1}.log" -f $Scope.ToLowerInvariant(), (Get-Date -Format 'yyyyMMdd-HHmmss'))
    Start-Transcript -Path $log -Force | Out-Null
    $transcriptStarted = $true
    Write-Host "Deadlimit local checks: Scope=$Scope Log=$log"
    if ([System.Environment]::OSVersion.Platform -ne [System.PlatformID]::Win32NT) {
        throw 'Native Deadlimit Manager checks require Windows. Do not report a non-Windows run as full validation.'
    }
    $shell = if (Get-Command pwsh -ErrorAction SilentlyContinue) { (Get-Command pwsh).Source } else { (Get-Command powershell.exe -ErrorAction Stop).Source }
    Push-Location -LiteralPath $root
    try {
        $basic = @(
            'ci-policy-smoke.ps1',
            'open-source-content-policy-smoke.ps1',
            'path-defaults-smoke.ps1',
            'installer-updater-smoke.ps1',
            'critical-stabilization-smoke.ps1',
            'ui-agent-contract-smoke.ps1'
        )
        foreach ($name in $basic) {
            $step = $name
            Write-Host ">>> $step"
            & $shell -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "internal/tests/$name")
            if ($LASTEXITCODE -ne 0) { throw "$step failed with exit code $LASTEXITCODE." }
        }
        if ($Scope -eq 'Full') {
            foreach ($command in @(@('restore', 'internal/src/Deadlimit/Deadlimit.csproj'), @('build', 'internal/src/Deadlimit/Deadlimit.csproj', '--configuration', 'Release', '--no-restore'))) {
                $step = 'dotnet ' + ($command -join ' ')
                Write-Host ">>> $step"
                & dotnet @command
                if ($LASTEXITCODE -ne 0) { throw "$step failed with exit code $LASTEXITCODE." }
            }
            $extra = @(
                'metal-material-preset-smoke.ps1',
                'texture-naming-alias-smoke.ps1',
                'prepare-behavior-smoke.ps1',
                'gltf-authoring-pipeline-smoke.ps1',
                'ui-localization-smoke.ps1',
                'window-shell-visibility-smoke.ps1',
                'updater-dirty-worktree-smoke.ps1',
                'hero-texture-dependency-contract-smoke.ps1',
                'hero-ability-extraction-contract-smoke.ps1',
                'hero-ui-extraction-contract-smoke.ps1',
                'csdk-editing-copy-smoke.ps1',
                'ability-material-source-staging-smoke.ps1',
                'retail-texture-override-resolution-smoke.ps1',
                'prepare-retail-material-fallback-smoke.ps1',
                'retail-texture-packaging-reuse-smoke.ps1',
                'launch-game-fastpath-smoke.ps1',
                'retail-mod-loading-searchpaths-smoke.ps1'
            )
            foreach ($name in $extra) {
                $step = $name
                Write-Host ">>> $step"
                & $shell -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "internal/tests/$name")
                if ($LASTEXITCODE -ne 0) { throw "$step failed with exit code $LASTEXITCODE." }
            }
            $step = 'Manager startup smoke'
            Write-Host ">>> $step"
            & dotnet run --project internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-build -- --startup-smoke
            if ($LASTEXITCODE -ne 0) { throw "$step failed with exit code $LASTEXITCODE." }
        }
        Write-Host "PASS: $Scope local checks. Log: $log"
    }
    finally { Pop-Location }
}
catch {
    $exitCode = 1
    Write-Host "FAIL: $step $($_.Exception.Message)" -ForegroundColor Red
}
finally {
    if ($transcriptStarted) { Stop-Transcript | Out-Null }
}
exit $exitCode
