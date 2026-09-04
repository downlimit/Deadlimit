$ErrorActionPreference = 'Stop'

$trackedFiles = @(& git ls-files --cached --others --exclude-standard)
if ($LASTEXITCODE -ne 0) {
    throw "git ls-files failed with exit code $LASTEXITCODE."
}

$forbiddenExtensions = @(
    '.7z', '.dll', '.dmx', '.exe', '.fbx', '.max', '.nupkg', '.rar', '.vagrp_c',
    '.vanim_c', '.vmat_c', '.vmdl_c', '.vmesh_c', '.vphys_c', '.vpk', '.vtex_c', '.zip'
)
$forbiddenPathPattern = '(^|/)(0source|game/citadel|content/citadel)(/|$)|(^|/)pak01(_dir)?\.'
$maximumTrackedFileBytes = 2MB

$violations = [System.Collections.Generic.List[string]]::new()

foreach ($trackedFile in $trackedFiles) {
    $normalizedPath = $trackedFile.Replace('\', '/')
    $extension = [IO.Path]::GetExtension($normalizedPath).ToLowerInvariant()

    if ($forbiddenExtensions -contains $extension) {
        $violations.Add("forbidden extension: $normalizedPath")
    }

    if ($normalizedPath -match $forbiddenPathPattern) {
        $violations.Add("forbidden content path: $normalizedPath")
    }

    if (Test-Path -LiteralPath $trackedFile -PathType Leaf) {
        $length = (Get-Item -LiteralPath $trackedFile -Force).Length
        if ($length -gt $maximumTrackedFileBytes) {
            $violations.Add("tracked file exceeds 2 MiB: $normalizedPath ($length bytes)")
        }
    }
}

if ($violations.Count -gt 0) {
    $violations | Sort-Object -Unique | ForEach-Object { Write-Error $_ }
    throw 'Open-source content policy check failed.'
}

Write-Host "Open-source content policy passed for $($trackedFiles.Count) repository files."
