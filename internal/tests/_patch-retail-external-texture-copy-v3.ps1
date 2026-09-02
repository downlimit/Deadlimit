$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot '_patch-retail-external-texture-copy-v2.ps1')

$smokePath = 'internal/tests/retail-external-texture-copy-smoke.ps1'
$text = Get-Content -LiteralPath $smokePath -Raw
$old = '    $count = [int]$copy.Invoke($null, @($heroFolder, $sourceRoot, $addonRoot))'
$new = @'
    $invokeArgs = [object[]]@([string]$heroFolder, [string]$sourceRoot, [string]$addonRoot)
    $count = [int]$copy.Invoke($null, $invokeArgs)
'@.TrimEnd()
if (-not $text.Contains($old)) { throw 'Reflection invocation line was not found in generated smoke.' }
$text = $text.Replace($old, $new)
Set-Content -LiteralPath $smokePath -Value $text -Encoding utf8NoBOM -NoNewline
