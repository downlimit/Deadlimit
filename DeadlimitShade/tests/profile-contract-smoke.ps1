[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$profilesPath = Join-Path $shadeRoot 'profiles'
$schemaPath = Join-Path $profilesPath 'schema.json'
$generatorPath = Join-Path $shadeRoot 'tools\Generate-CharacterProfiles.ps1'
$heroShaderPath = Join-Path $shadeRoot 'shaders\Deadlock_Hero.glsl'
$outlineShaderPath = Join-Path $shadeRoot 'shaders\Deadlock_Outline.glsl'

function Assert-True {
    param(
        [bool] $Condition,
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Near {
    param(
        [double] $Actual,
        [double] $Expected,
        [double] $Tolerance,
        [string] $Message
    )

    if ([Math]::Abs($Actual - $Expected) -gt $Tolerance) {
        throw "$Message Actual=$Actual Expected=$Expected"
    }
}

function Invoke-NprQuantize {
    param(
        [double] $Value,
        [double] $Sharpness
    )

    $safeSharpness = [Math]::Max(0.0, [Math]::Min(0.95, $Sharpness))
    $base = [Math]::Floor($Value)
    $fraction = $Value - $base
    $wing = if ($fraction -gt 0.5) { 1.0 - $fraction } else { $fraction }
    $exponent = 1.0 / (1.0 - $safeSharpness)
    $shapedWing = 0.5 * [Math]::Pow(2.0 * $wing, $exponent)
    return $base + $(if ($fraction -gt 0.5) { 1.0 - $shapedWing } else { $shapedWing })
}

$schemaText = Get-Content -LiteralPath $schemaPath -Raw
$profileFiles = @(Get-ChildItem -LiteralPath $profilesPath -Filter '*.json' -File |
    Where-Object Name -ne 'schema.json')
Assert-True ($profileFiles.Count -gt 0) 'At least one character profile is required.'

$profiles = @()
foreach ($profileFile in $profileFiles) {
    $profileText = Get-Content -LiteralPath $profileFile.FullName -Raw
    Assert-True ($profileText | Test-Json -Schema $schemaText) "Schema validation failed: $($profileFile.Name)"
    $profiles += $profileText | ConvertFrom-Json
}

foreach ($profile in $profiles) {
    Assert-True ($profile.directDiffuse.evidence -eq 'confirmed-pipeline-runtime') 'Captured Reduced-CSDK direct-diffuse globals must retain their runtime evidence class.'
    Assert-True ($profile.directDiffuse.runtimeSource -eq 'reduced-csdk-asset-browser') 'Direct-diffuse runtime evidence must not be presented as current retail runtime.'
    Assert-True ($profile.bounceLighting.runtimeGlobalsEvidence -eq 'confirmed-pipeline-runtime') 'Captured Reduced-CSDK bounce globals must retain their runtime evidence class.'
    Assert-True ($profile.bounceLighting.runtimeSource -eq 'reduced-csdk-asset-browser') 'Bounce runtime evidence must not be presented as current retail runtime.'
    Assert-True ($profile.bounceLighting.probeRuntimeSource -eq 'reduced-csdk-asset-browser') 'Probe-volume samples must retain their Reduced-CSDK source.'
    Assert-True ($profile.bounceLighting.probeEvidence -eq 'confirmed-pipeline-runtime') 'Captured six-direction probe values must retain runtime evidence.'
    Assert-True ($profile.bounceLighting.probeSpatialEvidence -eq 'calibrated-approximation') 'Freezing one runtime probe sample across the Painter mesh must remain explicit.'
    Assert-True ($profile.bounceLighting.evidence -eq 'calibrated-approximation') 'Painter probe colors and bounce strength remain calibrated approximations.'
    Assert-True ($profile.directDiffuse.steps -eq 2.0) 'Ivy must retain the captured two-step diffuse control.'
    Assert-True ($profile.directDiffuse.stepSharpness -eq 0.9) 'Ivy must retain the captured diffuse step sharpness.'
    Assert-True ($profile.directDiffuse.pbrBlend -eq 0.25) 'Ivy must retain the captured diffuse PBR blend.'
    Assert-True ($profile.directDiffuse.wrap -eq 0.8) 'Ivy must retain the captured direct-light wrap.'
    Assert-True ($profile.directDiffuse.normalization -eq 0.625) 'Ivy must retain the captured direct-light normalization.'
    Assert-Near ([double] $profile.bounceLighting.lightWeights[0]) 0.42 0.0000001 'Ivy must retain the captured NPR up weight.'
    Assert-Near ([double] $profile.bounceLighting.lightWeights[1]) 0.42 0.0000001 'Ivy must retain the captured NPR view weight.'
    Assert-Near ([double] $profile.bounceLighting.lightWeights[2]) 0.126 0.0000001 'Ivy must retain the captured NPR light weight.'
    Assert-True ($profile.bounceLighting.exposureControlEnabled -eq $true) 'Ivy must retain the captured NPR exposure-control gate.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[0]) 1.0 0.0000001 'Ivy must retain the captured upward exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[1]) 0.5 0.0000001 'Ivy must retain the captured side exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[2]) 0.1 0.0000001 'Ivy must retain the captured downward exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposurePbrBlend) 0.5 0.0000001 'Ivy must retain the captured exposure-control PBR blend.'
    Assert-Near ([double] $profile.bounceLighting.probePositiveX[0]) 1.064453125 0.0000001 'Ivy must retain the captured +X probe sample.'
    Assert-Near ([double] $profile.bounceLighting.probePositiveY[0]) 0.9052734375 0.0000001 'Ivy must retain the captured +Y probe sample.'
    Assert-Near ([double] $profile.bounceLighting.probePositiveZ[0]) 1.462890625 0.0000001 'Ivy must retain the captured +Z probe sample.'
    Assert-Near ([double] $profile.bounceLighting.probeNegativeX[0]) 0.5595703125 0.0000001 'Ivy must retain the captured -X probe sample.'
    Assert-Near ([double] $profile.bounceLighting.probeNegativeY[0]) 0.5546875 0.0000001 'Ivy must retain the captured -Y probe sample.'
    Assert-Near ([double] $profile.bounceLighting.probeNegativeZ[0]) 0.19970703125 0.0000001 'Ivy must retain the captured -Z probe sample.'
    Assert-Near ([double] $profile.bounceLighting.transmissiveStrength) 1.0 0.0000001 'Recovered NPR transmissive color must enter at unit strength.'
    Assert-True ($profile.previewLighting.runtimeSource -eq 'reduced-csdk-asset-browser') 'Preview direct light must retain its Reduced-CSDK source.'
    Assert-True ($profile.previewLighting.directLightEvidence -eq 'confirmed-pipeline-runtime') 'Preview direct-light values must retain runtime evidence.'
    Assert-Near ([double] $profile.previewLighting.keyIntensity) 1.6 0.0000001 'Ivy must retain the captured main-light radiance.'
    Assert-Near ([double] $profile.previewLighting.fillIntensity) 0.0 0.0000001 'The controlled runtime draw must not invent a second direct light.'
    Assert-True ([bool] $profile.bounceLighting.exposureControlEnabled) 'Ivy must retain the captured enabled NPR exposure-control gate.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[0]) 1.0 0.0000001 'Ivy must retain the captured up exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[1]) 0.5 0.0000001 'Ivy must retain the captured side exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposureTargets[2]) 0.1 0.0000001 'Ivy must retain the captured down exposure target.'
    Assert-Near ([double] $profile.bounceLighting.exposurePbrBlend) 0.5 0.0000001 'Ivy must retain the captured exposure-control PBR blend.'
    Assert-True ($profile.directSpecular.evidence -eq 'calibrated-approximation') 'Uncaptured direct-specular values must be classified as calibrated approximations.'
    Assert-True ($profile.rimLighting.evidence -eq 'calibrated-approximation') 'Uncaptured rim values must be classified as calibrated approximations.'
    Assert-True (-not $profile.directSpecular.enabled) 'Reduced CSDK Default runtime disables the direct-specular branch.'
    Assert-True ($profile.directSpecular.enabledEvidence -eq 'confirmed-pipeline-runtime') 'Direct-specular enablement must retain runtime provenance separately from calibrated controls.'
    Assert-True (-not $profile.rimLighting.enabled) 'Reduced CSDK Default runtime disables the NPR rim branch.'
    Assert-True ($profile.rimLighting.enabledEvidence -eq 'confirmed-pipeline-runtime') 'Rim enablement must retain runtime provenance separately from calibrated controls.'
    Assert-True ($profile.previewLighting.evidence -eq 'calibrated-approximation') 'Painter preview lighting must be classified as a calibrated approximation.'
}

Assert-True (($profiles | Group-Object id | Where-Object Count -gt 1).Count -eq 0) 'Character profile IDs must be unique.'
Assert-True (($profiles | Group-Object key | Where-Object Count -gt 1).Count -eq 0) 'Character profile keys must be unique.'
Assert-True (($profiles | Where-Object id -eq 0).Count -eq 0) 'Character profile ID 0 is reserved for Custom.'

$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("Deadlimit_ProfileTable_{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $tempDirectory | Out-Null
    $tempHeroShaderPath = Join-Path $tempDirectory 'Deadlock_Hero.glsl'
    $tempOutlineShaderPath = Join-Path $tempDirectory 'Deadlock_Outline.glsl'
    Copy-Item -LiteralPath $heroShaderPath -Destination $tempHeroShaderPath
    Copy-Item -LiteralPath $outlineShaderPath -Destination $tempOutlineShaderPath

    & $generatorPath -ProfilesPath $profilesPath -ShaderPaths @($tempHeroShaderPath, $tempOutlineShaderPath) | Out-Null
    Assert-True ((Get-Content -LiteralPath $tempHeroShaderPath -Raw) -ceq (Get-Content -LiteralPath $heroShaderPath -Raw)) 'Generated hero profile block is stale. Run Generate-CharacterProfiles.ps1.'
    Assert-True ((Get-Content -LiteralPath $tempOutlineShaderPath -Raw) -ceq (Get-Content -LiteralPath $outlineShaderPath -Raw)) 'Generated outline profile block is stale. Run Generate-CharacterProfiles.ps1.'
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

$heroShader = Get-Content -LiteralPath $heroShaderPath -Raw
$outlineShader = Get-Content -LiteralPath $outlineShaderPath -Raw
foreach ($source in @($heroShader, $outlineShader)) {
    Assert-True ($source.Contains('// Generated by tools/Generate-CharacterProfiles.ps1. Do not edit by hand.')) 'Both Painter shaders must contain the generated profile block.'
    Assert-True ($source.Contains('"Custom": 0')) 'Both Painter shaders must keep Custom at stable ID 0.'
    Assert-True ($source.Contains('"Ivy": 1')) 'Both Painter shaders must expose Ivy at stable ID 1.'
}

Assert-True ($heroShader.Contains('float dlNprQuantize(')) 'Hero shader is missing the NPR quantizer.'
Assert-True ($heroShader.Contains('"N dot L (Signed)": 7')) 'Hero shader is missing the signed N dot L debug view.'
Assert-True ($heroShader.Contains('"Wrapped Direct Diffuse": 8')) 'Hero shader is missing the wrapped debug view.'
Assert-True ($heroShader.Contains('"Quantized Direct Diffuse": 9')) 'Hero shader is missing the quantized debug view.'
Assert-True ($heroShader.Contains('"Final Direct Diffuse": 10')) 'Hero shader is missing the final diffuse debug view.'
Assert-True ($heroShader.Contains('"Direct Specular": 12')) 'Hero shader is missing the direct-specular isolation view.'
Assert-True ($heroShader.Contains('"Rim Contribution": 13')) 'Hero shader is missing the rim isolation view.'
Assert-True ($heroShader.Contains('"NPR Lighting Composite": 14')) 'Hero shader is missing the lighting-composite isolation view.'
Assert-True ($heroShader.Contains('"Painter PBR Baseline": 15')) 'Hero shader is missing the controlled Painter PBR comparison view.'
Assert-True ($heroShader.Contains('dl_lighting_input_mode == 1')) 'Hero shader is missing deterministic neutral diagnostic inputs.'
Assert-True ($heroShader.Contains('//: param auto main_light')) 'Hero shader must bind Painter main-light rotation for Shift+RMB lighting control.'
Assert-True ($heroShader.Contains('dlPainterYawAdjustedDirection(characterProfile.keyLightDirection)')) 'Material/Retail mode must rotate the Deadlimit key from Painter main_light.'
Assert-True ($heroShader.Contains('dlPainterYawAdjustedDirection(characterProfile.fillLightDirection)')) 'Material/Retail mode must rotate the Deadlimit fill from Painter main_light.'
Assert-True ($heroShader.Contains('profile.bounceProbePositiveX')) 'The fixed Ivy preview must use the captured six-direction probe sample.'
Assert-True ($heroShader.Contains('vec3 dlEvaluateSixDirectionalProbe(')) 'Bounce must evaluate the recovered ambient-cube basis.'
Assert-True ($heroShader.Contains('DLDirectSpecularSample dlEvaluateDirectSpecular(')) 'Hero shader is missing the controlled direct-specular contribution.'
Assert-True ($heroShader.Contains('DLRimSample dlEvaluateRim(')) 'Hero shader is missing the controlled rim contribution.'
Assert-True ($heroShader.Contains('DLBounceSample dlEvaluateBounce(')) 'Hero shader is missing the Deadlock-structured bounce approximation.'
Assert-True ($heroShader.Contains('profile.bounceDfaoInfluenceRange')) 'Bounce must expose the captured DfAO influence range.'
Assert-True ($heroShader.Contains('profile.bounceLightWeights')) 'Bounce must consume the captured NPR light weights.'
Assert-True ($heroShader.Contains('profile.bounceExposureTargets')) 'Bounce must consume the captured NPR exposure targets.'
Assert-True ($heroShader.Contains('dlExposureControlPreserveChroma')) 'Bounce must preserve probe chromaticity while fitting captured luminance targets.'
Assert-True ($heroShader.Contains('mix(downwardProbe, sideProbe, 2.0 * sample.quantizedCoordinate)')) 'Bounce must interpolate down, horizon and up probe bands using the recovered quantized coordinate.'
Assert-True ($heroShader.Contains('nprDirectionalProbe,')) 'Bounce must blend the directional probe toward the ordinary probe.'
Assert-True ($heroShader.Contains('(quantizedStep + 0.5) / steps')) 'Bounce must normalize the captured stepped coordinate like the recovered program.'
Assert-True ($heroShader.Contains('float lowerHemisphere = 1.0 - sample.quantizedCoordinate;')) 'Transmissive bounce must use the recovered lower-hemisphere weight.'
Assert-True ($heroShader.Contains('sample.transmissive = upwardProbe * transmissiveColor *')) 'Bounce approximation must use the material-local NPR transmissive color.'
Assert-True ($heroShader.Contains('"Retail Rim Mask": 16')) 'Hero shader is missing the retail rim-mask diagnostic.'
Assert-True ($heroShader.Contains('"NPR Bounce": 17')) 'Hero shader is missing the bounce diagnostic.'
Assert-True ($heroShader.Contains('"Retail NPR Transmissive": 18')) 'Hero shader is missing the material-local transmissive diagnostic.'
Assert-True ($heroShader.Contains('"Environment Specular Raw": 19')) 'Hero shader is missing the raw environment-specular diagnostic.'
Assert-True ($heroShader.Contains('"Environment Specular Final": 20')) 'Hero shader is missing the scaled environment-specular diagnostic.'
Assert-True (($heroShader.Split('pbrComputeSpecular(').Count - 1) -eq 2) 'Painter environment sampling must remain confined to the Deadlimit probe substitute and explicit PBR baseline view.'
Assert-True (($heroShader.Split('envIrradiance(').Count - 1) -eq 1) 'Painter panorama irradiance must remain confined to the explicit PBR baseline view.'
Assert-True ($heroShader.Contains('environmentSpecular.contribution')) 'Deadlimit shaded composition must include the controlled probe-specular substitute.'
Assert-True ($heroShader.Contains('dl_environment_specular_strength')) 'Environment specular must expose its calibrated preview strength.'
Assert-True ($heroShader -match 'vec3 linearOpaque = nprLightingComposite \+\s*pbrComputeEmissive' -and
    $heroShader -match 'diffuseShadingOutput\(dl_captured_display\s*\? dlCapturedDisplay\(linearOpaque\)\s*: linearOpaque\)') 'Shaded output must keep both Deadlimit modes on Painter linear surface outputs.'
Assert-True (-not $heroShader.Contains('#define DISABLE_FRAMEBUFFER_SRGB_CONVERSION')) 'Painter must own the single framebuffer linear-to-sRGB conversion.'

foreach ($value in @(0.0, 0.125, 0.25, 0.5, 0.75, 0.875, 1.0)) {
    Assert-Near (Invoke-NprQuantize $value 0.0) $value 0.0000001 'Sharpness 0 must preserve the input.'
}

$previous = -1.0
for ($index = 0; $index -le 100; $index++) {
    $value = $index / 100.0
    $quantized = Invoke-NprQuantize $value 0.75
    Assert-True ($quantized -ge $previous) 'NPR quantizer must be monotonic over [0, 1].'
    Assert-Near ($quantized + (Invoke-NprQuantize (1.0 - $value) 0.75)) 1.0 0.0000001 'NPR quantizer must remain symmetric around 0.5.'
    $previous = $quantized
}

Assert-True ((Invoke-NprQuantize 0.25 0.75) -lt 0.25) 'Sharpness must move the lower wing toward its step.'
Assert-True ((Invoke-NprQuantize 0.75 0.75) -gt 0.75) 'Sharpness must move the upper wing toward its step.'

Write-Output "Deadlimit Shade profile contract smoke passed for $($profiles.Count) profile(s)."
