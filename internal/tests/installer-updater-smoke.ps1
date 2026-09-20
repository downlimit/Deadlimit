$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) {
        throw "$Label contract is missing: $Pattern"
    }
}

function Assert-NotContains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -ge 0) {
        throw "$Label contains retired contract text: $Pattern"
    }
}

$installerPath = 'Install-Deadlimit.cmd'
$lines = [IO.File]::ReadAllLines($installerPath)
$marker = [Array]::IndexOf($lines, '# DEADLIMIT_POWERSHELL_INSTALLER')
if ($marker -lt 0 -or $marker -ge $lines.Length - 1) {
    throw 'Installer PowerShell payload marker is missing or empty.'
}

$payload = $lines[($marker + 1)..($lines.Length - 1)] -join [Environment]::NewLine
$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseInput($payload, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) {
    throw "Installer PowerShell payload has parse errors: $($errors -join '; ')"
}

foreach ($required in @(
    'https://github.com/downlimit/Deadlimit.git',
    'Confirm-DependencyInstall',
    'System.Windows.Forms.MessageBox',
    'Find-WinGet',
    'Git.Git',
    'Microsoft.DotNet.SDK.10',
    '--accept-source-agreements',
    '--accept-package-agreements',
    'Find-DotNet10Sdk',
    '--list-sdks',
    "^10\.0\.",
    'DEADLIMIT_INSTALLER_PATH',
    '[IO.Path]::GetFullPath($env:DEADLIMIT_INSTALLER_PATH)',
    '$installerDirectory = Split-Path -Parent $installerPath',
    '$installRoot = Join-Path $installerDirectory ''Deadlimit''',
    "Invoke-Git @('clone'",
    "'--branch','main','--single-branch'",
    'Move or remove that folder manually',
    "'DeadlimitManager.cmd'",
    "'Deadlimit Manager.lnk'",
    "'Deadlimit Updater.lnk'"
)) {
    Assert-Contains $payload $required 'Installer'
}

foreach ($retired in @(
    'releases/tags/latest-main',
    'Deadlimit-win-x64.zip',
    'DeadlimitPortableUpdater',
    'Deadlimit-release.json',
    'packageSha256',
    "'Programs\Deadlimit'"
)) {
    Assert-NotContains $payload $retired 'Installer'
}

Assert-NotContains $payload 'Test-LegacyDeadlimitInstallation' 'Installer'
Assert-NotContains $payload 'pre-git-' 'Installer'
Assert-NotContains $payload 'package-based Deadlimit installation' 'Installer'
Assert-NotContains $payload "'UserData'" 'Installer'

$entry = Get-Content -LiteralPath 'Update Deadlimit.cmd' -Raw
foreach ($required in @(
    'if not exist "%DEADLIMIT_ROOT%\.git"',
    'DeadlimitUpdater.bat',
    'DEADLIMIT_UPDATE_RELAUNCH',
    'DEADLIMIT_UPDATER_DEFAULT_ARGS=-NoLaunch',
    '%* %DEADLIMIT_UPDATER_DEFAULT_ARGS%'
)) {
    Assert-Contains $entry $required 'Git updater'
}
Assert-NotContains $entry 'DeadlimitPortableUpdater' 'Git updater'
Assert-NotContains $entry 'PackagePath' 'Git updater'

$rootLauncher = Get-Content -LiteralPath 'DeadlimitManager.cmd' -Raw
Assert-Contains $rootLauncher 'set "UPDATER=%ROOT%Update Deadlimit.cmd"' 'Updater shortcut routing'

$settingsVersion = Get-Content -LiteralPath 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' -Raw
foreach ($required in @(
    'Arguments = $"-WaitForPid {managerProcessId}"',
    'owner.BeginInvoke',
    'mainForm.BeginInvoke((Action)mainForm.Close)'
)) {
    Assert-Contains $settingsVersion $required 'In-app updater graceful shutdown'
}

$originFeature = Get-Content -LiteralPath 'internal/src/Deadlimit/App/UpdaterLaunchOriginFeature.cs' -Raw
foreach ($required in @(
    'DEADLIMIT_UPDATE_RELAUNCH',
    'ModuleInitializer',
    'EnvironmentVariableTarget.Process'
)) {
    Assert-Contains $originFeature $required 'In-app updater relaunch marker'
}

$updaterWorker = Get-Content -LiteralPath 'internal/DeadlimitUpdater.ps1' -Raw
Assert-Contains $updaterWorker '[int]$WaitForPid = 0' 'Git updater worker'
Assert-Contains $updaterWorker '$managerProcess.WaitForExit(60000)' 'Git updater worker'
Assert-Contains $updaterWorker 'Use UPDATE from Deadlimit Manager Settings so it can close safely and restart automatically.' 'Git updater worker'
Assert-NotContains $updaterWorker 'DeadlimitAggregator' 'Git updater worker'
Assert-NotContains $updaterWorker 'DeadlimitUpdater.NativeWindow' 'Git updater worker'
Assert-NotContains $updaterWorker 'Legacy Deadlimit Manager' 'Git updater worker'
Assert-NotContains $updaterWorker 'Stop-Process -Force' 'Git updater worker'

$workflow = Get-Content -LiteralPath '.github/workflows/build.yml' -Raw
foreach ($retired in @(
    'actions/upload-artifact',
    'Publish latest artist build',
    'latest-main',
    'gh release',
    'Deadlimit-win-x64.zip'
)) {
    Assert-NotContains $workflow $retired 'Build workflow'
}

foreach ($retiredPath in @(
    'internal/DeadlimitPortableUpdater.ps1',
    'internal/release/New-DeadlimitPortable.ps1',
    'internal/tests/portable-release-smoke.ps1',
    'internal/tests/portable-path-defaults-smoke.ps1'
)) {
    if (Test-Path -LiteralPath $retiredPath) {
        throw "Retired portable delivery file is still tracked: $retiredPath"
    }
}

Write-Host 'Clone installer, Git updater, and artifact-free CI contracts passed.'
