$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) {
        throw "$Label contract is missing: $Pattern"
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
    'https://api.github.com/repos/downlimit/Deadlimit/releases/tags/latest-main',
    'Deadlimit-win-x64.zip',
    'Deadlimit-win-x64.zip.sha256',
    'internal/DeadlimitPortableUpdater.ps1',
    'Get-FileSha256',
    'checksum mismatch',
    '$uri.Host -ne ''github.com''',
    "'Programs\Deadlimit'",
    'Deadlimit Manager.lnk',
    'Deadlimit Updater.lnk'
)) {
    Assert-Contains $payload $required 'Installer'
}

$entry = Get-Content -LiteralPath 'Update Deadlimit.cmd' -Raw
foreach ($required in @(
    'if exist "%DEADLIMIT_ROOT%\.git"',
    'DeadlimitUpdater.bat',
    'internal\DeadlimitPortableUpdater.ps1',
    '-InstallRoot "%DEADLIMIT_ROOT%"'
)) {
    Assert-Contains $entry $required 'Unified updater'
}

$worker = Get-Content -LiteralPath 'internal/DeadlimitPortableUpdater.ps1' -Raw
Assert-Contains $worker 'https://api.github.com/repos/downlimit/Deadlimit/releases/tags/latest-main' 'Installed updater'

$packager = Get-Content -LiteralPath 'internal/release/New-DeadlimitPortable.ps1' -Raw
Assert-Contains $packager "Join-Path `$outputRoot 'Deadlimit-release.json'" 'Artist package metadata'

$workflow = Get-Content -LiteralPath '.github/workflows/build.yml' -Raw
foreach ($required in @(
    'Publish latest artist build',
    "github.event_name == 'push' && github.ref == 'refs/heads/main'",
    "`$tag = 'latest-main'",
    "`$metadataAsset = 'artifacts/portable/Deadlimit-release.json'",
    'gh release upload $tag $metadataAsset'
)) {
    Assert-Contains $workflow $required 'Continuous artist delivery'
}

Write-Host 'Single installer, package, and unified updater entry contract passed.'
