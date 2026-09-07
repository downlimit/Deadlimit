$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$profile = Get-Content -LiteralPath (Join-Path $shadeRoot 'profiles\ivy.json') -Raw | ConvertFrom-Json
$pluginPath = Join-Path $shadeRoot 'painter_plugins\deadlimit_apply.py'
$nativeBackendPath = Join-Path $shadeRoot 'tools\Deadlimit.MeshPreview\Program.cs'
$installerPath = Join-Path $shadeRoot 'tools\Install-DeadlimitPainterPlugin.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True ($profile.outline.widthMillimeters -gt 0) 'Ivy outline width must be positive.'
Assert-True ($profile.outline.color.Count -eq 3) 'Ivy outline color must contain RGB.'
Assert-True ($null -ne $profile.painterApply) 'Ivy must expose a Painter apply recipe.'
Assert-True ($profile.painterApply.outlineAllMeshes -eq $true) 'Ivy Apply must outline the complete source scene.'
Assert-True ($profile.painterApply.heroTextureSets.Count -eq 3) 'Ivy Apply must target its three authored Texture Sets for hero shading.'
Assert-True ($profile.painterApply.heroTextureSets -contains 'ivy_builder_arms') 'Ivy Apply must include the authored arms slot.'
Assert-True ($profile.painterApply.heroTextureSets -contains 'ivy_builder_body') 'Ivy Apply must include the authored body slot.'
Assert-True ($profile.painterApply.heroTextureSets -contains 'ivy_builder_head') 'Ivy Apply must include the authored head slot.'
Assert-True ($profile.painterApply.retailPreview.vertexColorSource -eq 'models/heroes_wip/ivy/ivy_ivy.dmx') 'Ivy Apply must restore the extracted DMX color0 stream.'

$plugin = Get-Content -LiteralPath $pluginPath -Raw
Assert-True $plugin.Contains('Apply Deadlimit') 'Painter plugin must expose Apply Deadlimit.'
Assert-True $plugin.Contains('dl_character: CHARACTER_ID') 'Hero character ID must be assigned from the selected profile.'
Assert-True $plugin.Contains('dl_outline_character: CHARACTER_ID') 'Outline character ID must be assigned from the selected profile.'
Assert-True $plugin.Contains('preserve_strokes=True') 'Painter mesh reload must preserve strokes.'
Assert-True $plugin.Contains('Deadlock is never launched') 'Apply progress must state that the workflow is offline.'
Assert-True $plugin.Contains('deadlimit-shade-preview-cache') 'Apply must cache deterministic preview meshes.'
Assert-True $plugin.Contains('if PLUGIN_WIDGETS:') 'Plugin start must be idempotent across Painter reloads.'
Assert-True $plugin.Contains('Deadlimit.MeshPreview.exe') 'Apply must use the self-contained mesh converter.'
Assert-True $plugin.Contains('profile["painterApply"]["heroTextureSets"]') 'Apply must source hero-shaded slots from the character profile.'
Assert-True $plugin.Contains('_import_or_reuse_project_texture') 'Apply must reuse unchanged retail preview resources.'
Assert-True $plugin.Contains('_import_or_reuse_project_shader') 'Apply must embed content-addressed shaders in the Painter project.'
Assert-True $plugin.Contains('hero.shader = shaderResources.hero.name') 'Apply must instantiate the content-addressed project shader directly.'
Assert-True (-not $plugin.Contains('alg.shaders.updateShaderInstance')) 'Apply must not create ambiguous duplicate shader labels through Painter 9.1 updateShaderInstance.'
Assert-True $plugin.Contains('heroMatches[heroMatches.length - 1]') 'Apply must configure the mapped replacement when Painter retains an unused earlier instance.'
Assert-True $plugin.Contains('output.name[:8]') 'Retail preview resource identity must include the cache digest.'
Assert-True $plugin.Contains('Deadlimit View') 'Painter dock must provide shader-native solo previews for Painter 9.1.'
Assert-True $plugin.Contains('Lighting Inputs') 'Painter dock must switch deterministically between neutral diagnostic and material inputs.'
Assert-True $plugin.Contains('NPR Lighting Composite') 'Painter dock must expose the final lighting skeleton independently of material color.'
Assert-True $plugin.Contains('Painter PBR Baseline') 'Painter dock must expose the same-scene PBR comparison.'
Assert-True $plugin.Contains('dl_lighting_input_mode: inputMode') 'Diagnostic input selection must reach every hero and retail shader instance.'
Assert-True $plugin.Contains('hasOwnProperty.call(parameters, "dl_lighting_input_mode")') 'Diagnostic switching must ignore stale unused Painter shader instances from earlier embedded revisions.'
Assert-True $plugin.Contains('application.allWidgets()') 'Deadlimit View must restore every visible 2D/3D viewport selector to Material.'
Assert-True $plugin.Contains('F_VERTEX_COLOR') 'Retail preview must honor VMAT vertex-color materials such as Ivy eyes.'
Assert-True $plugin.Contains('dl_vertex_color_multiply: binding.vertexColorMultiply') 'Retail VMAT vertex-color strength must reach the shader instance.'
Assert-True $plugin.Contains('--vertex-color-dmx') 'Apply must restore extracted DMX vertex colors before Painter mesh reload.'
Assert-True (-not $plugin.Contains('3ds Max')) 'Painter Apply must not depend on 3ds Max.'
Assert-True (-not $plugin.Contains('powershell.exe')) 'Painter Apply must not invoke PowerShell.'

$outlineShader = Get-Content -LiteralPath (Join-Path $shadeRoot 'shaders\Deadlock_Outline.glsl') -Raw
$heroShader = Get-Content -LiteralPath (Join-Path $shadeRoot 'shaders\Deadlock_Hero.glsl') -Raw
Assert-True $outlineShader.Contains('emissiveColorOutput(outlineColor)') 'Outline Material view must expose the resolved profile color.'
Assert-True $outlineShader.Contains('albedoOutput(outlineColor)') 'Outline Base Color view must match the resolved Material-view color.'
Assert-True ($heroShader.IndexOf('baseColor *= mix(vec3(1.0), vertexColor, dl_vertex_color_multiply);') -lt $heroShader.IndexOf('if (dl_debug_view == 1)')) 'Resolved Base Color must include VMAT vertex-color multiplication in both preview modes.'
Assert-True $heroShader.Contains('directSpecular.contribution +') 'Hero shaded composition must include the independently evaluated direct specular term.'
Assert-True $heroShader.Contains('ambientOcclusion * rim.contribution') 'Hero shaded composition must include the independently evaluated rim term.'

$nativeBackend = Get-Content -LiteralPath $nativeBackendPath -Raw
Assert-True $nativeBackend.Contains('ApplyDmxVertexColors') 'The mesh backend must transfer extracted DMX color0 data.'
Assert-True $nativeBackend.Contains('outline.VertexColorChannels[channel].AddRange') 'The generated outline must preserve source vertex colors.'
Assert-True $nativeBackend.Contains('scene.Materials.Add(outlineMaterial)') 'Native backend must append the reserved outline material.'
Assert-True (-not $nativeBackend.Contains('outlineMaterials.Contains(sourceMaterial)')) 'Native backend must not exclude any source mesh from the outline.'
Assert-True $nativeBackend.Contains('node.MeshIndices.Add') 'Native backend must append shell meshes to source nodes.'
Assert-True $nativeBackend.Contains('face.Indices[0], face.Indices[2], face.Indices[1]') 'Native backend must reverse shell winding.'

$installer = Get-Content -LiteralPath $installerPath -Raw
Assert-True $installer.Contains('Adobe Substance 3D Painter\python\plugins') 'Installer must target Painter Python plugins.'
Assert-True $installer.Contains('assets\shaders\DeadlimitShade') 'Installer must deploy both Painter shaders.'
Assert-True $installer.Contains('$runtimeShaders') 'Installer must deploy content-addressed shader inputs with the plugin runtime.'
Write-Output 'Deadlimit Painter Apply contract smoke passed.'
