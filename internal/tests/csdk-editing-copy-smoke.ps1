$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CsdkEditableAssetCopyService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$copy = $type.GetMethod('CopySelectedFiles', $flags)
if ($null -eq $copy) {
    throw 'CSDK editable asset copy helper was not found.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-csdk-edit-copy-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $temp '0source'
$addon = Join-Path $temp 'content\citadel_addons\ivytest'
$backupParent = Join-Path $temp 'project\.deadlimit\fx_and_mat_bckps'

function Write-TestFile([string]$root, [string]$relative, [string]$content) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
    return $path
}

try {
    $bodyVmat = @"
Layer0
{
    "shader" "hero.vfx"
    "TextureColor" "materials/models/heroes/ivy/body_color.png"
    "g_tSelfIllumMask" "materials/particle/projected/ground_crack_shatter_trans.png"
    "Compiled Textures"
    {
        "g_tColor" "materials/models/heroes/ivy/body_color.vtex"
        "g_tSelfIllumMask" "materials/particle/projected/ground_crack_shatter_trans.vtex"
    }
}
"@
    $bodySource = Write-TestFile $source 'models\heroes_wip\ivy\body.vmat' $bodyVmat
    Write-TestFile $source 'materials\models\heroes\ivy\body_color.png' 'stock body color' | Out-Null
    Write-TestFile $source 'materials\particle\projected\ground_crack_shatter_trans.png' 'stock dissolve mask' | Out-Null
    Write-TestFile $source 'particles\abilities\tengu\stone_form.vpcf' 'retail fx v2' | Out-Null
    Write-TestFile $source 'particles\abilities\tengu\stone_form.vsnap' 'retail snap v2' | Out-Null
    Write-TestFile $source 'models\heroes_wip\ivy\ivy.vmdl' 'retail model - must not copy' | Out-Null

    Write-TestFile $addon 'models\heroes_wip\ivy\body.vmat' 'artist body before refresh' | Out-Null
    Write-TestFile $addon 'particles\abilities\tengu\stone_form.vpcf' 'artist fx before refresh' | Out-Null

    $result = $copy.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string]$backupParent,
        [bool]$true,
        [bool]$true,
        [bool]$true,
        [Threading.CancellationToken]::None))

    if ($result.MaterialCopiedCount -ne 1) {
        throw "Expected one copied VMAT, got $($result.MaterialCopiedCount)."
    }
    if ($result.AbilityFxCopiedCount -ne 2) {
        throw "Expected VPCF + VSNAP ability FX copies, got $($result.AbilityFxCopiedCount)."
    }
    if ($result.OverwrittenCount -ne 2) {
        throw "Expected two overwritten CSDK files, got $($result.OverwrittenCount)."
    }
    if ([string]::IsNullOrWhiteSpace([string]$result.BackupFolder)) {
        throw 'Backup-enabled copy did not create a timestamp backup folder.'
    }

    $bodyTarget = Join-Path $addon 'models\heroes_wip\ivy\body.vmat'
    $colorTarget = Join-Path $addon 'materials\models\heroes\ivy\body_color.png'
    $maskTarget = Join-Path $addon 'materials\particle\projected\ground_crack_shatter_trans.png'
    $fxTarget = Join-Path $addon 'particles\abilities\tengu\stone_form.vpcf'
    $snapTarget = Join-Path $addon 'particles\abilities\tengu\stone_form.vsnap'

    $preparedBody = Get-Content -LiteralPath $bodyTarget -Raw
    if (-not $preparedBody.Contains('"TextureColor" "materials/models/heroes/ivy/body_color.png"', [StringComparison]::Ordinal)) {
        throw 'Copied VMAT lost its editable Color source path.'
    }
    if (-not $preparedBody.Contains('"g_tSelfIllumMask" "materials/particle/projected/ground_crack_shatter_trans.png"', [StringComparison]::Ordinal)) {
        throw 'Copied VMAT lost its editable FX texture source path.'
    }
    if (-not (Test-Path -LiteralPath $colorTarget) -or -not (Test-Path -LiteralPath $maskTarget)) {
        throw 'CSDK material copy did not include required authoring texture dependencies.'
    }
    if ((Get-Content -LiteralPath $colorTarget -Raw).Trim() -ne 'stock body color') {
        throw 'CSDK Color authoring texture bytes are wrong.'
    }
    if ((Get-Content -LiteralPath $maskTarget -Raw).Trim() -ne 'stock dissolve mask') {
        throw 'CSDK FX mask authoring texture bytes are wrong.'
    }
    if (-not (Get-Content -LiteralPath $bodySource -Raw).Contains('body_color.png', [StringComparison]::Ordinal)) {
        throw '0source VMAT was modified while preparing the CSDK working copy.'
    }
    if ((Get-Content -LiteralPath $fxTarget -Raw).Trim() -ne 'retail fx v2') {
        throw 'VPCF was not refreshed from 0source.'
    }
    if ((Get-Content -LiteralPath $snapTarget -Raw).Trim() -ne 'retail snap v2') {
        throw 'VSNAP was not copied from 0source.'
    }
    if (Test-Path -LiteralPath (Join-Path $addon 'models\heroes_wip\ivy\ivy.vmdl')) {
        throw 'CSDK editing copy incorrectly copied a VMDL as ability FX.'
    }

    $bodyBackup = Join-Path ([string]$result.BackupFolder) 'models\heroes_wip\ivy\body.vmat'
    $fxBackup = Join-Path ([string]$result.BackupFolder) 'particles\abilities\tengu\stone_form.vpcf'
    if ((Get-Content -LiteralPath $bodyBackup -Raw).Trim() -ne 'artist body before refresh') {
        throw 'VMAT backup did not preserve the previous CSDK edit.'
    }
    if ((Get-Content -LiteralPath $fxBackup -Raw).Trim() -ne 'artist fx before refresh') {
        throw 'VPCF backup did not preserve the previous CSDK edit.'
    }

    $backupFolderCount = @(Get-ChildItem -LiteralPath $backupParent -Directory).Count
    Set-Content -LiteralPath $bodyTarget -Value 'manual body after first copy' -Encoding utf8NoBOM
    $second = $copy.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string]$backupParent,
        [bool]$true,
        [bool]$false,
        [bool]$false,
        [Threading.CancellationToken]::None))

    if ($second.MaterialCopiedCount -ne 1 -or $second.AbilityFxCopiedCount -ne 0) {
        throw 'Material-only no-backup refresh copied the wrong source set.'
    }
    if ($null -ne $second.BackupFolder) {
        throw 'YES, NO BACKUP semantics created a persistent CSDK backup.'
    }
    if (@(Get-ChildItem -LiteralPath $backupParent -Directory).Count -ne $backupFolderCount) {
        throw 'No-backup refresh created an unexpected timestamp backup folder.'
    }
    if (-not (Get-Content -LiteralPath $bodyTarget -Raw).Contains('body_color.png', [StringComparison]::Ordinal)) {
        throw 'No-backup material refresh did not restore the editable PNG reference.'
    }

    $third = $copy.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string]$backupParent,
        [bool]$true,
        [bool]$false,
        [bool]$true,
        [Threading.CancellationToken]::None))
    if ($third.MaterialCopiedCount -ne 0 -or $third.OverwrittenCount -ne 0 -or $null -ne $third.BackupFolder) {
        throw 'An unchanged editable material tree was treated as a fresh overwrite.'
    }

    $dialog = Get-Content -LiteralPath 'internal/src/Deadlimit/App/HeroExtractionOptionsDialog.cs' -Raw
    foreach ($required in @(
        'Extract hero',
        'Extract abilities',
        'Extract portraits & UI',
        'Extract textures by dependencies',
        'Извлекать текстуры по зависимостям',
        'Copy materials to CSDK for editing',
        'Copy ability FX to CSDK for editing',
        'DMX — CSDK build source',
        'glTF — DCC source in 0source\\glTFsource',
        'glTF files, buffers and texture data are refreshed only inside 0source\\glTFsource',
        'copyAbilityFxCheck.Enabled = false',
        'Отключено для Reduced CSDK 12',
        'для редактирования самого графа частиц нужен совместимый более новый CSDK',
        'BackupCsdkOverwrites: !removeBackupAfterSuccess'
    )) {
        if (-not $dialog.Contains($required, [StringComparison]::Ordinal)) {
            throw "Extraction dialog contract is missing: $required"
        }
    }
    if ($dialog.Contains('copyAbilityFxCheck.Enabled = extractAbilitiesCheck.Checked', [StringComparison]::Ordinal)) {
        throw 'The unsafe Reduced CSDK ability-FX editing checkbox can still be enabled.'
    }

    $extraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.cs' -Raw
    foreach ($required in @(
        'if (options.ExtractHero)',
        'ExtractResourceFolder(',
        'heroStagingFolder,',
        'includeTextures: true',
        'options.ExtractTextures || options.CopyMaterialsToCsdkForEditing',
        'options.CopyAbilityFxToCsdkForEditing',
        'is disabled for Reduced CSDK 12 because current Deadlock VPCF sources may use an incompatible newer format',
        'isGltf ? "gltf-source-extract-staging" : "source-extract-staging"',
        'isGltf ? "gltf-source-extraction-state.json" : "source-extraction-state.json"',
        'isGltf ? "glTFsource.previous" : "0source.previous"',
        'isGltf ? null : ["glTFsource"]',
        'new CsdkEditableAssetCopyService(_paths).Copy('
    )) {
        $normalizedRequired = $required.Replace('`n', "`n")
        if (-not $extraction.Contains($normalizedRequired, [StringComparison]::Ordinal)) {
            throw "Scoped extraction/CSDK-copy wiring is missing: $required"
        }
    }

    $gltfSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.Gltf.cs' -Raw
    foreach ($required in @(
        'new GltfModelExporter(fileLoader)',
        'ProgressReporter = new Progress<string>',
        'ExportAnimations = true',
        'exporter.AnimationFilter.Add(SkeletonOnlyAnimationFilter)',
        'ExportMaterials = includeTextures',
        'SatelliteImages = true',
        'exporter.Export(resource, outputPath, cancellationToken)',
        'NormalizeMixedPrimitiveVertexColors(outputPath)',
        'Array.Fill(whiteColors, byte.MaxValue)',
        'ValidateGltfSkinningContract(outputPath)'
    )) {
        if (-not $gltfSource.Contains($required, [StringComparison]::Ordinal)) {
            throw "glTF extraction wiring is missing: $required"
        }
    }

    $heroExtractionType = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
    $normalizeVertexColors = $heroExtractionType.GetMethod(
        'NormalizeMixedPrimitiveVertexColors',
        $flags)
    if ($null -eq $normalizeVertexColors) {
        throw 'glTF mixed-primitive Vertex Color normalizer was not found.'
    }

    $gltfFixtureFolder = Join-Path $temp 'gltf-vertex-color'
    New-Item -ItemType Directory -Path $gltfFixtureFolder -Force | Out-Null
    $gltfFixturePath = Join-Path $gltfFixtureFolder 'mixed.gltf'
    $gltfBufferPath = Join-Path $gltfFixtureFolder 'mixed.bin'
    [IO.File]::WriteAllBytes($gltfBufferPath, [byte[]]@())
    Set-Content -LiteralPath $gltfFixturePath -Encoding utf8NoBOM -Value @'
{
  "buffers": [{ "uri": "mixed.bin", "byteLength": 0 }],
  "bufferViews": [{ "buffer": 0, "byteOffset": 0, "byteLength": 0 }],
  "accessors": [
    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
    { "bufferView": 0, "componentType": 5126, "count": 3, "type": "VEC3" },
    { "bufferView": 0, "componentType": 5121, "normalized": true, "count": 3, "type": "VEC4" }
  ],
  "meshes": [
    { "primitives": [
      { "attributes": { "POSITION": 0 } },
      { "attributes": { "POSITION": 1, "COLOR_0": 2 } }
    ] },
    { "primitives": [
      { "attributes": { "POSITION": 0 } }
    ] }
  ]
}
'@

    $normalizeVertexColors.Invoke($null, [object[]]@([string]$gltfFixturePath))
    $normalizedGltf = Get-Content -LiteralPath $gltfFixturePath -Raw | ConvertFrom-Json
    $generatedAccessorIndex = $normalizedGltf.meshes[0].primitives[0].attributes.COLOR_0
    if ($generatedAccessorIndex -ne 3) {
        throw "Expected a generated white COLOR_0 accessor at index 3, got $generatedAccessorIndex."
    }
    if ($normalizedGltf.meshes[0].primitives[1].attributes.COLOR_0 -ne 2) {
        throw 'The original COLOR_0 accessor was replaced.'
    }
    if ($normalizedGltf.meshes[1].primitives[0].attributes.PSObject.Properties.Name -contains 'COLOR_0') {
        throw 'A mesh with no Vertex Color primitives received an unnecessary COLOR_0 accessor.'
    }
    $generatedAccessor = $normalizedGltf.accessors[$generatedAccessorIndex]
    if ($generatedAccessor.componentType -ne 5121 -or
        $generatedAccessor.normalized -ne $true -or
        $generatedAccessor.count -ne 3 -or
        $generatedAccessor.type -ne 'VEC4') {
        throw 'Generated white COLOR_0 accessor metadata is invalid.'
    }
    $whiteBytes = [IO.File]::ReadAllBytes($gltfBufferPath)
    if ($whiteBytes.Count -ne 12 -or
        @($whiteBytes | Where-Object { $_ -ne [byte]255 }).Count -ne 0 -or
        $normalizedGltf.buffers[0].byteLength -ne 12) {
        throw 'Generated white COLOR_0 buffer data is invalid.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'CSDK editable material/FX copy, authoring texture dependencies and backup smoke passed.'
