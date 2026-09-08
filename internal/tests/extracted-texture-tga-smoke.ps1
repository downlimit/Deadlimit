$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$encoderType = $assembly.GetType('Deadlimit.Core.TgaImageEncoder', $true)
$serviceType = $assembly.GetType('Deadlimit.Core.ExtractedTextureTgaService', $true)
$flags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static

$encode = $encoderType.GetMethod('EncodePng', $flags)
$createCopies = $serviceType.GetMethod('CreateCopies', $flags)
if ($null -eq $encode -or $null -eq $createCopies) {
    throw 'TGA extraction helpers were not found.'
}

# 1x1 RGBA PNG with pixel R=10, G=20, B=30, A=40.
$png = [Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPgEpHTAAAAzQBlapmEQgAAAABJRU5ErkJggg==')
$invokeArgs = [object[]]::new(1)
$invokeArgs[0] = $png
$tga = [byte[]]$encode.Invoke($null, $invokeArgs)

if ($tga.Length -ne 22) { throw "Unexpected 1x1 TGA length: $($tga.Length)." }
if ($tga[2] -ne 2) { throw 'TGA image type is not uncompressed true-color.' }
if ($tga[12] -ne 1 -or $tga[13] -ne 0 -or $tga[14] -ne 1 -or $tga[15] -ne 0) {
    throw 'TGA dimensions were not written as little-endian 1x1.'
}
if ($tga[16] -ne 32) { throw 'TGA pixel depth is not 32-bit.' }
if ($tga[17] -ne 0x28) { throw "Unexpected TGA descriptor: 0x$($tga[17].ToString('X2'))." }
if ($tga[18] -ne 30 -or $tga[19] -ne 20 -or $tga[20] -ne 10 -or $tga[21] -ne 40) {
    throw 'TGA BGRA pixel order or alpha preservation is incorrect.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-tga-smoke-' + [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $temp -Force | Out-Null
    $pngPath = Join-Path $temp 'sample.png'
    [IO.File]::WriteAllBytes($pngPath, $png)
    [IO.File]::WriteAllBytes((Join-Path $temp 'hdr.exr'), [byte[]](1,2,3,4))

    $copyArgs = [object[]]::new(2)
    $copyArgs[0] = [string]$temp
    $copyArgs[1] = [Threading.CancellationToken]::None
    $count = [int]$createCopies.Invoke($null, $copyArgs)
    if ($count -ne 1) { throw "Expected one PNG-to-TGA copy, got $count." }

    $tgaPath = Join-Path $temp 'sample.tga'
    if (-not (Test-Path -LiteralPath $tgaPath)) { throw 'Sibling TGA was not created.' }
    $written = [IO.File]::ReadAllBytes($tgaPath)
    if (-not [Linq.Enumerable]::SequenceEqual[byte]($tga, $written)) {
        throw 'Written TGA bytes differ from encoder output.'
    }
    if (Test-Path -LiteralPath (Join-Path $temp 'hdr.tga')) {
        throw 'HDR/EXR input was incorrectly flattened into TGA.'
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$settingsType = $assembly.GetType('Deadlimit.Core.ToolPathSettings', $true)
$settings = [Activator]::CreateInstance($settingsType)
if ($settings.ExportExtractedTexturesAsTga) {
    throw 'ExportExtractedTexturesAsTga must default to false.'
}

Write-Host 'Extracted texture TGA smoke passed.'
