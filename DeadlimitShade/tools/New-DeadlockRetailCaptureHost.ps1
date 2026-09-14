[CmdletBinding()]
param(
    [string]$RetailRoot = 'D:\Program Files (x86)\Steam\steamapps\common\Project8Staging\game\citadel',
    [string]$CsdkRoot = 'C:\WorkProjects\Deadlock\Reduced_CSDK_12',
    [string]$OutputRoot = (Join-Path ([System.IO.Path]::GetTempPath()) ('DeadlimitRetailCapture-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')))
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedDirectoryHash = '5ae9ee6e4aa57dac4cba96dbff4d785f6770585ae447e90763527e39d781142d'
$expectedArchiveHash = '31be2b2dc8ac067a994c4f78a5973a22e7fd414db85a7fae52e96b9af09211b3'
$expectedPixelShaderHash = 'eceff13193baccd5310db90ac9b3dd36928d941753c98494e349fa9e29826930'

$retailDirectory = Join-Path $RetailRoot 'shaders_vulkan_dir.vpk'
$retailArchive = Join-Path $RetailRoot 'shaders_vulkan_000.vpk'
$csdkGame = Join-Path $CsdkRoot 'game'
$csdkExecutable = Join-Path $csdkGame 'bin\win64\deadlock.exe'
$csdkGameInfo = Join-Path $csdkGame 'citadel\gameinfo.gi'
$csdkCitadel = Join-Path $csdkGame 'citadel'
$csdkContent = Join-Path $CsdkRoot 'content'
$csdkGameIvy = Join-Path $csdkGame 'citadel_addons\ivybuilder'
$csdkContentIvy = Join-Path $csdkContent 'citadel_addons\ivybuilder'
$csdkToolSettings = Join-Path $csdkGame '_toolsettings'

foreach ($requiredPath in @($retailDirectory, $retailArchive, $csdkExecutable, $csdkGameInfo, $csdkCitadel, $csdkGameIvy, $csdkContentIvy, $csdkToolSettings)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required capture input does not exist: $requiredPath"
    }
}
if (Test-Path -LiteralPath $OutputRoot) {
    throw "Capture host already exists; choose a new OutputRoot: $OutputRoot"
}

function Assert-FileHash {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Expected
    )
    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Expected) {
        throw "Unexpected SHA-256 for ${Path}: expected $Expected, got $actual"
    }
    return $actual
}

$directoryHash = Assert-FileHash -Path $retailDirectory -Expected $expectedDirectoryHash
$archiveHash = Assert-FileHash -Path $retailArchive -Expected $expectedArchiveHash

$captureGame = Join-Path $OutputRoot 'game'
$captureMod = Join-Path $captureGame 'citadel'
New-Item -ItemType Directory -Path $captureMod -Force | Out-Null

# Source 2 resolves the game root from the executable path. The large runtime
# trees are read-only junctions; known write locations are real temp folders.
foreach ($directoryName in @('bin', 'core', 'citadel_community_addons')) {
    $source = Join-Path $csdkGame $directoryName
    if (Test-Path -LiteralPath $source) {
        New-Item -ItemType Junction -Path (Join-Path $captureGame $directoryName) -Target $source | Out-Null
    }
}

$captureGameAddons = Join-Path $captureGame 'citadel_addons'
New-Item -ItemType Directory -Path $captureGameAddons -Force | Out-Null
Copy-Item -LiteralPath $csdkGameIvy -Destination $captureGameAddons -Recurse -Force
Copy-Item -LiteralPath $csdkToolSettings -Destination $captureGame -Recurse -Force

$captureContent = Join-Path $OutputRoot 'content'
New-Item -ItemType Directory -Path (Join-Path $captureContent 'citadel_addons') -Force | Out-Null
foreach ($directoryName in @('core', 'citadel')) {
    $source = Join-Path $csdkContent $directoryName
    if (Test-Path -LiteralPath $source) {
        New-Item -ItemType Junction -Path (Join-Path $captureContent $directoryName) -Target $source | Out-Null
    }
}
Copy-Item -LiteralPath $csdkContentIvy -Destination (Join-Path $captureContent 'citadel_addons') -Recurse -Force

$writableDirectories = @('cfg', 'replays', 'rpt', 'save', 'shadercache')
foreach ($sourceDirectory in Get-ChildItem -LiteralPath $csdkCitadel -Directory) {
    $destination = Join-Path $captureMod $sourceDirectory.Name
    if ($sourceDirectory.Name -in $writableDirectories) {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $sourceDirectory.FullName -Force | Copy-Item -Destination $destination -Recurse -Force
    }
    else {
        New-Item -ItemType Junction -Path $destination -Target $sourceDirectory.FullName | Out-Null
    }
}

foreach ($sourceFile in Get-ChildItem -LiteralPath $csdkCitadel -File) {
    if ($sourceFile.Name -notin @('console.log', 'shaders_vulkan_dir.vpk', 'shaders_vulkan_000.vpk', 'gameinfo.gi')) {
        Copy-Item -LiteralPath $sourceFile.FullName -Destination (Join-Path $captureMod $sourceFile.Name)
    }
}

Copy-Item -LiteralPath $retailDirectory -Destination (Join-Path $captureMod 'shaders_vulkan_dir.vpk')
Copy-Item -LiteralPath $retailArchive -Destination (Join-Path $captureMod 'shaders_vulkan_000.vpk')

$sourceGameInfo = [System.IO.File]::ReadAllText($csdkGameInfo)
$gameInfo = [regex]::Replace(
    $sourceGameInfo,
    '(?m)^\s*title\s+"Citadel"\s*$',
    '    title       "Deadlimit Retail Capture"',
    1)
$gameInfoPath = Join-Path $captureMod 'gameinfo.gi'
[System.IO.File]::WriteAllText($gameInfoPath, $gameInfo, [System.Text.UTF8Encoding]::new($false))

$captureExecutable = Join-Path $captureGame 'bin\win64\deadlock.exe'
$launchArguments = @(
    '-game', 'citadel',
    '-tools',
    '-multiple_tools_instances',
    '-allowmultiple',
    '-vulkan',
    '-insecure',
    '-allowdebug',
    '-toconsole',
    '-danger_mode_ignore_schema_mismatches',
    '+tool_request', 'met'
)
$manifest = [ordered]@{
    schemaVersion = 1
    createdUtc = [DateTime]::UtcNow.ToString('o')
    classification = 'confirmed by static retail evidence'
    retailRoot = $RetailRoot
    retailDirectorySha256 = $directoryHash
    retailArchiveSha256 = $archiveHash
    expectedPixelShaderPath = 'shaders/vfx/pbr_vulkan_60_ps.vcs'
    expectedPixelShaderSha256 = $expectedPixelShaderHash
    csdkRoot = $CsdkRoot
    csdkExecutable = $csdkExecutable
    captureExecutable = $captureExecutable
    captureGame = $captureMod
    launchArguments = $launchArguments
    runtimeShaderIdentity = 'blocked/unresolved until a captured draw proves the bound module hash'
}
$manifestPath = Join-Path $OutputRoot 'capture-host.json'
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $manifestPath -Encoding utf8NoBOM

[pscustomobject]@{
    OutputRoot = $OutputRoot
    GameInfo = $gameInfoPath
    Manifest = $manifestPath
    Executable = $captureExecutable
    Arguments = ($launchArguments -join ' ')
    RetailVpk = 'confirmed by static retail evidence'
    RuntimeShaderIdentity = 'blocked/unresolved until capture'
}
