$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static

$isMaterial = $type.GetMethod('IsMaterialReference', $flags)
$isTexture = $type.GetMethod('IsTextureReference', $flags)
$toCompiled = $type.GetMethod('ToCompiledResourcePath', $flags)
if ($null -eq $isMaterial -or $null -eq $isTexture -or $null -eq $toCompiled) {
    throw 'Hero extraction dependency helpers were not found.'
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

$settingsType = $assembly.GetType('Deadlimit.Core.ToolPathSettings', $true)
$settings = [Activator]::CreateInstance($settingsType)
if ($settings.ExtractHeroTextures) {
    throw 'ExtractHeroTextures must default to false.'
}

$settingsFormType = $assembly.GetType('Deadlimit.App.SettingsForm', $true)
$instanceFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
if ($null -eq $settingsFormType.GetField('_extractHeroTexturesCheck', $instanceFlags)) {
    throw 'Settings hero texture checkbox is missing.'
}
if ($null -eq $settingsFormType.GetField('_initialExtractHeroTextures', $instanceFlags)) {
    throw 'Settings initial hero texture state is missing.'
}
if ($null -eq $settingsFormType.GetMethod('AddHeroTextureExtractionRow', $instanceFlags)) {
    throw 'Settings hero texture row builder is missing.'
}

$inheritance = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/RetailVmdlInheritance.cs' -Raw
$online = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/OnlinePreparationSession.cs' -Raw
$requiredInheritance = @(
    'ProjectStore.GetToolPathSettings().ExtractHeroTextures',
    'RetailTextureOverrideService.BuildTargetIndex(sourceRoot)',
    'RetailTextureOverrideService.ResolveProjectRootOverrides(',
    'RetailTextureOverrideService.StageProjectRootOverrides('
)
foreach ($pattern in $requiredInheritance) {
    if (-not $inheritance.Contains($pattern, [StringComparison]::Ordinal)) {
        throw "PREPARE retail texture wiring is missing: $pattern"
    }
}
if (-not $online.Contains('RetailTextureOverrideService.ResolveOnlineTextureTarget(', [StringComparison]::Ordinal)) {
    throw 'ONLINE PREPARATION retail texture target routing is missing.'
}

Write-Host 'Hero texture dependency and pipeline contract smoke passed.'
