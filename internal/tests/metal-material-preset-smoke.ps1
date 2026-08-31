$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\src\Deadlimit\bin\Release\net10.0-windows\DeadlimitManager.dll'))
if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
    throw "DeadlimitManager.dll was not found: $assemblyPath"
}

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CustomMaterialAuthoringService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$method = $type.GetMethod('ApplyMaterialNameModifiers', $flags)
if ($null -eq $method) {
    throw 'ApplyMaterialNameModifiers was not found.'
}

function Invoke-MaterialPreset([string]$text, [string]$reference, [bool]$vertexColorMode) {
    return [string]$method.Invoke($null, @($text, $reference, $vertexColorMode))
}

$vertexColorSource = @'
Layer0
{
    "TextureRoughness1" "[0.800000 0.800000 0.800000 0.000000]"
    "g_flMetalness" "0.000"
}
'@
$vertexColorMetal = Invoke-MaterialPreset $vertexColorSource 'materials/test/armor_vertexcolor_metal.vmat' $true
if ($vertexColorMetal -notmatch '"TextureRoughness1"\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"') {
    throw "Vertex-color metal preset did not set Roughness to 128/128/128.`n$vertexColorMetal"
}
if ($vertexColorMetal -notmatch '"g_flMetalness"\s+"0\.800"') {
    throw "Vertex-color metal preset did not set Metalness to 0.800.`n$vertexColorMetal"
}
if ($vertexColorMetal -match 'g_flGlossiness') {
    throw "Vertex-color metal preset still writes the obsolete glossiness parameter.`n$vertexColorMetal"
}

$standardSource = @'
Layer0
{
    TextureRoughness "[0.800000 0.800000 0.800000 0.000000]"
    g_flMetalness "0.000"
}
'@
$standardMetal = Invoke-MaterialPreset $standardSource 'materials/test/armor_metal.vmat' $false
if ($standardMetal -notmatch 'TextureRoughness\s+"\[0\.501961 0\.501961 0\.501961 0\.000000\]"') {
    throw "Standard metal preset did not set Roughness to 128/128/128.`n$standardMetal"
}
if ($standardMetal -notmatch 'g_flMetalness\s+"0\.800"') {
    throw "Standard metal preset did not set Metalness to 0.800.`n$standardMetal"
}

$plain = Invoke-MaterialPreset $vertexColorSource 'materials/test/armor_vertexcolor.vmat' $true
if ($plain -ne $vertexColorSource) {
    throw 'A material without the metal keyword was modified by the metal preset.'
}

Write-Host 'Metal material-name preset smoke passed.'
