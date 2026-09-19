$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.RetailResourcePackagingPolicySmoke', $true)
$flags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
$run = $type.GetMethod('Run', $flags)
if ($null -eq $run) { throw 'Retail resource packaging smoke entry point was not found.' }
if ([int]$run.Invoke($null, @()) -ne 0) {
    throw 'Retail resource dependency closure selected the wrong files.'
}

$buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
foreach ($required in @(
    'RetailResourcePackagingPolicy.Resolve(',
    'packagingPlan.ExcludedRelativePaths',
    'Retail/redundant compiled outputs omitted from VPK',
    'Explicit project-root texture overrides forced into authored compilation'
)) {
    if (-not $buildSource.Contains($required, [StringComparison]::Ordinal)) {
        throw "Build & Test retail resource reuse wiring is missing: $required"
    }
}
$packagingSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/RetailResourcePackagingPolicy.cs' -Raw
if (-not $packagingSource.Contains('textureOverridePaths.Contains(sourceRelativePath)', [StringComparison]::Ordinal)) {
    throw 'Project-root texture overrides are not forced into the authored packaging roots.'
}

Write-Host 'Retail resource dependency-closure packaging smoke passed.'
