@echo off
setlocal EnableExtensions
set "DEADLIMIT_INSTALLER_PATH=%~f0"

pushd "%TEMP%" >nul 2>&1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:DEADLIMIT_INSTALLER_PATH; $l=[IO.File]::ReadAllLines($p); $m=[Array]::IndexOf($l,'# DEADLIMIT_POWERSHELL_INSTALLER'); if($m -lt 0){throw 'Deadlimit installer payload marker was not found.'}; $s=[scriptblock]::Create(($l[($m+1)..($l.Length-1)] -join [Environment]::NewLine)); & $s"
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul 2>&1

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%

# DEADLIMIT_POWERSHELL_INSTALLER
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose(); $stream.Dispose() }
}

function Get-ExpectedSha256([string]$Path) {
    $text = [IO.File]::ReadAllText($Path)
    $match = [Text.RegularExpressions.Regex]::Match($text, '(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])')
    if (-not $match.Success) { throw 'The Deadlimit package checksum is malformed.' }
    return $match.Value.ToLowerInvariant()
}

function Set-Shortcut([object]$Shell, [string]$Path, [string]$Target, [string]$WorkingDirectory, [string]$Icon) {
    $shortcut = $Shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = $Icon
    $shortcut.Description = [IO.Path]::GetFileNameWithoutExtension($Path)
    $shortcut.Save()
}

$workRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-installer-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'DeadlimitInstaller/0.1' }
    $api = 'https://api.github.com/repos/downlimit/Deadlimit/releases/tags/latest-main'
    Write-Host 'Locating the latest successful Deadlimit build...'
    $releases = @(Invoke-RestMethod -UseBasicParsing -Headers $headers -Uri $api)
    $release = $releases | Where-Object { -not $_.draft } | Select-Object -First 1
    if ($null -eq $release) { throw 'No published Deadlimit release is available.' }

    $archiveAsset = @($release.assets) | Where-Object { $_.name -eq 'Deadlimit-win-x64.zip' } | Select-Object -First 1
    $checksumAsset = @($release.assets) | Where-Object { $_.name -eq 'Deadlimit-win-x64.zip.sha256' } | Select-Object -First 1
    if ($null -eq $archiveAsset -or $null -eq $checksumAsset) {
        throw "Deadlimit release $($release.tag_name) has no ZIP/checksum pair."
    }

    foreach ($uriText in @($archiveAsset.browser_download_url, $checksumAsset.browser_download_url)) {
        $uri = [Uri]$uriText
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
            throw "Unexpected Deadlimit package source: $uriText"
        }
    }

    $archivePath = Join-Path $workRoot 'Deadlimit-win-x64.zip'
    $checksumPath = "$archivePath.sha256"
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $archiveAsset.browser_download_url -OutFile $archivePath
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $checksumAsset.browser_download_url -OutFile $checksumPath
    $expected = Get-ExpectedSha256 $checksumPath
    $actual = Get-FileSha256 $archivePath
    if (-not [string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Deadlimit package checksum mismatch. Installation was stopped.'
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $workerEntry = $zip.Entries | Where-Object { $_.FullName -eq 'internal/DeadlimitPortableUpdater.ps1' } | Select-Object -First 1
        if ($null -eq $workerEntry) { throw 'The verified Deadlimit package has no updater worker.' }
        $workerPath = Join-Path $workRoot 'DeadlimitPortableUpdater.ps1'
        [IO.Compression.ZipFileExtensions]::ExtractToFile($workerEntry, $workerPath, $true)
    }
    finally { $zip.Dispose() }

    $installRoot = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Programs\Deadlimit'
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $workerPath `
        -InstallRoot $installRoot -PackagePath $archivePath -ChecksumPath $checksumPath -NoLaunch
    if ($LASTEXITCODE -ne 0) { throw "Deadlimit installation failed with exit code $LASTEXITCODE." }

    $manager = Join-Path $installRoot 'DeadlimitManager.exe'
    $updater = Join-Path $installRoot 'Update Deadlimit.cmd'
    $updaterIcon = "$manager,0"
    $shell = New-Object -ComObject WScript.Shell
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $startFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Deadlimit'
    [IO.Directory]::CreateDirectory($startFolder) | Out-Null
    Set-Shortcut $shell (Join-Path $desktop 'Deadlimit Manager.lnk') $manager $installRoot "$manager,0"
    Set-Shortcut $shell (Join-Path $desktop 'Deadlimit Updater.lnk') $updater $installRoot $updaterIcon
    Set-Shortcut $shell (Join-Path $startFolder 'Deadlimit Manager.lnk') $manager $installRoot "$manager,0"
    Set-Shortcut $shell (Join-Path $startFolder 'Deadlimit Updater.lnk') $updater $installRoot $updaterIcon

    Write-Host "The latest Deadlimit build was installed successfully: $installRoot" -ForegroundColor Green
    Start-Process -FilePath $manager -WorkingDirectory $installRoot
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $workRoot) { Remove-Item -LiteralPath $workRoot -Recurse -Force }
}
