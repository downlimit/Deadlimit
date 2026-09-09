$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$parserType = $assembly.GetType('Deadlimit.Core.HeroAbilityVdataParser', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$resolveUi = $parserType.GetMethod('ResolveUiImages', $flags)
if ($null -eq $resolveUi) { throw 'Hero UI VData resolver was not found.' }

$heroes = @"
`tCitadelHeroData_Test =
`t{
`t`tm_strModelName = "models/heroes/test/test.vmdl"
`t`tm_strIconImageSmall = "s2r://panorama/images/heroes/test_sm_psd.vtex"
`t`tm_strIconHeroCard = "s2r://panorama/images/heroes/test_card_psd.vtex"
`t`tm_strIconHeroCardCritical = "s2r://panorama/images/heroes/test_card_critical_psd.vtex"
`t`tm_strIconHeroCardGloat = "s2r://panorama/images/heroes/test_card_gloat_psd.vtex"
`t`tm_strMinimapImage = "file://{images}/heroes/test_mm.png"
`t`tm_strTopBarVertical = "{images}/heroes/test_vertical.png"
`t}
`tCitadelHeroData_Other =
`t{
`t`tm_strModelName = "models/heroes/other/other.vmdl"
`t`tm_strIconHeroCard = "s2r://panorama/images/heroes/other_card_psd.vtex"
`t`tm_strMinimapImage = "s2r://panorama/images/heroes/other_mm_psd.vtex"
`t}
"@

$selection = $resolveUi.Invoke($null, @(
    [string]$heroes,
    [string]'models/heroes/test/test.vmdl_c'))
$paths = @($selection.ImageResourcePaths)
$expected = @(
    'panorama/images/heroes/test_card_critical_psd.vtex',
    'panorama/images/heroes/test_card_gloat_psd.vtex',
    'panorama/images/heroes/test_card_psd.vtex',
    'panorama/images/heroes/test_mm_png.vtex',
    'panorama/images/heroes/test_sm_psd.vtex',
    'panorama/images/heroes/test_vertical_png.vtex'
)
foreach ($path in $expected) {
    if ($paths -notcontains $path) { throw "Hero UI resolver is missing '$path'." }
}
if ($paths.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) hero UI resources, got $($paths.Count): $($paths -join ', ')"
}
if ($paths -contains 'panorama/images/heroes/other_card_psd.vtex') {
    throw 'Hero UI resolver leaked a portrait from another hero.'
}

$optionsType = $assembly.GetType('Deadlimit.Core.HeroExtractionOptions', $true)
$sourceOnly = $optionsType.GetProperty('SourceOnly').GetValue($null)
if ($sourceOnly.ExtractPortraitsAndUi) {
    throw 'SourceOnly extraction must disable portrait/UI extraction.'
}

$dialog = Get-Content -LiteralPath 'internal/src/Deadlimit/App/HeroExtractionOptionsDialog.cs' -Raw
if (-not $dialog.Contains('Extract portraits & UI', [StringComparison]::Ordinal) -or
    -not $dialog.Contains('Извлекать портреты и UI', [StringComparison]::Ordinal)) {
    throw 'Extraction dialog does not expose the portrait/UI checkbox in both locales.'
}

$extraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.cs' -Raw
if (-not $extraction.Contains('options.ExtractPortraitsAndUi', [StringComparison]::Ordinal) -or
    -not $extraction.Contains('ExtractHeroUiResources(', [StringComparison]::Ordinal)) {
    throw 'Hero portrait/UI extraction is not wired into the main source refresh.'
}

$uiExtraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.Ui.cs' -Raw
foreach ($required in @('HeroesVdataResourcePath', 'ResolveUiImages', 'ToCompiledResourcePath', 'ExtractResourceLocations')) {
    if (-not $uiExtraction.Contains($required, [StringComparison]::Ordinal)) {
        throw "Hero UI extraction implementation is missing: $required"
    }
}

Write-Host 'Hero portrait and UI extraction contract smoke passed.'
