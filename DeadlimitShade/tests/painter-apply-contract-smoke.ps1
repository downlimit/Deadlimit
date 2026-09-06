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
Assert-True (-not $plugin.Contains('3ds Max')) 'Painter Apply must not depend on 3ds Max.'
Assert-True (-not $plugin.Contains('powershell.exe')) 'Painter Apply must not invoke PowerShell.'

$outlineShader = Get-Content -LiteralPath (Join-Path $shadeRoot 'shaders\Deadlock_Outline.glsl') -Raw
Assert-True $outlineShader.Contains('emissiveColorOutput(outlineColor)') 'Outline Material view must expose the resolved profile color.'
Assert-True $outlineShader.Contains('albedoOutput(vec3(0.0))') 'Outline must not bake profile color into Painter Base Color data.'

$nativeBackend = Get-Content -LiteralPath $nativeBackendPath -Raw
Assert-True $nativeBackend.Contains('scene.Materials.Add(outlineMaterial)') 'Native backend must append the reserved outline material.'
Assert-True (-not $nativeBackend.Contains('outlineMaterials.Contains(sourceMaterial)')) 'Native backend must not exclude any source mesh from the outline.'
Assert-True $nativeBackend.Contains('node.MeshIndices.Add') 'Native backend must append shell meshes to source nodes.'
Assert-True $nativeBackend.Contains('face.Indices[0], face.Indices[2], face.Indices[1]') 'Native backend must reverse shell winding.'

$installer = Get-Content -LiteralPath $installerPath -Raw
Assert-True $installer.Contains('Adobe Substance 3D Painter\python\plugins') 'Installer must target Painter Python plugins.'
Assert-True $installer.Contains('assets\shaders\DeadlimitShade') 'Installer must deploy both Painter shaders.'
Write-Output 'Deadlimit Painter Apply contract smoke passed.'
