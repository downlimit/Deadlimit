[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $Capture,
    [Parameter(Mandatory)][string] $RenderDocExe,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [int] $EventId = 791,
    [string] $LutResource = 'ResourceId::1826',
    [string] $EnvironmentResourceName = 'envmaparray.vtex',
    [ValidateRange(0, 10000)][int] $CubeIndex = 0
)
$ErrorActionPreference = 'Stop'
$capturePath = (Resolve-Path -LiteralPath $Capture).Path
$rendererPath = (Resolve-Path -LiteralPath $RenderDocExe).Path
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputPath) {
    throw 'Use a new output directory; existing evidence is never overwritten.'
}
$configuration = @{
    capture = $capturePath
    output = $outputPath
    event = $EventId
    lut = $LutResource
    environment = $EnvironmentResourceName
    cube = $CubeIndex
} | ConvertTo-Json -Compress
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $rendererPath
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.WindowStyle = [Diagnostics.ProcessWindowStyle]::Hidden
$startInfo.ArgumentList.Add('--python')
$startInfo.ArgumentList.Add((Join-Path $PSScriptRoot 'export_deadlock_environment_capture.py'))
$startInfo.Environment['DEADLIMIT_ENV_CAPTURE_CONFIG'] = $configuration
$exportProcess = [Diagnostics.Process]::Start($startInfo)
if (-not $exportProcess.WaitForExit(60000)) {
    # Only terminate the specific helper process created by this invocation.
    $exportProcess.Kill()
    throw "Replay export timed out. Partial evidence: $outputPath"
}
if ($exportProcess.ExitCode -ne 0) {
    throw "Replay export failed. Inspect $outputPath\error.txt"
}
Get-Content -Raw -LiteralPath (Join-Path $outputPath 'manifest.json')
