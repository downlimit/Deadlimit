$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$nonPublicStatic = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static

# Russian and English ONLINE text must both activate the pulse feature.
$pulseType = $assembly.GetType('Deadlimit.App.OnlineCsdkPulseFeature', $true)
$isOnline = $pulseType.GetMethod('IsOnlineText', $nonPublicStatic)
if ($null -eq $isOnline) { throw 'OnlineCsdkPulseFeature.IsOnlineText was not found.' }
foreach ($text in @('▶  ONLINE CSDK', '▶  CSDK ONLINE', '▶  CSDK ОНЛАЙН', 'CSDK ОНЛАЙН')) {
    if (-not [bool]$isOnline.Invoke($null, @($text))) {
        throw "ONLINE pulse detector rejected '$text'."
    }
}
if ([bool]$isOnline.Invoke($null, @('▶  ЗАПУСК CSDK'))) {
    throw 'Normal CSDK launch text must not activate the ONLINE pulse.'
}

# Normal PREPARE parser must recognize the same practical texture naming used by exports.
$bindingType = $assembly.GetType('Deadlimit.Core.ProjectTextureBindingService', $true)
$parse = $bindingType.GetMethod('ParseTextureCandidate', $nonPublicStatic)
if ($null -eq $parse) { throw 'ProjectTextureBindingService.ParseTextureCandidate was not found.' }
$cases = [ordered]@{
    'ivy_builder_body_color.png' = 'color'
    'ivy_builder_body_normal.png' = 'normal'
    'ivy_builder_body_roughness.png' = 'roughness'
    'ivy_builder_body_metalnessmask.png' = 'metalness'
    'ivy_builder_body.MetallicMap.png' = 'metalness'
    'ivy_builder_body-NRM.png' = 'normal'
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

# Clean PREPARE keeps backup enabled by default but exposes an explicit no-backup choice.
$prepareType = $assembly.GetType('Deadlimit.Core.PrepareAuthoringService', $true)
$prepareMethod = $prepareType.GetMethods() | Where-Object { $_.Name -eq 'PrepareAsync' } | Select-Object -First 1
$backupParameter = $prepareMethod.GetParameters() | Where-Object { $_.Name -eq 'backupCustomMaterials' }
if ($null -eq $backupParameter) { throw 'PrepareAsync backupCustomMaterials parameter is missing.' }
if (-not $backupParameter.HasDefaultValue -or $backupParameter.DefaultValue -ne $true) {
    throw 'Clean PREPARE backup must remain enabled by default.'
}
$buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
foreach ($required in @('YES, NO BACKUP', 'ДА, БЕЗ БЭКАПА', 'DeadlimitDialogChoice.YesWithoutBackup', 'backupCustomMaterials: backupCustomMaterials')) {
    if (-not $buildSource.Contains($required)) { throw "Clean PREPARE UI contract missing: $required" }
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

& (Join-Path $PSScriptRoot 'hero-extraction-dependency-path-smoke.ps1')
& (Join-Path $PSScriptRoot 'hero-extraction-publish-smoke.ps1')
& (Join-Path $PSScriptRoot 'retail-external-texture-copy-smoke.ps1')

Write-Host 'Prepare behavior smoke passed.'
