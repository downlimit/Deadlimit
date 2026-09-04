$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$bootstrapPath = Join-Path $repoRoot 'Install-Deadlimit.cmd'
$lines = [IO.File]::ReadAllLines($bootstrapPath)
$marker = [Array]::IndexOf($lines, '# DEADLIMIT_POWERSHELL_BOOTSTRAP')
if ($marker -lt 0 -or $marker -ge $lines.Length - 1) {
    throw 'Portable bootstrap payload marker is missing or empty.'
}

$payload = $lines[($marker + 1)..($lines.Length - 1)] -join [Environment]::NewLine
$tokens = $null
$errors = $null
[void][Management.Automation.Language.Parser]::ParseInput($payload, [ref]$tokens, [ref]$errors)
if ($errors.Count -gt 0) {
    throw "Portable bootstrap PowerShell payload has parse errors: $($errors -join '; ')"
}

foreach ($required in @(
    'https://api.github.com/repos/downlimit/Deadlimit/releases?per_page=20',
    'DeadlimitPortableUpdater.ps1',
    'DeadlimitPortableUpdater.ps1.sha256',
    'Get-FileHash',
    'checksum mismatch',
    '$uri.Host -ne ''github.com'''
)) {
    if (-not $payload.Contains($required, [StringComparison]::Ordinal)) {
        throw "Portable bootstrap contract is missing: $required"
    }
}

Write-Host 'Portable single-file bootstrap parse and trust contract smoke passed.'
