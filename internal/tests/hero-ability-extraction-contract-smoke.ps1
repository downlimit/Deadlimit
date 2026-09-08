$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$parserType = $assembly.GetType('Deadlimit.Core.HeroAbilityVdataParser', $true)
$parserFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$resolve = $parserType.GetMethod('Resolve', $parserFlags)
if ($null -eq $resolve) { throw 'Hero ability VData resolver was not found.' }

$heroes = @"
`tCitadelHeroData_Test =
`t{
`t`tm_strModelName = "models/heroes/test/test.vmdl"
`t`tm_mapBoundAbilities =
`t`t{
`t`t`tESlot_Signature_1 = "ability_test_one"
`t`t`tESlot_Signature_2 = "ability_test_two"
`t`t}
`t}
`tCitadelHeroData_Other =
`t{
`t`tm_strModelName = "models/heroes/other/other.vmdl"
`t`tm_mapBoundAbilities =
`t`t{
`t`t`tESlot_Signature_1 = "ability_other"
`t`t}
`t}
"@

$abilities = @"
`tability_test_one =
`t{
`t`t_base = "ability_test_base"
`t`tm_Particle = resource:"particles/test/one.vpcf"
`t`tm_Icon = resource:"materials/test/one.vtex"
`t}
`tability_test_two =
`t{
`t`t_multibase =
`t`t[
`t`t`t"ability_test_mix"
`t`t]
`t`tm_Model = resource:"models/test/two.vmdl"
`t}
`tability_test_base =
`t{
`t`tm_Particle = resource:"particles/test/base.vpcf"
`t}
`tability_test_mix =
`t{
`t`tm_Material = resource:"materials/test/mix.vmat"
`t}
`tability_other =
`t{
`t`tm_Particle = resource:"particles/other/not_selected.vpcf"
`t}
"@

$selection = $resolve.Invoke($null, @(
    [string]$heroes,
    [string]$abilities,
    [string]'models/heroes/test/test.vmdl_c'))

$names = @($selection.AbilityNames)
$expectedNames = @('ability_test_base', 'ability_test_mix', 'ability_test_one', 'ability_test_two')
if ($names.Count -ne $expectedNames.Count) {
    throw "Expected $($expectedNames.Count) selected ability definitions, got $($names.Count): $($names -join ', ')"
}
foreach ($name in $expectedNames) {
    if ($names -notcontains $name) { throw "Selected hero ability closure is missing '$name'." }
}
if ($names -contains 'ability_other') { throw 'Ability resolver leaked an ability from another hero.' }

$roots = @($selection.VisualResourcePaths)
$expectedRoots = @(
    'materials/test/mix.vmat',
    'materials/test/one.vtex',
    'models/test/two.vmdl',
    'particles/test/base.vpcf',
    'particles/test/one.vpcf'
)
foreach ($root in $expectedRoots) {
    if ($roots -notcontains $root) { throw "Ability visual closure is missing '$root'." }
}
if ($roots -contains 'particles/other/not_selected.vpcf') {
    throw 'Ability visual closure leaked a resource from another hero.'
}

$serviceType = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$visualDependency = $serviceType.GetMethod('IsAbilityVisualDependency', $parserFlags)
if ($null -eq $visualDependency) { throw 'Ability visual dependency filter was not found.' }
if ([bool]$visualDependency.Invoke($null, @('materials/test/one.vtex', $false))) {
    throw 'Ability VTEX must be excluded when Extract textures is off.'
}
if (-not [bool]$visualDependency.Invoke($null, @('materials/test/one.vtex', $true))) {
    throw 'Ability VTEX must be included when Extract textures is on.'
}
if (-not [bool]$visualDependency.Invoke($null, @('particles/test/one.vpcf', $false))) {
    throw 'Ability VPCF must remain included when only Extract abilities is on.'
}

Write-Host 'Hero ability extraction contract smoke passed.'
