$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$textureType = $assembly.GetType('Deadlimit.Core.RetailTextureOverrideService', $true)
$manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
$buildTargets = $textureType.GetMethod('BuildTargetIndex', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
$resolveOverrides = $textureType.GetMethod('ResolveProjectRootOverrides', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
$stageOverrides = $textureType.GetMethod('StageProjectRootOverrides', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-prepare-authoring-textures-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $temp 'project'
$sourceRoot = Join-Path $projectRoot '0source'
$addonRoot = Join-Path $temp 'content\citadel_addons\ivytest'
$materialRelative = 'models\heroes_staging\tengu\tengu_v2\materials\ivy_wingsv3.vmat'
$colorRelative = 'models\heroes_staging\tengu\tengu_v2\materials\ivy_wingsv3_color.png'

function Write-TestFile([string]$root, [string]$relative, [string]$content) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
    return $path
}

function Write-TestBytes([string]$root, [string]$relative, [byte[]]$bytes) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    [IO.File]::WriteAllBytes($path, $bytes)
    return $path
}

try {
    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null

    $vmat = @"
Layer0
{
    "TextureColor" "models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3_color.png"
}
"Compiled Textures"
{
    "g_tColor" "models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3_color.vtex"
}
"@

    Write-TestFile $sourceRoot $materialRelative $vmat | Out-Null
    Write-TestBytes $sourceRoot $colorRelative ([byte[]](1,2,3,4)) | Out-Null
    Write-TestFile $addonRoot $materialRelative $vmat | Out-Null
    Write-TestBytes $addonRoot $colorRelative ([byte[]](1,2,3,4)) | Out-Null

    # Project-root artist source with the exact retail source name must replace the stock
    # CSDK working copy at the original Tengu/Ivy resource path.
    Write-TestBytes $projectRoot 'ivy_wingsv3_color.png' ([byte[]](8,7,6,5)) | Out-Null

    $manifest = [Activator]::CreateInstance($manifestType)
    $manifest.ProjectFolder = $projectRoot
    $manifest.SourceDumpFolderName = '0source'

    $targets = $buildTargets.Invoke($null, [object[]]@([string]$sourceRoot))
    $overrides = $resolveOverrides.Invoke($null, [object[]]@($manifest, $targets))
    if (@($overrides).Count -ne 1) {
        throw "Expected one exact project-root texture override, got $(@($overrides).Count)."
    }

    $staged = [int]$stageOverrides.Invoke($null, [object[]]@([string]$addonRoot, $overrides))
    if ($staged -ne 1) {
        throw "Expected one staged project-root texture override, got $staged."
    }

    $preparedVmat = Get-Content -LiteralPath (Join-Path $addonRoot $materialRelative) -Raw
    if (-not $preparedVmat.Contains('"TextureColor" "models/heroes_staging/tengu/tengu_v2/materials/ivy_wingsv3_color.png"', [StringComparison]::Ordinal)) {
        throw 'PREPARE changed an editable Ivy/Tengu VMAT away from its authoring PNG source.'
    }

    $preparedColor = Join-Path $addonRoot $colorRelative
    if (-not (Test-Path -LiteralPath $preparedColor)) {
        throw 'PREPARE removed the Ivy/Tengu authoring texture from CSDK.'
    }
    $colorBytes = [IO.File]::ReadAllBytes($preparedColor)
    if ($colorBytes.Length -ne 4 -or $colorBytes[0] -ne 8 -or $colorBytes[3] -ne 5) {
        throw 'Project-root artist texture did not replace the stock CSDK working copy.'
    }

    # Supporting ability source trees must keep their PNG dependencies in CSDK as well.
    $supportSourceRoot = Join-Path $sourceRoot 'models\heroes_wip\ivy\ability'
    $supportVmdl = Write-TestFile $supportSourceRoot 'stone_fx.vmdl' '{}'
    $supportVmat = @"
Layer0
{
    "TextureColor" "models/heroes_wip/ivy/ability/stone_fx_color.png"
}
"Compiled Textures"
{
    "g_tColor" "models/heroes_wip/ivy/ability/stone_fx_color.vtex"
}
"@
    Write-TestFile $supportSourceRoot 'stone_fx.vmat' $supportVmat | Out-Null
    Write-TestBytes $supportSourceRoot 'stone_fx_color.png' ([byte[]](3,3,3,3)) | Out-Null

    $mainDestination = Write-TestFile $addonRoot 'models\heroes_wip\ivy\ivy.vmdl' '{}'
    $resolverType = $assembly.GetType('Deadlimit.Core.ExtractedSourceAssetResolver', $true)
    $flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
    $stageOwners = $resolverType.GetMethod('StageOwningVmdlSourceTrees', $flags)
    if ($null -eq $stageOwners) {
        throw 'Supporting source staging method was not found.'
    }

    [void]$stageOwners.Invoke($null, [object[]]@(
        [string]$sourceRoot,
        [string]$addonRoot,
        [string[]]@($supportVmdl),
        [string]$mainDestination))

    $supportVmatTarget = Join-Path $addonRoot 'models\heroes_wip\ivy\ability\stone_fx.vmat'
    $supportTextureTarget = Join-Path $addonRoot 'models\heroes_wip\ivy\ability\stone_fx_color.png'
    if (-not (Test-Path -LiteralPath $supportVmatTarget)) {
        throw 'Supporting ability VMAT was not staged.'
    }
    if (-not (Test-Path -LiteralPath $supportTextureTarget)) {
        throw 'Supporting ability authoring PNG was not staged.'
    }
    if (-not (Get-Content -LiteralPath $supportVmatTarget -Raw).Contains('stone_fx_color.png', [StringComparison]::Ordinal)) {
        throw 'Supporting ability VMAT lost its PNG authoring reference.'
    }

    $overrideSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/RetailTextureOverrideService.cs' -Raw
    if ($overrideSource.Contains('RepairMissingRetailTextureReferences', [StringComparison]::Ordinal) -or
        $overrideSource.Contains('stockCopiesToDelete', [StringComparison]::Ordinal)) {
        throw 'Obsolete stock-source deletion/fallback logic is still present.'
    }

    $resolverSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ExtractedSourceAssetResolver.cs' -Raw
    if ($resolverSource.Contains('RetailTextureSourceExtensions.Contains(Path.GetExtension(sourceFile))', [StringComparison]::Ordinal)) {
        throw 'Supporting source staging still skips authoring texture sources.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'PREPARE authoring texture preservation, root override and supporting FX texture smoke passed.'
