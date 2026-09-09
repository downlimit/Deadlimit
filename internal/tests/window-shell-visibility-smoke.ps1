$ErrorActionPreference = 'Stop'

$appDir = 'internal/src/Deadlimit/App'
$programPath = 'internal/src/Deadlimit/Program.cs'
$startupPath = Join-Path $appDir 'StartupProgressForm.cs'
$activationRecoveryPath = Join-Path $appDir 'WindowActivationRecoveryFeature.cs'
$renderingStabilityPath = Join-Path $appDir 'UiRenderingStabilityFeature.cs'
$libraryHotfixPath = Join-Path $appDir 'ProjectLibraryHotfixFeature.cs'
$mainFormPath = Join-Path $appDir 'MainForm.cs'
$saveStatePath = Join-Path $appDir 'ProjectSaveStateFeature.cs'
$headerPath = Join-Path $appDir 'ProjectHeaderFeature.cs'
$steamStatusPath = Join-Path $appDir 'SteamStatusFeature.cs'
$externalChangePath = Join-Path $appDir 'ProjectExternalChangeFeature.cs'

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

if (Test-Path -LiteralPath $activationRecoveryPath) {
    throw 'Forced native Manager activation redraw hook must not return.'
}

$rendering = Get-Content -LiteralPath $renderingStabilityPath -Raw
foreach ($forbidden in @('WmSetRedraw', 'WM_SETREDRAW', 'RedrawWindow(')) {
    if ($rendering.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Top-level redraw suppression/recovery must not return: $forbidden"
    }
}
if (-not $rendering.Contains('Control.FromHandle(wParam) is SettingsForm settingsForm', [StringComparison]::Ordinal)) {
    throw 'Native activation hook must be scoped to Settings compatibility preparation only.'
}
if ($rendering.Contains('Control.FromHandle(wParam) is Form form', [StringComparison]::Ordinal)) {
    throw 'Generic Form activation interception must not return.'
}

$mainForm = Get-Content -LiteralPath $mainFormPath -Raw
if ($mainForm.Contains('Activated +=', [StringComparison]::Ordinal)) {
    throw 'MainForm must not scan or rebuild project state from Activated.'
}

$saveState = Get-Content -LiteralPath $saveStatePath -Raw
if ($saveState.Contains('form.Activated +=', [StringComparison]::Ordinal)) {
    throw 'Project save-state scanning must not run from form activation.'
}

$header = Get-Content -LiteralPath $headerPath -Raw
if ($header.Contains('form.Activated +=', [StringComparison]::Ordinal)) {
    throw 'Project cover loading must not run from form activation.'
}
if (-not $header.Contains('loadedHeaderWriteTimeUtc', [StringComparison]::Ordinal)) {
    throw 'Project cover should be cached by file state instead of re-decoded blindly.'
}

$steamStatus = Get-Content -LiteralPath $steamStatusPath -Raw
if ($steamStatus.Contains('form.Activated +=', [StringComparison]::Ordinal)) {
    throw 'Status metadata reads must not run from form activation.'
}

if (-not (Test-Path -LiteralPath $externalChangePath)) {
    throw 'Filesystem-driven project refresh feature is missing.'
}
$externalChange = Get-Content -LiteralPath $externalChangePath -Raw
foreach ($required in @(
    'FileSystemWatcher',
    'DebounceMilliseconds',
    'RefreshExternalProjectState',
    'ProjectHeaderFeature.Refresh',
    'ProjectSaveStateFeature.Refresh',
    'SteamStatusFeature.Refresh'
)) {
    if (-not $externalChange.Contains($required, [StringComparison]::Ordinal)) {
        throw "Filesystem-driven refresh contract is missing: $required"
    }
}
if (-not $program.Contains('ProjectExternalChangeFeature.Attach(form);', [StringComparison]::Ordinal)) {
    throw 'Manager does not attach the filesystem-driven project refresh feature.'
}

$libraryHotfix = Get-Content -LiteralPath $libraryHotfixPath -Raw
if ($libraryHotfix.Contains('_form.Deactivate +=', [StringComparison]::Ordinal) -or
    $libraryHotfix.Contains('HoldLibraryRefresh()', [StringComparison]::Ordinal) -or
    $libraryHotfix.Contains('_libraryRefreshHeld', [StringComparison]::Ordinal)) {
    throw 'Project library must not remain redraw-frozen while Manager is inactive.'
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

Write-Host 'Dialog ownership, shell visibility, and non-blocking Manager activation contract OK.'
