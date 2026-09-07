$ErrorActionPreference = 'Stop'

$programPath = 'internal/src/Deadlimit/Program.cs'
$featurePath = 'internal/src/Deadlimit/App/WindowShellVisibilityFeature.cs'

if (-not (Test-Path -LiteralPath $featurePath)) {
    throw "Window shell visibility feature is missing: $featurePath"
}

$program = Get-Content -LiteralPath $programPath -Raw
$feature = Get-Content -LiteralPath $featurePath -Raw

$requiredProgramPatterns = @(
    'WindowShellVisibilityFeature.Attach();',
    'ShowInTaskbar = true,',
    'GetLastActivePopup(targetWindow)',
    'IsWindowVisible(popupWindow)',
    'SetForegroundWindow(targetWindow)'
)

foreach ($pattern in $requiredProgramPatterns) {
    if (-not $program.Contains($pattern, [StringComparison]::Ordinal)) {
        throw "Window switching contract is missing from Program.cs: $pattern"
    }
}

$requiredFeaturePatterns = @(
    'Application.Idle += OnApplicationIdle;',
    'Application.OpenForms.Cast<Form>().ToArray()',
    'form is StartupProgressForm',
    'form.ShowInTaskbar = true;'
)

foreach ($pattern in $requiredFeaturePatterns) {
    if (-not $feature.Contains($pattern, [StringComparison]::Ordinal)) {
        throw "Window shell visibility contract is missing: $pattern"
    }
}

if ($feature.Contains('StartupProgressForm.ShowInTaskbar = true', [StringComparison]::Ordinal)) {
    throw 'Startup progress window must remain excluded from the taskbar policy.'
}

Write-Host 'Window shell visibility contract OK.'
