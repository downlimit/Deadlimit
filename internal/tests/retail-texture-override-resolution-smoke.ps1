$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$service = $assembly.GetType('Deadlimit.Core.RetailTextureOverrideService', $true)
$manifestType = $assembly.GetType('Deadlimit.Core.ProjectManifest', $true)

function Test-InvocationRefused {
    param(
        [Parameter(Mandatory = $true)][scriptblock] $Action,
        [Parameter(Mandatory = $true)][string] $MessagePattern
    )

    try {
        & $Action
        return $false
    }
    catch {
        $current = $_.Exception
        while ($null -ne $current) {
            if ($current -is [InvalidOperationException] -and $current.Message -match $MessagePattern) {
                return $true
            }
            $current = $current.InnerException
        }
        throw
    }
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-retail-texture-override-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $temp 'project'
$sourceRoot = Join-Path $projectRoot '0source'
$materialRoot = Join-Path $sourceRoot 'models\heroes_wip\ivy\materials'
$addonRoot = Join-Path $temp 'addon'

try {
    New-Item -ItemType Directory -Path $materialRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $addonRoot -Force | Out-Null

    $vmat = @"
Layer0
{
    "TextureColor" "materials/models/heroes/ivy/body_color.png"
    "TextureNormal" "materials/models/heroes/ivy/body_normal.png"
}
"@
    Set-Content -LiteralPath (Join-Path $materialRoot 'ivy_body.vmat') -Value $vmat -Encoding utf8NoBOM

    $artistColor = Join-Path $projectRoot 'body_color.png'
    [IO.File]::WriteAllBytes($artistColor, [byte[]](10,20,30,40))

    $targets = $service.GetMethod('BuildTargetIndex').Invoke($null, @([string]$sourceRoot))
    if ($targets.Count -ne 2) { throw "Expected 2 retail texture targets, got $($targets.Count)." }

    $manifest = [Activator]::CreateInstance($manifestType)
    $manifest.ProjectFolder = $projectRoot

    $disabledOverrides = $service.GetMethod('ResolveProjectRootOverrides').Invoke($null, @($manifest, $targets))
    if ($disabledOverrides.Count -ne 0) {
        throw 'Retail texture overrides must stay disabled when the last extraction did not include textures.'
    }

    $manifest.LastSourceExtractionIncludedTextures = $true
    $overrides = $service.GetMethod('ResolveProjectRootOverrides').Invoke($null, @($manifest, $targets))
    if ($overrides.Count -ne 1) { throw "Expected 1 project-root retail override, got $($overrides.Count)." }

    $override = $overrides[0]
    if ($override.RetailTextureResourcePath -ne 'materials/models/heroes/ivy/body_color.png') {
        throw "Unexpected retail target: $($override.RetailTextureResourcePath)"
    }
    if ($override.StagedSourceResourcePath -ne 'materials/models/heroes/ivy/body_color.png') {
        throw "Unexpected staged source path: $($override.StagedSourceResourcePath)"
    }

    $staged = [int]$service.GetMethod('StageProjectRootOverrides').Invoke($null, @([string]$addonRoot, $overrides))
    if ($staged -ne 1) { throw "Expected 1 staged override, got $staged." }

    $expected = Join-Path $addonRoot 'materials\models\heroes\ivy\body_color.png'
    if (-not (Test-Path -LiteralPath $expected)) { throw "Staged retail override was not found: $expected" }
    $bytes = [IO.File]::ReadAllBytes($expected)
    if ($bytes.Length -ne 4 -or $bytes[0] -ne 10 -or $bytes[3] -ne 40) {
        throw 'Staged retail override bytes do not match the project-root source.'
    }

    Remove-Item -LiteralPath $artistColor -Force
    $wrongExtension = Join-Path $projectRoot 'body_color.tga'
    [IO.File]::WriteAllBytes($wrongExtension, [byte[]](1,2,3,4))
    $extensionRefused = Test-InvocationRefused -MessagePattern 'different source extension' -Action {
        $null = $service.GetMethod('ResolveProjectRootOverrides').Invoke($null, @($manifest, $targets))
    }
    if (-not $extensionRefused) { throw 'Retail texture source-extension mismatch was not refused.' }

    Remove-Item -LiteralPath $wrongExtension -Force
    [IO.File]::WriteAllBytes($artistColor, [byte[]](10,20,30,40))

    $ambiguousMaterialRoot = Join-Path $sourceRoot 'models\heroes_wip\other\materials'
    New-Item -ItemType Directory -Path $ambiguousMaterialRoot -Force | Out-Null
    $ambiguous = @"
Layer0
{
    "TextureColor" "materials/shared/alternate/body_color.png"
}
"@
    Set-Content -LiteralPath (Join-Path $ambiguousMaterialRoot 'alternate.vmat') -Value $ambiguous -Encoding utf8NoBOM

    $ambiguousTargets = $service.GetMethod('BuildTargetIndex').Invoke($null, @([string]$sourceRoot))
    $refused = Test-InvocationRefused -MessagePattern 'matches more than one retail texture resource' -Action {
        $null = $service.GetMethod('ResolveProjectRootOverrides').Invoke($null, @($manifest, $ambiguousTargets))
    }
    if (-not $refused) { throw 'Ambiguous retail texture filename was not refused.' }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Retail texture override resolution smoke passed.'
