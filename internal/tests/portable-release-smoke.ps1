param(
    [string]$PackagePath,
    [string]$ChecksumPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$worker = Join-Path $repoRoot 'internal\DeadlimitPortableUpdater.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit portable smoke $([Guid]::NewGuid().ToString('N'))"
$installRoot = Join-Path $testRoot 'install\Deadlimit'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Invoke-ExecutableSmoke([string]$Executable, [string]$Argument, [string]$WorkingDirectory) {
    $process = Start-Process `
        -FilePath $Executable `
        -ArgumentList $Argument `
        -WorkingDirectory $WorkingDirectory `
        -PassThru
    if (-not $process.WaitForExit(30000)) {
        $process.Kill($true)
        throw "Packaged executable timed out: $Argument"
    }
    Assert-True ($process.ExitCode -eq 0) "Packaged executable smoke failed with exit code $($process.ExitCode): $Argument"
}

function Get-StreamSha256([IO.Stream]$Stream) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($Stream))).Replace('-', '') }
    finally { $sha.Dispose() }
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
        '-NoLaunch'
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
    $output = & $installedEntry -Rollback -NoLaunch 2>&1
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
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'Backup'))) 'First install unexpectedly created a rollback folder.'

    $userData = Join-Path $installRoot 'UserData'
    [IO.Directory]::CreateDirectory($userData) | Out-Null
    [IO.File]::WriteAllText((Join-Path $userData 'settings.json'), 'artist-settings')

    Invoke-Updater $v1
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'Backup'))) 'No-op update unexpectedly rotated the current install.'

    Invoke-Updater $invalid -ExpectFailure
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Rejected package changed the current install.'

    Invoke-Updater $badChecksum -ExpectFailure
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Checksum rejection changed the current install.'

    Invoke-Updater $traversal -ExpectFailure
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $testRoot 'escaped.txt'))) 'Traversal package wrote outside the staging root.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Traversal rejection changed the current install.'

    Invoke-Updater $v2
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-two') 'Update did not activate version two.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'Backup\DeadlimitManager.exe') -Raw) -eq 'version-one') 'Update did not preserve version one for rollback.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $userData 'settings.json') -Raw) -eq 'artist-settings') 'Update did not preserve portable UserData.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $installRoot 'Backup\UserData'))) 'Rollback payload duplicated portable UserData.'

    Invoke-InstalledRollback
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'DeadlimitManager.exe') -Raw) -eq 'version-one') 'Rollback did not restore version one.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $installRoot 'Backup\DeadlimitManager.exe') -Raw) -eq 'version-two') 'Rollback did not retain version two as the alternate install.'
    Assert-True ((Get-Content -LiteralPath (Join-Path $userData 'settings.json') -Raw) -eq 'artist-settings') 'Rollback did not preserve portable UserData.'

    if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
        Assert-True (-not [string]::IsNullOrWhiteSpace($ChecksumPath)) '-ChecksumPath is required when validating a real portable package.'
        $realInstall = Join-Path $testRoot 'real-install\Deadlimit'
        $realArchive = [IO.Path]::GetFullPath($PackagePath)
        $realChecksum = [IO.Path]::GetFullPath($ChecksumPath)
        $expectedHash = ([regex]::Match([IO.File]::ReadAllText($realChecksum), '(?i)[0-9a-f]{64}')).Value
        $actualHash = (Get-FileHash -LiteralPath $realArchive -Algorithm SHA256).Hash
        Assert-True ([string]::Equals($actualHash, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) 'Real portable package checksum does not match.'

        Add-Type -AssemblyName System.IO.Compression
        $archive = [IO.Compression.ZipFile]::OpenRead($realArchive)
        try {
            $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
            $entryLookup = @{}
            foreach ($entry in $fileEntries) { $entryLookup[$entry.FullName.Replace('\\', '/')] = $entry }
            $manifestEntry = $entryLookup['release-manifest.json']
            Assert-True ($null -ne $manifestEntry) 'Real portable package has no release manifest entry.'
            $reader = [IO.StreamReader]::new($manifestEntry.Open())
            try { $manifest = @($reader.ReadToEnd() | ConvertFrom-Json) }
            finally { $reader.Dispose() }
            Assert-True ($fileEntries.Count -eq $manifest.Count + 1) 'Real portable package contains undeclared files.'
            foreach ($item in $manifest) {
                $entry = $entryLookup[[string]$item.path]
                Assert-True ($null -ne $entry) "Manifest path is missing from the real package: $($item.path)"
                Assert-True ($entry.Length -eq [long]$item.bytes) "Manifest byte count differs: $($item.path)"
                $entryStream = $entry.Open()
                try { $entryHash = Get-StreamSha256 $entryStream }
                finally { $entryStream.Dispose() }
                Assert-True ([string]::Equals($entryHash, [string]$item.sha256, [StringComparison]::OrdinalIgnoreCase)) "Manifest SHA-256 differs: $($item.path)"
            }
            foreach ($forbiddenPath in @('UserData', 'Backup', 'Backup.next', 'Install-Deadlimit.cmd')) {
                Assert-True (-not $entryLookup.ContainsKey($forbiddenPath)) "Fresh portable package contains forbidden state/bootstrap path: $forbiddenPath"
            }
        }
        finally {
            $archive.Dispose()
        }

        Expand-Archive -LiteralPath $realArchive -DestinationPath $realInstall
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'DeadlimitManager.exe')) 'Extracted portable package has no Manager executable.'
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'release-manifest.json')) 'Real portable package has no release manifest.'
        Assert-True (Test-Path -LiteralPath (Join-Path $realInstall 'licenses') -PathType Container) 'Real portable package has no license payload.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $realInstall 'UserData'))) 'Fresh portable ZIP unexpectedly contains user state.'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $realInstall 'Backup'))) 'Fresh portable ZIP unexpectedly contains a rollback payload.'
        $realManager = Join-Path $realInstall 'DeadlimitManager.exe'
        Invoke-ExecutableSmoke $realManager '--release-policy-smoke' $realInstall
        Invoke-ExecutableSmoke $realManager '--startup-smoke' $realInstall

        $realUserData = Join-Path $realInstall 'UserData'
        [IO.Directory]::CreateDirectory($realUserData) | Out-Null
        [IO.File]::WriteAllText((Join-Path $realUserData 'portable-state.txt'), 'preserve-me')
        $realUpdater = Join-Path $realInstall 'Update Deadlimit.cmd'
        $realOutput = & $realUpdater -NoLaunch -PackagePath $realArchive -ChecksumPath $realChecksum 2>&1
        Assert-True ($LASTEXITCODE -eq 0) "Extracted portable updater no-op failed.`n$($realOutput -join "`n")"
        Assert-True ((Get-Content -LiteralPath (Join-Path $realUserData 'portable-state.txt') -Raw) -eq 'preserve-me') 'Extracted updater did not preserve portable UserData.'
    }

    $updaterSource = Get-Content -LiteralPath $worker -Raw
    foreach ($forbidden in @('WScript.Shell', 'SpecialFolder]::Programs', 'Programs\Deadlimit')) {
        Assert-True (-not $updaterSource.Contains($forbidden, [StringComparison]::Ordinal)) "Portable updater still contains system-install behavior: $forbidden"
    }

    Write-Host 'Portable release no-op, checksum/traversal/broken-package rejection, in-place update, UserData preservation, and rollback smoke passed.'
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
