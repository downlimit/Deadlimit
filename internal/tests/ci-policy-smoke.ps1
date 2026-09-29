# Fail-closed static protection against expensive GitHub Actions regressions.
# This check never dispatches Actions or modifies repository/account settings.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$directory = Join-Path $root '.github/workflows'
$rules = [ordered]@{
    'branch-hygiene.yml'             = @{ Triggers = @('pull_request'); MaxMinutes = 5; Cancel = 'false'; Permission = 'contents: write' }
    'build.yml'                      = @{ Triggers = @('pull_request', 'workflow_dispatch'); MaxMinutes = 40; Cancel = 'true'; Permission = 'contents: read' }
    'dco.yml'                        = @{ Triggers = @('pull_request'); MaxMinutes = 5; Cancel = 'true'; Permission = 'contents: read' }
    'hero-texture-smoke.yml'         = @{ Triggers = @('workflow_dispatch'); MaxMinutes = 25; Cancel = 'true'; Permission = 'contents: read' }
    'launch-game-fastpath-smoke.yml' = @{ Triggers = @('pull_request', 'workflow_dispatch'); MaxMinutes = 20; Cancel = 'true'; Permission = 'contents: read' }
    'release.yml'                    = @{ Triggers = @('workflow_dispatch'); MaxMinutes = 10; Cancel = 'false'; Permission = 'contents: write' }
    'ui-agent-contract-smoke.yml'    = @{ Triggers = @('workflow_dispatch'); MaxMinutes = 10; Cancel = 'true'; Permission = 'contents: read' }
}
$found = @(Get-ChildItem -LiteralPath $directory -File | Where-Object { $_.Extension -in @('.yml', '.yaml') })
$unexpected = @($found | Where-Object { -not $rules.Contains($_.Name) })
$missing = @($rules.Keys | Where-Object { -not (Test-Path -LiteralPath (Join-Path $directory $_)) })
if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
    throw "Workflow inventory changed. Update the approved policy. Unexpected: $($unexpected.Name -join ', '). Missing: $($missing -join ', ')."
}
$names = @{}
$group = 'group: ' + '$' + '{{ github.workflow }}-' + '$' + '{{ github.ref }}'
foreach ($file in $found) {
    $name = $file.Name
    $rule = $rules[$name]
    $content = Get-Content -LiteralPath $file.FullName -Raw
    $header = [regex]::Match($content, '(?m)^name:\s*(.+)$')
    if (-not $header.Success -or $names.ContainsKey($header.Groups[1].Value.Trim())) {
        throw "$name must have a unique workflow name."
    }
    $names[$header.Groups[1].Value.Trim()] = $true
    $on = [regex]::Match($content, '(?ms)^on:\s*\r?\n(?<events>.*?)(?=^\S|\z)')
    if (-not $on.Success) { throw "$name has no multiline trigger block." }
    $events = @([regex]::Matches($on.Groups['events'].Value, '(?m)^  ([a-z_]+):(?:\s|$)') | ForEach-Object { $_.Groups[1].Value })
    if (($events -join ',') -ne ($rule.Triggers -join ',')) {
        throw "$name has unapproved triggers: $($events -join ', '). Allowed: $($rule.Triggers -join ', ')."
    }
    if ($name -eq 'branch-hygiene.yml' -and $on.Groups['events'].Value -notmatch 'types:\s*\[closed\]') {
        throw 'Branch hygiene must only run after a PR closes.'
    }
    if ($name -eq 'release.yml') {
        if (-not $content.Contains('confirm_publish:') -or -not $content.Contains('default: false') -or
            -not $content.Contains('inputs.confirm_publish') -or -not $content.Contains('for name in native-build native-smoke')) {
            throw 'Source milestone publication needs confirmation and full native checks.'
        }
    }
    $perms = [regex]::Match($content, '(?ms)^permissions:\s*\r?\n(?<items>(?:^[ \t]+.*\r?\n)+)')
    if (-not $perms.Success) { throw "$name must specify top-level minimal permissions." }
    $items = @([regex]::Matches($perms.Groups['items'].Value, '(?m)^  ([a-z-]+):\s*(read|write)\s*$') | ForEach-Object { "$($_.Groups[1].Value): $($_.Groups[2].Value)" })
    $expected = @($rule.Permission)
    if ($name -eq 'release.yml') { $expected += 'checks: read' }
    if (($items -join ',') -ne ($expected -join ',')) { throw "$name changed least-privilege permissions: $($items -join ', ')." }
    if ($content -notmatch '(?m)^concurrency:\s*$' -or -not $content.Contains($group) -or $content -notmatch ("(?m)^  cancel-in-progress:\s*" + $rule.Cancel + "\s*$")) {
        throw "$name needs workflow/ref concurrency and approved cancellation behavior."
    }
    $jobs = [regex]::Match($content, '(?ms)^jobs:\s*\r?\n(?<items>.*)\z')
    if (-not $jobs.Success) { throw "$name has no jobs." }
    $jobCount = [regex]::Matches($jobs.Groups['items'].Value, '(?m)^  [a-zA-Z0-9_-]+:\s*$').Count
    $timeouts = @([regex]::Matches($jobs.Groups['items'].Value, '(?m)^    timeout-minutes:\s*(\d+)\s*$') | ForEach-Object { [int]$_.Groups[1].Value })
    if ($jobCount -lt 1 -or $timeouts.Count -ne $jobCount) { throw "$name needs a timeout for every job." }
    foreach ($minutes in $timeouts) {
        if ($minutes -lt 1 -or $minutes -gt $rule.MaxMinutes) { throw "$name exceeds its approved $($rule.MaxMinutes)-minute job budget." }
    }
    # Two required PR checks keep existing protected-main status names. Full Windows
    # work must remain a separately dispatched job; PR checks run on Ubuntu only.
    if ($name -in @('build.yml', 'launch-game-fastpath-smoke.yml')) {
        $quick = if ($name -eq 'build.yml') { 'build' } else { 'smoke' }
        $native = if ($name -eq 'build.yml') { 'native-build' } else { 'native-smoke' }
        $quickLimit = if ($name -eq 'build.yml') { 12 } else { 5 }
        if ($jobCount -ne 2) { throw "$name must contain exactly the PR and manual jobs." }
        $quickMatch = [regex]::Match($jobs.Groups['items'].Value, '(?ms)^  ' + [regex]::Escape($quick) + ':\s*\r?\n(?<body>.*?)(?=^  [a-zA-Z0-9_-]+:|\z)')
        $nativeMatch = [regex]::Match($jobs.Groups['items'].Value, '(?ms)^  ' + [regex]::Escape($native) + ':\s*\r?\n(?<body>.*?)(?=^  [a-zA-Z0-9_-]+:|\z)')
        if (-not $quickMatch.Success -or -not $nativeMatch.Success) { throw "$name changed required job identities." }
        $quickBody = $quickMatch.Groups['body'].Value
        $nativeBody = $nativeMatch.Groups['body'].Value
        if ($quickBody -notmatch "if: github.event_name == 'pull_request'" -or
            $quickBody -notmatch 'runs-on: ubuntu-latest' -or
            $quickBody -notmatch ('timeout-minutes:\s*' + $quickLimit + '\b') -or
            $nativeBody -notmatch "if: github.event_name == 'workflow_dispatch'" -or
            $nativeBody -notmatch 'runs-on: windows-latest') {
            throw "$name must isolate short Ubuntu PR checks from manually dispatched native Windows checks."
        }
        if ($name -eq 'build.yml' -and ($quickBody -notmatch 'dotnet build' -or $quickBody -notmatch 'EnableWindowsTargeting=true')) {
            throw 'Required PR build must actually cross-compile the Windows-targeted .NET application.'
        }
        if ($name -eq 'launch-game-fastpath-smoke.yml' -and $quickBody -notmatch 'launch-game-fastpath-smoke.ps1') {
            throw 'Required PR smoke must run the launch-game source contract.'
        }
    }
    if ($content -match '(?im)uses:\s*[^\r\n]*(?:actions/upload-artifact|actions/cache|cache/save|cache/restore|rust-cache)@' -or
        $content -match '(?im)^\s+cache:\s*\S') {
        throw "$name introduces artifact/cache storage without a reviewed exception."
    }
}
Write-Host "CI policy guard passed: $($found.Count) inventoried workflows, no automatic expensive suites, uploads, or caches."
