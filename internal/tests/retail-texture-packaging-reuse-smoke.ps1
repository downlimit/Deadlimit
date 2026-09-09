$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.RetailTexturePackagingPolicy', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$resolve = $type.GetMethod('ResolveReusableRetailCompiledTextures', $flags)
if ($null -eq $resolve) {
    throw 'Retail texture packaging reuse policy was not found.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-retail-texture-packaging-' + [Guid]::NewGuid().ToString('N'))
$source = Join-Path $temp '0source'
$addon = Join-Path $temp 'content\citadel_addons\ivytest'
$game = Join-Path $temp 'game\citadel_addons\ivytest'

function Write-TestFile([string]$root, [string]$relative, [string]$content) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8NoBOM
    return $path
}

function Write-TestBytes([string]$root, [string]$relative, [byte[]]$bytes) {
    $path = Join-Path $root $relative
    New-Item -ItemType Directory -Path (Split-Path $path) -Force | Out-Null
    [IO.File]::WriteAllBytes($path, $bytes)
    return $path
}

try {
    $stockVmat = @"
Layer0
{
    "TextureColor" "materials/test/stock.png"
}
"Compiled Textures"
{
    "g_tColor" "materials/test/stock.vtex"
}
"@
    Write-TestFile $source 'materials\test\stock.vmat' $stockVmat | Out-Null
    Write-TestBytes $source 'materials\test\stock.png' ([byte[]](1,2,3,4)) | Out-Null
    Write-TestBytes $addon 'materials\test\stock.png' ([byte[]](1,2,3,4)) | Out-Null
    Write-TestBytes $game 'materials\test\stock.vtex_c' ([byte[]](9,9,9)) | Out-Null

    $customVmat = @"
Layer0
{
    "TextureColor" "materials/test/custom.png"
}
"Compiled Textures"
{
    "g_tColor" "materials/test/custom.vtex"
}
"@
    Write-TestFile $source 'materials\test\custom.vmat' $customVmat | Out-Null
    Write-TestBytes $source 'materials\test\custom.png' ([byte[]](5,5,5,5)) | Out-Null
    Write-TestBytes $addon 'materials\test\custom.png' ([byte[]](8,8,8,8)) | Out-Null
    Write-TestBytes $game 'materials\test\custom.vtex_c' ([byte[]](7,7,7)) | Out-Null

    $generatedVmat = @"
Layer0
{
    "TextureColor" "materials/test/generated_color.png"
}
"Compiled Textures"
{
    "g_tColor" "materials/test/generated_vmat_g_tColor_deadbeef.vtex"
}
"@
    Write-TestFile $source 'materials\test\generated.vmat' $generatedVmat | Out-Null
    Write-TestBytes $source 'materials\test\generated_color.png' ([byte[]](2,2,2,2)) | Out-Null
    Write-TestBytes $addon 'materials\test\generated_color.png' ([byte[]](2,2,2,2)) | Out-Null
    Write-TestBytes $game 'materials\test\generated_vmat_g_tColor_deadbeef.vtex_c' ([byte[]](6,6,6)) | Out-Null

    $result = @($resolve.Invoke($null, [object[]]@(
        [string]$source,
        [string]$addon,
        [string]$game)))

    if ($result.Count -ne 1) {
        throw "Expected exactly one proven reusable retail texture, got $($result.Count): $($result -join ', ')"
    }
    if ($result -notcontains 'materials/test/stock.vtex_c') {
        throw 'Unchanged one-to-one stock texture was not marked reusable.'
    }
    if ($result -contains 'materials/test/custom.vtex_c') {
        throw 'Artist-modified texture was incorrectly marked reusable.'
    }
    if ($result -contains 'materials/test/generated_vmat_g_tColor_deadbeef.vtex_c') {
        throw 'Generated/hash texture identity was guessed and incorrectly marked reusable.'
    }

    $buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
    foreach ($required in @(
        'RetailTexturePackagingPolicy.ResolveReusableRetailCompiledTextures(',
        'excludedRelativePaths',
        'Retail texture outputs reused from Deadlock instead of packed'
    )) {
        if (-not $buildSource.Contains($required, [StringComparison]::Ordinal)) {
            throw "Build & Test retail texture reuse wiring is missing: $required"
        }
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Conservative retail texture VPK reuse smoke passed.'
