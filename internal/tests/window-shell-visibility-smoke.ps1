$ErrorActionPreference = 'Stop'

$appDir = 'internal/src/Deadlimit/App'
$programPath = 'internal/src/Deadlimit/Program.cs'
$startupPath = Join-Path $appDir 'StartupProgressForm.cs'

$appFiles = Get-ChildItem -LiteralPath $appDir -Filter '*.cs' -File
foreach ($file in $appFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ($file.Name -ne 'StartupProgressForm.cs' -and
        $text.Contains('ShowInTaskbar = false', [StringComparison]::Ordinal)) {
        throw "Interactive window is shell-hidden before first show: $($file.Name)"
    }
}

$startup = Get-Content -LiteralPath $startupPath -Raw
if (-not $startup.Contains('ShowInTaskbar = false;', [StringComparison]::Ordinal)) {
    throw 'StartupProgressForm must remain the only deliberately shell-hidden window.'
}

$requiredShellVisible = @(
    'MessageBox.cs',
    'SettingsForm.cs',
    'BuildTestSuccessDialog.cs',
    'ProjectCreationChoiceFeature.cs',
    'ProjectLibraryFeature.cs'
)
foreach ($name in $requiredShellVisible) {
    $path = Join-Path $appDir $name
    $text = Get-Content -LiteralPath $path -Raw
    if (-not $text.Contains('ShowInTaskbar = true', [StringComparison]::Ordinal)) {
        throw "User-facing dialog is not shell-visible before first show: $name"
    }
}

$legacyPolicyPath = Join-Path $appDir 'WindowShellVisibilityFeature.cs'
if (Test-Path -LiteralPath $legacyPolicyPath) {
    throw 'Late Application.Idle taskbar mutation must not return.'
}

$program = Get-Content -LiteralPath $programPath -Raw
if ($program.Contains('WindowShellVisibilityFeature.Attach();', [StringComparison]::Ordinal)) {
    throw 'Program still attaches the legacy late taskbar mutation.'
}

$settings = Get-Content -LiteralPath (Join-Path $appDir 'SettingsForm.cs') -Raw
if (-not $settings.Contains('dialog.ShowDialog(this)', [StringComparison]::Ordinal)) {
    throw 'Settings FolderBrowserDialog must have SettingsForm as its owner.'
}

$version = Get-Content -LiteralPath (Join-Path $appDir 'SettingsVersionFeature.cs') -Raw
if ($version.Contains('new FolderBrowserDialog', [StringComparison]::Ordinal) -and
    -not $version.Contains('dialog.ShowDialog(owner)', [StringComparison]::Ordinal)) {
    throw 'Settings relocation FolderBrowserDialog must have an owner.'
}

$creation = Get-Content -LiteralPath (Join-Path $appDir 'ProjectCreationChoiceFeature.cs') -Raw
if ($creation.Contains('new OpenFileDialog', [StringComparison]::Ordinal) -and
    -not $creation.Contains('dialog.ShowDialog(form)', [StringComparison]::Ordinal)) {
    throw 'VPK OpenFileDialog must have MainForm as its owner.'
}

$ownerlessCommonDialogs = @()
foreach ($file in $appFiles) {
    $text = Get-Content -LiteralPath $file.FullName -Raw
    if ($text -match '\b(?:OpenFileDialog|SaveFileDialog|FolderBrowserDialog)\b' -and
        $text -match '\.ShowDialog\(\)') {
        $ownerlessCommonDialogs += $file.Name
    }
}
if ($ownerlessCommonDialogs.Count -gt 0) {
    throw "Ownerless native common dialog(s): $($ownerlessCommonDialogs -join ', ')"
}

$ownerlessCustom = @()
foreach ($file in $appFiles) {
    if ($file.Name -eq 'MessageBox.cs') { continue }
    $matches = Select-String -LiteralPath $file.FullName -Pattern '\.ShowDialog\(\)' -AllMatches
    if ($matches) { $ownerlessCustom += $file.Name }
}
if ($ownerlessCustom.Count -gt 0) {
    throw "Ownerless modal dialog call(s) require review: $($ownerlessCustom -join ', ')"
}

Write-Host 'Dialog ownership and shell-visibility contract OK.'
