$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.HeroExtractionService', $true)
$flags = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static
$publish = $type.GetMethod('PublishRefreshedSourceInPlace', $flags)
if ($null -eq $publish) { throw 'PublishRefreshedSourceInPlace was not found.' }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('deadlimit-source-publish-' + [Guid]::NewGuid().ToString('N'))
$staging = Join-Path $temp 'staging'
$output = Join-Path $temp '0source'
$previous = Join-Path $temp '0source.previous'
try {
    New-Item -ItemType Directory -Path (Join-Path $staging 'models\hero') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $output 'models\hero') -Force | Out-Null

    Set-Content -LiteralPath (Join-Path $output 'models\hero\keep.txt') -Value 'old' -NoNewline
    Set-Content -LiteralPath (Join-Path $output 'models\hero\stale.txt') -Value 'stale' -NoNewline
    Set-Content -LiteralPath (Join-Path $staging 'models\hero\keep.txt') -Value 'new' -NoNewline
    Set-Content -LiteralPath (Join-Path $staging 'models\hero\added.txt') -Value 'added' -NoNewline

    $args = [object[]]@([string]$staging, [string]$output, [string]$previous)
    $publish.Invoke($null, $args) | Out-Null

    if ((Get-Content -LiteralPath (Join-Path $output 'models\hero\keep.txt') -Raw) -ne 'new') { throw 'Existing file was not refreshed.' }
    if (-not (Test-Path -LiteralPath (Join-Path $output 'models\hero\added.txt'))) { throw 'New file was not published.' }
    if (Test-Path -LiteralPath (Join-Path $output 'models\hero\stale.txt')) { throw 'Stale file survived in-place refresh.' }
    if ((Get-Content -LiteralPath (Join-Path $previous 'models\hero\keep.txt') -Raw) -ne 'old') { throw 'Previous source backup was not preserved.' }
    if (-not (Test-Path -LiteralPath (Join-Path $previous 'models\hero\stale.txt'))) { throw 'Previous source backup is incomplete.' }
    if (Test-Path -LiteralPath $staging) { throw 'Staging folder survived successful in-place refresh.' }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Hero extraction in-place publish smoke passed.'