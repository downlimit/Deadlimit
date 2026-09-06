[CmdletBinding()]
param(
    [string] $DocumentsPath = [Environment]::GetFolderPath('MyDocuments')
)

$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$pluginRoot = Join-Path $DocumentsPath 'Adobe\Adobe Substance 3D Painter\python\plugins'
$runtimeRoot = Join-Path $pluginRoot 'deadlimit_apply_runtime'
$runtimeProfiles = Join-Path $runtimeRoot 'profiles'
$runtimeTools = Join-Path $runtimeRoot 'tools'
$shaderRoot = Join-Path $DocumentsPath 'Adobe\Adobe Substance 3D Painter\assets\shaders\DeadlimitShade'

foreach ($directory in @($pluginRoot, $runtimeRoot, $runtimeProfiles, $runtimeTools, $shaderRoot)) {
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}

# Remove files owned by the retired DCC prototype. The installed runtime now
# contains only the native, format-preserving mesh converter.
foreach ($retiredTool in @(
    'Apply-DeadlimitToPainterMesh.ps1',
    'New-FbxOutlinePreview.ms',
    'New-FbxOutlinePreview.ps1'
)) {
    $retiredPath = Join-Path $runtimeTools $retiredTool
    if (Test-Path -LiteralPath $retiredPath -PathType Leaf) {
        Remove-Item -LiteralPath $retiredPath -Force
    }
}

Copy-Item -LiteralPath (Join-Path $shadeRoot 'painter_plugins\deadlimit_apply.py') `
    -Destination (Join-Path $pluginRoot 'deadlimit_apply.py') -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'profiles\ivy.json') `
    -Destination (Join-Path $runtimeProfiles 'ivy.json') -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'profiles\schema.json') `
    -Destination (Join-Path $runtimeProfiles 'schema.json') -Force
foreach ($shaderName in @('Deadlock_Hero.glsl', 'Deadlock_Outline.glsl')) {
    Copy-Item -LiteralPath (Join-Path $shadeRoot "shaders\$shaderName") `
        -Destination (Join-Path $shaderRoot $shaderName) -Force
}
foreach ($toolName in @(
    'Deadlimit.MeshPreview.exe',
    'assimp.dll'
)) {
    $publishRoot = Join-Path $shadeRoot 'tools\Deadlimit.MeshPreview\bin\Release\net8.0\win-x64\publish'
    $sourceTool = Join-Path $publishRoot $toolName
    if (-not (Test-Path -LiteralPath $sourceTool -PathType Leaf)) {
        throw "Missing published Painter runtime '$sourceTool'. Run dotnet publish first."
    }
    Copy-Item -LiteralPath $sourceTool `
        -Destination (Join-Path $runtimeTools $toolName) -Force
}
Copy-Item -LiteralPath (Join-Path $shadeRoot 'third_party\AssimpNetter-LICENSE.txt') `
    -Destination (Join-Path $runtimeTools 'AssimpNetter-License.txt') -Force

[ordered] @{
    plugin = Join-Path $pluginRoot 'deadlimit_apply.py'
    runtime = $runtimeRoot
    shaders = $shaderRoot
    profiles = @('ivy')
} | ConvertTo-Json -Compress
