$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CustomMaterialAuthoringService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$method = $type.GetMethod('ParseTextureCandidate', $flags)
if ($null -eq $method) {
    throw 'ParseTextureCandidate reflection hook was not found.'
}

$aliases = [ordered]@{
    TextureColor = @(
        'basecolor','base_color','basecolour','base_colour','basecol','base_col',
        'diffuse','diffusemap','diffuse_map','diffusemask','diffuse_mask','diff',
        'albedo','albedomap','albedo_map','albedomask','albedo_mask',
        'color','colormap','color_map','colormask','color_mask','colour','colourmap','colour_map','colourmask','colour_mask','col'
    )
    TextureNormal = @('normal','normalmap','normal_map','normalmask','normal_mask','normals','norm','nrm')
    TextureRoughness = @('roughness','roughnessmap','roughness_map','roughnessmask','roughness_mask','rough','roughmap','rough_map','roughmask','rough_mask','rgh')
    TextureAmbientOcclusion = @(
        'ambientocclusion','ambient_occlusion','ambientocclusionmap','ambient_occlusion_map',
        'ambientocclusionmask','ambient_occlusion_mask','occlusion','occlusionmap','occlusion_map',
        'occlusionmask','occlusion_mask','ao','aomap','ao_map','aomask','ao_mask'
    )
    TextureMetalness = @(
        'metalness','metalnessmap','metalness_map','metalnessmask','metalness_mask',
        'metallic','metallicmap','metallic_map','metallicmask','metallic_mask',
        'metal','metalmap','metal_map','metalmask','metal_mask','mtl'
    )
}

foreach ($slot in $aliases.Keys) {
    foreach ($alias in $aliases[$slot]) {
        $path = "C:\temp\ivy_builder_body_$alias.png"
        $candidate = $method.Invoke($null, @($path, 'materials/ivybuilder'))
        if ($null -eq $candidate) {
            throw "Alias '$alias' did not parse for $slot."
        }
        if ($candidate.SlotKey -ne $slot) {
            throw "Alias '$alias' resolved to $($candidate.SlotKey), expected $slot."
        }
        if ($candidate.BaseToken -ne 'ivybuilderbody') {
            throw "Alias '$alias' produced base token '$($candidate.BaseToken)', expected 'ivybuilderbody'."
        }
    }
}

foreach ($separator in @('-', ' ', '.')) {
    $path = "C:\temp\ivy_builder_body${separator}metalnessmask.png"
    $candidate = $method.Invoke($null, @($path, 'materials/ivybuilder'))
    if ($null -eq $candidate -or $candidate.SlotKey -ne 'TextureMetalness') {
        throw "Separator '$separator' did not resolve metalnessmask."
    }
}

foreach ($packed in @('orm', 'rma', 'mra')) {
    $path = "C:\temp\ivy_builder_body_$packed.png"
    $candidate = $method.Invoke($null, @($path, 'materials/ivybuilder'))
    if ($null -ne $candidate) {
        throw "Packed texture alias '$packed' must remain unbound unless channel semantics are explicitly supported."
    }
}

Write-Host 'Texture naming alias smoke passed.'
