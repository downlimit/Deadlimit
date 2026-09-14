[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$expectedNames = @(
    'Default', 'Old Default', 'Headlight', 'Dark', 'Sunny', 'Overcast',
    'Purple Sunset', 'Field', 'Midtown', 'Interior', 'Interior Factory',
    'Studio Scene', 'Particle Preview with Postproc', 'Nuke Basic',
    'Nuke Basic + Headlight', 'Nuke Basic Mono',
    'Nuke Basic Mono + Headlight', 'Dust2 Basic',
    'Dust2 Basic + Headlight', 'Haze V1'
)

$catalogPath = Join-Path $shadeRoot 'lighting\preview-presets.json'
$catalog = Get-Content -LiteralPath $catalogPath -Raw | ConvertFrom-Json
$names = @($catalog.presets | ForEach-Object name)
Assert-True ($names.Count -eq 20) 'Lighting Preview catalog must contain exactly 20 presets.'
Assert-True (($names -join '|') -ceq ($expectedNames -join '|')) 'Lighting Preview names/order must match Reduced CSDK.'
Assert-True (($names | Sort-Object -Unique).Count -eq 20) 'Lighting Preview names must be unique.'

$byName = @{}
foreach ($preset in $catalog.presets) { $byName[$preset.name] = $preset }
foreach ($preset in $catalog.presets) {
    if ($preset.inherits) {
        Assert-True ($byName.ContainsKey($preset.inherits)) "Unknown inherited preset: $($preset.inherits)"
    }
    Assert-True ($null -ne $preset.backgroundMap -or $null -ne $preset.inherits) "Missing background source: $($preset.name)"
}

$default = $byName['Default']
Assert-True ($default.lights.Count -eq 2) 'Default must retain key and fill lights.'
Assert-True ([bool] $default.lights[0].castsShadows) 'Default key must retain direct-shadow intent.'
Assert-True (-not [bool] $default.lights[1].castsShadows) 'Default fill must remain unshadowed.'
Assert-True ($default.environment.sourceImage -eq 'materials/editor/sky_default_grey_exr_98c96aa.png') 'Default must bind the recovered CSDK sky image.'
Assert-True ([bool] $default.rim.enabled) 'Default must enable the runtime-confirmed NPR rim branch.'
Assert-True ([math]::Abs([double] $default.rim.cutoff - 1.0) -lt 1e-9) 'Default rim cutoff must match cb0[11].y.'
Assert-True ([math]::Abs([double] $default.rim.sharpness - 0.01) -lt 1e-9) 'Default rim sharpness must match cb0[11].z.'
Assert-True ([math]::Abs([double] $default.rim.strength - 0.3) -lt 1e-9) 'Default rim strength must match cb0[11].w.'
Assert-True (($default.rim.upRamp -join '|') -eq '0|1') 'Default rim up-ramp must match cb0[12].xy.'
Assert-True ([bool] $default.rim.depthOcclusion) 'Default must retain the captured scene-depth occlusion intent.'

$plugin = Get-Content -LiteralPath (Join-Path $shadeRoot 'painter_plugins\deadlimit_apply.py') -Raw
$hero = Get-Content -LiteralPath (Join-Path $shadeRoot 'shaders\Deadlock_Hero.glsl') -Raw
$outline = Get-Content -LiteralPath (Join-Path $shadeRoot 'shaders\Deadlock_Outline.glsl') -Raw
$profile = Get-Content -LiteralPath (Join-Path $shadeRoot 'profiles\ivy.json') -Raw | ConvertFrom-Json

Assert-True $plugin.Contains('lighting_preset_combo') 'Deadlimit panel must expose the Lighting Preset selector.'
Assert-True $plugin.Contains('dl_preset_key_casts_shadows') 'Preset shadow intent must reach the hero shader.'
Assert-True $plugin.Contains('"dl_selected_environment"') 'Available CSDK environments must become the shader-owned reflection source.'
Assert-True $plugin.Contains('"dl_environment_specular_enabled": bool(environment_bound)') 'Missing CSDK environments must disable character reflections.'
Assert-True $hero.Contains('getAO(inputs.sparse_coord, true, true)') 'Deadlock authored AO must bypass Painter AO Intensity.'
Assert-True $hero.Contains('if (!dl_environment_available)') 'Absent selected environment must disable reflection sampling.'
Assert-True $hero.Contains('DLRimSettings dlActiveRimSettings()') 'Rim must be owned by Lighting Preview state.'
Assert-True $hero.Contains('exp2(exponent - 1.0) * pow(wing, exponent)') 'Rim must use the recovered symmetric sharpness curve.'
Assert-True $hero.Contains('settings.strength * ambientOcclusion * rimMask * painterDepthOcclusion') 'Rim must use authored AO and tint_rim.g with Painter depth suppression.'
Assert-True (-not $hero.Contains('profile.rimLightingEnabled')) 'Character profiles must not hard-disable preset rim.'
Assert-True ($null -eq $profile.rimLighting) 'Ivy profile must not own rim settings.'
Assert-True $outline.Contains('//: state blend add_multiply') 'Outline must use the recovered dual-source blend family.'
Assert-True $outline.Contains('color1Output(vec4(destinationTint, 1.0))') 'Outline must provide the recovered destination multiplier.'

Write-Output 'Deadlimit Lighting Preview contract smoke passed.'
