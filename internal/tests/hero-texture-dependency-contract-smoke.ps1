$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static

$isBridge = $type.GetMethod('IsTextureDependencyBridgeReference', $flags)
$isMaterial = $type.GetMethod('IsMaterialReference', $flags)
$isTexture = $type.GetMethod('IsTextureReference', $flags)
$toCompiled = $type.GetMethod('ToCompiledResourcePath', $flags)
if ($null -eq $isBridge -or $null -eq $isMaterial -or $null -eq $isTexture -or $null -eq $toCompiled) {
    throw 'Hero extraction dependency helpers were not found.'
}

if (-not [bool]$isBridge.Invoke($null, @('models/heroes/ivy/ivy.vmdl'))) {
    throw 'VMDL dependency bridge was not recognized.'
}
if (-not [bool]$isBridge.Invoke($null, @('models/heroes/ivy/ivy_model.vmesh'))) {
    throw 'VMesh dependency bridge was not recognized.'
}
if (-not [bool]$isBridge.Invoke($null, @('models/heroes/ivy/ivy_model.vmesh_c'))) {
    throw 'Compiled VMesh dependency bridge was not recognized.'
}
if ([bool]$isBridge.Invoke($null, @('models/heroes/ivy/asset_sequences.vagrp'))) {
    throw 'Animation group was incorrectly classified as a texture dependency bridge.'
}

if (-not [bool]$isMaterial.Invoke($null, @('models/heroes/ivy/body.vmat'))) {
    throw 'VMAT dependency was not recognized.'
}
if (-not [bool]$isMaterial.Invoke($null, @('models/heroes/ivy/body.vmat_c'))) {
    throw 'Compiled VMAT dependency was not recognized.'
}
if ([bool]$isMaterial.Invoke($null, @('models/heroes/ivy/body.vtex'))) {
    throw 'VTEX was incorrectly classified as a material dependency.'
}

if (-not [bool]$isTexture.Invoke($null, @('models/heroes/ivy/body_color.vtex'))) {
    throw 'VTEX dependency was not recognized.'
}
if (-not [bool]$isTexture.Invoke($null, @('models/heroes/ivy/body_color.vtex_c'))) {
    throw 'Compiled VTEX dependency was not recognized.'
}
if ([bool]$isTexture.Invoke($null, @('models/heroes/ivy/body.vmat'))) {
    throw 'VMAT was incorrectly classified as a texture dependency.'
}

$compiledMaterial = [string]$toCompiled.Invoke($null, @('models/heroes/ivy/body.vmat'))
if ($compiledMaterial -ne 'models/heroes/ivy/body.vmat_c') {
    throw "Unexpected compiled material path: $compiledMaterial"
}
$alreadyCompiled = [string]$toCompiled.Invoke($null, @('models/heroes/ivy/body_color.vtex_c'))
if ($alreadyCompiled -ne 'models/heroes/ivy/body_color.vtex_c') {
    throw "Compiled dependency path was modified: $alreadyCompiled"
}

$optionsType = $assembly.GetType('Deadlimit.Core.HeroExtractionOptions', $true)
$sourceOnly = $optionsType.GetProperty('SourceOnly').GetValue($null)
if ($sourceOnly.ExtractTextures -or $sourceOnly.ExtractAbilities) {
    throw 'SourceOnly extraction options must disable both optional extraction scopes.'
}

$manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
$manifest = [Activator]::CreateInstance($manifestType)
if ($manifest.LastSourceExtractionIncludedTextures -or $manifest.LastSourceExtractionIncludedAbilities) {
    throw 'New project manifests must default optional extraction flags to false.'
}

$settingsFormType = $assembly.GetType('Deadlimit.App.SettingsForm', $true)
$instanceFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
if ($null -ne $settingsFormType.GetField('_extractHeroTexturesCheck', $instanceFlags)) {
    throw 'Hero texture extraction must not be a Settings checkbox.'
}
if ($null -ne $settingsFormType.GetMethod('AddHeroTextureExtractionRow', $instanceFlags)) {
    throw 'Hero texture extraction must not have a Settings row.'
}

$extraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.cs' -Raw
$requiredExtraction = @(
    'HeroExtractionOptions options',
    'options.ExtractTextures',
    'options.ExtractAbilities',
    'includeTextures || !IsTextureReference(item.Path)',
    'ExtractHeroAbilityDependencies(',
    'LastSourceExtractionIncludedTextures = options.ExtractTextures',
    'LastSourceExtractionIncludedAbilities = options.ExtractAbilities'
)
foreach ($pattern in $requiredExtraction) {
    if (-not $extraction.Contains($pattern, [StringComparison]::Ordinal)) {
        throw "Per-run extraction wiring is missing: $pattern"
    }
}

$dialog = Get-Content -LiteralPath 'internal/src/Deadlimit/App/HeroExtractionOptionsDialog.cs' -Raw
$mainForm = Get-Content -LiteralPath 'internal/src/Deadlimit/App/MainForm.cs' -Raw
if (-not $dialog.Contains('Extract textures', [StringComparison]::Ordinal) -or
    -not $dialog.Contains('Extract abilities', [StringComparison]::Ordinal)) {
    throw 'Extraction dialog does not expose both per-run checkboxes.'
}
if (-not $mainForm.Contains('HeroExtractionOptionsDialog.Show(this, hasExistingSource)', [StringComparison]::Ordinal)) {
    throw 'MainForm does not show the extraction options dialog for the extraction command.'
}
if ($mainForm.Contains('0source already contains files. Refresh it from the current Deadlock game client build?', [StringComparison]::Ordinal)) {
    throw 'Legacy conditional refresh MessageBox is still wired in MainForm.'
}

$overrideService = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/RetailTextureOverrideService.cs' -Raw
foreach ($required in @(
    'bool HasExtractedSourceFile = false',
    'existing with { HasExtractedSourceFile = true }',
    'new RetailTextureTarget(resourcePath, string.Empty, HasExtractedSourceFile: true)',
    'A source-backed VMAT is enough provenance',
    'var targetsByStem = targets'
)) {
    if (-not $overrideService.Contains($required, [StringComparison]::Ordinal)) {
        throw "Source-backed retail texture override contract is missing: $required"
    }
}
if ($overrideService.Contains('targets.Where(target => target.HasExtractedSourceFile).ToArray()', [StringComparison]::Ordinal)) {
    throw 'VMAT-backed texture overrides are still gated by extracted retail image files.'
}

$online = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/OnlinePreparationSession.cs' -Raw
if (-not $online.Contains('RetailTextureOverrideService.ResolveOnlineTextureTarget(', [StringComparison]::Ordinal)) {
    throw 'ONLINE PREPARATION retail texture target routing is missing.'
}

Write-Host 'Hero texture dependency and source-backed override contract smoke passed.'
