$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$nonPublicStatic = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$publicStatic = [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static

$layoutType = $assembly.GetType('Deadlimit.Core.ProjectAuthoringLayout', $true)
$layoutRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-layout-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($layoutRoot) | Out-Null
    $layoutType.GetMethod('EnsureStructure', $publicStatic).Invoke($null, @([string]$layoutRoot))
    foreach ($folder in @('0source', '1authoring', '2concept', '3scene', '4texture', '5promo', '6temp')) {
        if (-not [IO.Directory]::Exists((Join-Path $layoutRoot $folder))) {
            throw "Artist project layout is missing $folder."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $layoutRoot) {
        Remove-Item -LiteralPath $layoutRoot -Recurse -Force
    }
}

# Normal authoring FBX files must contribute their material slot names to PREPARE.
# Cover both Autodesk ASCII and the default binary FBX string-property encoding.
$fbxMaterialReaderType = $assembly.GetType('Deadlimit.Core.FbxMaterialReferenceReader', $true)
$fbxMaterialRead = $fbxMaterialReaderType.GetMethod('Read', $publicStatic)
if ($null -eq $fbxMaterialRead) { throw 'FbxMaterialReferenceReader.Read was not found.' }
$fbxMaterialRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-fbx-material-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fbxMaterialRoot) | Out-Null

    $asciiPath = Join-Path $fbxMaterialRoot 'ascii.fbx'
    $asciiText = @'
; FBX 7.4.0 project file
Material: 101, "Material::lashter_head", "" {
}
'@
    [IO.File]::WriteAllText($asciiPath, $asciiText)
    $asciiRefs = @($fbxMaterialRead.Invoke($null, @([string]$asciiPath)))
    if (($asciiRefs.Count -ne 1) -or
        ($asciiRefs[0].SourceName -ne 'lashter_head') -or
        ($asciiRefs[0].AuthoringReference -ne 'materials/lashter_head')) {
        throw 'ASCII FBX material slot was not normalized to the authoring material reference.'
    }

    $binaryPath = Join-Path $fbxMaterialRoot 'binary.fbx'
    $payload = [Text.Encoding]::UTF8.GetBytes('Material::lashter_head')
    $bytes = [Collections.Generic.List[byte]]::new()
    $bytes.AddRange([Text.Encoding]::ASCII.GetBytes('Kaydara FBX Binary  '))
    $bytes.AddRange([byte[]]@(0, 26, 0))
    $bytes.Add([byte][char]'S')
    $bytes.AddRange([BitConverter]::GetBytes([uint32]$payload.Length))
    $bytes.AddRange($payload)
    [IO.File]::WriteAllBytes($binaryPath, $bytes.ToArray())
    $binaryRefs = @($fbxMaterialRead.Invoke($null, @([string]$binaryPath)))
    if (($binaryRefs.Count -ne 1) -or
        ($binaryRefs[0].SourceName -ne 'lashter_head') -or
        ($binaryRefs[0].AuthoringReference -ne 'materials/lashter_head')) {
        throw 'Binary FBX material slot was not normalized to the authoring material reference.'
    }

    # Autodesk FBX 7300 commonly stores the namespace in binary form as
    # "name\0\x01Material" instead of the older "Material::name" spelling.
    $binary7300Path = Join-Path $fbxMaterialRoot 'binary-7300.fbx'
    $payload7300 = [Text.Encoding]::UTF8.GetBytes("lashtester_head`0$([char]1)Material")
    $bytes7300 = [Collections.Generic.List[byte]]::new()
    $bytes7300.AddRange([Text.Encoding]::ASCII.GetBytes('Kaydara FBX Binary  '))
    $bytes7300.AddRange([byte[]]@(0, 26, 0))
    $bytes7300.Add([byte][char]'S')
    $bytes7300.AddRange([BitConverter]::GetBytes([uint32]$payload7300.Length))
    $bytes7300.AddRange($payload7300)
    [IO.File]::WriteAllBytes($binary7300Path, $bytes7300.ToArray())
    $binary7300Refs = @($fbxMaterialRead.Invoke($null, @([string]$binary7300Path)))
    if (($binary7300Refs.Count -ne 1) -or
        ($binary7300Refs[0].SourceName -ne 'lashtester_head') -or
        ($binary7300Refs[0].AuthoringReference -ne 'materials/lashtester_head')) {
        throw 'FBX 7300 binary namespace material slot was not normalized to the authoring material reference.'
    }
}
finally {
    if (Test-Path -LiteralPath $fbxMaterialRoot) {
        Remove-Item -LiteralPath $fbxMaterialRoot -Recurse -Force
    }
}

# The DMX Vertex Color sidecar reader must parse binary FBX node records,
# including compressed arrays and both pre-7500 and 7500+ record layouts.
function New-TestFbxProperty([char]$Type, $Value, [bool]$Compressed = $false) {
    [pscustomobject]@{ Type = $Type; Value = $Value; Compressed = $Compressed }
}
function New-TestFbxNode([string]$Name, [object[]]$Properties = @(), [object[]]$Children = @()) {
    [pscustomobject]@{ Name = $Name; Properties = $Properties; Children = $Children }
}
function Write-TestFbxProperty([IO.BinaryWriter]$Writer, $Property) {
    $Writer.Write([byte][char]$Property.Type)
    switch ([char]$Property.Type) {
        'L' { $Writer.Write([int64]$Property.Value); return }
        'S' {
            $payload = [Text.Encoding]::UTF8.GetBytes([string]$Property.Value)
            $Writer.Write([uint32]$payload.Length)
            $Writer.Write($payload)
            return
        }
        { $_ -eq 'd' -or $_ -eq 'i' } {
            $rawStream = [IO.MemoryStream]::new()
            try {
                $rawWriter = [IO.BinaryWriter]::new($rawStream)
                try {
                    foreach ($value in @($Property.Value)) {
                        if ($Property.Type -eq 'd') { $rawWriter.Write([double]$value) }
                        else { $rawWriter.Write([int32]$value) }
                    }
                    $rawWriter.Flush()
                    $raw = $rawStream.ToArray()
                }
                finally { $rawWriter.Dispose() }
            }
            finally { $rawStream.Dispose() }

            $stored = $raw
            $encoding = [uint32]0
            if ($Property.Compressed) {
                $compressedStream = [IO.MemoryStream]::new()
                try {
                    $zlib = [IO.Compression.ZLibStream]::new($compressedStream, [IO.Compression.CompressionLevel]::Optimal, $true)
                    try { $zlib.Write($raw, 0, $raw.Length) }
                    finally { $zlib.Dispose() }
                    $stored = $compressedStream.ToArray()
                    $encoding = [uint32]1
                }
                finally { $compressedStream.Dispose() }
            }

            $Writer.Write([uint32]@($Property.Value).Count)
            $Writer.Write($encoding)
            $Writer.Write([uint32]$stored.Length)
            $Writer.Write([byte[]]$stored)
            return
        }
        default { throw "Unsupported test FBX property type: $($Property.Type)" }
    }
}
function Write-TestFbxNode([IO.BinaryWriter]$Writer, $Node, [bool]$Wide) {
    $start = $Writer.BaseStream.Position
    if ($Wide) {
        $Writer.Write([uint64]0); $Writer.Write([uint64]0); $Writer.Write([uint64]0)
    }
    else {
        $Writer.Write([uint32]0); $Writer.Write([uint32]0); $Writer.Write([uint32]0)
    }

    $nameBytes = [Text.Encoding]::UTF8.GetBytes([string]$Node.Name)
    $Writer.Write([byte]$nameBytes.Length)
    $Writer.Write($nameBytes)
    $propertyStart = $Writer.BaseStream.Position
    foreach ($property in @($Node.Properties)) { Write-TestFbxProperty $Writer $property }
    $propertyEnd = $Writer.BaseStream.Position

    foreach ($child in @($Node.Children)) { Write-TestFbxNode $Writer $child $Wide }
    if (@($Node.Children).Count -gt 0) {
        $Writer.Write([byte[]]::new($(if ($Wide) { 25 } else { 13 })))
    }

    $end = $Writer.BaseStream.Position
    $Writer.BaseStream.Position = $start
    if ($Wide) {
        $Writer.Write([uint64]$end)
        $Writer.Write([uint64]@($Node.Properties).Count)
        $Writer.Write([uint64]($propertyEnd - $propertyStart))
    }
    else {
        $Writer.Write([uint32]$end)
        $Writer.Write([uint32]@($Node.Properties).Count)
        $Writer.Write([uint32]($propertyEnd - $propertyStart))
    }
    $Writer.BaseStream.Position = $end
}
function Write-TestBinaryVertexColorFbx([string]$Path, [uint32]$Version) {
    $wide = $Version -ge 7500
    $model = New-TestFbxNode 'Model' @(
        (New-TestFbxProperty 'L' ([int64]100)),
        (New-TestFbxProperty 'S' 'Model::dynamo_body'),
        (New-TestFbxProperty 'S' 'Mesh')
    )
    $colorLayer = New-TestFbxNode 'LayerElementColor' @() @(
        (New-TestFbxNode 'MappingInformationType' @((New-TestFbxProperty 'S' 'ByPolygonVertex'))),
        (New-TestFbxNode 'ReferenceInformationType' @((New-TestFbxProperty 'S' 'Direct'))),
        (New-TestFbxNode 'Colors' @((New-TestFbxProperty 'd' ([double[]]@(1,0,0,1, 0,1,0,1, 0,0,1,1)) $true)))
    )
    $geometry = New-TestFbxNode 'Geometry' @(
        (New-TestFbxProperty 'L' ([int64]200)),
        (New-TestFbxProperty 'S' 'Geometry::dynamo_body'),
        (New-TestFbxProperty 'S' 'Mesh')
    ) @(
        (New-TestFbxNode 'Vertices' @((New-TestFbxProperty 'd' ([double[]]@(0,0,0, 1,0,0, 0,1,0)) $true))),
        (New-TestFbxNode 'PolygonVertexIndex' @((New-TestFbxProperty 'i' ([int[]]@(0,1,-3)) $true))),
        $colorLayer
    )
    $objects = New-TestFbxNode 'Objects' @() @($model, $geometry)
    $connections = New-TestFbxNode 'Connections' @() @(
        (New-TestFbxNode 'C' @(
            (New-TestFbxProperty 'S' 'OO'),
            (New-TestFbxProperty 'L' ([int64]200)),
            (New-TestFbxProperty 'L' ([int64]100))
        ))
    )

    $stream = [IO.File]::Open($Path, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $writer = [IO.BinaryWriter]::new($stream)
        try {
            $writer.Write([Text.Encoding]::ASCII.GetBytes('Kaydara FBX Binary  '))
            $writer.Write([byte[]]@(0, 26, 0))
            $writer.Write($Version)
            Write-TestFbxNode $writer $objects $wide
            Write-TestFbxNode $writer $connections $wide
            $writer.Write([byte[]]::new($(if ($wide) { 25 } else { 13 })))
            $writer.Flush()
        }
        finally { $writer.Dispose() }
    }
    finally { $stream.Dispose() }
}

$vertexFbxReaderType = $assembly.GetType('Deadlimit.Core.AsciiFbxVertexColorReader', $true)
$vertexFbxRead = $vertexFbxReaderType.GetMethod('Read', $publicStatic)
if ($null -eq $vertexFbxRead) { throw 'AsciiFbxVertexColorReader.Read was not found.' }
$binaryVertexRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-binary-vertexcolor-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($binaryVertexRoot) | Out-Null
    foreach ($version in [uint32[]]@(7400, 7500)) {
        $fixture = Join-Path $binaryVertexRoot "vertexcolor-$version.fbx"
        Write-TestBinaryVertexColorFbx $fixture $version
        $meshes = @($vertexFbxRead.Invoke($null, @([string]$fixture)))
        if (($meshes.Count -ne 1) -or ($meshes[0].Name -ne 'dynamo_body') -or (-not $meshes[0].HasColors) -or ($meshes[0].ControlPoints.Count -ne 3) -or ($meshes[0].Polygons.Count -ne 1) -or ($meshes[0].Polygons[0].Colors.Count -ne 3)) {
            throw "Binary FBX $version Vertex Color fixture was not parsed correctly."
        }
    }
}
finally {
    if (Test-Path -LiteralPath $binaryVertexRoot) { Remove-Item -LiteralPath $binaryVertexRoot -Recurse -Force }
}

# Prepared DMX files must resolve material paths themselves because opening a
# RenderMeshFile directly in ModelDoc does not apply the parent VMDL MaterialGroup remaps.
$preparedDmxRemapType = $assembly.GetType('Deadlimit.Core.PreparedDmxMaterialRemapService', $true)
$preparedDmxApply = $preparedDmxRemapType.GetMethod('Apply', $publicStatic)
$vmdlRemapType = $assembly.GetType('Deadlimit.Core.VmdlMaterialRemap', $true)
if ($null -eq $preparedDmxApply -or $null -eq $vmdlRemapType) {
    throw 'Prepared DMX direct-material remap service was not found.'
}
$dmxMaterialRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-dmx-material-remap-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($dmxMaterialRoot) | Out-Null
    $dmxMaterialPath = Join-Path $dmxMaterialRoot 'preview.dmx'
    $dmxMaterialText = @'
<!-- dmx encoding keyvalues2 1 format model 22 -->
"DmeModel"
{
    "id" "elementid" "11111111-1111-1111-1111-111111111111"
    "name" "string" "root"
    "retailMaterial" "DmeMaterial"
    {
        "id" "elementid" "22222222-2222-2222-2222-222222222222"
        "name" "string" "materials/models/heroes_wip/dynamo/materials/dynamo_body.vmat"
        "mtlName" "string" "materials/models/heroes_wip/dynamo/materials/dynamo_body.vmat"
    }
    "customMaterial" "DmeMaterial"
    {
        "id" "elementid" "33333333-3333-3333-3333-333333333333"
        "name" "string" "materials/hotpot_head_vertexcolor_metalness.vmat"
        "mtlName" "string" "materials/hotpot_head_vertexcolor_metalness.vmat"
    }
}
'@
    [IO.File]::WriteAllText($dmxMaterialPath, $dmxMaterialText)

    $directRemaps = [Array]::CreateInstance($vmdlRemapType, 2)
    $directRemaps.SetValue(
        [Activator]::CreateInstance($vmdlRemapType, [object[]]@(
            'materials/models/heroes_wip/dynamo/materials/dynamo_body.vmat',
            'models/heroes_wip/dynamo/materials/dynamo_body.vmat')),
        0)
    $directRemaps.SetValue(
        [Activator]::CreateInstance($vmdlRemapType, [object[]]@(
            'materials/hotpot_head_vertexcolor_metalness',
            'materials/hotpotdynamo/hotpot_head_vertexcolor_metalness.vmat')),
        1)

    $rewrittenMaterialCount = [int]$preparedDmxApply.Invoke(
        $null,
        [object[]]@([string]$dmxMaterialPath, $directRemaps))
    if ($rewrittenMaterialCount -ne 2) {
        throw "Prepared DMX direct-material remap rewrote $rewrittenMaterialCount material elements instead of 2."
    }

    $rewrittenDmxText = [IO.File]::ReadAllText($dmxMaterialPath)
    foreach ($expected in @(
        'models/heroes_wip/dynamo/materials/dynamo_body.vmat',
        'materials/hotpotdynamo/hotpot_head_vertexcolor_metalness.vmat')) {
        if (-not $rewrittenDmxText.Contains($expected)) {
            throw "Prepared DMX direct-material remap did not write expected target: $expected"
        }
    }
    foreach ($stale in @(
        'materials/models/heroes_wip/dynamo/materials/dynamo_body.vmat',
        'materials/hotpot_head_vertexcolor_metalness.vmat')) {
        if ($rewrittenDmxText.Contains($stale)) {
            throw "Prepared DMX direct-material remap left stale source path: $stale"
        }
    }
}
finally {
    if (Test-Path -LiteralPath $dmxMaterialRoot) {
        Remove-Item -LiteralPath $dmxMaterialRoot -Recurse -Force
    }
}

$atomicFileType = $assembly.GetType('Deadlimit.Core.AtomicFile', $true)
$atomicWriteAllText = $atomicFileType.GetMethod(
    'WriteAllText',
    $publicStatic,
    $null,
    [Type[]]@([string], [string], [Text.Encoding]),
    $null)
if ($null -eq $atomicWriteAllText) { throw 'AtomicFile.WriteAllText was not found.' }
$atomicRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-atomic-file-$([Guid]::NewGuid().ToString('N'))"
$atomicTarget = Join-Path $atomicRoot 'project.json'
$atomicReady = Join-Path $atomicRoot 'locked.ready'
$atomicLocker = $null
try {
    [IO.Directory]::CreateDirectory($atomicRoot) | Out-Null
    [IO.File]::WriteAllText($atomicTarget, 'before')
    $escapedTarget = $atomicTarget.Replace("'", "''")
    $escapedReady = $atomicReady.Replace("'", "''")
    $lockerCode = @"
`$stream = [IO.File]::Open('$escapedTarget', [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
try {
    [IO.File]::WriteAllText('$escapedReady', '')
    Start-Sleep -Milliseconds 350
}
finally {
    `$stream.Dispose()
}
"@
    $encodedLockerCode = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($lockerCode))
    $atomicLocker = Start-Process `
        -FilePath (Get-Command pwsh).Source `
        -ArgumentList @('-NoProfile', '-EncodedCommand', $encodedLockerCode) `
        -WindowStyle Hidden `
        -PassThru
    $readyDeadline = [DateTime]::UtcNow.AddSeconds(5)
    while (-not (Test-Path -LiteralPath $atomicReady) -and [DateTime]::UtcNow -lt $readyDeadline) {
        Start-Sleep -Milliseconds 25
    }
    if (-not (Test-Path -LiteralPath $atomicReady)) {
        throw 'AtomicFile contention smoke could not establish the external file lock.'
    }

    $atomicWriteArguments = [object[]]::new(3)
    $atomicWriteArguments[0] = [string]$atomicTarget
    $atomicWriteArguments[1] = [string]'after'
    $atomicWriteArguments[2] = $null
    $atomicWriteAllText.Invoke($null, $atomicWriteArguments)
    if ([IO.File]::ReadAllText($atomicTarget) -ne 'after') {
        throw 'AtomicFile contention retry did not publish the replacement contents.'
    }
    if (@(Get-ChildItem -LiteralPath $atomicRoot -Force -File -Filter '.project.json.tmp-*').Count -ne 0) {
        throw 'AtomicFile contention retry left a temporary file behind.'
    }
}
finally {
    if ($null -ne $atomicLocker) {
        if (-not $atomicLocker.HasExited -and -not $atomicLocker.WaitForExit(2000)) {
            $atomicLocker.Kill($true)
            $atomicLocker.WaitForExit()
        }
        $atomicLocker.Dispose()
    }
    if (Test-Path -LiteralPath $atomicRoot) {
        Remove-Item -LiteralPath $atomicRoot -Recurse -Force
    }
}

$buildServiceSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
$manifestSaveCount = ([regex]::Matches($buildServiceSource, 'ProjectStore\.Save\(manifest\);')).Count
if ($manifestSaveCount -ne 1) {
    throw "BuildAndTestService must publish its manifest once after AG2 and compiled-model updates; found $manifestSaveCount saves."
}
if ($buildServiceSource.Contains('mainModelWasCompiled', [StringComparison]::Ordinal)) {
    throw 'AnimGraph2 repair is still gated on the main VMDL being a direct compile target.'
}
foreach ($required in @(
    'ResourceCompiler can rebuild the main VMDL transitively',
    'Verifying AnimGraph2 / NmSkeleton on the compiled character model',
    'ApplyAg2(manifest, compiledMainModel, log, cancellationToken);')) {
    if (-not $buildServiceSource.Contains($required, [StringComparison]::Ordinal)) {
        throw "Unconditional compiled-model animation repair contract is missing: $required"
    }
}
foreach ($required in @(
    'slotOwnership.EnsureSlotAvailable(manifest);',
    'slotOwnership.RecordSuccessfulDeployment(manifest, vpkPath);',
    'VPK slot ownership updated by the deployment transaction.')) {
    if (-not $buildServiceSource.Contains($required, [StringComparison]::Ordinal)) {
        throw "Core VPK ownership transaction contract is missing: $required"
    }
}
$buildFeatureSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
if (-not $buildFeatureSource.Contains('if (manifest.Mode == ProjectMode.ImportedVpk)', [StringComparison]::Ordinal)) {
    throw 'BuildFeature does not leave authoring VPK ownership to the core deployment transaction.'
}

# BUILD FOR TEST must force selected resources and their raw dependencies past
# ResourceCompiler's timestamp cache while retaining the previous output on failure.
foreach ($required in @(
    'CompileOutputInvalidation.Begin(',
    'invalidatedOutputs.Commit();',
    'invalidatedOutputs.Restore(log);',
    'Restored previous compiled outputs after failed forced rebuild.',
    'process.Kill(entireProcessTree: true);')) {
    if (-not $buildServiceSource.Contains($required, [StringComparison]::Ordinal)) {
        throw "Compiled-output invalidation contract is missing: $required"
    }
}
$buildServiceType = $assembly.GetType('Deadlimit.Core.BuildAndTestService', $true)
$isLooseHeroSelectResource = $buildServiceType.GetMethod('IsLooseHeroSelectResource', $nonPublicStatic)
if ($null -eq $isLooseHeroSelectResource) {
    throw 'BuildAndTestService.IsLooseHeroSelectResource was not found.'
}
$heroSelectPackages = [string[]]@('maps/ui/hero_prefabs/tengu.vpk')
foreach ($looseSceneResource in @(
    'maps/ui/hero_prefabs/tengu.vmap_c',
    'maps/ui/hero_prefabs/tengu/worldnodes/n0.vwnod_c',
    'maps/ui/hero_prefabs/tengu/entities/unnamed_9.vmdl_c')) {
    if (-not [bool]$isLooseHeroSelectResource.Invoke(
        $null,
        [object[]]@([string]$looseSceneResource, $heroSelectPackages))) {
        throw "Loose hero-select resource was not excluded from the outer VPK: $looseSceneResource"
    }
}
foreach ($retainedResource in @(
    'maps/ui/hero_prefabs/tengu.vpk',
    'models/heroes_wip/ivy/ivy.vmdl_c',
    'maps/ui/hero_prefabs/another/world.vwrld_c')) {
    if ([bool]$isLooseHeroSelectResource.Invoke(
        $null,
        [object[]]@([string]$retainedResource, $heroSelectPackages))) {
        throw "Unrelated authored resource was treated as a loose hero-select duplicate: $retainedResource"
    }
}
$dependencyOutput = $buildServiceType.GetMethod('GetDependencyCompiledRelativePath', $nonPublicStatic)
if ($null -eq $dependencyOutput) {
    throw 'BuildAndTestService.GetDependencyCompiledRelativePath was not found.'
}
$dependencyCases = @{
    'models/hero_body.dmx' = 'models/hero_body.vmesh_c'
    'models/hero_body.fbx' = 'models/hero_body.vmesh_c'
    'materials/hero_body_color.png' = 'materials/hero_body_color.vtex_c'
    'materials/hero_body_normal.tga' = 'materials/hero_body_normal.vtex_c'
}
foreach ($entry in $dependencyCases.GetEnumerator()) {
    $actual = [string]$dependencyOutput.Invoke($null, [object[]]@([string]$entry.Key))
    if ($actual -ne $entry.Value) {
        throw "Dependency output '$($entry.Key)' resolved to '$actual', expected '$($entry.Value)'."
    }
}
if ($null -ne $dependencyOutput.Invoke($null, [object[]]@([string]'scripts/game.js'))) {
    throw 'A non-model/non-texture source was assigned a dependency-only compiled output.'
}
$invalidationType = $buildServiceType.GetNestedType('CompileOutputInvalidation', [Reflection.BindingFlags]::NonPublic)
$beginInvalidation = $invalidationType.GetMethod('Begin', $nonPublicStatic)
$instanceFlags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Instance
$restoreInvalidation = $invalidationType.GetMethod('Restore', $instanceFlags)
$commitInvalidation = $invalidationType.GetMethod('Commit', $instanceFlags)
if ($null -eq $beginInvalidation -or $null -eq $restoreInvalidation -or $null -eq $commitInvalidation) {
    throw 'CompileOutputInvalidation transaction methods were not found.'
}
$invalidationRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-output-invalidation-$([Guid]::NewGuid().ToString('N'))"
$contentRoot = Join-Path $invalidationRoot 'content'
$gameRoot = Join-Path $invalidationRoot 'game'
$metadataRoot = Join-Path $invalidationRoot 'metadata'
$modelSource = Join-Path $contentRoot 'models\hero.vmdl'
$modelOutput = Join-Path $gameRoot 'models\hero.vmdl_c'
$textureOutput = Join-Path $gameRoot 'materials\hero_color.vtex_c'
try {
    [IO.Directory]::CreateDirectory((Split-Path $modelSource)) | Out-Null
    [IO.Directory]::CreateDirectory((Split-Path $modelOutput)) | Out-Null
    [IO.Directory]::CreateDirectory((Split-Path $textureOutput)) | Out-Null
    [IO.Directory]::CreateDirectory($metadataRoot) | Out-Null
    [IO.File]::WriteAllText($modelSource, 'source')
    [IO.File]::WriteAllText($modelOutput, 'old-model')
    [IO.File]::WriteAllText($textureOutput, 'old-texture')
    $logBuilder = [Text.StringBuilder]::new()
    $beginArguments = [object[]]@(
        [string]$contentRoot,
        [string]$gameRoot,
        [string]$metadataRoot,
        [string[]]@($modelSource),
        [string[]]@('materials/hero_color.png'),
        $logBuilder)
    $transaction = $beginInvalidation.Invoke($null, $beginArguments)
    if ((Test-Path -LiteralPath $modelOutput) -or (Test-Path -LiteralPath $textureOutput)) {
        throw 'Compile output invalidation left a selected stale output in place.'
    }
    $restoreInvalidation.Invoke($transaction, [object[]]@($logBuilder))
    if ([IO.File]::ReadAllText($modelOutput) -ne 'old-model' -or
        [IO.File]::ReadAllText($textureOutput) -ne 'old-texture') {
        throw 'Compile output invalidation did not restore previous outputs after failure.'
    }

    $transaction = $beginInvalidation.Invoke($null, $beginArguments)
    [IO.File]::WriteAllText($modelOutput, 'new-model')
    [IO.File]::WriteAllText($textureOutput, 'new-texture')
    $commitInvalidation.Invoke($transaction, @())
    if ([IO.File]::ReadAllText($modelOutput) -ne 'new-model' -or
        [IO.File]::ReadAllText($textureOutput) -ne 'new-texture') {
        throw 'Compile output invalidation replaced successful rebuilt outputs with stale backups.'
    }
    if (@(Get-ChildItem -LiteralPath $metadataRoot -Directory -Filter 'compile-output-backup-*').Count -ne 0) {
        throw 'Compile output invalidation left a transaction backup after success.'
    }
}
finally {
    if (Test-Path -LiteralPath $invalidationRoot) {
        Remove-Item -LiteralPath $invalidationRoot -Recurse -Force
    }
}

# All supported Deadlimit installations are Git checkouts. User state is centralized
# under LocalAppData and the toolchain service has no release-channel gate.
$userDataType = $assembly.GetType('Deadlimit.Core.UserDataPaths', $true)
$resolveUserData = $userDataType.GetMethod('ResolveRoot', $nonPublicStatic)
if ($null -eq $resolveUserData) { throw 'UserDataPaths.ResolveRoot was not found.' }
$resolvedUserData = [string]$resolveUserData.Invoke($null, @())
$expectedUserData = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Deadlimit'
if (-not [string]::Equals($resolvedUserData, $expectedUserData, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Deadlimit user data root is unexpected: $resolvedUserData"
}

foreach ($path in @(
    'internal/src/Deadlimit/Core/ProjectStore.cs',
    'internal/src/Deadlimit/Core/HeroCatalogService.cs',
    'internal/src/Deadlimit/App/ProjectLibraryFeature.cs')) {
    $source = Get-Content -LiteralPath $path -Raw
    if ($source.Contains('SpecialFolder.LocalApplicationData', [StringComparison]::Ordinal)) {
        throw "User state bypasses UserDataPaths in: $path"
    }
}

$toolchainSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ToolchainDependencyService.cs' -Raw
if ($toolchainSource.Contains('ReleaseChannelPolicy', [StringComparison]::Ordinal)) {
    throw 'Retired release-channel toolchain policy remains in ToolchainDependencyService.'
}
foreach ($required in @(
    'IsCsdkSetupCurrent(csdkRoot, catalog.Generation, depotKeys)',
    'DownloadRequiredDepotsAsync(',
    'arguments.AddRange(depots.Select(depot => depot.DepotId));',
    'arguments.Add("-qr");',
    'arguments.Add("-remember-password");',
    'InstallCsdkArchiveAsync(catalog, stagedCsdkRoot',
    'CopyDirectory(stagedGameRoot, Path.Combine(csdkRoot, "game")')) {
    if (-not $toolchainSource.Contains($required, [StringComparison]::Ordinal)) {
        throw "CSDK fine-tuning safety contract is missing: $required"
    }
}
if ($toolchainSource.Contains('DepotArguments(depot, csdkRoot)', [StringComparison]::Ordinal)) {
    throw 'CSDK fine-tuning still downloads depots directly into the live CSDK folder.'
}

$toolchainType = $assembly.GetType('Deadlimit.Core.ToolchainDependencyService', $true)
$isSetupCurrent = $toolchainType.GetMethod('IsCsdkSetupCurrent', $nonPublicStatic)
if ($null -eq $isSetupCurrent) { throw 'ToolchainDependencyService.IsCsdkSetupCurrent was not found.' }
$setupRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-csdk-setup-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $setupRoot 'game\citadel')) | Out-Null
    [IO.File]::WriteAllText((Join-Path $setupRoot 'csdkcfg.exe'), '')
    [IO.File]::WriteAllText((Join-Path $setupRoot 'game\citadel\gameinfo.gi'), '')
    $marker = @{
        generation = 12
        depots = @(
            @{ AppId = '1422450'; DepotId = '1422451'; ManifestId = 'manifest-a' }
            @{ AppId = '1422450'; DepotId = '1422456'; ManifestId = 'manifest-b' }
        )
    } | ConvertTo-Json -Depth 4
    [IO.File]::WriteAllText((Join-Path $setupRoot '.deadlimit-csdk-setup.json'), $marker)
    [string[]]$expectedDepots = @(
        '1422450:1422451:manifest-a',
        '1422450:1422456:manifest-b'
    )
    $currentArgs = [object[]]@([string]$setupRoot, [int]12, [string[]]$expectedDepots)
    if (-not [bool]$isSetupCurrent.Invoke($null, $currentArgs)) {
        throw 'A complete matching CSDK fine-tuning marker was not recognized.'
    }
    [string[]]$changedDepots = @('1422450:1422451:different')
    $changedArgs = [object[]]@([string]$setupRoot, [int]12, [string[]]$changedDepots)
    if ([bool]$isSetupCurrent.Invoke($null, $changedArgs)) {
        throw 'A mismatched CSDK fine-tuning marker was incorrectly treated as current.'
    }
}
finally {
    if (Test-Path -LiteralPath $setupRoot) {
        Remove-Item -LiteralPath $setupRoot -Recurse -Force
    }
}

$buildType = $assembly.GetType('Deadlimit.Core.BuildAndTestService', $true)
$findUnsupportedParticles = $buildType.GetMethod('FindUnsupportedParticleSources', $nonPublicStatic)
if ($null -eq $findUnsupportedParticles) { throw 'BuildAndTestService.FindUnsupportedParticleSources was not found.' }
$resolveFullCompileTargets = $buildType.GetMethod('ResolveFullCompileTargets', $nonPublicStatic)
if ($null -eq $resolveFullCompileTargets) { throw 'BuildAndTestService.ResolveFullCompileTargets was not found.' }
$resolveProjectOwnedSources = $buildType.GetMethod('ResolveProjectOwnedSources', $nonPublicStatic)
if ($null -eq $resolveProjectOwnedSources) { throw 'BuildAndTestService.ResolveProjectOwnedSources was not found.' }
$loadBaselineHashes = $buildType.GetMethod('LoadOrUpdateSourceBaselineHashes', $nonPublicStatic)
if ($null -eq $loadBaselineHashes) { throw 'BuildAndTestService.LoadOrUpdateSourceBaselineHashes was not found.' }
$hashContentTreeCached = $buildType.GetMethod('HashContentTreeCached', $nonPublicStatic)
if ($null -eq $hashContentTreeCached) { throw 'BuildAndTestService.HashContentTreeCached was not found.' }
$compileSelectionRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-compile-selection-$([Guid]::NewGuid().ToString('N'))"
try {
    $contentRoot = Join-Path $compileSelectionRoot 'content'
    $sourceRoot = Join-Path $compileSelectionRoot '0source'
    [IO.Directory]::CreateDirectory((Join-Path $contentRoot 'particles')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $sourceRoot 'particles')) | Out-Null
    $retailCopy = Join-Path $contentRoot 'particles\retail-copy.vpcf'
    $edited = Join-Path $contentRoot 'particles\edited.vpcf'
    $newEffect = Join-Path $contentRoot 'particles\new-effect.vpcf'
    [IO.File]::WriteAllText($retailCopy, 'retail')
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'particles\retail-copy.vpcf'), 'retail')
    [IO.File]::WriteAllText($edited, 'edited')
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'particles\edited.vpcf'), 'retail')
    [IO.File]::WriteAllText($newEffect, 'new')
    $contentCachePath = Join-Path $compileSelectionRoot 'prepared-content-hashes.json'
    $contentCacheArgs = [object[]]@(
        [string]$contentRoot,
        [string]$contentCachePath,
        [Threading.CancellationToken]::None)
    $firstContentHash = $hashContentTreeCached.Invoke($null, $contentCacheArgs)
    if ($firstContentHash.HashedCount -ne 3 -or $firstContentHash.ReusedCount -ne 0) {
        throw 'First prepared-content hash pass did not hash every file.'
    }
    $secondContentHash = $hashContentTreeCached.Invoke($null, $contentCacheArgs)
    if ($secondContentHash.HashedCount -ne 0 -or $secondContentHash.ReusedCount -ne 3) {
        throw 'Unchanged prepared content did not reuse every cached SHA-256.'
    }
    $oldEditedHash = [string]$secondContentHash.Hashes['particles/edited.vpcf']
    [IO.File]::WriteAllText($edited, 'EDITED')
    [IO.File]::SetLastWriteTimeUtc($edited, [DateTime]::UtcNow.AddSeconds(2))
    $changedContentHash = $hashContentTreeCached.Invoke($null, $contentCacheArgs)
    if ($changedContentHash.HashedCount -ne 1 `
        -or $changedContentHash.ReusedCount -ne 2 `
        -or [string]$changedContentHash.Hashes['particles/edited.vpcf'] -eq $oldEditedHash) {
        throw 'Prepared-content hash cache did not rehash the changed file only.'
    }
    [IO.File]::WriteAllText($edited, 'edited')
    $hashes = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @($retailCopy, $edited, $newEffect)) {
        $relative = [IO.Path]::GetRelativePath($contentRoot, $path).Replace('\', '/')
        $hashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $cachePath = Join-Path $compileSelectionRoot 'source-baseline-hashes.json'
    $requiredPaths = [string[]]@($hashes.Keys)
    $cacheArgs = [object[]]@(
        [string]$sourceRoot,
        $requiredPaths,
        [string]'extraction-1',
        [string]$cachePath,
        [Text.StringBuilder]::new(),
        [Threading.CancellationToken]::None)
    $baselineHashes = $loadBaselineHashes.Invoke($null, $cacheArgs)
    if (-not (Test-Path -LiteralPath $cachePath) -or $baselineHashes.Count -ne 2) {
        throw 'Source baseline hash cache was not populated for extracted files.'
    }
    $retailRelative = [IO.Path]::GetRelativePath($contentRoot, $retailCopy).Replace('\', '/')
    $cachedRetailHash = [string]$baselineHashes[$retailRelative]
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'particles\retail-copy.vpcf'), 'new extraction bytes')
    $sameIdentityHashes = $loadBaselineHashes.Invoke($null, $cacheArgs)
    if ([string]$sameIdentityHashes[$retailRelative] -ne $cachedRetailHash) {
        throw 'A stable extraction identity unexpectedly rehashed the immutable 0source baseline.'
    }
    $cacheArgs[2] = [string]'extraction-2'
    $newIdentityHashes = $loadBaselineHashes.Invoke($null, $cacheArgs)
    if ([string]$newIdentityHashes[$retailRelative] -eq $cachedRetailHash) {
        throw 'A changed extraction identity did not invalidate the 0source baseline cache.'
    }
    [IO.File]::WriteAllText((Join-Path $sourceRoot 'particles\retail-copy.vpcf'), 'retail')
    $cacheArgs[2] = [string]'extraction-3'
    $baselineHashes = $loadBaselineHashes.Invoke($null, $cacheArgs)
    $projectOwnedArgs = [object[]]@($hashes, $baselineHashes)
    $projectOwned = $resolveProjectOwnedSources.Invoke($null, $projectOwnedArgs)
    $selectionArgs = [object[]]@(
        [string]$contentRoot,
        [string[]]@($retailCopy, $edited, $newEffect),
        $projectOwned)
    $selected = @($resolveFullCompileTargets.Invoke($null, $selectionArgs))
    if ($selected.Count -ne 2 -or $selected -contains $retailCopy -or $selected -notcontains $edited -or $selected -notcontains $newEffect) {
        throw 'Full-build compile selection did not reuse the retail-identical source baseline.'
    }
}
finally {
    if (Test-Path -LiteralPath $compileSelectionRoot) {
        Remove-Item -LiteralPath $compileSelectionRoot -Recurse -Force
    }
}

# Incremental dependency invalidation must select only actual VMAT/VMDL consumers,
# while a successful VPCF with an existing compiled output remains cached.
$resolveIncrementalCompileTargets = $buildType.GetMethod('ResolveIncrementalCompileTargets', $nonPublicStatic)
if ($null -eq $resolveIncrementalCompileTargets) {
    throw 'BuildAndTestService.ResolveIncrementalCompileTargets was not found.'
}
$dependencyRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-dependency-selection-$([Guid]::NewGuid().ToString('N'))"
try {
    $contentRoot = Join-Path $dependencyRoot 'content'
    $gameRoot = Join-Path $dependencyRoot 'game'
    foreach ($folder in @(
        (Join-Path $contentRoot 'materials\a'),
        (Join-Path $contentRoot 'materials\b'),
        (Join-Path $contentRoot 'models\a'),
        (Join-Path $contentRoot 'models\b'),
        (Join-Path $contentRoot 'particles'),
        (Join-Path $gameRoot 'materials\a'),
        (Join-Path $gameRoot 'materials\b'),
        (Join-Path $gameRoot 'models\a'),
        (Join-Path $gameRoot 'models\b'),
        (Join-Path $gameRoot 'particles'))) {
        [IO.Directory]::CreateDirectory($folder) | Out-Null
    }

    $vmatA = Join-Path $contentRoot 'materials\a\a.vmat'
    $vmatB = Join-Path $contentRoot 'materials\b\b.vmat'
    $vmdlA = Join-Path $contentRoot 'models\a\a.vmdl'
    $vmdlB = Join-Path $contentRoot 'models\b\b.vmdl'
    $particle = Join-Path $contentRoot 'particles\cached.vpcf'
    [IO.File]::WriteAllText($vmatA, 'TextureColor "materials/a/a.png"')
    [IO.File]::WriteAllText($vmatB, 'TextureColor "materials/b/b.png"')
    [IO.File]::WriteAllText($vmdlA, 'RenderMeshFile "models/a/a.dmx"')
    [IO.File]::WriteAllText($vmdlB, 'RenderMeshFile "models/b/b.dmx"')
    [IO.File]::WriteAllText($particle, '<!-- kv3 encoding:text format:vpcf63:version{x} -->')

    foreach ($relativeOutput in @(
        'materials\a\a.vmat_c',
        'materials\b\b.vmat_c',
        'models\a\a.vmdl_c',
        'models\b\b.vmdl_c',
        'particles\cached.vpcf_c')) {
        [IO.File]::WriteAllText((Join-Path $gameRoot $relativeOutput), 'compiled')
    }

    $directSources = [string[]]@($vmatA, $vmatB, $vmdlA, $vmdlB, $particle)
    $changedDependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $changedDependencies.Add('materials/a/a.png') | Out-Null
    $changedDependencies.Add('models/a/a.dmx') | Out-Null
    $removedDependencies = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $ownedSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($relative in @('materials/a/a.vmat','materials/b/b.vmat','models/a/a.vmdl','models/b/b.vmdl','particles/cached.vpcf')) {
        $ownedSources.Add($relative) | Out-Null
    }

    $incrementalArgs = [object[]]@(
        [string]$contentRoot,
        [string]$gameRoot,
        $directSources,
        $changedDependencies,
        $removedDependencies,
        $ownedSources)
    $incrementalTargets = @($resolveIncrementalCompileTargets.Invoke($null, $incrementalArgs))
    if ($incrementalTargets.Count -ne 2 `
        -or $incrementalTargets -notcontains $vmatA `
        -or $incrementalTargets -notcontains $vmdlA `
        -or $incrementalTargets -contains $vmatB `
        -or $incrementalTargets -contains $vmdlB `
        -or $incrementalTargets -contains $particle) {
        throw 'Incremental dependency selection broadened beyond actual consumers or rebuilt a cached VPCF.'
    }

    Remove-Item -LiteralPath (Join-Path $gameRoot 'particles\cached.vpcf_c') -Force
    $targetsWithMissingParticle = @($resolveIncrementalCompileTargets.Invoke($null, $incrementalArgs))
    if ($targetsWithMissingParticle -notcontains $particle) {
        throw 'A project-owned VPCF with a genuinely missing compiled output was not selected.'
    }
}
finally {
    if (Test-Path -LiteralPath $dependencyRoot) {
        Remove-Item -LiteralPath $dependencyRoot -Recurse -Force
    }
}

$selectParticlesToSkip = $buildType.GetMethod('SelectParticleSourcesToSkip', $nonPublicStatic)
if ($null -eq $selectParticlesToSkip) { throw 'BuildAndTestService.SelectParticleSourcesToSkip was not found.' }
$particleSources = [string[]]@('C:\addon\supported.vpcf', 'C:\addon\failed-64.vpcf', 'C:\addon\failed-65.vpcf')
$requestedSkippedParticles = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$requestedSkippedParticles.Add('C:\addon\failed-64.vpcf') | Out-Null
$requestedSkippedParticles.Add('C:\addon\failed-65.vpcf') | Out-Null
$selectiveSkipArgs = [object[]]@($particleSources, $requestedSkippedParticles, $false)
$selectiveSkipped = @($selectParticlesToSkip.Invoke($null, $selectiveSkipArgs))
if ($selectiveSkipped.Count -ne 2 -or $selectiveSkipped -contains 'C:\addon\supported.vpcf') {
    throw 'Selective VPCF fallback did not preserve a successfully compiled particle source.'
}
$skipAllArgs = [object[]]@($particleSources, $requestedSkippedParticles, $true)
$allSkipped = @($selectParticlesToSkip.Invoke($null, $skipAllArgs))
if ($allSkipped.Count -ne 3) {
    throw 'Legacy all-VPCF fallback did not select every particle source.'
}
$particleRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-particle-format-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($particleRoot) | Out-Null
    [IO.File]::WriteAllText((Join-Path $particleRoot 'supported.vpcf'), '<!-- kv3 encoding:text format:vpcf63:version{x} -->')
    [IO.File]::WriteAllText((Join-Path $particleRoot 'newer-64.vpcf'), '<!-- kv3 encoding:text format:vpcf64:version{x} -->')
    [IO.File]::WriteAllText((Join-Path $particleRoot 'newer-65.vpcf'), '<!-- kv3 encoding:text format:vpcf65:version{x} -->')
    $particleArgs = [object[]]@([string]$particleRoot, [int]63, [Threading.CancellationToken]::None)
    $unsupported = @($findUnsupportedParticles.Invoke($null, $particleArgs))
    if ($unsupported.Count -ne 2) {
        throw "Expected two unsupported particle sources; found $($unsupported.Count)."
    }
}
finally {
    if (Test-Path -LiteralPath $particleRoot) {
        Remove-Item -LiteralPath $particleRoot -Recurse -Force
    }
}
$settingsSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/SettingsForm.cs' -Raw
foreach ($retired in @('ReleaseChannelPolicy', 'PortableToolchainNotice', '_allowUnverifiedToolchainAutomation')) {
    if ($settingsSource.Contains($retired, [StringComparison]::Ordinal)) {
        throw "Retired portable Settings policy remains: $retired"
    }
}

# LIVE SYNC is the only user-facing active-mode text that activates the pulse feature.
$pulseType = $assembly.GetType('Deadlimit.App.OnlineCsdkPulseFeature', $true)
$isLiveSync = $pulseType.GetMethod('IsLiveSyncText', $nonPublicStatic)
if ($null -eq $isLiveSync) { throw 'OnlineCsdkPulseFeature.IsLiveSyncText was not found.' }
foreach ($text in @('▶  LIVE SYNC', 'LIVE SYNC')) {
    if (-not [bool]$isLiveSync.Invoke($null, @($text))) {
        throw "LIVE SYNC pulse detector rejected '$text'."
    }
}
foreach ($text in @('▶  LAUNCH CSDK', '▶  ЗАПУСК CSDK', '▶  ONLINE CSDK', '▶  CSDK ОНЛАЙН')) {
    if ([bool]$isLiveSync.Invoke($null, @($text))) {
        throw "Non-LIVE-SYNC text must not activate the pulse: '$text'."
    }
}

# Normal PREPARE parser must recognize the same practical texture naming used by exports.
$bindingType = $assembly.GetType('Deadlimit.Core.ProjectTextureBindingService', $true)
$parse = $bindingType.GetMethod('ParseTextureCandidate', $nonPublicStatic)
if ($null -eq $parse) { throw 'ProjectTextureBindingService.ParseTextureCandidate was not found.' }
$cases = [ordered]@{
    'ivy_builder_body_color.png' = 'color'
    'ivy_builder_body_normal.png' = 'normal'
    'ivy_builder_body_roughness.png' = 'roughness'
    'ivy_builder_body_rimmask.png' = 'rimmask'
    'ivy_builder_body_RimLightMask.png' = 'rimmask'
    'ivy_builder_body_metalnessmask.png' = 'metalness'
    'ivy_builder_body.MetallicMap.png' = 'metalness'
    'ivy_builder_body-NRM.png' = 'normal'
}

# Rim-light mask creation/regeneration follows rimmask -> AO -> white.
$applyRimFallback = $bindingType.GetMethod('ApplyRimLightMaskFallback', $nonPublicStatic)
$textureFallback = $bindingType.GetMethod('GetTextureFallback', $nonPublicStatic)
if ($null -eq $applyRimFallback -or $null -eq $textureFallback) {
    throw 'Rim-light mask fallback helpers were not found.'
}
$rimBindings = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$rimBindings['ao'] = 'materials/ivybuilder/textures/ivy_builder_body_ambientocclusion.png'
$rimLog = [Text.StringBuilder]::new()
$applyRimFallback.Invoke($null, @($rimBindings, $rimLog, 'materials/ivybuilder/ivy_builder_body.vmat'))
if ($rimBindings['rimmask'] -ne $rimBindings['ao']) {
    throw 'Missing rim-light mask did not fall back to the matching AO texture.'
}
$whiteRim = [string]$textureFallback.Invoke($null, @('TextureRimLightMask1', $false))
if ($whiteRim -ne '[1.000000 1.000000 1.000000 0.000000]') {
    throw "Rim-light mask neutral fallback is '$whiteRim', expected white."
}
foreach ($entry in $cases.GetEnumerator()) {
    $candidate = $parse.Invoke($null, @("C:\temp\$($entry.Key)", 'materials/ivybuilder'))
    if ($null -eq $candidate) { throw "Normal PREPARE parser rejected $($entry.Key)." }
    if ($candidate.Semantic -ne $entry.Value) {
        throw "$($entry.Key) resolved to semantic '$($candidate.Semantic)', expected '$($entry.Value)'."
    }
    if ($candidate.BaseToken -ne 'ivybuilderbody') {
        throw "$($entry.Key) resolved base token '$($candidate.BaseToken)', expected 'ivybuilderbody'."
    }
}

# Matching maps must be insertable even when a Material Editor VMAT omitted the slot.
$preferred = $bindingType.GetMethod('GetPreferredStandardTextureKey', $nonPublicStatic)
$upsert = $bindingType.GetMethod('UpsertTextureAssignment', $nonPublicStatic)
if ($null -eq $preferred -or $null -eq $upsert) {
    throw 'Project texture replace-or-insert helpers were not found.'
}
$key = [string]$preferred.Invoke($null, @('roughness', $false))
if ($key -ne 'TextureRoughness') { throw "Unexpected standard roughness slot '$key'." }
$source = "Layer0`n{`n    `"TextureColor`"`t`"[0.5 0.5 0.5 0]`"`n}`n"
$texture = 'materials/ivybuilder/textures/ivy_builder_body_roughness.png'
$patched = [string]$upsert.Invoke($null, @($source, $key, $texture))
if (-not $patched.Contains('"TextureRoughness"')) { throw 'Missing roughness slot was not inserted.' }
if (-not $patched.Contains($texture)) { throw 'Inserted roughness slot did not receive the matching project texture.' }
$patchedAgain = [string]$upsert.Invoke($null, @($patched, $key, 'materials/ivybuilder/textures/new_roughness.png'))
if (([regex]::Matches($patchedAgain, '"TextureRoughness"')).Count -ne 1) {
    throw 'Texture upsert created a duplicate standard slot.'
}

# Material Editor writes CRLF VMATs. PREPARE must recognize those Texture* lines
# and collapse equivalent legacy insertions instead of appending them every run.
$readAssignments = $bindingType.GetMethod('ReadAssignments', $nonPublicStatic)
$deduplicateAssignments = $bindingType.GetMethod('RemoveRedundantBoundTextureAssignments', $nonPublicStatic)
if ($null -eq $readAssignments -or $null -eq $deduplicateAssignments) {
    throw 'Project texture CRLF/deduplication helpers were not found.'
}
$aoTexture = 'materials/ivybuilder/textures/ivy_builder_body_ambientocclusion.png'
$crlfVmat = "Layer0`r`n{`r`n`tTextureAmbientOcclusion1 `"$aoTexture`"`r`n    `"TextureAmbientOcclusion`"`t`"$aoTexture`"`r`n    `"TextureAmbientOcclusion`"`t`"$aoTexture`"`r`n}`r`n"
if (@($readAssignments.Invoke($null, @($crlfVmat))).Count -ne 3) {
    throw 'Project texture parser did not recognize CRLF Texture* assignments.'
}
$aoBindings = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::OrdinalIgnoreCase)
$aoBindings['ao'] = $aoTexture
$deduplicatedVmat = [string]$deduplicateAssignments.Invoke($null, @($crlfVmat, $aoBindings))
if (([regex]::Matches($deduplicatedVmat, 'TextureAmbientOcclusion')).Count -ne 1 `
    -or -not $deduplicatedVmat.Contains('TextureAmbientOcclusion1', [StringComparison]::Ordinal)) {
    throw 'Equivalent CRLF ambient-occlusion assignments were not collapsed to the preferred slot.'
}

# Clean PREPARE exposes independent reset sections and explicit backup/no-backup actions.
$prepareType = $assembly.GetType('Deadlimit.Core.PrepareAuthoringService', $true)
$prepareMethod = $prepareType.GetMethods() | Where-Object { $_.Name -eq 'PrepareAsync' } | Select-Object -First 1
$optionsParameter = $prepareMethod.GetParameters() | Where-Object { $_.Name -eq 'options' }
if ($null -eq $optionsParameter) { throw 'PrepareAsync options parameter is missing.' }
if (-not $optionsParameter.HasDefaultValue -or $null -ne $optionsParameter.DefaultValue) {
    throw 'Normal PREPARE must default to preserve-artist-work options.'
}
$buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
foreach ($required in @(
    'BuildFailureLogService.EnsureCurrentFailureLog(',
    'buildAttemptStartedUtc',
    'failureLogSummary')) {
    if (-not $buildSource.Contains($required)) {
        throw "Early BUILD & TEST failure logging contract is missing: $required"
    }
}
$errorLogShortcutSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ErrorLogShortcutFeature.cs' -Raw
foreach ($required in @('HasLogFiles(logsFolder)', 'Directory.EnumerateFiles(logsFolder, "*.log"')) {
    if (-not $errorLogShortcutSource.Contains($required)) {
        throw "Error dialogs can still offer OPEN LOGS when the project has no log files: $required"
    }
}
foreach ($required in @('CleanPrepareDialog.Choose(form)', 'options: options')) {
    if (-not $buildSource.Contains($required)) { throw "Clean PREPARE UI contract missing: $required" }
}
$dialogSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/CleanPrepareDialog.cs' -Raw
foreach ($required in @(
    'BACK UP & REPREPARE',
    'REPREPARE WITHOUT BACKUP',
    'Materials',
    'Physics',
    'Effects',
    'Hero select scene',
    'Сцена выбора героя',
    'PrepareHeroSelectScene: _heroSelectScene.Checked')) {
    if (-not $dialogSource.Contains($required)) { throw "Clean PREPARE selection dialog contract missing: $required" }
}
if ($dialogSource.Contains('isChecked: true')) {
    throw 'Clean PREPARE sections must all start unchecked.'
}
foreach ($forbidden in @(
    '_withoutBackupButton.Visible',
    '_backupButton.Text =')) {
    if ($dialogSource.Contains($forbidden)) {
        throw "Clean PREPARE bottom actions must stay constant: $forbidden"
    }
}

# Ordinary PREPARE must not rewrite or migrate an existing author VMAT.
$prepareSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/PrepareAuthoringService.cs' -Raw
foreach ($required in @(
    'LoadPreservingMaterials(manifest)',
    'SavePreservingMaterials(manifest, knownOwnership)',
    'mutateExistingMaterials: regenerateCustomMaterials',
    'var finalTextureRepairs = regenerateCustomMaterials',
    'var cleanGameOutput = options.ResetSections != PrepareResetSections.None',
    'if (options.PrepareHeroSelectScene)',
    'new HeroSelectScenePreparationService(_paths).Prepare(',
    'FbxMaterialReferenceReader.ReadMany(rootFbxFiles)',
    '.Concat(fbxMaterialReferences)',
    'ResolveExactFbxCustomMaterialRemaps(',
    'material.SourceName + ".vmat"',
    'Ordinary PREPARE preserved addon runtime output for incremental BUILD & TEST')) {
    if (-not $prepareSource.Contains($required)) {
        throw "Ordinary PREPARE byte-preservation contract is missing: $required"
    }
}
$heroSelectSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroSelectScenePreparationService.cs' -Raw
foreach ($required in @(
    'citadel/maps/ui/hero_prefabs',
    '.EndsWith(".vmap_c"',
    'FileExtract.Extract(resource, fileLoader, null)',
    'Prepared hero-select authoring resource',
    'PublishContentFilePreserving(',
    'AuthoringResourceCreatedCount',
    'Prepared hero-select runtime resource',
    'package.ReadEntry(entry, out byte[] rawData)',
    'RuntimeCreatedCount',
    'RemoveLegacyLooseCompiledMaps(addonContentRoot, addonGameRoot)',
    'IsLooseCompiledMapResource(resourcePath)',
    'if (File.Exists(outputPath))',
    'File.Move(temporaryPath, outputPath, overwrite: false)')) {
    if (-not $heroSelectSource.Contains($required)) {
        throw "Hero-select scene preparation contract is missing: $required"
    }
}
$heroSelectType = $assembly.GetType('Deadlimit.Core.HeroSelectScenePreparationService', $true)
$removeLegacyLooseMaps = $heroSelectType.GetMethods($nonPublicStatic) |
    Where-Object {
        $_.Name -eq 'RemoveLegacyLooseCompiledMaps' -and
        $_.GetParameters().Count -eq 2 -and
        $_.GetParameters()[0].ParameterType -eq [string]
    } |
    Select-Object -First 1
if ($null -eq $removeLegacyLooseMaps) {
    throw 'Legacy loose hero-select VMAP cleanup helper was not found.'
}
$heroSelectCleanupRoot = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-hero-select-cleanup-$([Guid]::NewGuid().ToString('N'))"
try {
    $contentRoot = Join-Path $heroSelectCleanupRoot 'content'
    $gameRoot = Join-Path $heroSelectCleanupRoot 'game'
    $contentScenes = Join-Path $contentRoot 'maps/ui/hero_prefabs'
    $gameScenes = Join-Path $gameRoot 'maps/ui/hero_prefabs'
    [IO.Directory]::CreateDirectory($contentScenes) | Out-Null
    [IO.Directory]::CreateDirectory($gameScenes) | Out-Null

    [IO.File]::WriteAllText((Join-Path $contentScenes 'prof_smoke.vmap'), 'editable map')
    [IO.File]::WriteAllBytes((Join-Path $gameScenes 'prof_smoke.vmap_c'), [byte[]]@(1,2,3,4))
    [IO.File]::WriteAllBytes((Join-Path $gameScenes 'unrelated.vmap_c'), [byte[]]@(5,6,7,8))

    $removedLooseMaps = [int]$removeLegacyLooseMaps.Invoke(
        $null,
        [object[]]@([string]$contentRoot, [string]$gameRoot))
    if ($removedLooseMaps -ne 1 -or
        (Test-Path -LiteralPath (Join-Path $gameScenes 'prof_smoke.vmap_c')) -or
        -not (Test-Path -LiteralPath (Join-Path $gameScenes 'unrelated.vmap_c'))) {
        throw 'Legacy hero-select loose vmap_c cleanup did not remove only the compiled counterpart of an editable VMAP.'
    }
}
finally {
    Remove-Item -LiteralPath $heroSelectCleanupRoot -Recurse -Force -ErrorAction SilentlyContinue
}

$buildFeatureSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
if (-not $buildFeatureSource.Contains('HeroSelectScenePreparationService.RemoveLegacyLooseCompiledMaps(manifest, paths)')) {
    throw 'CSDK launch does not self-heal legacy loose hero-select vmap_c files.'
}

$buildPipelineSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
foreach ($required in @(
    'FindDirectDependents(',
    '.Chunk(CompileBatchSize)',
    'ProbeParticleBatchFailuresAsync(',
    'BuildHeroSelectScenePackagesAsync(',
    'Hero-select authoring models rebuilt:',
    'CreateHeroSelectPackage(',
    'Hero-select nested payload restricted to scene resources:',
    'OverlayPackageEntries(',
    '"-world"',
    '"-phys"',
    '"-vis"',
    'Authored hero-select packages built:',
    'TIMING {stage}: {elapsed.TotalSeconds:F3}s')) {
    if (-not $buildPipelineSource.Contains($required)) {
        throw "Incremental build performance contract is missing: $required"
    }
}
foreach ($forbidden in @(
    'foreach (var relativePath in includedCompiledResources.OrderBy(',
    'IReadOnlySet<string> includedCompiledResources')) {
    if ($buildPipelineSource.Contains($forbidden, [StringComparison]::Ordinal)) {
        throw "Hero-select packaging still copies outer mod resources into the nested scene VPK: $forbidden"
    }
}
$customMaterialSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/CustomMaterialAuthoringService.cs' -Raw
foreach ($required in @(
    'Existing custom VMAT preserved byte-for-byte during ordinary PREPARE',
    'removeStaleTextures: regenerateExistingMaterials',
    'new("TextureRimLightMask", NeutralWhite')) {
    if (-not $customMaterialSource.Contains($required)) {
        throw "Custom material preservation/rim-mask contract is missing: $required"
    }
}

# ModelDoc physics nodes survive normal retail refreshes and can be replaced as a unit.
$retailPhysicsSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/RetailPhysicsAuthoringService.cs' -Raw
foreach ($required in @(
    'm_pFeModel',
    'CreateSoftbody(cloth.Chains)',
    'PhysicsJointRevolute',
    'enable_limit',
    'RecoverCubicControl',
    'RecoverGoalDamping',
    'allow_rotation',
    'stretch_spring',
    'child_sibling_spring',
    'bend_spring',
    'torsion_spring',
    'explicit_length',
    'animated_length',
    'suspender',
    'antishrink',
    'vertex_map',
    'stiff_hinge',
    'stiff_hinge_angle',
    'motion_bias',
    'collision_layer_0',
    'collision_layer_1',
    'collision_layer_2',
    'collision_layer_3',
    'RepairMissingParentAnchors',
    'CreateFixedClothAnchor',
    'extrude_sides',
    'extrude_twist',
    'extrude_forward_axis',
    'stray_radius',
    'stray_radius_stretchiness',
    'end_effector',
    'm_TreeCollisionMasks',
    'lock_translation',
    'world_collision',
    'twist_relax',
    'extra_iterations')) {
    if (-not $retailPhysicsSource.Contains($required)) {
        throw "Retail physics reconstruction contract missing: $required"
    }
}
$retailPhysicsType = $assembly.GetType('Deadlimit.Core.RetailPhysicsAuthoringService', $true)
$findMissingParents = $retailPhysicsType.GetMethod('FindMissingParentJointNames', $nonPublicStatic)
if ($null -eq $findMissingParents) {
    throw 'Retail cloth missing-parent validation helper was not found.'
}
[string[]]$clothNames = @('segment_a_0_R', 'segment_a_end_R', 'segment_b_0_R', 'segment_b_end_R', 'wing_2_R', 'wing_end_R')
[string[]]$clothParents = @($null, 'segment_a_0_R', 'wing_1_R', 'segment_b_0_R', 'wing_1_R', 'wing_2_R')
$missingParents = @($findMissingParents.Invoke($null, [object[]]@($clothNames, $clothParents)))
if ($missingParents.Count -ne 1 -or $missingParents[0] -ne 'wing_1_R') {
    throw "Retail cloth parent validation did not isolate wing_1_R: $($missingParents -join ', ')"
}
$repairInvalidClothParents = $retailPhysicsType.GetMethod('RepairInvalidClothParentAnchors')
if ($null -eq $repairInvalidClothParents) {
    throw 'Retail cloth parent repair entry point was not found.'
}
$clothRepairTemp = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-cloth-parent-$([Guid]::NewGuid().ToString('N')).vmdl"
try {
    $invalidCloth = @'
rootNode =
{
    children =
    [
        {
            _class = "Softbody"
            children =
            [
                {
                    _class = "ClothChain"
                    root_bone = "segment_a_0_R"
                    chain =
                    {
                        joints =
                        [
                            { joint_name = "segment_a_0_R" simulate = false },
                            { joint_name = "wing_2_R" joint_parent = "wing_1_R" simulate = false },
                            { joint_name = "wing_end_R" joint_parent = "wing_2_R" },
                        ]
                    }
                },
            ]
        },
    ]
}
'@
    [IO.File]::WriteAllText($clothRepairTemp, $invalidCloth)
    $repairCount = [int]$repairInvalidClothParents.Invoke($null, [object[]]@([string]$clothRepairTemp))
    $repairedCloth = [IO.File]::ReadAllText($clothRepairTemp)
    if ($repairCount -ne 1 `
        -or $repairedCloth -notmatch 'root_bone\s*=\s*"wing_1_R"' `
        -or ([regex]::Matches($repairedCloth, 'joint_name\s*=\s*"wing_1_R"')).Count -ne 1) {
        throw "Invalid ClothChain parent was not repaired with one fixed wing_1_R anchor.`n$repairedCloth"
    }
    $stableCloth = $repairedCloth
    if ([int]$repairInvalidClothParents.Invoke($null, [object[]]@([string]$clothRepairTemp)) -ne 0 `
        -or [IO.File]::ReadAllText($clothRepairTemp) -ne $stableCloth) {
        throw 'ClothChain parent repair was not idempotent.'
    }
}
finally {
    Remove-Item -LiteralPath $clothRepairTemp -Force -ErrorAction SilentlyContinue
}

# Wall Worm DCC import/export can sanitize retail $cloth_* bones to _cloth_*.
# Reconcile only when the artist skeleton proves the underscore bone exists and
# the original dollar-prefixed bone does not.
$reconcileClothNames = $retailPhysicsType.GetMethod(
    'ReconcileWallWormClothBoneNamesFromJointNames',
    $nonPublicStatic)
if ($null -eq $reconcileClothNames) {
    throw 'Wall Worm cloth bone-name reconciliation helper was not found.'
}
$clothNameTemp = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-cloth-name-$([Guid]::NewGuid().ToString('N')).vmdl"
try {
    $clothNameVmdl = @'
rootNode =
{
    children =
    [
        {
            _class = "Softbody"
            children =
            [
                {
                    _class = "ClothChain"
                    root_bone = "$cloth_m0p130"
                    chain =
                    {
                        joints =
                        [
                            { joint_name = "$cloth_m0p130" simulate = false },
                            { joint_name = "$cloth_m0p62" joint_parent = "$cloth_m0p130" },
                            { joint_name = "$cloth_keep" },
                        ]
                    }
                },
                {
                    _class = "ClothChain"
                    root_bone = "$cloth_missing"
                    chain =
                    {
                        joints =
                        [
                            { joint_name = "$cloth_missing" simulate = false },
                        ]
                    }
                },
            ]
        },
    ]
}
'@
    [IO.File]::WriteAllText($clothNameTemp, $clothNameVmdl)

    $artistJoints = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$artistJoints.Add('_cloth_m0p130')
    [void]$artistJoints.Add('_cloth_m0p62')
    [void]$artistJoints.Add('_cloth_keep')
    [void]$artistJoints.Add('$cloth_keep')

    $clothNameResult = $reconcileClothNames.Invoke(
        $null,
        [object[]]@([string]$clothNameTemp, $artistJoints))
    $clothNameText = [IO.File]::ReadAllText($clothNameTemp)
    if (($clothNameResult.BoneRemaps.Count -ne 2) -or
        ($clothNameResult.RewrittenReferenceCount -ne 4) -or
        ($clothNameResult.RemovedIncompatibleChainCount -ne 1) -or
        ($clothNameResult.RemovedIncompatibleChainRoots.Count -ne 1) -or
        ($clothNameResult.RemovedIncompatibleChainRoots[0] -ne '$cloth_missing') -or
        $clothNameText.Contains('$cloth_m0p130') -or
        $clothNameText.Contains('$cloth_m0p62') -or
        $clothNameText.Contains('$cloth_missing') -or
        (-not $clothNameText.Contains('_cloth_m0p130')) -or
        (-not $clothNameText.Contains('_cloth_m0p62'))) {
        throw "Wall Worm cloth compatibility was not reconciled safely.\n$clothNameText"
    }
    if (-not $clothNameText.Contains('$cloth_keep')) {
        throw 'A valid retail $cloth_* bone was rewritten even though the artist skeleton still contains it.'
    }

    $stableClothNameText = $clothNameText
    $stableClothNameResult = $reconcileClothNames.Invoke(
        $null,
        [object[]]@([string]$clothNameTemp, $artistJoints))
    if (($stableClothNameResult.RewrittenReferenceCount -ne 0) -or
        ($stableClothNameResult.RemovedIncompatibleChainCount -ne 0) -or
        ([IO.File]::ReadAllText($clothNameTemp) -ne $stableClothNameText)) {
        throw 'Wall Worm cloth compatibility reconciliation is not idempotent.'
    }
}
finally {
    Remove-Item -LiteralPath $clothNameTemp -Force -ErrorAction SilentlyContinue
}

foreach ($required in @(
    'ReconcileWallWormClothBoneNames(',
    'ReadArtistDmxJointNames(',
    'artistJointNames.Contains(retailName)',
    'var wallWormName = "_" + retailName[1..]',
    'unresolvedProceduralBones',
    'ExpandClothChainRemovalRange(',
    'RemovedIncompatibleChainCount',
    'RetailClothReadResult.Empty',
    'FindLossyClothFeatures')) {
    if (-not $retailPhysicsSource.Contains($required)) {
        throw "Retail physics safety contract missing: $required"
    }
}
$prepareSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/PrepareAuthoringService.cs' -Raw
if (-not $prepareSource.Contains('Retail physics warning:')) {
    throw 'Retail physics warnings are not surfaced by clean prepare.'
}
if ((-not $prepareSource.Contains('ReconcileWallWormClothBoneNames(')) -or
    (-not $prepareSource.Contains('cloth bone alias')) -or
    (-not $prepareSource.Contains('incompatible retail chains removed')) -or
    (-not $prepareSource.Contains('skipped incompatible retail ClothChain root'))) {
    throw 'PREPARE does not reconcile Wall Worm _cloth_* names and reject incompatible retail $cloth_* chains.'
}
if ($retailPhysicsSource.Contains('name = "Retail ragdoll joints"')) {
    throw 'Physics joints must be direct PhysicsJointList children; a Folder causes ResourceCompiler to drop them.'
}

$inheritanceType = $assembly.GetType('Deadlimit.Core.RetailVmdlInheritance', $true)
$capturePhysics = $inheritanceType.GetMethod('CaptureAuthoringPhysics')
$restorePhysics = $inheritanceType.GetMethod('RestoreAuthoringPhysics')
$captureAnimations = $inheritanceType.GetMethod('CaptureAuthoringAnimations')
$restoreAnimations = $inheritanceType.GetMethod('RestoreAuthoringAnimations')
$containsRootNode = $inheritanceType.GetMethod('ContainsRootNode')
$physicsTemp = Join-Path ([IO.Path]::GetTempPath()) "deadlimit-physics-$([Guid]::NewGuid().ToString('N')).vmdl"
try {
    $artistVmdl = @'
rootNode =
{
    children =
    [
        { _class = "JiggleBoneList" name = "artist ears" },
        { _class = "Softbody" name = "artist cloth" },
        { _class = "PhysicsJointList" name = "artist joints" },
        { _class = "PhysicsShapeList" name = "artist bodies" },
        { _class = "AnimationList" children = [ { _class = "AnimFile" name = "ui_hero_select" children = [ { _class = "AnimEvent" event_class = "AE_CL_CLOTH_STIFFEN" } ] } ] },
        { _class = "RenderMeshList" name = "artist mesh" },
    ]
}
'@
    [IO.File]::WriteAllText($physicsTemp, $artistVmdl)
    $snapshot = $capturePhysics.Invoke($null, [object[]]@([string]$physicsTemp))
    $animationSnapshot = $captureAnimations.Invoke($null, [object[]]@([string]$physicsTemp))
    if ($snapshot.Nodes.Count -ne 4) { throw "Expected four artist physics nodes; found $($snapshot.Nodes.Count)." }
    if ($animationSnapshot.Nodes.Count -ne 1) { throw "Expected one artist AnimationList; found $($animationSnapshot.Nodes.Count)." }

    $retailVmdl = @'
rootNode =
{
    children =
    [
        { _class = "PhysicsShapeList" name = "retail bodies" },
        { _class = "AnimationList" children = [ { _class = "AnimFile" name = "ui_hero_select" } ] },
        { _class = "RenderMeshList" name = "retail mesh" },
    ]
}
'@
    [IO.File]::WriteAllText($physicsTemp, $retailVmdl)
    $restorePhysics.Invoke($null, [object[]]@([string]$physicsTemp, $snapshot))
    $restoreAnimations.Invoke($null, [object[]]@([string]$physicsTemp, $animationSnapshot))
    $restoredPhysics = [IO.File]::ReadAllText($physicsTemp)
    foreach ($required in @('artist ears', 'artist cloth', 'artist joints', 'artist bodies', 'retail mesh', 'AE_CL_CLOTH_STIFFEN')) {
        if (-not $restoredPhysics.Contains($required)) { throw "Normal PREPARE authoring preservation lost: $required" }
    }
    if (-not [bool]$containsRootNode.Invoke($null, [object[]]@([string]$physicsTemp, [string]'PhysicsJointList'))) {
        throw 'Restored VMDL is missing PhysicsJointList.'
    }
}
finally {
    if (Test-Path -LiteralPath $physicsTemp) { Remove-Item -LiteralPath $physicsTemp -Force }
}

# ONLINE CSDK must recover structural root changes without requiring another click.
$onlineSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs' -Raw
foreach ($required in @(
    'RefreshBaselineAutomaticallyAsync(session)',
    'await new PrepareAuthoringService(paths).PrepareAsync(manifest, progress);',
    'ReferenceEquals(_session, activeSession)',
    'CaptureOnlineSourceSnapshot(manifest.ProjectFolder)',
    '_autoPrepareRequested = true;',
    '_autoPrepareRequested = false;'
)) {
    if (-not $onlineSource.Contains($required)) { throw "ONLINE automatic PREPARE contract missing: $required" }
}
$manualRecoveryText = 'Normal-click this button once to run full PREPARE FOR CSDK'
if ($onlineSource.Contains($manualRecoveryText)) {
    throw 'ONLINE structural changes must not require an extra PREPARE click.'
}

# The long DMX/FBX pair debounce belongs only to a mesh whose assigned faceSet
# material contains "vertexcolor" and still needs a current external sidecar.
$onlineSessionType = $assembly.GetType('Deadlimit.Core.OnlinePreparationSession', $true)
$shouldWaitForPair = $onlineSessionType.GetMethod('ShouldWaitForVertexColorPair', $nonPublicStatic)
$vertexStateType = $assembly.GetType('Deadlimit.Core.VertexColorSourceState', $true)
if ($null -eq $shouldWaitForPair -or $null -eq $vertexStateType) {
    throw 'ONLINE Vertex Color pair-wait policy was not found.'
}
function New-VertexState(
    [bool]$usesMaterial,
    [bool]$embedded,
    [bool]$sidecarExists,
    [bool]$sidecarCurrent
) {
    return [Activator]::CreateInstance(
        $vertexStateType,
        [object[]]@($usesMaterial, $embedded, 'source.fbx', $sidecarExists, $sidecarCurrent, 'test'))
}
if ([bool]$shouldWaitForPair.Invoke($null, @((New-VertexState $false $false $true $false)))) {
    throw 'A stale sidecar must not delay DMX that has no faceSet material containing vertexcolor.'
}
if (-not [bool]$shouldWaitForPair.Invoke($null, @((New-VertexState $true $false $false $false)))) {
    throw 'A Vertex Color material without a sidecar must receive the bounded pair wait.'
}
if ([bool]$shouldWaitForPair.Invoke($null, @((New-VertexState $true $true $false $false)))) {
    throw 'Embedded Vertex Color must not wait for an external sidecar.'
}
if ([bool]$shouldWaitForPair.Invoke($null, @((New-VertexState $true $false $true $true)))) {
    throw 'A current Vertex Color source pair must not receive the long debounce.'
}

# Wall Worm may omit jointList or mix real DmeJoint bones and ordinary DmeDag
# render nodes inside it. Only a mesh attached to a real joint is a skeleton helper.
$datamodelAssembly = [Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $assemblyPath) 'Datamodel.NET.dll'))
$documentType = $datamodelAssembly.GetType('Datamodel.Datamodel', $true)
$elementType = $datamodelAssembly.GetType('Datamodel.Element', $true)
$elementArrayType = $datamodelAssembly.GetType('Datamodel.ElementArray', $true)
$elementConstructor = $elementType.GetConstructors() |
    Where-Object { $_.GetParameters().Count -eq 4 } |
    Select-Object -First 1
# A source FBX may preserve a quad/ngon while the DMX exporter triangulates it.
# Vertex Color transfer must accept that count difference only when control-point
# correspondence proves every target corner's source color.
$sidecarType = $assembly.GetType('Deadlimit.Core.VertexColorSidecarService', $true)
$matchPolygonColors = $sidecarType.GetMethod('TryMatchPolygonColors', $nonPublicStatic)
$targetPolygonType = $sidecarType.GetNestedType('TargetPolygon', [Reflection.BindingFlags]::NonPublic)
$fbxPolygonType = $assembly.GetType('Deadlimit.Core.FbxVertexColorPolygon', $true)
$dmxColorType = $datamodelAssembly.GetType('Datamodel.Color', $true)
if ($null -eq $matchPolygonColors -or $null -eq $targetPolygonType -or $null -eq $fbxPolygonType) {
    throw 'Triangulated Vertex Color transfer test types were not found.'
}
$targetPolygonsForTriangulation = [Array]::CreateInstance($targetPolygonType, 2)
$targetPolygonsForTriangulation.SetValue(
    [Activator]::CreateInstance($targetPolygonType, [object[]]@([int[]]@(0,1,2), [int[]]@(0,1,2))),
    0)
$targetPolygonsForTriangulation.SetValue(
    [Activator]::CreateInstance($targetPolygonType, [object[]]@([int[]]@(3,4,5), [int[]]@(0,2,3))),
    1)
$red = [Activator]::CreateInstance($dmxColorType, [object[]]@([byte]255,[byte]0,[byte]0,[byte]255))
$green = [Activator]::CreateInstance($dmxColorType, [object[]]@([byte]0,[byte]255,[byte]0,[byte]255))
$blue = [Activator]::CreateInstance($dmxColorType, [object[]]@([byte]0,[byte]0,[byte]255,[byte]255))
$white = [Activator]::CreateInstance($dmxColorType, [object[]]@([byte]255,[byte]255,[byte]255,[byte]255))
$sourceColors = [Array]::CreateInstance($dmxColorType, 4)
$sourceColors.SetValue($red, 0)
$sourceColors.SetValue($green, 1)
$sourceColors.SetValue($blue, 2)
$sourceColors.SetValue($white, 3)
$sourcePolygonsForTriangulation = [Array]::CreateInstance($fbxPolygonType, 1)
$sourcePolygonsForTriangulation.SetValue(
    [Activator]::CreateInstance($fbxPolygonType, [object[]]@([int[]]@(0,1,2,3), $sourceColors, $null)),
    0)
[System.Numerics.Vector3[]]$quadPositions = @(
    [System.Numerics.Vector3]::new(0,0,0),
    [System.Numerics.Vector3]::new(1,0,0),
    [System.Numerics.Vector3]::new(1,1,0),
    [System.Numerics.Vector3]::new(0,1,0)
)
$triangulationArgs = [object[]]@(
    'triangulated_quad',
    $targetPolygonsForTriangulation,
    $sourcePolygonsForTriangulation,
    $null,
    $quadPositions,
    $quadPositions,
    $null,
    $null
)
if (-not [bool]$matchPolygonColors.Invoke($null, $triangulationArgs)) {
    throw "Triangulated DMX quad was rejected: $($triangulationArgs[7])"
}
$triangulatedColors = $triangulationArgs[6]
$expectedTriangulatedColors = @($red,$green,$blue,$red,$blue,$white)
if ($triangulatedColors.Count -ne $expectedTriangulatedColors.Count) {
    throw "Triangulated DMX color count mismatch: $($triangulatedColors.Count)"
}
for ($index = 0; $index -lt $expectedTriangulatedColors.Count; $index++) {
    if (-not $triangulatedColors[$index].Equals($expectedTriangulatedColors[$index])) {
        throw "Triangulated DMX color mismatch at corner $index."
    }
}

# Non-uniform object-space transforms must not block a safe Vertex Color transfer
# when the FBX quad and triangulated DMX share an unambiguous UV/color surface.
$streamColumnType = $sidecarType.GetNestedType('StreamColumn', [Reflection.BindingFlags]::NonPublic)
if ($null -eq $streamColumnType) {
    throw 'Vertex Color StreamColumn test type was not found.'
}
[System.Numerics.Vector2[]]$quadUvs = @(
    [System.Numerics.Vector2]::new(0,0),
    [System.Numerics.Vector2]::new(1,0),
    [System.Numerics.Vector2]::new(1,1),
    [System.Numerics.Vector2]::new(0,1)
)
$targetUvValues = [object[]]@($quadUvs[0], $quadUvs[1], $quadUvs[2], $quadUvs[3])
$targetTexcoords = [Activator]::CreateInstance(
    $streamColumnType,
    [object[]]@(
        [type][System.Numerics.Vector2[]],
        $targetUvValues,
        [int[]]@(0,1,2,0,2,3),
        $true))
$sourcePolygonsWithUvs = [Array]::CreateInstance($fbxPolygonType, 1)
$sourcePolygonsWithUvs.SetValue(
    [Activator]::CreateInstance(
        $fbxPolygonType,
        [object[]]@([int[]]@(0,1,2,3), $sourceColors, $quadUvs)),
    0)
[System.Numerics.Vector3[]]$nonUniformDmxPositions = @(
    [System.Numerics.Vector3]::new(0,0,0),
    [System.Numerics.Vector3]::new(2,0,0),
    [System.Numerics.Vector3]::new(2,3,0),
    [System.Numerics.Vector3]::new(0,3,0)
)
$uvFallbackArgs = [object[]]@(
    'triangulated_nonuniform_quad',
    $targetPolygonsForTriangulation,
    $sourcePolygonsWithUvs,
    $targetTexcoords,
    $nonUniformDmxPositions,
    $quadPositions,
    $null,
    $null
)
if (-not [bool]$matchPolygonColors.Invoke($null, $uvFallbackArgs)) {
    throw "UV Vertex Color fallback rejected a transformed triangulated surface: $($uvFallbackArgs[7])"
}
$uvFallbackColors = $uvFallbackArgs[6]
for ($index = 0; $index -lt $expectedTriangulatedColors.Count; $index++) {
    if (-not $uvFallbackColors[$index].Equals($expectedTriangulatedColors[$index])) {
        throw "UV Vertex Color fallback mismatch at corner $index."
    }
}

# Overlapping UVs are safe only when they resolve to one color. A conflicting
# UV/color pair must still fail instead of silently assigning the wrong paint.
[System.Numerics.Vector2[]]$ambiguousUvs = @(
    [System.Numerics.Vector2]::new(0,0),
    [System.Numerics.Vector2]::new(0,0),
    [System.Numerics.Vector2]::new(1,1),
    [System.Numerics.Vector2]::new(0,1)
)
$ambiguousSourcePolygons = [Array]::CreateInstance($fbxPolygonType, 1)
$ambiguousSourcePolygons.SetValue(
    [Activator]::CreateInstance(
        $fbxPolygonType,
        [object[]]@([int[]]@(0,1,2,3), $sourceColors, $ambiguousUvs)),
    0)
$ambiguousUvArgs = [object[]]@(
    'ambiguous_uv_quad',
    $targetPolygonsForTriangulation,
    $ambiguousSourcePolygons,
    $targetTexcoords,
    $nonUniformDmxPositions,
    $quadPositions,
    $null,
    $null
)
if ([bool]$matchPolygonColors.Invoke($null, $ambiguousUvArgs)) {
    throw 'UV Vertex Color fallback accepted one UV mapped to conflicting source colors.'
}

# Real Wall Worm pairs can have no usable UVs, split DMX control points, triangulated
# FBX quads, and an exporter axis-frame mismatch at the same time. Prove the whole
# point cloud under signed-axis permutation + independent axis scale before using
# source control-point colors.
$axisTargetPolygons = [Array]::CreateInstance($targetPolygonType, 2)
$axisTargetPolygons.SetValue(
    [Activator]::CreateInstance($targetPolygonType, [object[]]@([int[]]@(0,1,2), [int[]]@(0,1,2))),
    0)
$axisTargetPolygons.SetValue(
    [Activator]::CreateInstance($targetPolygonType, [object[]]@([int[]]@(3,4,5), [int[]]@(3,4,5))),
    1)
[System.Numerics.Vector3[]]$axisTargetPositions = @(
    [System.Numerics.Vector3]::new(0.0,0.0,0.0),
    [System.Numerics.Vector3]::new(1.0,0.2,0.1),
    [System.Numerics.Vector3]::new(1.1,2.0,0.4),
    [System.Numerics.Vector3]::new(0.0,0.0,0.0),
    [System.Numerics.Vector3]::new(1.1,2.0,0.4),
    [System.Numerics.Vector3]::new(-0.1,1.8,-0.2)
)
[System.Numerics.Vector3[]]$axisSourcePositions = @(
    [System.Numerics.Vector3]::new(5.0,10.0,-4.0),
    [System.Numerics.Vector3]::new(5.2,7.0,-3.9),
    [System.Numerics.Vector3]::new(5.8,6.7,-3.0),
    [System.Numerics.Vector3]::new(4.6,10.3,-3.1)
)
$axisFallbackArgs = [object[]]@(
    'axis_swizzled_split_quad',
    $axisTargetPolygons,
    $sourcePolygonsForTriangulation,
    $null,
    $axisTargetPositions,
    $axisSourcePositions,
    $null,
    $null
)
if (-not [bool]$matchPolygonColors.Invoke($null, $axisFallbackArgs)) {
    throw "Axis-aware Vertex Color fallback rejected a fully matching point cloud: $($axisFallbackArgs[7])"
}
$axisFallbackColors = $axisFallbackArgs[6]
for ($index = 0; $index -lt $expectedTriangulatedColors.Count; $index++) {
    if (-not $axisFallbackColors[$index].Equals($expectedTriangulatedColors[$index])) {
        throw "Axis-aware Vertex Color fallback mismatch at corner $index."
    }
}

$vertexColorSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/VertexColorSidecarService.cs' -Raw
foreach ($required in @(
    'TryMapAxisAwareSplitControlPoints(',
    'signed-axis permutation',
    'independent axis scale',
    'More than one signed-axis/non-uniform transform produces a different exact point correspondence.')) {
    if (-not $vertexColorSource.Contains($required)) {
        throw "Axis-aware Vertex Color point-map safety contract is missing: $required"
    }
}

$skeletonFilterType = $assembly.GetType('Deadlimit.Core.DmxSkeletonShapeFilter', $true)
$findJointShapes = $skeletonFilterType.GetMethod(
    'FindJointShapeMeshIds',
    [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
if ($null -eq $findJointShapes) { throw 'DmxSkeletonShapeFilter.FindJointShapeMeshIds was not found.' }
function New-TestDmxElement($owner, [string]$name, [string]$className) {
    return $elementConstructor.Invoke([object[]]@($owner, $name, $null, $className))
}

$noSkeletonDocument = [Activator]::CreateInstance($documentType, [object[]]@('model', 22))
$noSkeletonModel = New-TestDmxElement $noSkeletonDocument 'model_without_skin_bones' 'DmeModel'
$noSkeletonDocument.Root = $noSkeletonModel
$noSkeletonShapes = $findJointShapes.Invoke($null, @($noSkeletonDocument))
if ($noSkeletonShapes.Count -ne 0) {
    throw 'A DmeModel without jointList must produce an empty skeleton-helper set.'
}

$mixedDocument = [Activator]::CreateInstance($documentType, [object[]]@('model', 22))
$mixedModel = New-TestDmxElement $mixedDocument 'mixed_model' 'DmeModel'
$joint = New-TestDmxElement $mixedDocument 'bone' 'DmeJoint'
$jointShape = New-TestDmxElement $mixedDocument 'bone_mesh' 'DmeMesh'
$renderDag = New-TestDmxElement $mixedDocument 'render' 'DmeDag'
$renderMesh = New-TestDmxElement $mixedDocument 'render_mesh' 'DmeMesh'
$joint['shape'] = $jointShape
$renderDag['shape'] = $renderMesh
$mixedJointList = [Activator]::CreateInstance($elementArrayType)
$mixedJointList.Add($joint)
$mixedJointList.Add($renderDag)
$mixedModel['jointList'] = $mixedJointList
$mixedDocument.Root = $mixedModel
$mixedShapes = $findJointShapes.Invoke($null, @($mixedDocument))
if (-not $mixedShapes.Contains($jointShape.ID.ToString())) {
    throw 'A DmeMesh attached to a real DmeJoint must remain a skeleton helper.'
}
if ($mixedShapes.Contains($renderMesh.ID.ToString())) {
    throw 'A DmeDag render mesh listed in jointList must remain eligible for Vertex Color transfer.'
}

# DMX and FBX exporters may split control points differently and may write different
# evaluated positions. Ordered polygon ownership is still a safe proof when repeated
# DMX control points map consistently to the same FBX control points across the surface.
$orderedTopologyType = $assembly.GetType('Deadlimit.Core.VertexColorOrderedTopologyFallbackService', $true)
$hasOrderedTopology = $orderedTopologyType.GetMethod('HasOrderedSplitTopologyCorrespondence', $nonPublicStatic)
if ($null -eq $hasOrderedTopology) {
    throw 'Ordered split-topology Vertex Color fallback contract was not found.'
}
$targetPolygons = [int[][]]@(
    [int[]]@(0, 1, 2),
    [int[]]@(2, 1, 3),
    [int[]]@(4, 3, 5),
    [int[]]@(5, 3, 6)
)
$matchingSourcePolygons = [int[][]]@(
    [int[]]@(10, 11, 12),
    [int[]]@(12, 11, 13),
    [int[]]@(14, 13, 15),
    [int[]]@(15, 13, 16)
)
$reorderedSourcePolygons = [int[][]]@(
    [int[]]@(10, 11, 12),
    [int[]]@(14, 13, 15),
    [int[]]@(12, 11, 13),
    [int[]]@(15, 13, 16)
)
if (-not [bool]$hasOrderedTopology.Invoke($null, [object[]]@($targetPolygons, $matchingSourcePolygons))) {
    throw 'Ordered split-topology correspondence rejected a valid exporter-split surface.'
}
if ([bool]$hasOrderedTopology.Invoke($null, [object[]]@($targetPolygons, $reorderedSourcePolygons))) {
    throw 'Ordered split-topology correspondence accepted reordered polygon ownership.'
}
$preparedDmxRemapSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/PreparedDmxMaterialRemapService.cs' -Raw
foreach ($required in @(
    'Datamodel.Datamodel.Load',
    'DmeMaterial',
    'material.Name = remappedName',
    'material["mtlName"] = remappedMtlName')) {
    if (-not $preparedDmxRemapSource.Contains($required)) {
        throw "Prepared DMX direct-material remap contract is missing: $required"
    }
}
if ((-not $prepareSource.Contains('PreparedDmxMaterialRemapService.Apply(')) -or
    (-not $prepareSource.Contains('overlay.PreparedDmxPath'))) {
    throw 'PREPARE does not apply resolved material targets to staged DMX files.'
}

$guardSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/VertexColorSourceGuard.cs' -Raw
if (([regex]::Matches($guardSource, 'VertexColorTransferService\.TryApply\(')).Count -ne 2) {
    throw 'PREPARE validation and staged transfer must both use the safe Vertex Color transfer wrapper.'
}

# Primary FBX files carry their own vertex-color streams. Only DMX inputs may require
# a *_vertexcolor.fbx sidecar, and the sidecar reader must accept Autodesk ASCII or Binary FBX.
if (-not $prepareSource.Contains('VertexColorSourceGuard.ValidateForPrepare(')) {
    throw 'DMX Vertex Color preflight is missing.'
}
if (-not $prepareSource.Contains('rootDmxFiles')) {
    throw 'DMX Vertex Color preflight no longer receives rootDmxFiles.'
}
if ($prepareSource.Contains('VertexColorSourceGuard.ValidateForPrepare(rootFbxFiles')) {
    throw 'Primary FBX must never be gated by the DMX Vertex Color sidecar preflight.'
}
$asciiFbxSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/AsciiFbxVertexColorReader.cs' -Raw
$binaryFbxSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BinaryFbxVertexColorReader.cs' -Raw
foreach ($required in @(
    'BinaryFbxVertexColorReader.IsBinary(path)',
    'BinaryFbxVertexColorReader.Read(path)',
    'Autodesk ASCII or Binary FBX format')) {
    if (-not $asciiFbxSource.Contains($required)) {
        throw "Binary FBX dispatch contract is missing: $required"
    }
}
foreach ($required in @(
    'Kaydara FBX Binary',
    'ZLibStream',
    'LayerElementColor',
    'PolygonVertexIndex',
    'MappingInformationType',
    'IndexToDirect')) {
    if (-not $binaryFbxSource.Contains($required)) {
        throw "Binary FBX Vertex Color reader contract is missing: $required"
    }
}

& (Join-Path $PSScriptRoot 'hero-extraction-dependency-path-smoke.ps1')
& (Join-Path $PSScriptRoot 'hero-extraction-publish-smoke.ps1')
& (Join-Path $PSScriptRoot 'retail-external-texture-copy-smoke.ps1')

Write-Host 'Prepare behavior smoke passed.'
