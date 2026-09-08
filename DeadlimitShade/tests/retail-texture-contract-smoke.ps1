$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$profile = Get-Content -LiteralPath (Join-Path $shadeRoot 'profiles\ivy.json') -Raw | ConvertFrom-Json
$backend = Get-Content -LiteralPath (Join-Path $shadeRoot 'tools\Deadlimit.RetailTextures\Program.cs') -Raw
$plugin = Get-Content -LiteralPath (Join-Path $shadeRoot 'painter_plugins\deadlimit_apply.py') -Raw

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

$preview = $profile.painterApply.retailPreview
Assert-True ($preview.sourcePolicy -eq 'project-0source-then-retail-vpk') 'Retail preview must prefer the project 0source extraction.'
Assert-True ($preview.materialBindings.Count -eq 4) 'Ivy retail preview must describe all four Valve Texture Sets in the current source FBX.'
Assert-True (($preview.materialBindings.textureSet | Select-Object -Unique).Count -eq 4) 'Retail Texture Set bindings must be unique.'
Assert-True (($preview.materialBindings.material | Where-Object { $_ -notmatch '\.vmat_c$' }).Count -eq 0) 'Retail bindings must address compiled material resources.'
Assert-True ($preview.vertexColorSource -eq 'models/heroes_wip/ivy/ivy_ivy.dmx') 'Ivy retail preview must name the exact extracted DMX vertex-color source.'

Assert-True $backend.Contains('FindExactExtractedSource(sourceRoot, texturePath)') 'Retail extraction must check 0source before decoding a VPK texture.'
Assert-True $backend.Contains('origin = "0source"') 'The cache manifest must record reused 0source inputs.'
Assert-True $backend.Contains('origin = "retail-vpk"') 'The cache manifest must record retail VPK fallbacks.'
Assert-True $plugin.Contains('"g_tTintMaskRimLightMask"') 'Painter Apply must consume the retail tint/rim texture already emitted by the cache manifest.'
Assert-True $plugin.Contains('"g_tNprTransmissiveColor"') 'Painter Apply must consume each material-local retail NPR transmissive texture.'
Assert-True $backend.Contains('TextureExtract.ToPngImage') 'Retail texture fallback must use ValveResourceFormat decoding.'
Assert-True $backend.Contains('intParams = material.IntParams') 'Retail manifest must preserve VMAT feature switches.'
Assert-True $backend.Contains('floatParams = material.FloatParams') 'Retail manifest must preserve VMAT scalar parameters.'
Assert-True (-not $backend.Contains('WriteEntry')) 'Retail extractor must not write to the source VPK.'

Write-Output 'Deadlimit retail texture contract smoke passed.'
