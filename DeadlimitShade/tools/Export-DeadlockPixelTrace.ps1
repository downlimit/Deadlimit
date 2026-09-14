[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Capture,
    [Parameter(Mandatory)][string] $RenderDocExe,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int] $EventId,
    [Parameter(Mandatory)][string[]] $Pixels
)
$ErrorActionPreference = 'Stop'
$pixelPairs = @($Pixels | ForEach-Object {
    if ($_ -notmatch '^(\d+),(\d+)$') { throw 'Pixel format: X,Y (non-negative integers).' }
    ,@([int]$Matches[1], [int]$Matches[2])
})
$configuration = @{
    capture = (Resolve-Path -LiteralPath $Capture).Path
    output = [IO.Path]::GetFullPath($OutputDirectory)
    event = $EventId
    pixels = $pixelPairs
}
if (Test-Path -LiteralPath $configuration.output) {
    throw 'Use a new output directory; existing evidence is never overwritten.'
}
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = (Resolve-Path -LiteralPath $RenderDocExe).Path
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.ArgumentList.Add('--python')
$startInfo.ArgumentList.Add((Join-Path $PSScriptRoot 'export_deadlock_pixel_trace.py'))
$startInfo.Environment['DEADLIMIT_PIXEL_TRACE_CONFIG'] = $configuration | ConvertTo-Json -Depth 4 -Compress
$traceProcess = [Diagnostics.Process]::Start($startInfo)
if (-not $traceProcess.WaitForExit(45000)) {
    $traceProcess.Kill()
    throw "Owned replay helper timed out. Partial evidence: $($configuration.output)"
}
if ($traceProcess.ExitCode -ne 0) {
    throw "Pixel trace failed. Inspect $($configuration.output)\error.txt"
}
Get-Content -Raw -LiteralPath (Join-Path $configuration.output 'manifest.json')
