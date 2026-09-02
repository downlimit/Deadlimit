$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.RetailVmdlInheritance', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$copy = $type.GetMethod('CopyExternalRetailTextureDependencies', $flags)
if ($null -eq $copy) { throw 'CopyExternalRetailTextureDependencies was not found.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-retail-dep-' + [Guid]::NewGuid().ToString('N'))
$sourceRoot = Join-Path $temp '0source'
$heroFolder = Join-Path $sourceRoot 'models\heroes_wip\vampirebat'
$heroMaterials = Join-Path $heroFolder 'materials'
$externalTexture = Join-Path $sourceRoot 'models\heroes_wip\bookworm\materials\outline_layout.png'
$addonRoot = Join-Path $temp 'addon'
try {
    New-Item -ItemType Directory -Path $heroMaterials -Force | Out-Null
    New-Item -ItemType Directory -Path (Split-Path $externalTexture) -Force | Out-Null
    New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null

    $vmat = @"
Layer0
{
    "TextureColor" "models/heroes_wip/vampirebat/materials/body.png"
    "TextureDetail" "models/heroes_wip/bookworm/materials/outline_layout.png"
}
"@
    Set-Content -LiteralPath (Join-Path $heroMaterials 'vampirebat_bag.vmat') -Value $vmat -Encoding utf8NoBOM
    [IO.File]::WriteAllBytes($externalTexture, [byte[]](1,2,3,4))

    $invokeArgs = [object[]]@([string]$heroFolder, [string]$sourceRoot, [string]$addonRoot)
    $count = [int]$copy.Invoke($null, $invokeArgs)
    if ($count -ne 1) { throw "Expected one external texture dependency copy, got $count." }

    $expected = Join-Path $addonRoot 'models\heroes_wip\bookworm\materials\outline_layout.png'
    if (-not (Test-Path -LiteralPath $expected)) {
        throw "Cross-hero texture dependency was not copied to its Source 2 resource path: $expected"
    }

    $bytes = [IO.File]::ReadAllBytes($expected)
    if ($bytes.Length -ne 4 -or $bytes[0] -ne 1 -or $bytes[3] -ne 4) {
        throw 'Copied cross-hero texture dependency content does not match source.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Retail external texture dependency copy smoke passed.'