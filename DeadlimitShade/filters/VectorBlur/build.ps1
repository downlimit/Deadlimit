[CmdletBinding()]
param(
    [string]$PythonExe = "python",
    [string]$SbsCookerExe = "sbscooker"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$BuildDir = Join-Path $Root "build"
$DistDir = Join-Path $Root "dist"
$SourceSbs = Join-Path $BuildDir "Deadlimit_Vector_Blur.sbs"

New-Item -ItemType Directory -Force -Path $BuildDir | Out-Null
New-Item -ItemType Directory -Force -Path $DistDir | Out-Null

Write-Host "[1/2] Generating SBS source..."
& $PythonExe (Join-Path $Root "build_vector_blur.py") --output $SourceSbs
if ($LASTEXITCODE -ne 0) {
    throw "PySBS generation failed with exit code $LASTEXITCODE."
}

Write-Host "[2/2] Cooking SBSAR..."
& $SbsCookerExe cook --inputs $SourceSbs --output-path $DistDir --output-name "Deadlimit_Vector_Blur"
if ($LASTEXITCODE -ne 0) {
    throw "sbscooker failed with exit code $LASTEXITCODE."
}

$Cooked = Get-ChildItem -Path $DistDir -Filter "Deadlimit_Vector_Blur*.sbsar" | Select-Object -First 1
if (-not $Cooked) {
    throw "sbscooker returned success but no Deadlimit_Vector_Blur SBSAR was produced."
}

Write-Host "Built: $($Cooked.FullName)"
