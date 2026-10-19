$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::LoadFrom((Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'))
$tempRoot = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-imported-prepare-' + [Guid]::NewGuid().ToString('N'))))
function Write-Fixture($path, $text) {
    [IO.Directory]::CreateDirectory((Split-Path $path)) | Out-Null
    [IO.File]::WriteAllText($path, $text)
}
try {
    $folder = Join-Path $tempRoot 'project'
    $csdk = Join-Path $tempRoot 'csdk'
    [IO.Directory]::CreateDirectory((Join-Path $csdk 'content')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $csdk 'game')) | Out-Null
    $m = [Deadlimit.Core.ProjectManifest]::new()
    $m.Mode = [Deadlimit.Core.ProjectMode]::ImportedVpk
    $m.ProjectFolder = $folder
    $m.ProjectName = 'ImportedPrepareSmoke'
    $m.ImportedVpk = [Deadlimit.Core.ImportedVpkMetadata]::new()
    $m.ImportedVpk.PrimaryModelResources.Add('models/test.vmdl_c')
    $vmdl = '{ rootNode = { _class = "RootNode" children = [ { _class = "NmSkeletonList" children = [] }, { _class = "RenderMeshList" children = [] } ] } }'
    Write-Fixture (Join-Path $folder '1authoring/models/test.vmdl') $vmdl
    Write-Fixture (Join-Path $folder '1authoring/materials/custom/body.vmat') 'authored shader parameters'
    Write-Fixture (Join-Path $folder '1authoring/materials/custom/_variants/hash/body.png') 'texture variant'
    Write-Fixture (Join-Path $folder '.deadlimit/imported-compiled/models/test.vmdl_c') 'compiled model baseline'
    Write-Fixture (Join-Path $folder '.deadlimit/imported-compiled/maps/ui/hero.vpk') 'nested prefab baseline'
    Write-Fixture (Join-Path $folder '.deadlimit/imported-authoring-map.json') '{"SchemaVersion":2,"Entries":[{"CompiledPath":"models/test.vmdl_c","AuthoringPath":"models/test.vmdl"}],"AuthoringFiles":[]}'
    [Deadlimit.Core.ProjectStore]::Save($m)
    $settings = [Deadlimit.Core.ToolPathSettings]::new()
    $settings.CsdkRoot = $csdk
    $paths = [Deadlimit.Core.DeadlimitPaths]::new($settings)
    $service = [Deadlimit.Core.PrepareAuthoringService]::new($paths)
    $result = $service.PrepareAsync($m, $null, [Threading.CancellationToken]::None, $null).GetAwaiter().GetResult()
    $content = $result.AddonContentRoot
    if ((Get-Content (Join-Path $content 'materials/custom/body.vmat') -Raw) -ne 'authored shader parameters') { throw 'Material parameters changed' }
    if (-not (Test-Path (Join-Path $content 'materials/custom/_variants/hash/body.png'))) { throw 'Texture variant path lost' }
    if ((Get-Content $result.SourceVmdlPath -Raw).Contains('NmSkeletonList')) { throw 'CSDK unsupported model class retained' }
    if ((Get-Content (Join-Path $folder '1authoring/models/test.vmdl') -Raw) -ne $vmdl) { throw 'Authoring source modified' }
    $runtime = Join-Path $csdk ('game/citadel_addons/' + $result.AddonName)
    if ((Get-Content (Join-Path $runtime 'maps/ui/hero.vpk') -Raw) -ne 'nested prefab baseline') { throw 'Nested runtime VPK lost' }
    Write-Fixture (Join-Path $content 'materials/custom/body.vmat') 'CSDK artist edit'
    Write-Fixture (Join-Path $folder '1authoring/materials/custom/_variants/hash/body.png') 'updated source texture'
    $second = $service.PrepareAsync($m, $null, [Threading.CancellationToken]::None, $null).GetAwaiter().GetResult()
    if ((Get-Content (Join-Path $content 'materials/custom/body.vmat') -Raw) -ne 'CSDK artist edit') { throw 'CSDK artist edit overwritten' }
    if ((Get-Content (Join-Path $content 'materials/custom/_variants/hash/body.png') -Raw) -ne 'updated source texture') { throw 'Unedited managed target not synchronized' }
    if (Test-Path (Join-Path $folder '0source/models/test.vmdl')) { throw 'Test must succeed without a retail extraction' }
    Write-Output 'Imported VPK PREPARE smoke: PASS (paths, material parameters, variants, nested VPK, compatibility, source preservation, repeat sync and CSDK edits).'
} finally {
    $resolved = [IO.Path]::GetFullPath($tempRoot)
    $allowed = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\deadlimit-imported-prepare-'
    if (-not $resolved.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected test cleanup path' }
    if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
