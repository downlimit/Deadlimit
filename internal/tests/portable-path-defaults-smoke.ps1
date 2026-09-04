$ErrorActionPreference = 'Stop'

$pathsSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/DeadlimitPaths.cs' -Raw
$forbiddenFragments = @(
    'C:' + '\WorkProjects\Deadlock',
    'D:' + '\Program Files (x86)\Steam'
)

foreach ($fragment in $forbiddenFragments) {
    if ($pathsSource.Contains($fragment, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DeadlimitPaths contains a workstation-specific default: $fragment"
    }
}

$requiredFragments = @(
    'ResolveDeadlimitRoot()',
    'ResolveWorkspaceRoot(DefaultDeadlimitRoot)',
    'DefaultRetailDeadlockRoot = ""',
    'Path.Combine(DefaultWorkspaceRoot, "Reduced_CSDK_12")',
    'Path.Combine(DefaultWorkspaceRoot, "DeadlockTools")'
)

foreach ($fragment in $requiredFragments) {
    if (-not $pathsSource.Contains($fragment, [StringComparison]::Ordinal)) {
        throw "Portable default-path contract is missing: $fragment"
    }
}

$retiredEntryPoints = @(
    'DeadlimitAggregator.cmd',
    'DeadlimitAggregatorUpdater.bat',
    'internal/DeadlimitAggregator.cmd',
    'internal/DeadlimitAggregatorLauncher.vbs'
)

foreach ($path in $retiredEntryPoints) {
    if (Test-Path -LiteralPath $path) {
        throw "Retired Aggregator entry point is still tracked: $path"
    }
}

Write-Host 'Portable path defaults and retired entry-point contracts passed.'
