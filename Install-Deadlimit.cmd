@echo off
setlocal EnableExtensions
set "DEADLIMIT_BOOTSTRAP_PATH=%~f0"

pushd "%TEMP%" >nul 2>&1
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$p=$env:DEADLIMIT_BOOTSTRAP_PATH; $l=[IO.File]::ReadAllLines($p); $m=[Array]::IndexOf($l,'# DEADLIMIT_POWERSHELL_BOOTSTRAP'); if($m -lt 0){throw 'Deadlimit bootstrap payload marker was not found.'}; $s=[scriptblock]::Create(($l[($m+1)..($l.Length-1)] -join [Environment]::NewLine)); & $s"
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul 2>&1

if not "%EXIT_CODE%"=="0" pause
exit /b %EXIT_CODE%

# DEADLIMIT_POWERSHELL_BOOTSTRAP
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$workRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-bootstrap-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($workRoot) | Out-Null
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $headers = @{ 'User-Agent' = 'DeadlimitBootstrap/0.1' }
    $api = 'https://api.github.com/repos/downlimit/Deadlimit/releases?per_page=20'
    Write-Host 'Locating the latest published Deadlimit release...'
    $releases = @(Invoke-RestMethod -Headers $headers -Uri $api)
    $release = $releases | Where-Object { -not $_.draft } | Select-Object -First 1
    if ($null -eq $release) {
        throw 'No published Deadlimit release is available.'
    }

    $workerAsset = @($release.assets) |
        Where-Object { $_.name -eq 'DeadlimitPortableUpdater.ps1' } |
        Select-Object -First 1
    $checksumAsset = @($release.assets) |
        Where-Object { $_.name -eq 'DeadlimitPortableUpdater.ps1.sha256' } |
        Select-Object -First 1
    if ($null -eq $workerAsset -or $null -eq $checksumAsset) {
        throw "Deadlimit release $($release.tag_name) has no verified portable updater assets."
    }

    foreach ($uriText in @($workerAsset.browser_download_url, $checksumAsset.browser_download_url)) {
        $uri = [Uri]$uriText
        if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'github.com') {
            throw "Unexpected Deadlimit updater source: $uriText"
        }
    }

    $worker = Join-Path $workRoot 'DeadlimitPortableUpdater.ps1'
    $checksum = "$worker.sha256"
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $workerAsset.browser_download_url -OutFile $worker
    Invoke-WebRequest -UseBasicParsing -Headers $headers -Uri $checksumAsset.browser_download_url -OutFile $checksum
    $checksumText = [IO.File]::ReadAllText($checksum)
    $match = [Text.RegularExpressions.Regex]::Match($checksumText, '(?i)(?<![0-9a-f])[0-9a-f]{64}(?![0-9a-f])')
    if (-not $match.Success) {
        throw 'Deadlimit updater checksum asset is malformed.'
    }
    $actual = (Get-FileHash -LiteralPath $worker -Algorithm SHA256).Hash
    if (-not [string]::Equals($actual, $match.Value, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Deadlimit updater checksum mismatch. Installation was stopped.'
    }

    Write-Host "Verified Deadlimit updater from release $($release.tag_name)."
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $worker
    if ($LASTEXITCODE -ne 0) {
        throw "Deadlimit updater failed with exit code $LASTEXITCODE."
    }
    exit 0
}
catch {
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
finally {
    if (Test-Path -LiteralPath $workRoot) {
        Remove-Item -LiteralPath $workRoot -Recurse -Force
    }
}
