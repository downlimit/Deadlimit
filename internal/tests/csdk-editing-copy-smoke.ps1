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
    Write-TestFile $source 'particles\abilities\tengu\stone_form.vpcf' 'retail fx v2' | Out-Null
    Write-TestFile $source 'particles\abilities\tengu\stone_form.vsnap' 'retail snap v2' | Out-Null
    Write-TestFile $source 'models\heroes_wip\ivy\ivy.vmdl' 'retail model - must not copy' | Out-Null
    Write-TestFile $source 'materials\heroes\ivy\body_color.png' 'texture - must not copy' | Out-Null

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
    if (-not ([IO.Path]::GetFullPath([string]$result.BackupFolder)).StartsWith([IO.Path]::GetFullPath([string]$backupParent), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Backup escaped fx_and_mat_bckps: $($result.BackupFolder)"
    }

    $bodyTarget = Join-Path $addon 'models\heroes_wip\ivy\body.vmat'
    $fxTarget = Join-Path $addon 'particles\abilities\tengu\stone_form.vpcf'
    $snapTarget = Join-Path $addon 'particles\abilities\tengu\stone_form.vsnap'
    $preparedBody = Get-Content -LiteralPath $bodyTarget -Raw
    if (-not $preparedBody.Contains('"TextureColor" "materials/models/heroes/ivy/body_color.vtex"', [StringComparison]::Ordinal)) {
        throw 'Copied VMAT did not restore the retail VTEX for a shader-remapped texture parameter.'
    }
    if (-not $preparedBody.Contains('"g_tSelfIllumMask" "materials/particle/projected/ground_crack_shatter_trans.vtex"', [StringComparison]::Ordinal)) {
        throw 'Copied VMAT did not restore the retail VTEX for an exact texture parameter.'
    }
    if ($preparedBody.Contains('"TextureColor" "materials/models/heroes/ivy/body_color.png"', [StringComparison]::Ordinal) -or
        $preparedBody.Contains('"g_tSelfIllumMask" "materials/particle/projected/ground_crack_shatter_trans.png"', [StringComparison]::Ordinal)) {
        throw 'Copied VMAT still points active texture slots at decompiled image files.'
    }
    $sourceBodyAfterCopy = Get-Content -LiteralPath $bodySource -Raw
    if (-not $sourceBodyAfterCopy.Contains('body_color.png', [StringComparison]::Ordinal)) {
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
    if (Test-Path -LiteralPath (Join-Path $addon 'materials\heroes\ivy\body_color.png')) {
        throw 'CSDK editing copy incorrectly copied an image texture.'
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
    if (-not (Get-Content -LiteralPath $bodyTarget -Raw).Contains('body_color.vtex', [StringComparison]::Ordinal)) {
        throw 'No-backup material refresh did not restore the retail VTEX reference.'
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
        throw 'An unchanged prepared VMAT was treated as a fresh overwrite.'
    }

    $dialog = Get-Content -LiteralPath 'internal/src/Deadlimit/App/HeroExtractionOptionsDialog.cs' -Raw
    foreach ($required in @(
        'Extract hero',
        'Extract abilities',
        'Extract portraits & UI',
        'Extract textures',
        'Copy materials to CSDK for editing',
        'Copy ability FX to CSDK for editing',
        'copyAbilityFxCheck.Enabled = extractAbilitiesCheck.Checked',
        'BackupCsdkOverwrites: !removeBackupAfterSuccess'
    )) {
        if (-not $dialog.Contains($required, [StringComparison]::Ordinal)) {
            throw "Extraction dialog contract is missing: $required"
        }
    }

    $extraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.cs' -Raw
    foreach ($required in @(
        'if (options.ExtractHero)',
        'options.ExtractTextures || options.CopyMaterialsToCsdkForEditing',
        'new CsdkEditableAssetCopyService(_paths).Copy(',
        'options.CopyAbilityFxToCsdkForEditing && !options.ExtractAbilities'
    )) {
        if (-not $extraction.Contains($required, [StringComparison]::Ordinal)) {
            throw "Scoped extraction/CSDK-copy wiring is missing: $required"
        }
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'CSDK editing copy, retail texture fallback, backup, and extraction-scope smoke passed.'
