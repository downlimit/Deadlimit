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
if ([string]::IsNullOrWhiteSpace($env:DEADLIMIT_INSTALLER_PATH)) {
    throw 'Deadlimit installer path is unavailable.'
}
$installerPath = [IO.Path]::GetFullPath($env:DEADLIMIT_INSTALLER_PATH)
$installerDirectory = Split-Path -Parent $installerPath
$installRoot = Join-Path $installerDirectory 'Deadlimit'
$userDataRoot = Join-Path $localAppData 'Deadlimit'

function Refresh-ProcessPath {
    $machinePath = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::Machine)
    $userPath = [Environment]::GetEnvironmentVariable('Path', [EnvironmentVariableTarget]::User)
    $env:Path = (@($machinePath, $userPath) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join ';'
}

function Find-Executable([string]$CommandName, [string[]]$CandidatePaths) {
    $command = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
        return [IO.Path]::GetFullPath($command.Source)
    }

    foreach ($candidate in $CandidatePaths | Select-Object -Unique) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and
            (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    return $null
}

function Find-Git {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Git\cmd\git.exe'),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'Git\cmd\git.exe' }),
        (Join-Path $env:LOCALAPPDATA 'Programs\Git\cmd\git.exe')
    )
    return Find-Executable 'git.exe' $candidates
}

function Has-DotNet10Sdk([string]$DotNetPath) {
    if ([string]::IsNullOrWhiteSpace($DotNetPath) -or
        -not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
        return $false
    }

    $sdkList = @(& $DotNetPath --list-sdks 2>$null)
    return $LASTEXITCODE -eq 0 -and
        $null -ne ($sdkList | Where-Object { $_ -match '^10\.0\.' } | Select-Object -First 1)
}

function Find-DotNet10Sdk {
    $command = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($null -ne $command) { $command.Source }),
        (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'),
        $(if (${env:ProgramFiles(x86)}) { Join-Path ${env:ProgramFiles(x86)} 'dotnet\dotnet.exe' }),
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($candidate in $candidates) {
        if (Has-DotNet10Sdk $candidate) {
            return [IO.Path]::GetFullPath($candidate)
        }
    }

    return $null
}

function Find-WinGet {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Microsoft\WindowsApps\winget.exe')
    )
    return Find-Executable 'winget.exe' $candidates
}

function Confirm-DependencyInstall([string[]]$MissingDependencies) {
    Add-Type -AssemblyName System.Windows.Forms

    $isRussian = [Globalization.CultureInfo]::CurrentUICulture.TwoLetterISOLanguageName -eq 'ru'
    $list = ($MissingDependencies | ForEach-Object { "• $_" }) -join [Environment]::NewLine
    if ($isRussian) {
        $title = 'Установка Deadlimit'
        $message = @"
Для Deadlimit не хватает:

$list

Установить автоматически через Windows Package Manager (WinGet)?
Windows может показать системный запрос UAC.
"@
    }
    else {
        $title = 'Deadlimit installation'
        $message = @"
Deadlimit is missing:

$list

Install automatically with Windows Package Manager (WinGet)?
Windows may show a system UAC prompt.
"@
    }

    $answer = [System.Windows.Forms.MessageBox]::Show(
        $message,
        $title,
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Question,
        [System.Windows.Forms.MessageBoxDefaultButton]::Button1)

    return $answer -eq [System.Windows.Forms.DialogResult]::Yes
}

function Install-WinGetPackage([string]$WingetPath, [string]$PackageId, [string]$DisplayName) {
    Write-Host "Installing $DisplayName through WinGet..."
    & $WingetPath install --id $PackageId --exact --source winget --accept-source-agreements --accept-package-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "WinGet could not install $DisplayName (exit code $LASTEXITCODE)."
    }
}

function Invoke-Git([string[]]$Arguments) {
    & $gitPath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: git $($Arguments -join ' ')" }
}

function Copy-DirectoryChildren([string]$Source, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Container)) { return }
    [IO.Directory]::CreateDirectory($Destination) | Out-Null
    foreach ($item in @(Get-ChildItem -LiteralPath $Source -Force)) {
        Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force
    }
}

function Test-LegacyDeadlimitInstallation([string]$Root) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) { return $false }

    $launcher = Join-Path $Root 'DeadlimitManager.cmd'
    $updater = Join-Path $Root 'DeadlimitUpdater.bat'
    $project = Join-Path $Root 'internal\src\Deadlimit\Deadlimit.csproj'
    $managerExe = Join-Path $Root 'DeadlimitManager.exe'

    return (Test-Path -LiteralPath $launcher -PathType Leaf) -and
        ((Test-Path -LiteralPath $updater -PathType Leaf) -or
         (Test-Path -LiteralPath $project -PathType Leaf) -or
         (Test-Path -LiteralPath $managerExe -PathType Leaf))
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

Refresh-ProcessPath
$gitPath = Find-Git
$dotnetPath = Find-DotNet10Sdk
$missingDependencies = @()
if ([string]::IsNullOrWhiteSpace($gitPath)) {
    $missingDependencies += 'Git for Windows'
}
if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
    $missingDependencies += '.NET 10 SDK'
}

if ($missingDependencies.Count -gt 0) {
    if (-not (Confirm-DependencyInstall $missingDependencies)) {
        Write-Host 'Deadlimit installation cancelled by the user.'
        exit 0
    }

    $wingetPath = Find-WinGet
    if ([string]::IsNullOrWhiteSpace($wingetPath)) {
        throw 'Windows Package Manager (WinGet) was not found. Install Microsoft App Installer / WinGet, then run Install-Deadlimit.cmd again.'
    }

    if ([string]::IsNullOrWhiteSpace($gitPath)) {
        Install-WinGetPackage $wingetPath 'Git.Git' 'Git for Windows'
    }
    if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
        Install-WinGetPackage $wingetPath 'Microsoft.DotNet.SDK.10' '.NET 10 SDK'
    }

    Refresh-ProcessPath
    $gitPath = Find-Git
    $dotnetPath = Find-DotNet10Sdk

    if ([string]::IsNullOrWhiteSpace($gitPath)) {
        throw 'Git for Windows was installed but git.exe could not be located. Restart Windows and run Install-Deadlimit.cmd again.'
    }
    if ([string]::IsNullOrWhiteSpace($dotnetPath)) {
        throw '.NET 10 SDK installation finished, but SDK 10.x could not be located. Restart Windows and run Install-Deadlimit.cmd again.'
    }
}

$toolPaths = @(
    [IO.Path]::GetDirectoryName($gitPath),
    [IO.Path]::GetDirectoryName($dotnetPath)
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$env:Path = (($toolPaths + @($env:Path)) -join ';')

[IO.Directory]::CreateDirectory((Split-Path -Parent $installRoot)) | Out-Null

if (Test-Path -LiteralPath (Join-Path $installRoot '.git') -PathType Container) {
    $origin = (& $gitPath -C $installRoot remote get-url origin).Trim()
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
        if (-not (Test-LegacyDeadlimitInstallation $installRoot)) {
            throw @"
Deadlimit will not replace the existing folder because it cannot prove that the folder is an older Deadlimit installation:
$installRoot

Move or remove that folder manually, then run Install-Deadlimit.cmd again.
"@
        }

        $legacyRoot = "$installRoot.pre-git-$([Guid]::NewGuid().ToString('N'))"
        Write-Host 'Migrating the previous package-based Deadlimit installation to a Git checkout...'
        Move-Item -LiteralPath $installRoot -Destination $legacyRoot
    }
    try {
        Write-Host 'Cloning Deadlimit main...'
        Invoke-Git @('clone','--branch','main','--single-branch',$repositoryUrl,$installRoot)
        if ($null -ne $legacyRoot) {
            Copy-DirectoryChildren (Join-Path $legacyRoot 'UserData') $userDataRoot
            Write-Host "Previous Deadlimit installation preserved for manual review: $legacyRoot" -ForegroundColor Yellow
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
