[CmdletBinding()]
param(
    [switch]$Rollback,
    [switch]$NoLaunch,
    [switch]$NoShortcuts,
    [string]$InstallRoot,
    [string]$PackagePath,
    [string]$ChecksumPath,
    [string]$ReleaseApiUrl = 'https://api.github.com/repos/downlimit/Deadlimit/releases?per_page=20'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-DefaultInstallRoot {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    return Join-Path $localAppData 'Programs\Deadlimit'
}

function Resolve-SafeInstallRoot([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        $Path = Get-DefaultInstallRoot
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

function Assert-PackageChecksum([string]$ArchivePath, [string]$HashPath) {
    $expected = Get-ExpectedSha256 $HashPath
    $actual = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
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

function Set-DeadlimitShortcuts([string]$Root) {
    if ($NoShortcuts) {
        return
    }

    $manager = Join-Path $Root 'DeadlimitManager.exe'
    $updater = Join-Path $Root 'Update Deadlimit.cmd'
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $programs = [Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)
    $startFolder = Join-Path $programs 'Deadlimit'
    [IO.Directory]::CreateDirectory($startFolder) | Out-Null

    $shell = New-Object -ComObject WScript.Shell
    foreach ($destination in @($desktop, $startFolder)) {
        if (-not [string]::IsNullOrWhiteSpace($destination)) {
            $managerLink = $shell.CreateShortcut((Join-Path $destination 'Deadlimit Manager.lnk'))
            $managerLink.TargetPath = $manager
            $managerLink.WorkingDirectory = $Root
            $managerLink.IconLocation = "$manager,0"
            $managerLink.Description = 'Deadlimit Manager'
            $managerLink.Save()

            if (Test-Path -LiteralPath $updater -PathType Leaf) {
                $updateLink = $shell.CreateShortcut((Join-Path $destination 'Update Deadlimit.lnk'))
                $updateLink.TargetPath = $updater
                $updateLink.WorkingDirectory = $Root
                $updateLink.IconLocation = "$manager,0"
                $updateLink.Description = 'Update Deadlimit'
                $updateLink.Save()
            }
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
    $swapRoot = "$CurrentRoot.rollback-$([Guid]::NewGuid().ToString('N'))"
    Move-Item -LiteralPath $CurrentRoot -Destination $swapRoot
    try {
        Move-Item -LiteralPath $PreviousRoot -Destination $CurrentRoot
        Move-Item -LiteralPath $swapRoot -Destination $PreviousRoot
    }
    catch {
        if (-not (Test-Path -LiteralPath $CurrentRoot) -and (Test-Path -LiteralPath $swapRoot)) {
            Move-Item -LiteralPath $swapRoot -Destination $CurrentRoot
        }
        throw
    }

    Set-DeadlimitShortcuts $CurrentRoot
    Write-Host "Deadlimit rollback complete. Previous/current installations were swapped: $CurrentRoot" -ForegroundColor Green
    if (-not $NoLaunch) {
        Start-Process -FilePath (Join-Path $CurrentRoot 'DeadlimitManager.exe') -WorkingDirectory $CurrentRoot
    }
}

$resolvedInstallRoot = Resolve-SafeInstallRoot $InstallRoot
$resolvedBackupRoot = "$resolvedInstallRoot.previous"
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
                Set-DeadlimitShortcuts $resolvedInstallRoot
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

    Stop-DeadlimitManager
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedInstallRoot)) | Out-Null
    Remove-ExactDirectory $resolvedBackupRoot
    $movedCurrent = $false
    if (Test-Path -LiteralPath $resolvedInstallRoot -PathType Container) {
        Move-Item -LiteralPath $resolvedInstallRoot -Destination $resolvedBackupRoot
        $movedCurrent = $true
    }

    try {
        Move-Item -LiteralPath $stageRoot -Destination $resolvedInstallRoot
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
        Remove-ExactDirectory $resolvedInstallRoot
        if ($movedCurrent -and (Test-Path -LiteralPath $resolvedBackupRoot -PathType Container)) {
            Move-Item -LiteralPath $resolvedBackupRoot -Destination $resolvedInstallRoot
        }
        throw
    }

    Set-DeadlimitShortcuts $resolvedInstallRoot
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
