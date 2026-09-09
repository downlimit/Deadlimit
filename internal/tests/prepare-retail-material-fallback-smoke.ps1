$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$textureType = $assembly.GetType('Deadlimit.Core.RetailTextureOverrideService', $true)
$manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
$buildTargets = $textureType.GetMethod('BuildTargetIndex', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
$resolveOverrides = $textureType.GetMethod('ResolveProjectRootOverrides', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
$stageOverrides = $textureType.GetMethod('StageProjectRootOverrides', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-prepare-retail-fallback-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $temp 'project'
$sourceRoot = Join-Path $projectRoot '0source'
$addonRoot = Join-Path $temp 'content\citadel_addons\ivytest'
$materialRelative = 'materials\heroes\ivy\ivy_wingsv3.vmat'
$colorRelative = 'materials\heroes\ivy\ivy_wingsv3_color.png'
$normalRelative = 'materials\heroes\ivy\ivy_wingsv3_normal.png'
$manualRelative = 'materials\heroes\ivy\ivy_wingsv3_manual.png'

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
    "TextureColor" "materials/heroes/ivy/ivy_wingsv3_color.png"
    "TextureNormal" "materials/heroes/ivy/ivy_wingsv3_normal.png"
    "TextureDetail" "materials/heroes/ivy/ivy_wingsv3_manual.png"
}
"Compiled Textures"
{
    "TextureColor" "materials/heroes/ivy/ivy_wingsv3_color.vtex"
    "TextureNormal" "materials/heroes/ivy/ivy_wingsv3_normal.vtex"
    "TextureDetail" "materials/heroes/ivy/ivy_wingsv3_manual.vtex"
}
"@

    Write-TestFile $sourceRoot $materialRelative $vmat | Out-Null
    Write-TestFile $addonRoot $materialRelative $vmat | Out-Null

    # Stock color exists in 0source and was redundantly copied into the addon.
    # PREPARE must stop referencing/carrying that identical copy.
    Write-TestBytes $sourceRoot $colorRelative ([byte[]](1,2,3,4)) | Out-Null
    Write-TestBytes $addonRoot $colorRelative ([byte[]](1,2,3,4)) | Out-Null

    # Normal is not extracted at all. The copied VMAT still has to fall back to retail .vtex.

    # A user-edited source already living in CSDK differs from the extracted stock source.
    # Do not silently replace or delete it.
    Write-TestBytes $sourceRoot $manualRelative ([byte[]](5,5,5,5)) | Out-Null
    Write-TestBytes $addonRoot $manualRelative ([byte[]](9,9,9,9)) | Out-Null

    # Explicit project-root override must survive the stock cleanup and be staged normally.
    $artistColor = Write-TestBytes $projectRoot 'ivy_wingsv3_color.png' ([byte[]](8,7,6,5))

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

    $preparedVmatPath = Join-Path $addonRoot $materialRelative
    $preparedVmat = Get-Content -LiteralPath $preparedVmatPath -Raw
    if (-not $preparedVmat.Contains('"TextureColor" "materials/heroes/ivy/ivy_wingsv3_color.vtex"', [StringComparison]::Ordinal)) {
        throw 'PREPARE did not restore the stock Color slot to retail .vtex.'
    }
    if (-not $preparedVmat.Contains('"TextureNormal" "materials/heroes/ivy/ivy_wingsv3_normal.vtex"', [StringComparison]::Ordinal)) {
        throw 'PREPARE did not restore a missing stock Normal slot to retail .vtex.'
    }
    if (-not $preparedVmat.Contains('"TextureDetail" "materials/heroes/ivy/ivy_wingsv3_manual.png"', [StringComparison]::Ordinal)) {
        throw 'PREPARE overwrote an existing non-stock CSDK texture source.'
    }

    $preparedColor = Join-Path $addonRoot $colorRelative
    if (-not (Test-Path -LiteralPath $preparedColor)) {
        throw 'Project-root artist override was not staged after stock cleanup.'
    }
    $colorBytes = [IO.File]::ReadAllBytes($preparedColor)
    if ($colorBytes.Length -ne 4 -or $colorBytes[0] -ne 8 -or $colorBytes[3] -ne 5) {
        throw 'Project-root artist override bytes were replaced by the stock source.'
    }

    if (Test-Path -LiteralPath (Join-Path $addonRoot $normalRelative)) {
        throw 'PREPARE unexpectedly created a stock Normal source image.'
    }

    $manualBytes = [IO.File]::ReadAllBytes((Join-Path $addonRoot $manualRelative))
    if ($manualBytes.Length -ne 4 -or $manualBytes[0] -ne 9) {
        throw 'Existing non-stock CSDK texture source was modified or deleted.'
    }

    # Supporting ability VMDL trees are staged later than the main retail model tree.
    # They must follow the same rule: no stock image copy, VMAT falls back to retail .vtex.
    $supportSourceRoot = Join-Path $sourceRoot 'models\heroes_wip\ivy\ability'
    $supportVmdl = Write-TestFile $supportSourceRoot 'stone_fx.vmdl' '{}'
    $supportVmat = @"
Layer0
{
    "TextureColor" "models/heroes_wip/ivy/ability/stone_fx_color.png"
}
"Compiled Textures"
{
    "TextureColor" "models/heroes_wip/ivy/ability/stone_fx_color.vtex"
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

    $preparedSupportVmatPath = Join-Path $addonRoot 'models\heroes_wip\ivy\ability\stone_fx.vmat'
    $preparedSupportVmat = Get-Content -LiteralPath $preparedSupportVmatPath -Raw
    if (-not $preparedSupportVmat.Contains('"TextureColor" "models/heroes_wip/ivy/ability/stone_fx_color.vtex"', [StringComparison]::Ordinal)) {
        throw 'Supporting ability VMAT did not fall back to its retail .vtex.'
    }
    if (Test-Path -LiteralPath (Join-Path $addonRoot 'models\heroes_wip\ivy\ability\stone_fx_color.png')) {
        throw 'Supporting source staging copied an unchanged stock texture into the addon.'
    }

    $resolverSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ExtractedSourceAssetResolver.cs' -Raw
    if (-not $resolverSource.Contains('RetailTextureOverrideService.RepairMissingRetailTextureReferences(addonContentRoot)', [StringComparison]::Ordinal)) {
        throw 'Supporting source staging is not wired to retail texture fallback repair.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'PREPARE retail VMAT fallback and stock-texture cleanup smoke passed.'
