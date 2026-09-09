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
                        name = "mesh"
                        filename = "$renderMeshPath"
                    },
                ]
            },
        ]
    }
}
"@
}

# A root artist DMX may belong to an extracted ability/supporting VMDL rather than
# the main hero VMDL. Resolve it from 0source, stage its owner context, and make
# the same CSDK compatibility/material patch reach that owner VMDL.
$routingTemp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-source-routing-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $routingTemp 'project'
$sourceRoot = Join-Path $projectRoot '0source'
$sourceHeroRoot = Join-Path $sourceRoot 'models\heroes_wip\ivy'
$sourceAbilityRoot = Join-Path $sourceHeroRoot 'ability'
$addonRoot = Join-Path $routingTemp 'content\citadel_addons\ivybuilder'
$preparedHeroRoot = Join-Path $addonRoot 'models\heroes_wip\ivy'
$preparedAbilityRoot = Join-Path $preparedHeroRoot 'ability'
$abilityName = 'tengu_stone_form_model_fx_ivy.dmx'
$abilityResource = 'models/heroes_wip/ivy/ability/tengu_stone_form_model_fx_ivy.dmx'
$mainResource = 'models/heroes_wip/ivy/ivy_ivy.dmx'
try {
    New-Item -ItemType Directory -Path $projectRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $sourceHeroRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $sourceAbilityRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $preparedHeroRoot -Force | Out-Null

    $artistMain = Join-Path $projectRoot 'ivy_ivy.dmx'
    $artistAbility = Join-Path $projectRoot $abilityName
    Set-Content -LiteralPath $artistMain -Value 'artist main' -Encoding utf8NoBOM
    Set-Content -LiteralPath $artistAbility -Value 'artist ability' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $sourceHeroRoot 'ivy_ivy.dmx') -Value 'retail main' -Encoding utf8NoBOM
    Set-Content -LiteralPath (Join-Path $sourceAbilityRoot $abilityName) -Value 'retail ability' -Encoding utf8NoBOM

    $sourceMainVmdl = Join-Path $sourceHeroRoot 'ivy.vmdl'
    $sourceAbilityVmdl = Join-Path $sourceAbilityRoot 'tengu_stone_form_model_fx_ivy.vmdl'
    Set-Content -LiteralPath $sourceMainVmdl -Value (New-TestVmdl $mainResource) -Encoding utf8NoBOM
    Set-Content -LiteralPath $sourceAbilityVmdl -Value (New-TestVmdl $abilityResource) -Encoding utf8NoBOM

    $preparedMainVmdl = Join-Path $preparedHeroRoot 'ivy.vmdl'
    Set-Content -LiteralPath $preparedMainVmdl -Value (New-TestVmdl $mainResource) -Encoding utf8NoBOM

    $resolverType = $assembly.GetType('Deadlimit.Core.ExtractedSourceAssetResolver', $true)
    $resolveDmxTarget = $resolverType.GetMethod('ResolveDmxTarget', $flags)
    $stageOwners = $resolverType.GetMethod('StageOwningVmdlSourceTrees', $flags)
    if ($null -eq $resolveDmxTarget -or $null -eq $stageOwners) {
        throw 'Extracted-source DMX routing contract was not found.'
    }

    $dmxTarget = $resolveDmxTarget.Invoke($null, [object[]]@([string]$artistAbility, [string]$sourceRoot))
    if ($null -eq $dmxTarget -or $dmxTarget.ResourcePath -ne $abilityResource) {
        throw "Ability DMX did not resolve to its extracted retail resource path: $($dmxTarget.ResourcePath)"
    }
    if (@($dmxTarget.OwnerVmdlSourcePaths).Count -ne 1 -or
        -not [string]::Equals([string]$dmxTarget.OwnerVmdlSourcePaths[0], [IO.Path]::GetFullPath($sourceAbilityVmdl), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Ability DMX did not resolve to its extracted owning VMDL.'
    }

    [void]$stageOwners.Invoke($null, [object[]]@(
        [string]$sourceRoot,
        [string]$addonRoot,
        [string[]]@($sourceAbilityVmdl),
        [string]$preparedMainVmdl))
    $preparedAbilityVmdl = Join-Path $preparedAbilityRoot 'tengu_stone_form_model_fx_ivy.vmdl'
    if (-not (Test-Path -LiteralPath $preparedAbilityVmdl)) {
        throw 'Supporting ability VMDL context was not staged.'
    }
    $supportingBeforePatch = Get-Content -LiteralPath $preparedAbilityVmdl -Raw
    if (-not $supportingBeforePatch.StartsWith('// DEADLIMIT_SUPPORTING_SOURCE_OVERLAY', [StringComparison]::Ordinal)) {
        throw 'Supporting ability VMDL was not marked for compatibility/material patching.'
    }

    $remapType = $assembly.GetType('Deadlimit.Core.VmdlMaterialRemap', $true)
    $remap = [Activator]::CreateInstance($remapType, [object[]]@('materials/original.vmat', 'materials/ivybuilder/custom.vmat'))
    $remapArray = [Array]::CreateInstance($remapType, 1)
    $remapArray.SetValue($remap, 0)
    $patch = $type.GetMethod('PatchAuthoringVmdl', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
    [void]$patch.Invoke($null, [object[]]@([string]$preparedMainVmdl, $remapArray))

    $supportingAfterPatch = Get-Content -LiteralPath $preparedAbilityVmdl -Raw
    if ($supportingAfterPatch.StartsWith('// DEADLIMIT_SUPPORTING_SOURCE_OVERLAY', [StringComparison]::Ordinal)) {
        throw 'Supporting VMDL staging marker leaked into final authoring content.'
    }
    if (-not $supportingAfterPatch.Contains('materials/ivybuilder/custom.vmat', [StringComparison]::Ordinal)) {
        throw 'Supporting ability VMDL did not receive the same generated material remap as the main model.'
    }

    $onlineResolverType = $assembly.GetType('Deadlimit.Core.ArtistDmxTargetResolver', $true)
    $resolveOnline = $onlineResolverType.GetMethod('Resolve', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
    $onlineMappings = @($resolveOnline.Invoke($null, [object[]]@(
        [string]$preparedMainVmdl,
        [string]'ivy',
        [string[]]@($artistMain, $artistAbility),
        [string]$sourceRoot)))
    $abilityMapping = $onlineMappings | Where-Object { $_.ArtistDmxPath -eq [IO.Path]::GetFullPath($artistAbility) }
    if ($null -eq $abilityMapping -or $abilityMapping.TargetResourcePath -ne $abilityResource) {
        throw 'ONLINE PREPARATION did not preserve the source-backed ability DMX target.'
    }

    # Direct extracted UI images are valid provenance even when they are not referenced
    # by a VMAT and the general texture-extraction checkbox was not used.
    $uiSource = Join-Path $sourceRoot 'panorama\images\heroes\ivy_mm.png'
    New-Item -ItemType Directory -Path (Split-Path $uiSource) -Force | Out-Null
    [IO.File]::WriteAllBytes($uiSource, [byte[]](7,8,9))
    $artistUi = Join-Path $projectRoot 'ivy_mm.png'
    [IO.File]::WriteAllBytes($artistUi, [byte[]](9,8,7))

    $textureType = $assembly.GetType('Deadlimit.Core.RetailTextureOverrideService', $true)
    $buildTargets = $textureType.GetMethod('BuildTargetIndex', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
    $resolveOverrides = $textureType.GetMethod('ResolveProjectRootOverrides', [Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static)
    $targets = $buildTargets.Invoke($null, [object[]]@([string]$sourceRoot))
    $manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)
    $manifest = [Activator]::CreateInstance($manifestType)
    $manifest.ProjectFolder = $projectRoot
    $manifest.SourceDumpFolderName = '0source'
    $manifest.LastSourceExtractionIncludedTextures = $false
    $overrides = @($resolveOverrides.Invoke($null, [object[]]@($manifest, $targets)))
    $uiOverride = $overrides | Where-Object { $_.ArtistSourcePath -eq $artistUi }
    if ($null -eq $uiOverride -or $uiOverride.RetailTextureResourcePath -ne 'panorama/images/heroes/ivy_mm.png') {
        throw 'Direct extracted minimap/portrait UI image did not resolve to its retail resource path.'
    }

    # Duplicate extracted basenames stay fail-closed rather than becoming a filename heuristic.
    $duplicateAbility = Join-Path $sourceRoot 'models\other\tengu_stone_form_model_fx_ivy.dmx'
    New-Item -ItemType Directory -Path (Split-Path $duplicateAbility) -Force | Out-Null
    Set-Content -LiteralPath $duplicateAbility -Value 'duplicate' -Encoding utf8NoBOM
    $ambiguousRejected = $false
    try {
        [void]$resolveDmxTarget.Invoke($null, [object[]]@([string]$artistAbility, [string]$sourceRoot))
    }
    catch {
        $exception = $_.Exception
        while ($null -ne $exception.InnerException) {
            $exception = $exception.InnerException
        }
        if ($exception -is [InvalidOperationException] -and
            $exception.Message.Contains('matches more than one extracted retail source', [StringComparison]::Ordinal)) {
            $ambiguousRejected = $true
        }
        else {
            throw
        }
    }
    if (-not $ambiguousRejected) {
        throw 'Ambiguous extracted DMX basenames were not rejected.'
    }
}
finally {
    Remove-Item -LiteralPath $routingTemp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Retail external dependency and source-backed asset routing smoke passed.'
