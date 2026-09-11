$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-gltf-pipeline-' + [Guid]::NewGuid().ToString('N'))

function New-TestVmdl([string]$renderMeshPath) {
    return @"
<!-- kv3 encoding:text:version{00000000-0000-0000-0000-000000000000} format:modeldoc:version{00000000-0000-0000-0000-000000000000} -->
{
    rootNode =
    {
        children =
        [
            {
                _class = "RenderMeshList"
                children =
                [
                    {
                        _class = "RenderMeshFile"
                        name = "hero"
                        filename = "$renderMeshPath"
                    },
                ]
            },
        ]
    }
}
"@
}

try {
    $project = Join-Path $temp 'project'
    $primaryModel = Join-Path $project '0source\models\heroes\hero\hero.vmdl'
    $fallbackModel = Join-Path $project '0source\glTFpipeline\models\heroes\hero\hero.vmdl'
    $addonRoot = Join-Path $temp 'addon'
    $preparedVmdl = Join-Path $addonRoot 'models\heroes\hero\hero.vmdl'
    New-Item -ItemType Directory -Path $project,(Split-Path $fallbackModel),(Split-Path $preparedVmdl) -Force | Out-Null
    Set-Content -LiteralPath $fallbackModel -Value (New-TestVmdl 'models/heroes/hero/body.dmx') -Encoding utf8NoBOM
    Set-Content -LiteralPath $preparedVmdl -Value (New-TestVmdl 'models/heroes/hero/body.dmx') -Encoding utf8NoBOM

    foreach ($name in @('body.dmx','body.fbx','body_vertexcolor.fbx','hero.gltf','hero.glb','hero.png')) {
        Set-Content -LiteralPath (Join-Path $project $name) -Value $name -Encoding utf8NoBOM
    }

    $scan = [Deadlimit.Core.ProjectScanner]::Scan($project)
    if (@($scan.DmxFiles).Count -ne 1 -or @($scan.FbxFiles).Count -ne 1 -or @($scan.GltfFiles).Count -ne 2) {
        throw "Root model scan did not expose DMX/FBX/glTF/GLB correctly: $scan"
    }
    if (@($scan.FbxFiles) -contains 'body_vertexcolor.fbx') {
        throw 'The Vertex Color FBX sidecar leaked into the root model-source list.'
    }

    $manifest = [Deadlimit.Core.ProjectManifest]::new()
    $manifest.ProjectFolder = $project
    $manifest.SourceDumpFolderName = '0source'
    $manifest.RetailMainModel = 'models/heroes/hero/hero.vmdl_c'
    $resolved = [Deadlimit.Core.RetailVmdlInheritance]::FindRetailVmdl($manifest)
    if (-not [string]::Equals($resolved, [IO.Path]::GetFullPath($fallbackModel), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Retail VMDL did not fall back to 0source\glTFpipeline.'
    }

    New-Item -ItemType Directory -Path (Split-Path $primaryModel) -Force | Out-Null
    Set-Content -LiteralPath $primaryModel -Value (New-TestVmdl 'models/heroes/hero/body.dmx') -Encoding utf8NoBOM
    $resolved = [Deadlimit.Core.RetailVmdlInheritance]::FindRetailVmdl($manifest)
    if (-not [string]::Equals($resolved, [IO.Path]::GetFullPath($primaryModel), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Primary 0source did not take precedence over glTFpipeline.'
    }

    $sourceCopy = [Deadlimit.Core.RetailModelSourceCopyResult]::new(
        [string]$primaryModel,
        [string]$preparedVmdl,
        [string](Split-Path $preparedVmdl),
        1)
    $artistFbx = Join-Path $project 'body.fbx'
    $fbxResults = [Deadlimit.Core.RetailVmdlInheritance]::OverlayArtistFbx(
        $sourceCopy,
        $addonRoot,
        'hero',
        [string[]]@($artistFbx))
    $preparedFbx = Join-Path $addonRoot 'models\heroes\hero\body.fbx'
    if (@($fbxResults).Count -ne 1 -or -not (Test-Path -LiteralPath $preparedFbx)) {
        throw 'Root FBX was not staged at its ModelDoc render-mesh path.'
    }
    $patchedVmdl = Get-Content -LiteralPath $preparedVmdl -Raw
    if (-not $patchedVmdl.Contains('filename = "models/heroes/hero/body.fbx"', [StringComparison]::Ordinal)) {
        throw 'Prepared ModelDoc did not switch the matching RenderMeshFile from DMX to FBX.'
    }

    $gltfSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.Gltf.cs' -Raw
    foreach ($required in @(
        'ExportAnimations = true',
        'ExportExtras = false',
        'SplitGltfPrimitivesForDcc(outputPath)',
        'RetargetSplitMorphAnimations',
        'CompactPrimitiveAccessors')) {
        if (-not $gltfSource.Contains($required, [StringComparison]::Ordinal)) {
            throw "Full glTF extraction contract is missing: $required"
        }
    }
    if ($gltfSource.Contains('AnimationFilter.Add', [StringComparison]::Ordinal)) {
        throw 'glTF extraction still installs an animation filter and can silently lose clips.'
    }

    $buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
    foreach ($required in @(
        'var renderMeshDependencyChanged',
        'Path.GetExtension(path), ".dmx"',
        'Path.GetExtension(path), ".fbx"')) {
        if (-not $buildSource.Contains($required, [StringComparison]::Ordinal)) {
            throw "Incremental render-mesh rebuild contract is missing: $required"
        }
    }

    $adapter = $assembly.GetType('Deadlimit.Core.GltfAuthoringAdapter', $true)
    if ($null -eq $adapter.GetMethod('Overlay', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)) {
        throw 'The project-root glTF/GLB to companion-DMX PREPARE adapter is missing.'
    }

    Write-Output 'glTF extraction, source priority, root model scan and FBX authoring smoke passed.'
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
