$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$workflowDir = Join-Path $repoRoot '.github/workflows'
$expected = @{
    'branch-hygiene.yml' = @('pull_request')
    'build.yml' = @('pull_request', 'workflow_dispatch')
    'dco.yml' = @('pull_request')
    'hero-texture-smoke.yml' = @('workflow_dispatch')
    'launch-game-fastpath-smoke.yml' = @('pull_request', 'workflow_dispatch')
    'release.yml' = @('workflow_dispatch')
    'ui-agent-contract-smoke.yml' = @('workflow_dispatch')
}
$files = @(Get-ChildItem -LiteralPath $workflowDir -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
if ($files.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) approved workflow files, found $($files.Count). Update the documented inventory and policy guard for deliberate changes."
}

foreach ($file in $files) {
    if (-not $expected.ContainsKey($file.Name)) {
        throw "Unreviewed workflow added: $($file.Name)"
    }
    $source = Get-Content -LiteralPath $file.FullName -Raw
    $eventsBlock = [regex]::Match($source, '(?m)^on:[ \t]*\r?\n(?<events>(?:^ {2,}.*(?:\r?\n|$)|^[ \t]*(?:\r?\n|$))*)')
    if (-not $eventsBlock.Success) {
        throw "$($file.Name): top-level on: block missing or malformed."
    }
    $events = @([regex]::Matches($eventsBlock.Groups['events'].Value, '(?m)^  ([a-z_]+):') | ForEach-Object { $_.Groups[1].Value } | Sort-Object)
    $wanted = @($expected[$file.Name] | Sort-Object)
    if (($events -join ',') -ne ($wanted -join ',')) {
        throw "$($file.Name): unapproved events: $($events -join ','). Expected: $($wanted -join ',')."
    }
    if ($source -match '(?mi)^\s*-\s*uses:\s*actions/(upload-artifact|download-artifact|cache)@' -or
        $source -match '(?mi)^\s*cache:\s*(nuget|npm|yarn|pnpm)' -or
        $source -match '(?mi)^\s*-\s*uses:\s*.*(?:cache/save|cache/restore)@') {
        throw "$($file.Name): artifacts and hosted caches require an approved, bounded exception."
    }
    $permissionBlock = [regex]::Match($source, '(?m)^permissions:[ \t]*\r?\n(?<body>(?:^ {2,}.*(?:\r?\n|$)|^[ \t]*(?:\r?\n|$))*)')
    if (-not $permissionBlock.Success -or $permissionBlock.Groups['body'].Value -notmatch '(?m)^  contents: (read|write)$') {
        throw "$($file.Name): explicit least-privilege contents permission missing."
    }
    $writeAllowed = $file.Name -in @('release.yml', 'branch-hygiene.yml')
    if ($permissionBlock.Groups['body'].Value -match '(?m)^  contents: write$' -and -not $writeAllowed) {
        throw "$($file.Name): contents: write is not approved for this workflow."
    }
    $group = [regex]::Match($source, '(?m)^  group:\s*(\S.*)$')
    $prefix = 'deadlimit-' + [IO.Path]::GetFileNameWithoutExtension($file.Name).Replace('release', 'release') + '-'
    if (-not $group.Success -or -not $group.Groups[1].Value.StartsWith($prefix, [StringComparison]::Ordinal) -or
        $source -notmatch '(?m)^  cancel-in-progress: (true|false|\$\{\{.+\}\})$') {
        throw "$($file.Name): unique concurrency group or cancellation rule missing."
    }
    $jobsStart = [regex]::Match($source, '(?m)^jobs:[ \t]*\r?\n')
    if (-not $jobsStart.Success) {
        throw "$($file.Name): jobs block missing."
    }
    $jobsText = $source.Substring($jobsStart.Index + $jobsStart.Length)
    $jobHeaders = [regex]::Matches($jobsText, '(?m)^  ([a-z][a-z0-9-]*):[ \t]*(?:#.*)?$')
    if ($jobHeaders.Count -eq 0) {
        throw "$($file.Name): no jobs discovered."
    }
    for ($i = 0; $i -lt $jobHeaders.Count; $i++) {
        $start = $jobHeaders[$i].Index
        $end = if ($i + 1 -lt $jobHeaders.Count) { $jobHeaders[$i + 1].Index } else { $jobsText.Length }
        $job = $jobsText.Substring($start, $end - $start)
        $name = $jobHeaders[$i].Groups[1].Value
        if ($job -notmatch '(?m)^    runs-on:\s+\S+' -or $job -notmatch '(?m)^    timeout-minutes:\s*(\d+)\s*$') {
            throw "$($file.Name)/$($name): runner or explicit job timeout missing."
        }
        $timeout = [int]$Matches[1]
        if ($timeout -lt 1 -or $timeout -gt 45) {
            throw "$($file.Name)/$($name): timeout of $timeout exceeds the approved 45-minute maximum."
        }
        $requiredSmoke = $file.Name -eq 'launch-game-fastpath-smoke.yml' -and $name -eq 'smoke'
        if ($requiredSmoke -and ($job -notmatch 'internal/tests/launch-game-fastpath-smoke.ps1' -or
                                 $job -notmatch 'internal/tests/window-shell-visibility-smoke.ps1')) {
            throw 'Required smoke must retain launch-game and window-shell coverage.'
        }
        if ($events -contains 'pull_request' -and
            ($job -match '(?m)^    runs-on:\s+windows-' -or $job -match '\bdotnet\s+(restore|build|publish)\b') -and
            $job -notmatch "if: github\.event_name == 'workflow_dispatch'" -and -not $requiredSmoke) {
            throw "$($file.Name)/$($name): expensive Windows/.NET job may run automatically on PR."
        }
    }
    if ($file.Name -eq 'release.yml' -and
        ($source -notmatch "inputs\.confirm == 'publish-source-milestone'" -or
         $source -notmatch "select\(\.name == .full-windows." -or
         $source -notmatch 'gh release create')) {
        throw 'Source publication must remain explicitly confirmed and gated on manual Windows validation.'
    }
    if ($file.Name -ne 'release.yml' -and $source -match '\bgh release (create|upload|delete)\b') {
        throw "$($file.Name): release mutation outside the approved source-publication workflow."
    }
    Write-Host "Actions cost policy: PASS $($file.Name) ($($events -join ', '); $($jobHeaders.Count) job(s))."
}
Write-Host 'Actions cost policy: all approved workflows passed.'
