param([string]$RetailRoot, [switch]$IvyOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$assemblyPath = (Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll').Path
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$type = $assembly.GetType('Deadlimit.Core.CompiledModelCameraRepairSmoke', $true)
try {
    if ([int]$type.GetMethod('Run').Invoke($null, @()) -ne 0) { throw 'Camera repair smoke failed.' }
    Write-Host 'Camera repair synthetic and imported BUILD & TEST checks passed.'
    $soundType = $assembly.GetType('Deadlimit.Core.CompiledModelSoundRepairSmoke', $true)
    if ([int]$soundType.GetMethod('Run').Invoke($null, @()) -ne 0) { throw 'Tagged-sound repair smoke failed.' }
    Write-Host 'Tagged-sound schema and author-data preservation checks passed.'
    if ($RetailRoot) {
        $liveMethod = if ($IvyOnly) { 'RunLiveIvy' } else { 'RunLive' }
        Write-Output $type.GetMethod($liveMethod).Invoke($null, @($RetailRoot))
    }
} catch {
    $failure = $_.Exception
    while ($failure.InnerException) { $failure = $failure.InnerException }
    throw $failure
}
