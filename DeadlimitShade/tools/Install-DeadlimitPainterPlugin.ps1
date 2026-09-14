[CmdletBinding()]
param(
    [string] $DocumentsPath = [Environment]::GetFolderPath('MyDocuments'),

    [ValidateRange(1, 65535)]
    [int] $PainterRemotePort = 60041,

    [switch] $SkipOpenDock
)

$ErrorActionPreference = 'Stop'
$shadeRoot = Split-Path -Parent $PSScriptRoot
$pythonRoot = Join-Path $DocumentsPath 'Adobe\Adobe Substance 3D Painter\python'
$pluginRoot = Join-Path $pythonRoot 'plugins'
$startupRoot = Join-Path $pythonRoot 'startup'
$runtimeRoot = Join-Path $startupRoot 'deadlimit_apply_runtime'
$runtimeProfiles = Join-Path $runtimeRoot 'profiles'
$runtimeLighting = Join-Path $runtimeRoot 'lighting'
$runtimeTools = Join-Path $runtimeRoot 'tools'
$runtimeShaders = Join-Path $runtimeRoot 'shaders'
$shaderRoot = Join-Path $DocumentsPath 'Adobe\Adobe Substance 3D Painter\assets\shaders\DeadlimitShade'

foreach ($directory in @($pluginRoot, $startupRoot, $runtimeRoot, $runtimeProfiles, $runtimeLighting, $runtimeTools, $runtimeShaders, $shaderRoot)) {
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

$retiredOptionalPlugin = Join-Path $pluginRoot 'deadlimit_apply.py'
if (Test-Path -LiteralPath $retiredOptionalPlugin -PathType Leaf) {
    Remove-Item -LiteralPath $retiredOptionalPlugin -Force
}
$installedPlugin = Join-Path $startupRoot 'deadlimit_apply.py'
Copy-Item -LiteralPath (Join-Path $shadeRoot 'painter_plugins\deadlimit_apply.py') `
    -Destination $installedPlugin -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'profiles\ivy.json') `
    -Destination (Join-Path $runtimeProfiles 'ivy.json') -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'profiles\schema.json') `
    -Destination (Join-Path $runtimeProfiles 'schema.json') -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'lighting\preview-presets.json') `
    -Destination (Join-Path $runtimeLighting 'preview-presets.json') -Force
foreach ($shaderName in @('Deadlock_Hero.glsl', 'Deadlock_Outline.glsl')) {
    $sourceShader = Join-Path $shadeRoot "shaders\$shaderName"
    Copy-Item -LiteralPath $sourceShader -Destination (Join-Path $shaderRoot $shaderName) -Force
    Copy-Item -LiteralPath $sourceShader -Destination (Join-Path $runtimeShaders $shaderName) -Force
}
foreach ($toolName in @(
    'Deadlimit.MeshPreview.exe',
    'assimp.dll'
)) {
    $publishRoot = Join-Path $shadeRoot 'tools\Deadlimit.MeshPreview\bin\Release\net10.0\win-x64\publish'
    $sourceTool = Join-Path $publishRoot $toolName
    if (-not (Test-Path -LiteralPath $sourceTool -PathType Leaf)) {
        throw "Missing published Painter runtime '$sourceTool'. Run dotnet publish first."
    }
    Copy-Item -LiteralPath $sourceTool `
        -Destination (Join-Path $runtimeTools $toolName) -Force
}
$retailPublishRoot = Join-Path $shadeRoot 'tools\Deadlimit.RetailTextures\bin\Release\net10.0\win-x64\publish'
$retailTool = Join-Path $retailPublishRoot 'Deadlimit.RetailTextures.exe'
if (-not (Test-Path -LiteralPath $retailTool -PathType Leaf)) {
    throw "Missing published Painter runtime '$retailTool'. Run dotnet publish first."
}
Copy-Item -LiteralPath $retailTool -Destination (Join-Path $runtimeTools 'Deadlimit.RetailTextures.exe') -Force
Copy-Item -LiteralPath (Join-Path $shadeRoot 'third_party\AssimpNetter-LICENSE.txt') `
    -Destination (Join-Path $runtimeTools 'AssimpNetter-License.txt') -Force

$dockOpened = $false
if (-not $SkipOpenDock) {
    $startPlugin = @'
import importlib
import sys
import substance_painter_plugins

substance_painter_plugins.update_sys_path()
old = sys.modules.get("deadlimit_apply")
if old is not None:
    try:
        substance_painter_plugins.close_plugin(old)
    except Exception:
        pass
    sys.modules.pop("deadlimit_apply", None)
module = importlib.import_module("deadlimit_apply")
substance_painter_plugins.start_plugin(module)
True
'@
    $encodedScript = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($startPlugin))
    $body = @{ python = $encodedScript } | ConvertTo-Json -Compress
    foreach ($hostName in @('127.0.0.1', '[::1]', 'localhost')) {
        try {
            $response = Invoke-RestMethod `
                -Uri "http://${hostName}:$PainterRemotePort/run.json" `
                -Method Post `
                -ContentType 'application/json' `
                -Body $body `
                -TimeoutSec 5
            if ($null -eq $response.error) {
                $dockOpened = $true
                break
            }
        }
        catch {
            # Remote scripting is optional. Startup installation remains valid.
        }
    }
}

[ordered] @{
    plugin = $installedPlugin
    runtime = $runtimeRoot
    shaders = $shaderRoot
    profiles = @('ivy')
    launch = if ($dockOpened) { 'dock-opened' } else { 'restart-painter' }
} | ConvertTo-Json -Compress
