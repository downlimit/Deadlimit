param(
    [switch]$Full
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$log = Join-Path ([IO.Path]::GetTempPath()) ("Deadlimit-validation-{0:yyyyMMdd-HHmmss}-{1}.log" -f (Get-Date), $PID)
$failed = $false
Push-Location $root
try {
    Start-Transcript -LiteralPath $log -ErrorAction Stop | Out-Null
    try {
        function Invoke-Script([string]$Path) {
            Write-Host "CHECK: $Path"
            $global:LASTEXITCODE = 0
            & (Join-Path $root $Path)
            if ($LASTEXITCODE -ne 0) { throw "$Path exited with $LASTEXITCODE." }
        }
        foreach ($script in @(
            'internal/tests/workflow-cost-policy-smoke.ps1',
            'internal/tests/open-source-content-policy-smoke.ps1',
            'internal/tests/path-defaults-smoke.ps1',
            'internal/tests/installer-updater-smoke.ps1',
            'internal/tests/critical-stabilization-smoke.ps1',
            'internal/tests/ui-agent-contract-smoke.ps1'
        )) {
            Invoke-Script $script
        }
        if ($Full) {
            Write-Host 'CHECK: dotnet restore and Release build (Windows + .NET 10 SDK)'
            & dotnet restore internal/src/Deadlimit/Deadlimit.csproj
            if ($LASTEXITCODE -ne 0) { throw "dotnet restore exited with $LASTEXITCODE." }
            & dotnet build internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-restore
            if ($LASTEXITCODE -ne 0) { throw "dotnet build exited with $LASTEXITCODE." }
            foreach ($script in @(
                'internal/tests/metal-material-preset-smoke.ps1',
                'internal/tests/texture-naming-alias-smoke.ps1',
                'internal/tests/prepare-behavior-smoke.ps1',
                'internal/tests/gltf-authoring-pipeline-smoke.ps1',
                'internal/tests/ui-localization-smoke.ps1',
                'internal/tests/window-shell-visibility-smoke.ps1',
                'internal/tests/updater-dirty-worktree-smoke.ps1',
                'internal/tests/hero-texture-dependency-contract-smoke.ps1',
                'internal/tests/hero-ability-extraction-contract-smoke.ps1',
                'internal/tests/hero-ui-extraction-contract-smoke.ps1',
                'internal/tests/csdk-editing-copy-smoke.ps1',
                'internal/tests/ability-material-source-staging-smoke.ps1',
                'internal/tests/retail-texture-override-resolution-smoke.ps1',
                'internal/tests/prepare-retail-material-fallback-smoke.ps1',
                'internal/tests/retail-texture-packaging-reuse-smoke.ps1',
                'internal/tests/launch-game-fastpath-smoke.ps1',
                'internal/tests/retail-mod-loading-searchpaths-smoke.ps1'
            )) {
                Invoke-Script $script
            }
            Write-Host 'CHECK: startup smoke'
            & dotnet run --project internal/src/Deadlimit/Deadlimit.csproj --configuration Release --no-build -- --startup-smoke
            if ($LASTEXITCODE -ne 0) { throw "Startup smoke exited with $LASTEXITCODE." }
        }
        Write-Host 'Deadlimit local validation: PASS'
    } catch {
        $failed = $true
        Write-Error -ErrorAction Continue "Deadlimit local validation: FAIL - $_"
    } finally {
        Stop-Transcript | Out-Null
    }
} finally {
    Pop-Location
}
Write-Host "Validation log (local only): $log"
if ($failed) { exit 1 }
exit 0
