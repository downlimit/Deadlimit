$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$agentsPath = Join-Path $repoRoot 'AGENTS.md'
$guidelinesPath = Join-Path $repoRoot 'internal\docs\UI_GUIDELINES.md'
$feedbackPath = Join-Path $repoRoot 'internal\src\Deadlimit\App\SettingsFeedbackFeature.cs'
$factoryPath = Join-Path $repoRoot 'internal\src\Deadlimit\App\SettingsUiFactory.cs'
$mainFormPath = Join-Path $repoRoot 'internal\src\Deadlimit\App\MainForm.cs'

foreach ($path in @($agentsPath, $guidelinesPath, $feedbackPath, $factoryPath, $mainFormPath)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required UI contract file is missing: $path"
    }
}

$agents = Get-Content -LiteralPath $agentsPath -Raw
foreach ($required in @('internal/docs/UI_GUIDELINES.md', 'SettingsUiFactory', 'RichToolTip', 'ShowDialog(owner)', 'ShowInTaskbar = false')) {
    if (-not $agents.Contains($required)) {
        throw "AGENTS.md is missing required UI guidance token: $required"
    }
}

$guidelines = Get-Content -LiteralPath $guidelinesPath -Raw
if (-not $guidelines.Contains('must end with an ellipsis (`…`)')) {
    throw 'UI guidelines must document the dialog-action ellipsis convention.'
}

$mainForm = Get-Content -LiteralPath $mainFormPath -Raw
if (-not $mainForm.Contains('UiText.T("EXTRACT HERO SOURCE…", "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ…")')) {
    throw 'EXTRACT HERO SOURCE must keep the dialog-action ellipsis in both locales.'
}

$feedback = Get-Content -LiteralPath $feedbackPath -Raw
if (-not $feedback.Contains('SettingsUiFactory.CreateActionButton()')) {
    throw 'Feedback Settings action must use the shared Settings action-button factory.'
}
if (-not $feedback.Contains('new RichToolTip()')) {
    throw 'Feedback Settings tooltip must use RichToolTip.'
}
if ($feedback -match 'new\s+Button\b') {
    throw 'Feedback Settings reintroduced a one-off Button.'
}
if ($feedback -match 'new\s+ToolTip\b') {
    throw 'Feedback Settings reintroduced a native ToolTip.'
}

$factory = Get-Content -LiteralPath $factoryPath -Raw
foreach ($required in @('AutoSize = true', 'Anchor = AnchorStyles.Left', 'Margin = new Padding(0, 4, 0, 4)')) {
    if (-not $factory.Contains($required)) {
        throw "Settings action-button factory lost expected shared style token: $required"
    }
}
foreach ($forbidden in @('Width = 94', 'Height = 26')) {
    if ($factory.Contains($forbidden)) {
        throw "Settings action-button factory reintroduced the old fixed-size style: $forbidden"
    }
}

Write-Host 'UI agent contract smoke passed.'
