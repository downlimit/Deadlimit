[CmdletBinding()]
param(
    [switch]$Rollback,
    [switch]$NoLaunch,
    [string]$InstallRoot,
    [string]$PackagePath,
    [string]$ChecksumPath,
    [string]$ReleaseApiUrl = 'https://api.github.com/repos/downlimit/Deadlimit/releases?per_page=20'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-SafeInstallRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = $env:DEADLIMIT_PORTABLE_ROOT
    }
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'The portable root was not provided. Run Update Deadlimit.cmd from the extracted Deadlimit folder.'
    }

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $volumeRoot = [IO.Path]::GetPathRoot($fullPath).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    if ([string]::Equals($fullPath, $volumeRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a volume root as the Deadlimit install folder: $fullPath"
    }

    $parent = Split-Path -Parent $fullPath
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "Deadlimit install folder has no safe parent: $fullPath"
    }

    return $fullPath
}

function Remove-ExactDirectory([string]$Path) {
    if (Test-Path -LiteralPath $Path) {
        $item = Get-Item -LiteralPath $Path -Force
        if (-not $item.PSIsContainer) {
            throw "Expected a directory but found a file: $Path"
        }
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Get-ExpectedSha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Checksum file was not found: $Path"
    }

    $text = [IO.File]::ReadAllText([IO.Path]::GetFullPath($Path))
    $match = [Text.RegularExpressions.Regex]::Match($text, '(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])')
    if (-not $match.Success) {
        throw "Checksum file does not contain one SHA-256 value: $Path"
    }
    return $match.Value.ToLowerInvariant()
}

function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Assert-PackageChecksum([string]$ArchivePath, [string]$HashPath) {
    $expected = Get-ExpectedSha256 $HashPath
    $actual = Get-FileSha256 $ArchivePath
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Deadlimit package checksum mismatch. Expected $expected, received $actual."
    }
    return $actual
}

function Expand-SafeZip([string]$ArchivePath, [string]$OutputRoot) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Directory]::CreateDirectory($OutputRoot) | Out-Null
    $root = [IO.Path]::GetFullPath($OutputRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
    $totalBytes = [Int64]0

    $archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        if ($archive.Entries.Count -gt 10000) {
            throw "Deadlimit package has too many entries: $($archive.Entries.Count)."
        }

        foreach ($entry in $archive.Entries) {
            $totalBytes += [Int64]$entry.Length
            if ($entry.Length -gt 512MB -or $totalBytes -gt 2GB) {
                throw 'Deadlimit package exceeds the extraction safety limit.'
            }

            $unixMode = ($entry.ExternalAttributes -shr 16) -band 0xF000
            if ($unixMode -eq 0xA000) {
                throw "Deadlimit package contains a symbolic link: $($entry.FullName)"
            }

            $relative = $entry.FullName.Replace('/', [IO.Path]::DirectorySeparatorChar)
            $destination = [IO.Path]::GetFullPath((Join-Path $root $relative))
            if (-not $destination.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                throw "Deadlimit package entry escapes the install root: $($entry.FullName)"
            }

            if ([string]::IsNullOrEmpty($entry.Name)) {
                [IO.Directory]::CreateDirectory($destination) | Out-Null
                continue
            }

            [IO.Directory]::CreateDirectory((Split-Path -Parent $destination)) | Out-Null
            $source = $entry.Open()
            try {
                $target = [IO.FileStream]::new($destination, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
                try {
                    $source.CopyTo($target)
                }
                finally {
                    $target.Dispose()
                }
            }
            finally {
                $source.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-ReleasePackage([string]$WorkRoot) {
    if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
        if ([string]::IsNullOrWhiteSpace($ChecksumPath)) {
            throw '-ChecksumPath is required with -PackagePath.'
        }
        return [pscustomobject]@{
            Archive = [IO.Path]::GetFullPath($PackagePath)
            Checksum = [IO.Path]::GetFullPath($ChecksumPath)
            Tag = 'local-package'
            Source = [IO.Path]::GetFullPath($PackagePath)
        }
    }

    if (-not $ReleaseApiUrl.StartsWith('https://', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The Deadlimit release API must use HTTPS.'
    }

    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'DeadlimitPortableUpdater/0.1' }
    $releases = @(Invoke-RestMethod -UseBasicParsing -Headers $headers -Uri $ReleaseApiUrl)
    $release = $releases | Where-Object { -not $_.draft } | Select-Object -First 1
    if ($null -eq $release) {
        throw 'No published Deadlimit release is available.'
    }

    $archiveAsset = @($release.assets) |
        Where-Object { $_.name -eq 'Deadlimit-win-x64.zip' } |
        Select-Object -First 1
    $checksumAsset = @($release.assets) |
        Where-Object { $_.name -eq 'Deadlimit-win-x64.zip.sha256' } |
        Select-Object -First 1
    if ($null -eq $archiveAsset -or $null -eq $checksumAsset) {
        throw "Deadlimit release $($release.tag_name) does not contain the required portable ZIP and checksum."
    }

    foreach ($uriText in @($archiveAsset.browser_download_url, $checksumAsset.browser_download_url)) {
        $uri = [Uri]$uriText
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
            throw "Unexpected Deadlimit release asset source: $uriText"
        }
    }

    $archive = Join-Path $WorkRoot 'Deadlimit-win-x64.zip'
    $checksum = Join-Path $WorkRoot 'Deadlimit-win-x64.zip.sha256'
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $archiveAsset.browser_download_url -OutFile $archive
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $checksumAsset.browser_download_url -OutFile $checksum
    return [pscustomobject]@{
        Archive = $archive
        Checksum = $checksum
        Tag = [string]$release.tag_name
        Source = [string]$archiveAsset.browser_download_url
    }
}

function Stop-DeadlimitManager {
    Get-Process -Name DeadlimitManager, DeadlimitAggregator, Deadlimit -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Get-ManagedPayloadItems([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return @()
    }
    return @(Get-ChildItem -LiteralPath $Root -Force | Where-Object {
        $_.Name -notin @('UserData', 'Backup', 'Backup.next')
    })
}

function Move-DirectoryChildren([string]$Source, [string]$Destination) {
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Move-Item -LiteralPath $item.FullName -Destination $Destination
    }
}

function Clear-ManagedPayload([string]$Root) {
    foreach ($item in @(Get-ManagedPayloadItems $Root)) {
        if ($item.PSIsContainer) {
            Remove-ExactDirectory $item.FullName
        }
        else {
            Remove-Item -LiteralPath $item.FullName -Force
        }
    }
}

function Invoke-Rollback([string]$CurrentRoot, [string]$PreviousRoot) {
    if (-not (Test-Path -LiteralPath $CurrentRoot -PathType Container)) {
        throw "The current Deadlimit installation was not found: $CurrentRoot"
    }
    if (-not (Test-Path -LiteralPath $PreviousRoot -PathType Container)) {
        throw "No previous Deadlimit installation is available: $PreviousRoot"
    }

    Stop-DeadlimitManager
    $swapRoot = Join-Path $CurrentRoot 'Backup.next'
    if (Test-Path -LiteralPath $swapRoot) {
        throw "An incomplete update backup must be resolved before rollback: $swapRoot"
    }

    [IO.Directory]::CreateDirectory($swapRoot) | Out-Null
    foreach ($item in @(Get-ManagedPayloadItems $CurrentRoot)) {
        Move-Item -LiteralPath $item.FullName -Destination $swapRoot
    }
    try {
        Move-DirectoryChildren $PreviousRoot $CurrentRoot
        Remove-ExactDirectory $PreviousRoot
        Move-Item -LiteralPath $swapRoot -Destination $PreviousRoot
    }
    catch {
        if (Test-Path -LiteralPath $swapRoot -PathType Container) {
            Clear-ManagedPayload $CurrentRoot
            Move-DirectoryChildren $swapRoot $CurrentRoot
            Remove-ExactDirectory $swapRoot
        }
        throw
    }

    Write-Host "Deadlimit rollback complete. UserData stayed in place: $CurrentRoot" -ForegroundColor Green
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $CurrentRoot 'DeadlimitManager.exe') -WorkingDirectory $CurrentRoot
    }
}

$resolvedInstallRoot = Resolve-SafeInstallRoot $InstallRoot
$resolvedBackupRoot = Join-Path $resolvedInstallRoot 'Backup'
$workRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-portable-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($workRoot) | Out-Null

try {
    if ($Rollback) {
        Invoke-Rollback $resolvedInstallRoot $resolvedBackupRoot
        exit 0
    }

    Write-Host 'Checking the latest published Deadlimit portable release...'
    $package = Get-ReleasePackage $workRoot
    $packageHash = Assert-PackageChecksum $package.Archive $package.Checksum

    $markerPath = Join-Path $resolvedInstallRoot '.deadlimit-install.json'
    if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
        try {
            $installed = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ($installed.packageSha256 -eq $packageHash -and
                (Test-Path -LiteralPath (Join-Path $resolvedInstallRoot 'DeadlimitManager.exe') -PathType Leaf)) {
                Write-Host 'Deadlimit is already up to date.' -ForegroundColor Green
                exit 0
            }
        }
        catch {
            Write-Host 'The current install marker is unreadable; the verified package will repair the installation.' -ForegroundColor Yellow
        }
    }

    $stageRoot = Join-Path $workRoot 'stage'
    Expand-SafeZip $package.Archive $stageRoot
    $managerPath = Join-Path $stageRoot 'DeadlimitManager.exe'
    $releasePath = Join-Path $stageRoot 'release.json'
    if (-not (Test-Path -LiteralPath $managerPath -PathType Leaf)) {
        throw 'The verified package does not contain DeadlimitManager.exe.'
    }
    if (-not (Test-Path -LiteralPath $releasePath -PathType Leaf)) {
        throw 'The verified package does not contain release.json.'
    }
    $release = Get-Content -LiteralPath $releasePath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace([string]$release.version)) {
        throw 'The verified package release metadata has no version.'
    }

    foreach ($reservedName in @('UserData', 'Backup', 'Backup.next')) {
        if (Test-Path -LiteralPath (Join-Path $stageRoot $reservedName)) {
            throw "The verified package contains the reserved portable data path: $reservedName"
        }
    }

    $currentReleasePath = Join-Path $resolvedInstallRoot 'release.json'
    if (Test-Path -LiteralPath $currentReleasePath -PathType Leaf) {
        try {
            $currentRelease = Get-Content -LiteralPath $currentReleasePath -Raw | ConvertFrom-Json
            if ([string]$currentRelease.version -eq [string]$release.version) {
                Write-Host 'Deadlimit is already up to date.' -ForegroundColor Green
                exit 0
            }
        }
        catch {
            Write-Host 'Current release metadata is unreadable; the verified package will repair the program files.' -ForegroundColor Yellow
        }
    }

    Stop-DeadlimitManager
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedInstallRoot)) | Out-Null
    [IO.Directory]::CreateDirectory($resolvedInstallRoot) | Out-Null
    $nextBackupRoot = Join-Path $resolvedInstallRoot 'Backup.next'
    if (Test-Path -LiteralPath $nextBackupRoot) {
        throw "An incomplete portable update must be resolved before continuing: $nextBackupRoot"
    }

    $currentItems = @(Get-ManagedPayloadItems $resolvedInstallRoot)
    $movedCurrent = $currentItems.Count -gt 0
    [IO.Directory]::CreateDirectory($nextBackupRoot) | Out-Null
    foreach ($item in $currentItems) {
        Move-Item -LiteralPath $item.FullName -Destination $nextBackupRoot
    }

    try {
        Move-DirectoryChildren $stageRoot $resolvedInstallRoot
        Remove-ExactDirectory $stageRoot
        $marker = [ordered]@{
            version = [string]$release.version
            tag = [string]$package.Tag
            packageSha256 = $packageHash
            source = [string]$package.Source
            installedUtc = [DateTimeOffset]::UtcNow.ToString('O')
        }
        $marker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resolvedInstallRoot '.deadlimit-install.json') -Encoding UTF8
    }
    catch {
        Clear-ManagedPayload $resolvedInstallRoot
        if (Test-Path -LiteralPath $nextBackupRoot -PathType Container) {
            Move-DirectoryChildren $nextBackupRoot $resolvedInstallRoot
            Remove-ExactDirectory $nextBackupRoot
        }
        throw
    }

    if ($movedCurrent) {
        Remove-ExactDirectory $resolvedBackupRoot
        Move-Item -LiteralPath $nextBackupRoot -Destination $resolvedBackupRoot
    }
    else {
        Remove-ExactDirectory $nextBackupRoot
    }

    Write-Host "Deadlimit $($release.version) installed successfully: $resolvedInstallRoot" -ForegroundColor Green
    if ($movedCurrent) {
        Write-Host "The previous installation is available for rollback: $resolvedBackupRoot"
    }
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $resolvedInstallRoot 'DeadlimitManager.exe') -WorkingDirectory $resolvedInstallRoot
    }
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    try {
        Remove-ExactDirectory $workRoot
    }
    catch {
    }
}
