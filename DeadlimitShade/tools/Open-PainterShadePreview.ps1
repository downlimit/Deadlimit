[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PreviewMesh,

    [ValidateSet('Custom', 'Ivy')]
    [string] $Character = 'Custom',

    [switch] $CharacterDiagnostic,

    [switch] $HeroOnly,

    [switch] $UseExistingProject,

    [switch] $ReloadExistingProject,

    [ValidateRange(1, 65535)]
    [int] $Port = 60041
)

$ErrorActionPreference = 'Stop'
$profilesRoot = Join-Path $PSScriptRoot '..\profiles'
$resolvedMesh = (Resolve-Path -LiteralPath $PreviewMesh).Path.Replace('\', '/')

if ($UseExistingProject -and $ReloadExistingProject) {
    throw 'UseExistingProject and ReloadExistingProject are mutually exclusive.'
}
if ($HeroOnly -and $ReloadExistingProject) {
    throw 'HeroOnly cannot be combined with ReloadExistingProject.'
}

function Invoke-PainterRequest {
    param(
        [Parameter(Mandatory)]
        [string] $BaseUri,

        [Parameter(Mandatory)]
        [ValidateSet('js', 'python')]
        [string] $Language,

        [Parameter(Mandatory)]
        [string] $Script,

        [int] $TimeoutSeconds = 60
    )

    $encodedScript = [Convert]::ToBase64String(
        [Text.Encoding]::UTF8.GetBytes($Script))
    $body = @{ $Language = $encodedScript } | ConvertTo-Json -Compress
    $response = Invoke-RestMethod `
        -Uri "$BaseUri/run.json" `
        -Method Post `
        -ContentType 'application/json' `
        -Body $body `
        -TimeoutSec $TimeoutSeconds

    if ($null -ne $response -and
        $response.PSObject.Properties.Name -contains 'error') {
        throw "Painter $Language request failed: $($response.error)"
    }

    return $response
}

function Find-PainterEndpoint {
    param([int] $EndpointPort)

    $candidates = @(
        "http://127.0.0.1:$EndpointPort",
        "http://[::1]:$EndpointPort",
        "http://localhost:$EndpointPort"
    )
    $failures = [System.Collections.Generic.List[string]]::new()

    foreach ($candidate in $candidates) {
        try {
            $version = Invoke-PainterRequest `
                -BaseUri $candidate `
                -Language js `
                -Script 'alg.version.painter' `
                -TimeoutSeconds 5
            return [pscustomobject]@{
                BaseUri = $candidate
                Version = [string] $version
            }
        }
        catch {
            $failures.Add("$candidate -> $($_.Exception.Message)")
        }
    }

    throw "Painter remote scripting is unavailable. Tried: $($failures -join '; ')"
}

function Resolve-CharacterId {
    param([string] $CharacterName)

    if ($CharacterName -eq 'Custom') {
        return 0
    }

    $profilePath = Join-Path $profilesRoot ($CharacterName.ToLowerInvariant() + '.json')
    $profile = Get-Content -Raw -LiteralPath $profilePath | ConvertFrom-Json
    return [int] $profile.id
}

$endpoint = Find-PainterEndpoint -EndpointPort $Port
$meshLiteral = $resolvedMesh | ConvertTo-Json -Compress
$useExistingLiteral = if ($UseExistingProject) { 'True' } else { 'False' }
$reloadExistingLiteral = if ($ReloadExistingProject) { 'True' } else { 'False' }
$heroOnlyPythonLiteral = if ($HeroOnly) { 'True' } else { 'False' }
$heroOnlyJsLiteral = if ($HeroOnly) { 'true' } else { 'false' }
$preReloadState = $null

if ($ReloadExistingProject) {
    $stateScript = @'
(function() {
  var instances = alg.shaders.instances();
  var hero = instances.filter(function(item) { return item.label === "Deadlimit Hero"; })[0];
  var outline = instances.filter(function(item) { return item.label === "Deadlimit Outline"; })[0];
  if (!hero || !outline) {
    throw new Error("Reload requires existing Deadlimit shader instances");
  }
  return JSON.stringify({
    mapping: alg.shaders.shaderInstancesToObject().texturesets,
    heroCharacter: alg.shaders.parameter(hero.id, "dl_character").value,
    heroDebugView: alg.shaders.parameter(hero.id, "dl_debug_view").value,
    outlineCharacter: alg.shaders.parameter(outline.id, "dl_outline_character").value
  });
})()
'@
    $preReloadJson = Invoke-PainterRequest `
        -BaseUri $endpoint.BaseUri `
        -Language js `
        -Script $stateScript
    $preReloadState = $preReloadJson | ConvertFrom-Json
}

$projectScript = @"
import substance_painter.project as project
import substance_painter.textureset as textureset

mesh_path = $meshLiteral
use_existing = $useExistingLiteral
reload_existing = $reloadExistingLiteral

if reload_existing:
    if not project.is_open():
        raise RuntimeError("ReloadExistingProject was requested, but no Painter project is open")
    reload_settings = project.MeshReloadingSettings(
        import_cameras=True,
        preserve_strokes=True)
    def deadlimit_reload_finished(status):
        pass
    project.reload_mesh(mesh_path, reload_settings, deadlimit_reload_finished)
elif use_existing:
    if not project.is_open():
        raise RuntimeError("UseExistingProject was requested, but no Painter project is open")
else:
    if project.is_open():
        raise RuntimeError("Refusing to replace an already open Painter project")
    settings = project.Settings(
        normal_map_format=project.NormalMapFormat.OpenGL,
        tangent_space_mode=project.TangentSpace.PerVertex)
    project.create(mesh_file_path=mesh_path, settings=settings)

if not reload_existing:
    actual_mesh = project.last_imported_mesh_path().replace("\\", "/")
    if actual_mesh.lower() != mesh_path.lower():
        raise RuntimeError("Unexpected imported mesh: " + actual_mesh)
"@

Invoke-PainterRequest `
    -BaseUri $endpoint.BaseUri `
    -Language python `
    -Script $projectScript `
    -TimeoutSeconds 120 | Out-Null

$deadline = [DateTime]::UtcNow.AddSeconds(120)
$projectReady = $false
while ([DateTime]::UtcNow -lt $deadline) {
        $projectStatusJson = Invoke-PainterRequest `
            -BaseUri $endpoint.BaseUri `
            -Language js `
            -Script 'JSON.stringify({busy: alg.project.isBusy(), mesh: alg.project.lastImportedMeshUrl()})' `
            -TimeoutSeconds 10
        $projectStatus = $projectStatusJson | ConvertFrom-Json
        $reportedMesh = ([string] $projectStatus.mesh).Replace('file:///', '').Replace('\', '/')
        if (-not $projectStatus.busy -and
            $reportedMesh.ToLowerInvariant() -eq $resolvedMesh.ToLowerInvariant()) {
            $projectReady = $true
            break
        }
        Start-Sleep -Milliseconds 500
}
if (-not $projectReady) {
    throw "Painter did not become ready on '$resolvedMesh' within 120 seconds."
}

$verificationScript = @"
import substance_painter.project as project
import substance_painter.textureset as textureset

expected_mesh = $meshLiteral
hero_only = $heroOnlyPythonLiteral
actual_mesh = project.last_imported_mesh_path().replace("\\", "/")
if actual_mesh.lower() != expected_mesh.lower():
    raise RuntimeError("Unexpected imported mesh: " + actual_mesh)

names = sorted(item.name() for item in textureset.all_texture_sets())
if not names:
    raise RuntimeError("Painter project has no Texture Sets")
if not hero_only and "__deadlimit_outline" not in names:
    raise RuntimeError("Missing reserved __deadlimit_outline texture set: " + repr(names))
if not hero_only and len(names) < 2:
    raise RuntimeError("Preview mesh has no source hero texture set: " + repr(names))
"@

Invoke-PainterRequest `
    -BaseUri $endpoint.BaseUri `
    -Language python `
    -Script $verificationScript | Out-Null

if ($ReloadExistingProject) {
    $postReloadJson = Invoke-PainterRequest `
        -BaseUri $endpoint.BaseUri `
        -Language js `
        -Script $stateScript
    $postReloadState = $postReloadJson | ConvertFrom-Json
    $preStateJson = $preReloadState | ConvertTo-Json -Depth 8 -Compress
    $postStateJson = $postReloadState | ConvertTo-Json -Depth 8 -Compress
    if ($preStateJson -ne $postStateJson) {
        throw "Painter shader/profile state changed during mesh reload. Before: $preStateJson After: $postStateJson"
    }
}

Invoke-PainterRequest `
    -BaseUri $endpoint.BaseUri `
    -Language js `
    -Script 'alg.resources.refreshShelves(); "REFRESH_REQUESTED"' | Out-Null
Start-Sleep -Seconds 2

$characterId = Resolve-CharacterId -CharacterName $Character
$debugView = if ($CharacterDiagnostic) { 11 } else { 0 }
$assignmentScript = @"
(function() {
  var heroOnly = $heroOnlyJsLiteral;
  var heroResources = alg.resources.findResources("*", "*Deadlock_Hero*");
  var outlineResources = heroOnly
    ? []
    : alg.resources.findResources("*", "*Deadlock_Outline*");
  if (heroResources.length !== 1) {
    throw new Error("Expected one hero shader resource; found " + heroResources.length);
  }
  if (!heroOnly && outlineResources.length !== 1) {
    throw new Error("Expected one outline shader resource; found " + outlineResources.length);
  }

  var current = alg.shaders.shaderInstancesToObject();
  var shaderNames = Object.keys(current.shaders);
  if (shaderNames.length === 0) {
    throw new Error("Painter project has no source shader instance");
  }

  var source = current.shaders[shaderNames[0]];
  var hero = JSON.parse(JSON.stringify(source));
  hero.shader = "Deadlock_Hero";
  hero.shaderInstance = "Deadlimit Hero";
  hero.parameters = {};

  var shaders = { "Deadlimit Hero": hero };
  if (!heroOnly) {
    var outline = JSON.parse(JSON.stringify(source));
    outline.shader = "Deadlock_Outline";
    outline.shaderInstance = "Deadlimit Outline";
    outline.parameters = {};
    shaders["Deadlimit Outline"] = outline;
  }

  var textureSets = {};
  Object.keys(current.texturesets).forEach(function(name) {
    textureSets[name] = {
      shader: !heroOnly && name === "__deadlimit_outline"
        ? "Deadlimit Outline"
        : "Deadlimit Hero"
    };
  });
  if (Object.keys(textureSets).length === 0) {
    throw new Error("Painter project has no Texture Sets");
  }
  if (!heroOnly &&
      (!textureSets.__deadlimit_outline || Object.keys(textureSets).length < 2)) {
    throw new Error("Expected source Texture Sets plus __deadlimit_outline");
  }

  alg.shaders.shaderInstancesFromObject({
    format: current.format,
    shaders: shaders,
    texturesets: textureSets
  });

  var instances = alg.shaders.instances();
  var heroInstance = instances.filter(function(item) {
    return item.label === "Deadlimit Hero";
  })[0];
  var outlineInstance = instances.filter(function(item) {
    return item.label === "Deadlimit Outline";
  })[0];
  if (!heroInstance || (!heroOnly && !outlineInstance)) {
    throw new Error("Expected Deadlimit shader instances were not created");
  }

  alg.shaders.setParameters(heroInstance.id, {
    dl_character: $characterId,
    dl_debug_view: $debugView
  });
  if (!heroOnly) {
    alg.shaders.setParameters(outlineInstance.id, {
      dl_outline_character: $characterId,
      dl_outline_use_character_color: true
    });
  }

  return JSON.stringify({
    endpoint: "$($endpoint.BaseUri)",
    painterVersion: "$($endpoint.Version)",
    mesh: $meshLiteral,
    textureSets: Object.keys(textureSets).sort(),
    heroShader: heroInstance.shader,
    outlineShader: outlineInstance ? outlineInstance.shader : null,
    heroOnly: heroOnly,
    character: "$Character",
    characterId: $characterId,
    characterDiagnostic: $(if ($CharacterDiagnostic) { 'true' } else { 'false' }),
    meshReloaded: $(if ($ReloadExistingProject) { 'true' } else { 'false' }),
    reloadPreservedShaderState: $(if ($ReloadExistingProject) { 'true' } else { 'null' }),
    mapping: alg.shaders.shaderInstancesToObject().texturesets
  });
})()
"@

$summaryJson = Invoke-PainterRequest `
    -BaseUri $endpoint.BaseUri `
    -Language js `
    -Script $assignmentScript
$summary = $summaryJson | ConvertFrom-Json
$summary | ConvertTo-Json -Depth 8
