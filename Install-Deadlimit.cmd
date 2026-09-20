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

$repositoryUrl = 'https://github.com/downlimit/Deadlimit.git'
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$installRoot = Join-Path $localAppData 'Programs\Deadlimit'
$userDataRoot = Join-Path $localAppData 'Deadlimit'

function Require-Command([string]$Name, [string]$Message) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if ($null -eq $command) { throw $Message }
    return $command
}

function Invoke-Git([string[]]$Arguments) {
    & $git.Source @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: git $($Arguments -join ' ')" }
}

function Copy-DirectoryChildren([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Publish-Shortcuts([string]$Root) {
    $managerShortcut = Join-Path $Root 'Deadlimit Manager.lnk'
    $updaterShortcut = Join-Path $Root 'Deadlimit Updater.lnk'
    foreach ($path in @($managerShortcut, $updaterShortcut)) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Deadlimit shortcut was not created: $path" }
    }
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $startFolder = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::Programs)) 'Deadlimit'
    [IO.Directory]::CreateDirectory($startFolder) | Out-Null
    Copy-Item -LiteralPath $managerShortcut -Destination (Join-Path $desktop 'Deadlimit Manager.lnk') -Force
    Copy-Item -LiteralPath $updaterShortcut -Destination (Join-Path $desktop 'Deadlimit Updater.lnk') -Force
    Copy-Item -LiteralPath $managerShortcut -Destination (Join-Path $startFolder 'Deadlimit Manager.lnk') -Force
    Copy-Item -LiteralPath $updaterShortcut -Destination (Join-Path $startFolder 'Deadlimit Updater.lnk') -Force
}

$git = Require-Command 'git.exe' 'Git for Windows is required to install Deadlimit. Install Git, then run Install-Deadlimit.cmd again.'
$dotnet = Require-Command 'dotnet.exe' '.NET 10 SDK is required to install Deadlimit. Install the .NET 10 SDK, then run Install-Deadlimit.cmd again.'
$sdkList = @(& $dotnet.Source --list-sdks)
if ($LASTEXITCODE -ne 0 -or -not ($sdkList | Where-Object { $_ -match '^10\.0\.' })) {
    throw '.NET 10 SDK was not found. Install the .NET 10 SDK, then run Install-Deadlimit.cmd again.'
}

[IO.Directory]::CreateDirectory((Split-Path -Parent $installRoot)) | Out-Null

if (Test-Path -LiteralPath (Join-Path $installRoot '.git') -PathType Container) {
    $origin = (& $git.Source -C $installRoot remote get-url origin).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($origin)) {
        throw "The existing Deadlimit checkout has no readable origin remote: $installRoot"
    }
    $normalizedOrigin = $origin.TrimEnd('/').ToLowerInvariant()
    if ($normalizedOrigin -notin @('https://github.com/downlimit/deadlimit.git','https://github.com/downlimit/deadlimit')) {
        throw "The existing folder is a Git checkout with an unexpected origin: $origin"
    }
    Write-Host 'Updating the existing Deadlimit checkout...'
    $updater = Join-Path $installRoot 'DeadlimitUpdater.bat'
    if (-not (Test-Path -LiteralPath $updater -PathType Leaf)) {
        throw "DeadlimitUpdater.bat was not found in the existing checkout: $installRoot"
    }
    & $env:ComSpec /d /c "`"$updater`" -NoWait -NoLaunch"
    if ($LASTEXITCODE -ne 0) { throw 'The existing Deadlimit checkout could not be updated.' }
}
else {
    $legacyRoot = $null
    if (Test-Path -LiteralPath $installRoot) {
        $legacyRoot = "$installRoot.pre-git-$([Guid]::NewGuid().ToString('N'))"
        Write-Host 'Migrating the previous package-based Deadlimit installation to a Git checkout...'
        Move-Item -LiteralPath $installRoot -Destination $legacyRoot
    }
    try {
        Write-Host 'Cloning Deadlimit main...'
        Invoke-Git @('clone','--branch','main','--single-branch',$repositoryUrl,$installRoot)
        if ($null -ne $legacyRoot) {
            Copy-DirectoryChildren (Join-Path $legacyRoot 'UserData') $userDataRoot
            Remove-Item -LiteralPath $legacyRoot -Recurse -Force
        }
    }
    catch {
        if (Test-Path -LiteralPath $installRoot) { Remove-Item -LiteralPath $installRoot -Recurse -Force -ErrorAction SilentlyContinue }
        if ($null -ne $legacyRoot -and (Test-Path -LiteralPath $legacyRoot)) { Move-Item -LiteralPath $legacyRoot -Destination $installRoot }
        throw
    }
}

$launcher = Join-Path $installRoot 'DeadlimitManager.cmd'
if (-not (Test-Path -LiteralPath $launcher -PathType Leaf)) { throw "DeadlimitManager.cmd was not found after installation: $installRoot" }
Write-Host 'Building Deadlimit Manager with the local .NET 10 SDK...'
& $env:ComSpec /d /c "`"$launcher`" --refresh-only"
if ($LASTEXITCODE -ne 0) { throw "Deadlimit Manager build failed with exit code $LASTEXITCODE." }

Publish-Shortcuts $installRoot
Write-Host "Deadlimit installed successfully: $installRoot" -ForegroundColor Green
Start-Process -FilePath (Join-Path $installRoot 'Deadlimit Manager.lnk') -WorkingDirectory $installRoot
