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
    -not $extraction.Contains('ExtractHeroUiResources(', [StringComparison]::Ordinal) -or
    -not $extraction.Contains('CopyPortraitsToAuthoring(', [StringComparison]::Ordinal) -or
    -not $extraction.Contains('"portraits"', [StringComparison]::Ordinal) -or
    -not $extraction.Contains('if (File.Exists(destination))', [StringComparison]::Ordinal)) {
    throw 'Hero portrait/UI extraction is not wired into the main source refresh.'
}

$uiExtraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.Ui.cs' -Raw
foreach ($required in @('HeroesVdataResourcePath', 'ResolveUiImages', 'ToCompiledResourcePath', 'ExtractResourceLocations')) {
    if (-not $uiExtraction.Contains($required, [StringComparison]::Ordinal)) {
        throw "Hero UI extraction implementation is missing: $required"
    }
}

$serviceType = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$copyPortraits = $serviceType.GetMethod('CopyPortraitsToAuthoring', [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static)
if ($null -eq $copyPortraits) { throw 'CopyPortraitsToAuthoring was not found.' }
$copyRoot = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-portrait-copy-' + [Guid]::NewGuid().ToString('N'))
try {
    $project = Join-Path $copyRoot 'project'
    $scope = Join-Path $copyRoot 'scope'
    $sourceA = Join-Path $scope 'panorama\images\heroes\test_card.png'
    $sourceB = Join-Path $scope 'panorama\images\heroes\test_mm.tga'
    $duplicateSourceB = Join-Path $scope 'zzz\duplicate\test_mm.tga'
    $portraits = Join-Path $project '1authoring\portraits'
    $existingA = Join-Path $portraits 'test_card.png'
    New-Item -ItemType Directory -Path (Split-Path $sourceA),(Split-Path $existingA) -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $duplicateSourceB) -Force | Out-Null
    [IO.File]::WriteAllBytes($sourceA, [byte[]](1,2,3))
    [IO.File]::WriteAllBytes($sourceB, [byte[]](4,5,6))
    [IO.File]::WriteAllBytes($duplicateSourceB, [byte[]](7,8,9))
    [IO.File]::WriteAllBytes($existingA, [byte[]](9,9,9))
    $manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
    $manifest = [Activator]::CreateInstance($manifestType)
    $manifest.ProjectFolder = $project
    $copyPortraits.Invoke($null, [object[]]@($manifest, [string]$scope, [Threading.CancellationToken]::None))
    if ([IO.File]::ReadAllBytes($existingA)[0] -ne 9) {
        throw 'Portrait convenience copy overwrote an existing artist edit.'
    }
    $copiedB = Join-Path $portraits 'test_mm.tga'
    if (-not (Test-Path -LiteralPath $copiedB) -or [IO.File]::ReadAllBytes($copiedB)[0] -ne 4) {
        throw 'Portrait convenience copy did not flatten a missing 1authoring source deterministically.'
    }
    if (@(Get-ChildItem -LiteralPath $portraits -Directory).Count -ne 0) {
        throw 'Portrait convenience copy recreated extracted resource subfolders under 1authoring\portraits.'
    }
}
finally {
    Remove-Item -LiteralPath $copyRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Hero portrait and UI extraction contract smoke passed.'
