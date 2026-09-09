$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.dll'))
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "DeadlimitManager.dll was not found: $assemblyPath"
}

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CustomMaterialAuthoringService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$method = $type.GetMethod('ApplyMaterialNameModifiers', $flags)
if ($null -eq $method) {
    throw 'ApplyMaterialNameModifiers was not found.'
}

function Invoke-MaterialPreset([string]$text, [string]$reference, [bool]$vertexColorMode) {
    return [string]$method.Invoke($null, @($text, $reference, $vertexColorMode))
}

$vertexColorSource = @'
Layer0
{
    "TextureRoughness1" "[0.800000 0.800000 0.800000 0.000000]"
    "g_flMetalness" "0.000"
}
'@
$vertexColorMetal = Invoke-MaterialPreset $vertexColorSource 'materials/test/armor_vertexcolor_metal.vmat' $true
if ($vertexColorMetal -notmatch '"TextureRoughness1"\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"') {
    throw "Vertex-color metal preset did not set Roughness to 128/128/128.`n$vertexColorMetal"
}
if (([regex]::Matches($vertexColorMetal, 'TextureRoughness1')).Count -ne 1 -or $vertexColorMetal -match 'TextureRoughness1[^\r\n]*0\.800000') {
    throw "Vertex-color metal preset left a duplicate/stale Roughness assignment.`n$vertexColorMetal"
}
if ($vertexColorMetal -notmatch '"g_flMetalness"\s+"0\.800"') {
    throw "Vertex-color metal preset did not set Metalness to 0.800.`n$vertexColorMetal"
}
if ($vertexColorMetal -match 'g_flGlossiness') {
    throw "Vertex-color metal preset still writes the obsolete glossiness parameter.`n$vertexColorMetal"
}

$standardSource = @'
Layer0
{
    TextureRoughness "[0.800000 0.800000 0.800000 0.000000]"
    g_flMetalness "0.000"
}
'@
$standardMetal = Invoke-MaterialPreset $standardSource 'materials/test/armor_metal.vmat' $false
if ($standardMetal -notmatch 'TextureRoughness\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"') {
    throw "Standard metal preset did not set Roughness to 128/128/128.`n$standardMetal"
}
if (([regex]::Matches($standardMetal, 'TextureRoughness')).Count -ne 1 -or $standardMetal -match 'TextureRoughness[^\r\n]*0\.800000') {
    throw "Standard metal preset left a duplicate/stale Roughness assignment.`n$standardMetal"
}
if ($standardMetal -notmatch 'g_flMetalness\s+"0\.800"') {
    throw "Standard metal preset did not set Metalness to 0.800.`n$standardMetal"
}

$plain = Invoke-MaterialPreset $vertexColorSource 'materials/test/armor_vertexcolor.vmat' $true
if ($plain -ne $vertexColorSource) {
    throw 'A material without the metal keyword was modified by the metal preset.'
}

# Regression: old projects may have a registry-owned VMAT that has already migrated to
# DEADLIMIT_MANAGED_CUSTOM_VMAT_V5, while the registry predates explicit name-modifier
# revision tracking. Such a file is still Deadlimit-owned and must receive the preset once.
$registryStoreType = $assembly.GetType('Deadlimit.Core.ManagedCustomMaterialRegistryStore', $true)
$registryType = $assembly.GetType('Deadlimit.Core.ManagedCustomMaterialRegistry', $true)
$ownershipType = $assembly.GetType('Deadlimit.Core.ManagedCustomMaterialOwnership', $true)
$manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
$publicStatic = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static
$saveRegistry = $registryStoreType.GetMethod('Save', $publicStatic)
$loadRegistry = $registryStoreType.GetMethod('Load', $publicStatic)
$mergeOwnership = $registryStoreType.GetMethod('MergeKnownWithCurrent', $publicStatic)
if ($null -eq $saveRegistry -or $null -eq $loadRegistry -or $null -eq $mergeOwnership) {
    throw 'Managed custom material registry lifecycle methods were not found.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-metal-lifecycle-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $temp 'project'
$addonRoot = Join-Path $temp 'content\citadel_addons\ivymetal'
$sourceVmdl = Join-Path $addonRoot 'models\heroes_wip\ivy\ivy.vmdl'
$materialPath = Join-Path $addonRoot 'materials\ivymetal\statue_vertexcolor_metal.vmat'
try {
    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $sourceVmdl) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $materialPath) -Force | Out-Null
    Set-Content -LiteralPath $sourceVmdl -Value 'test vmdl' -Encoding utf8NoBOM

    $managedVmat = @'
// DEADLIMIT_MANAGED_CUSTOM_VMAT_V5
Layer0
{
    "TextureRoughness1" "[0.800000 0.800000 0.800000 0.000000]"
    "g_flMetalness" "0.000"
}
'@
    Set-Content -LiteralPath $materialPath -Value $managedVmat -Encoding utf8NoBOM

    $manifest = [Activator]::CreateInstance($manifestType)
    $manifest.ProjectFolder = $projectRoot
    $manifest.SourceVmdl = $sourceVmdl

    $ownership = [Activator]::CreateInstance(
        $ownershipType,
        [object[]]@(
            'materials/statue_vertexcolor_metal',
            'materials/ivymetal/statue_vertexcolor_metal.vmat',
            $true,
            0))
    $ownershipArray = [Array]::CreateInstance($ownershipType, 1)
    $ownershipArray.SetValue($ownership, 0)

    [void]$saveRegistry.Invoke($null, [object[]]@($manifest, $ownershipArray))

    $migrated = Get-Content -LiteralPath $materialPath -Raw
    if ($migrated -notmatch '"g_flMetalness"\s+"0\.800"') {
        throw "Registry-owned managed VMAT did not receive the one-time Metalness preset.`n$migrated"
    }
    if ($migrated -notmatch '"TextureRoughness1"\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"') {
        throw "Registry-owned managed VMAT did not receive the one-time Roughness preset.`n$migrated"
    }

    $loaded = $loadRegistry.Invoke($null, [object[]]@($manifest))
    if ($loaded.Materials.Count -ne 1 -or $loaded.Materials[0].NameModifierRevision -ne 1) {
        throw 'One-time name-modifier migration revision was not persisted.'
    }

    # A later PREPARE rebuilds current ownership from the DMX. It must retain the saved
    # revision so a user-authored Metalness override is not reset on every PREPARE.
    $currentOwnership = [Activator]::CreateInstance(
        $ownershipType,
        [object[]]@(
            'materials/statue_vertexcolor_metal',
            'materials/ivymetal/statue_vertexcolor_metal.vmat',
            $true,
            0))
    $currentArray = [Array]::CreateInstance($ownershipType, 1)
    $currentArray.SetValue($currentOwnership, 0)
    $merged = $mergeOwnership.Invoke($null, [object[]]@($loaded, $currentArray))
    if ($merged.Count -ne 1 -or $merged[0].NameModifierRevision -ne 1) {
        throw 'Saved name-modifier revision was lost while merging current DMX ownership.'
    }

    $manualVmat = $migrated -replace '"g_flMetalness"\s+"0\.800"', '"g_flMetalness" "0.000"'
    $manualVmat = $manualVmat -replace '"TextureRoughness1"\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"', '"TextureRoughness1" "[0.900000 0.900000 0.900000 0.000000]"'
    Set-Content -LiteralPath $materialPath -Value $manualVmat -Encoding utf8NoBOM
    [void]$saveRegistry.Invoke($null, [object[]]@($manifest, $merged))

    $afterManual = Get-Content -LiteralPath $materialPath -Raw
    if ($afterManual -notmatch '"g_flMetalness"\s+"0\.000"' -or
        $afterManual -notmatch '"TextureRoughness1"\s+"\[0\.900000 0\.900000 0\.900000 0\.000000\]"') {
        throw "A completed name-modifier revision overwrote a later manual material edit.`n$afterManual"
    }

    # If Material Editor stripped Deadlimit's marker before the migration revision was ever
    # recorded, fail safe: preserve the unmarked file rather than guessing that it is untouched.
    $unmarkedPath = Join-Path $addonRoot 'materials\ivymetal\artist_vertexcolor_metal.vmat'
    $unmarkedVmat = @'
Layer0
{
    "TextureRoughness1" "[0.900000 0.900000 0.900000 0.000000]"
    "g_flMetalness" "0.000"
}
'@
    Set-Content -LiteralPath $unmarkedPath -Value $unmarkedVmat -Encoding utf8NoBOM
    $unmarkedOwnership = [Activator]::CreateInstance(
        $ownershipType,
        [object[]]@(
            'materials/artist_vertexcolor_metal',
            'materials/ivymetal/artist_vertexcolor_metal.vmat',
            $true,
            0))
    $unmarkedArray = [Array]::CreateInstance($ownershipType, 1)
    $unmarkedArray.SetValue($unmarkedOwnership, 0)
    [void]$saveRegistry.Invoke($null, [object[]]@($manifest, $unmarkedArray))
    $unmarkedAfter = Get-Content -LiteralPath $unmarkedPath -Raw
    if ($unmarkedAfter -notmatch '"g_flMetalness"\s+"0\.000"' -or
        $unmarkedAfter -notmatch '"TextureRoughness1"\s+"\[0\.900000 0\.900000 0\.900000 0\.000000\]"') {
        throw 'Unmarked registry-owned VMAT was incorrectly treated as safe for automatic preset migration.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Metal material-name preset and lifecycle smoke passed.'
