$ErrorActionPreference = 'Stop'

$sourcePath = 'internal/src/Deadlimit/Core/CustomMaterialAuthoringService.cs'
$text = Get-Content -LiteralPath $sourcePath -Raw

$pattern = '(?ms)    private static readonly TextureSlotDefinition\[\] TextureSlots =\r?\n    \[\r?\n.*?^    \];'
$replacement = @'
    private static readonly TextureSlotDefinition[] TextureSlots =
    [
        new("TextureColor", NeutralColor,
        [
            "basecolor", "base_color", "basecolour", "base_colour", "basecol", "base_col",
            "diffuse", "diffusemap", "diffuse_map", "diffusemask", "diffuse_mask", "diff",
            "albedo", "albedomap", "albedo_map", "albedomask", "albedo_mask",
            "color", "colormap", "color_map", "colormask", "color_mask", "colour",
            "colourmap", "colour_map", "colourmask", "colour_mask", "col"
        ]),
        new("TextureNormal", NeutralNormal,
        [
            "normal", "normalmap", "normal_map", "normalmask", "normal_mask", "normals", "norm", "nrm"
        ]),
        new("TextureRoughness", NeutralRoughness,
        [
            "roughness", "roughnessmap", "roughness_map", "roughnessmask", "roughness_mask",
            "rough", "roughmap", "rough_map", "roughmask", "rough_mask", "rgh"
        ]),
        new("TextureAmbientOcclusion", NeutralWhite,
        [
            "ambientocclusion", "ambient_occlusion", "ambientocclusionmap", "ambient_occlusion_map",
            "ambientocclusionmask", "ambient_occlusion_mask", "occlusion", "occlusionmap",
            "occlusion_map", "occlusionmask", "occlusion_mask", "ao", "aomap", "ao_map",
            "aomask", "ao_mask"
        ]),
        new("TextureMetalness", NeutralBlack,
        [
            "metalness", "metalnessmap", "metalness_map", "metalnessmask", "metalness_mask",
            "metallic", "metallicmap", "metallic_map", "metallicmask", "metallic_mask",
            "metal", "metalmap", "metal_map", "metalmask", "metal_mask", "mtl"
        ]),
    ];
'@
$rx = [regex]::new($pattern)
if ($rx.Matches($text).Count -ne 1) {
    throw 'Could not uniquely locate TextureSlots definition.'
}
$text = $rx.Replace($text, $replacement, 1)

$oldSeparators = 'foreach (var separator in new[] { "_", "-", " " })'
$newSeparators = 'foreach (var separator in new[] { "_", "-", " ", "." })'
if (-not $text.Contains($oldSeparators)) {
    throw 'Could not locate texture suffix separator list.'
}
$text = $text.Replace($oldSeparators, $newSeparators)

$oldLog = '        log.AppendLine("Custom texture naming: <material>_color|diffuse|basecolor|albedo, _normal, _rough|roughness, _ao|occlusion, _metal|metalness|metallic; specialty Texture* fields may also bind by matching the material prefix plus the Texture parameter semantic name.");'
$newLog = '        log.AppendLine("Custom texture naming: standard PBR slots accept broad common aliases (for example BaseColor/BaseColour/Albedo/Diffuse/Color, Normal/NormalMap/NRM, Roughness/Rough/RGH, AO/AmbientOcclusion/Occlusion, Metalness/Metallic/Metal plus Map/Mask variants such as MetalnessMask). Separators _, -, space, and . are accepted. Packed ORM/RMA/MRA names are intentionally not auto-bound because channel layout is ambiguous. Specialty Texture* fields may also bind by matching the material prefix plus the Texture parameter semantic name.");'
if (-not $text.Contains($oldLog)) {
    throw 'Could not locate custom texture naming log line.'
}
$text = $text.Replace($oldLog, $newLog)
Set-Content -LiteralPath $sourcePath -Value $text -Encoding utf8NoBOM

$testPath = 'internal/tests/texture-naming-alias-smoke.ps1'
$test = @'
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
'@
Set-Content -LiteralPath $testPath -Value $test -Encoding utf8NoBOM
