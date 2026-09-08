$ErrorActionPreference = 'Stop'

$assemblyPath = (Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll').Path
$outputRoot = Split-Path -Parent $assemblyPath
$runtimeRid = switch ([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture) {
    ([Runtime.InteropServices.Architecture]::X64) { 'win-x64' }
    ([Runtime.InteropServices.Architecture]::X86) { 'win-x86' }
    ([Runtime.InteropServices.Architecture]::Arm64) { 'win-arm64' }
    default { throw "Unsupported smoke-test process architecture: $([Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture)" }
}
$nativeSkia = Get-ChildItem -LiteralPath $outputRoot -Filter 'libSkiaSharp.dll' -File -Recurse |
    Where-Object { $_.FullName -match [Regex]::Escape("runtimes\$runtimeRid\native") } |
    Select-Object -First 1
if ($null -eq $nativeSkia) {
    throw "Packaged $runtimeRid libSkiaSharp.dll was not found under build output: $outputRoot"
}

$nativeDir = Split-Path -Parent $nativeSkia.FullName
$env:PATH = $nativeDir + [IO.Path]::PathSeparator + $env:PATH
[Runtime.InteropServices.NativeLibrary]::Load($nativeSkia.FullName) | Out-Null

$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$encoderType = $assembly.GetType('Deadlimit.Core.TgaImageEncoder', $true)
$serviceType = $assembly.GetType('Deadlimit.Core.ExtractedTextureTgaService', $true)
$flags = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static

$encode = $encoderType.GetMethod('EncodeImage', $flags)
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

    # The decoder is content-based. Reusing a known-good tiny raster under each supported
    # extension tests the service extension contract independently of fixture encoders.
    foreach ($fileName in @('sample_png.png', 'sample_jpeg.jpeg', 'sample_webp.webp', 'preserve.png')) {
        [IO.File]::WriteAllBytes((Join-Path $temp $fileName), $png)
    }
    [IO.File]::WriteAllBytes((Join-Path $temp 'hdr.exr'), [byte[]](1,2,3,4))

    $preservedTgaPath = Join-Path $temp 'preserve.tga'
    $preservedTgaBytes = [byte[]](9,8,7,6)
    [IO.File]::WriteAllBytes($preservedTgaPath, $preservedTgaBytes)

    $copyArgs = [object[]]::new(2)
    $copyArgs[0] = [string]$temp
    $copyArgs[1] = [Threading.CancellationToken]::None
    $count = [int]$createCopies.Invoke($null, $copyArgs)
    if ($count -ne 3) { throw "Expected three LDR image-to-TGA copies, got $count." }

    foreach ($stem in @('sample_png', 'sample_jpeg', 'sample_webp')) {
        $tgaPath = Join-Path $temp ($stem + '.tga')
        if (-not (Test-Path -LiteralPath $tgaPath)) { throw "Sibling TGA was not created: $tgaPath" }
        $written = [IO.File]::ReadAllBytes($tgaPath)
        if ([Convert]::ToBase64String($tga) -ne [Convert]::ToBase64String($written)) {
            throw "Written TGA bytes differ from encoder output: $tgaPath"
        }
    }

    if (Test-Path -LiteralPath (Join-Path $temp 'hdr.tga')) {
        throw 'HDR/EXR input was incorrectly flattened into TGA.'
    }

    $preservedAfter = [IO.File]::ReadAllBytes($preservedTgaPath)
    if ([Convert]::ToBase64String($preservedTgaBytes) -ne [Convert]::ToBase64String($preservedAfter)) {
        throw 'An existing extracted TGA was overwritten.'
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
