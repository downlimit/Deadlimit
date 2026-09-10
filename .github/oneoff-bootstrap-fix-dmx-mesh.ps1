$ErrorActionPreference = 'Stop'

$helperPath = '.github/oneoff-fix-experimental-dmx-mesh.ps1'
$helper = Get-Content -LiteralPath $helperPath -Raw
$old = @'
if (-not $script.Contains($oldPull)) {
    throw 'Experimental pull preflight block was not found.'
}
$script = $script.Replace($oldPull, $newPull)
'@
$new = @'
if (-not $script.Contains($oldPull)) {
    $oldPull = $oldPull.Replace("`r`n", "`n")
}
if (-not $script.Contains($oldPull)) {
    $oldPull = $oldPull.Replace("`n", "`r`n")
}
if (-not $script.Contains($oldPull)) {
    throw 'Experimental pull preflight block was not found after line-ending normalization.'
}
$script = $script.Replace($oldPull, $newPull)
'@
if (-not $helper.Contains($old)) {
    $old = $old.Replace("`r`n", "`n")
}
if (-not $helper.Contains($old)) {
    $old = $old.Replace("`n", "`r`n")
}
if (-not $helper.Contains($old)) {
    throw 'Patch helper guard block was not found.'
}
$helper = $helper.Replace($old, $new)
[IO.File]::WriteAllText((Resolve-Path $helperPath).Path, $helper, [Text.UTF8Encoding]::new($false))

& $helperPath
