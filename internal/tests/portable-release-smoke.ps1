param(
    [string]$PackagePath,
    [string]$ChecksumPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$worker = Join-Path $repoRoot 'internal\DeadlimitPortableUpdater.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-portable-smoke-$([Guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $testRoot 'install\Deadlimit'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function New-SyntheticPackage([string]$Name, [string]$Version, [string]$Payload, [switch]$MissingManager) {
    $source = Join-Path $testRoot "$Name-source"
    $archive = Join-Path $testRoot "$Name.zip"
    $checksum = "$archive.sha256"
    [IO.Directory]::CreateDirectory($source) | Out-Null
    if (-not $MissingManager) {
        [IO.File]::WriteAllText((Join-Path $source 'DeadlimitManager.exe'), $Payload)
    }
    $portableInternal = Join-Path $source 'internal'
    [IO.Directory]::CreateDirectory($portableInternal) | Out-Null
    Copy-Item -LiteralPath $worker -Destination $portableInternal
    Copy-Item -LiteralPath (Join-Path $repoRoot 'internal\release\Update Deadlimit.cmd') -Destination $source
    [IO.File]::WriteAllText(
        (Join-Path $source 'release.json'),
        (@{ product = 'Deadlimit Manager'; version = $Version } | ConvertTo-Json))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($source, $archive)
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksum, "$hash  $([IO.Path]::GetFileName($archive))`n")
    return [pscustomobject]@{ Archive = $archive; Checksum = $checksum }
}

function New-TraversalPackage {
    Add-Type -AssemblyName System.IO.Compression
    $archivePath = Join-Path $testRoot 'traversal.zip'
    $checksumPath = "$archivePath.sha256"
    $stream = [IO.File]::Open($archivePath, [IO.FileMode]::CreateNew)
    try {
        $archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $entry = $archive.CreateEntry('../escaped.txt')
            $writer = [IO.StreamWriter]::new($entry.Open())
            try {
                $writer.Write('must-not-escape')
            }
            finally {
                $writer.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }
    $hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText($checksumPath, "$hash  traversal.zip`n")
    return [pscustomobject]@{ Archive = $archivePath; Checksum = $checksumPath }
}

function Invoke-Updater([object]$Package, [switch]$ExpectFailure, [switch]$Rollback) {
    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', $worker,
        '-InstallRoot', $installRoot,
        '-NoLaunch',
        '-NoShortcuts'
    )
    if ($Rollback) {
        $arguments += '-Rollback'
    }
    else {
        $arguments += @('-PackagePath', $Package.Archive, '-ChecksumPath', $Package.Checksum)
    }

    $output = & powershell.exe @arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($ExpectFailure) {
        Assert-True ($exitCode -ne 0) "Portable updater unexpectedly accepted a broken package.`n$($output -join "`n")"
    }
    else {
        Assert-True ($exitCode -eq 0) "Portable updater failed with exit code $exitCode.`n$($output -join "`n")"
    }
}

function Invoke-InstalledRollback {
    $installedEntry = Join-Path $installRoot 'Update Deadlimit.cmd'
    Assert-True (Test-Path -LiteralPath $installedEntry -PathType Leaf) 'Installed rollback entry point is missing.'
    $output = & $installedEntry -Rollback -NoLaunch -NoShortcuts 2>&1
    Assert-True ($LASTEXITCODE -eq 0) "Installed rollback entry failed with exit code $LASTEXITCODE.`n$($output -join "`n")"
}

try {
    [IO.Directory]::CreateDirectory($testRoot) | Out-Null
    $v1 = New-SyntheticPackage 'v1' '0.1.0-beta.1' 'version-one'
    $v2 = New-SyntheticPackage 'v2' '0.1.0-beta.2' 'version-two'
    $invalid = New-SyntheticPackage 'invalid' '0.1.0-broken' 'broken' -MissingManager
    $traversal = New-TraversalPackage
    $badChecksumPath = Join-Path $testRoot 'bad-checksum.sha256'
    [IO.File]::WriteAllText($badChecksumPath, "$('0' * 64)  v2.zip`n")
    $badChecksum = [pscustomobject]@{ Archive = $v2.Archive; Checksum = $badChecksumPath }

    Invoke-Updater $v1
    Assert-True (Test-Path -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe')) 'First install did not produce DeadlimitManager.exe.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'First install payload is incorrect.'
    Assert-True (-not (Test-Path -LiteralPath "$installRoot.previous")) 'First install unexpectedly created a rollback folder.'

    Invoke-Updater $v1
    Assert-True (-not (Test-Path -LiteralPath "$installRoot.previous")) 'No-op update unexpectedly rotated the current install.'

    Invoke-Updater $invalid -ExpectFailure
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Rejected package changed the current install.'

    Invoke-Updater $badChecksum -ExpectFailure
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Checksum rejection changed the current install.'

    Invoke-Updater $traversal -ExpectFailure
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $testRoot 'escaped.txt'))) 'Traversal package wrote outside the staging root.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Traversal rejection changed the current install.'

    Invoke-Updater $v2
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-two') 'Update did not activate version two.'
    Assert-True ((Get-Content -LiteralPath (Join-Path "$installRoot.previous" 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Update did not preserve version one for rollback.'

    Invoke-InstalledRollback
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Rollback did not restore version one.'
    Assert-True ((Get-Content -LiteralPath (Join-Path "$installRoot.previous" 'DeadlimitManager.exe') -Raw) -eq 'version-two') 'Rollback did not retain version two as the alternate install.'

    if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
        Assert-True (-not [string]::IsNullOrWhiteSpace($ChecksumPath)) '-ChecksumPath is required when validating a real portable package.'
        $realInstall = Join-Path $testRoot 'real-install\Deadlimit'
        $realArguments = @(
            '-NoLogo', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $worker,
            '-InstallRoot', $realInstall, '-NoLaunch', '-NoShortcuts',
            '-PackagePath', ([IO.Path]::GetFullPath($PackagePath)),
            '-ChecksumPath', ([IO.Path]::GetFullPath($ChecksumPath))
        )
        $realOutput = & powershell.exe @realArguments 2>&1
        Assert-True ($LASTEXITCODE -eq 0) "Real portable package install failed.`n$($realOutput -join "`n")"
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'DeadlimitManager.exe')) 'Real portable package has no Manager executable after install.'
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'release-manifest.json')) 'Real portable package has no release manifest.'
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'licenses') -PathType Container) 'Real portable package has no license payload.'
    }

    Write-Host 'Portable release install, no-op, checksum/traversal/broken-package rejection, update, and rollback smoke passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
