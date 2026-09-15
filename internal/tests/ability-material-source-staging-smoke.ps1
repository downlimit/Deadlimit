$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CsdkEditableAssetCopyService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$stage = $type.GetMethod('StageMissingAbilityMaterialSources', $flags)
if ($null -eq $stage) {
    throw 'Missing ability material source staging helper was not found.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-ability-material-stage-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $temp '0source'
$addon = Join-Path $temp 'content\citadel_addons\ivytest'

function Write-TestFile([string]$root, [string]$relative, [string]$content) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
    return $path
}

try {
    $missingMaterial = @"
Layer0
{
    "shader" "hero.vfx"
    "TextureColor" "materials/particle/tengu/tether_cable_color.png"
    "Compiled Textures"
    {
        "g_tColor" "materials/particle/tengu/tether_cable_color.vtex"
    }
}
"@
    $existingMaterial = @"
Layer0
{
    "shader" "hero.vfx"
    "TextureColor" "materials/particle/tengu/existing_color.png"
}
"@

    Write-TestFile $source 'materials\models\heroes\tengu\tengu_tether_cable.vmat' $missingMaterial | Out-Null
    Write-TestFile $source 'materials\models\heroes\tengu\tengu_existing_fx.vmat' $existingMaterial | Out-Null
    Write-TestFile $source 'materials\models\heroes\tengu\unrelated_hero.vmat' 'unrelated hero material' | Out-Null
    Write-TestFile $source 'materials\particle\tengu\tether_cable_color.png' 'tether texture source' | Out-Null
    Write-TestFile $source 'materials\particle\tengu\existing_color.png' 'existing texture source' | Out-Null

    Write-TestFile $addon 'materials\models\heroes\tengu\tengu_existing_fx.vmat' 'manual artist material - preserve me' | Out-Null
    Write-TestFile $addon 'materials\particle\tengu\existing_color.png' 'manual artist texture - preserve me' | Out-Null

    [string[]]$abilityScope = @(
        'materials/models/heroes/tengu/tengu_tether_cable.vmat',
        'materials/models/heroes/tengu/tengu_existing_fx.vmat',
        'particles/abilities/tengu/tether.vpcf'
    )

    $result = $stage.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string[]]$abilityScope,
        [Threading.CancellationToken]::None))

    if ($result.MaterialCopiedCount -ne 1) {
        throw "Expected one missing ability VMAT to be staged, got $($result.MaterialCopiedCount)."
    }
    if ($result.TextureCopiedCount -ne 1) {
        throw "Expected one missing ability texture source to be staged, got $($result.TextureCopiedCount)."
    }

    $missingTarget = Join-Path $addon 'materials\models\heroes\tengu\tengu_tether_cable.vmat'
    $missingTextureTarget = Join-Path $addon 'materials\particle\tengu\tether_cable_color.png'
    if (-not (Test-Path -LiteralPath $missingTarget)) {
        throw 'Missing ability VMAT was not staged into CSDK content.'
    }
    if (-not (Test-Path -LiteralPath $missingTextureTarget)) {
        throw 'Ability VMAT authoring texture source was not staged into CSDK content.'
    }
    if (-not (Get-Content -LiteralPath $missingTarget -Raw).Contains('tether_cable_color.png', [StringComparison]::Ordinal)) {
        throw 'Staged ability VMAT content is wrong.'
    }
    if ((Get-Content -LiteralPath $missingTextureTarget -Raw).Trim() -ne 'tether texture source') {
        throw 'Staged ability texture source bytes are wrong.'
    }

    $existingTarget = Join-Path $addon 'materials\models\heroes\tengu\tengu_existing_fx.vmat'
    $existingTextureTarget = Join-Path $addon 'materials\particle\tengu\existing_color.png'
    if ((Get-Content -LiteralPath $existingTarget -Raw).Trim() -ne 'manual artist material - preserve me') {
        throw 'Automatic ability material staging overwrote an existing CSDK VMAT.'
    }
    if ((Get-Content -LiteralPath $existingTextureTarget -Raw).Trim() -ne 'manual artist texture - preserve me') {
        throw 'Automatic ability material staging overwrote an existing authoring texture.'
    }
    if (Test-Path -LiteralPath (Join-Path $addon 'materials\models\heroes\tengu\unrelated_hero.vmat')) {
        throw 'Automatic ability material staging copied a VMAT outside the ability extraction scope.'
    }

    $second = $stage.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string[]]$abilityScope,
        [Threading.CancellationToken]::None))
    if ($second.MaterialCopiedCount -ne 0 -or $second.TextureCopiedCount -ne 0) {
        throw 'Repeated ability material staging did not preserve the existing CSDK tree.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Ability material source staging smoke passed.'
