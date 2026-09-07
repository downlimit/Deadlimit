$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$nonPublicStatic = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static

$type = $assembly.GetType('Deadlimit.Core.RetailModLoadingService', $true)
$patch = $type.GetMethod('PatchSearchPathsBody', $nonPublicStatic)
if ($null -eq $patch) {
    throw 'RetailModLoadingService.PatchSearchPathsBody was not found.'
}

function Invoke-SearchPathPatch([string]$body) {
    return [string]$patch.Invoke($null, [object[]]@($body, "`r`n"))
}

function Assert-LineCount(
    [string]$text,
    [string]$pattern,
    [int]$expected,
    [string]$message
) {
    $actual = @($text -split "\r?\n" | Where-Object { $_ -match $pattern }).Count
    if ($actual -ne $expected) {
        throw "$message Expected $expected, found $actual.`n$text"
    }
}

# Stock retail has implicit MOD/default-write semantics: with no explicit Mod/Write
# keys, Source 2 derives both from the first Game path (citadel). Enabling addons
# must materialize those paths before citadel/addons becomes the first Game path.
$vanilla = @"
            Game_Language       citadel_*LANGUAGE*
            Game_LowViolence    citadel_lv

            Game                citadel
            Game                core
"@
$patchedVanilla = Invoke-SearchPathPatch $vanilla
Assert-LineCount $patchedVanilla '^\s*Game\s+citadel[\\/]addons\s*$' 1 'Vanilla patch must add one addons Game path.'
Assert-LineCount $patchedVanilla '^\s*Mod\s+citadel\s*$' 1 'Vanilla patch must preserve the implicit MOD root explicitly.'
Assert-LineCount $patchedVanilla '^\s*Write\s+citadel\s*$' 1 'Vanilla patch must preserve the implicit default write root explicitly.'
Assert-LineCount $patchedVanilla '^\s*Game\s+citadel\s*$' 1 'Vanilla patch must retain the retail citadel Game path.'

$gameLines = @($patchedVanilla -split "\r?\n" | Where-Object { $_ -match '^\s*Game\s+' })
if ($gameLines.Count -lt 3 -or $gameLines[0] -notmatch 'citadel[\\/]addons') {
    throw "The addons Game path must remain ahead of retail Game citadel for override priority.`n$patchedVanilla"
}

# Repair the exact legacy Deadlimit state that could already be on disk: addons
# was inserted earlier, but Mod/Write were left implicit and therefore changed.
$legacyBroken = @"
            Game                citadel/addons

            Game                citadel
            Game                core
"@
$patchedLegacy = Invoke-SearchPathPatch $legacyBroken
Assert-LineCount $patchedLegacy '^\s*Game\s+citadel[\\/]addons\s*$' 1 'Legacy repair must not duplicate the addons Game path.'
Assert-LineCount $patchedLegacy '^\s*Mod\s+citadel\s*$' 1 'Legacy repair must restore MOD citadel.'
Assert-LineCount $patchedLegacy '^\s*Write\s+citadel\s*$' 1 'Legacy repair must restore Write citadel.'

# Explicit user configuration is authoritative. Deadlimit only materializes the
# implicit retail defaults when those keys were absent; it must not replace a
# pre-existing explicit Mod or Write root.
$explicitCustom = @"
            Mod                 custom_mod
            Write               custom_write
            Game                citadel
            Game                core
"@
$patchedCustom = Invoke-SearchPathPatch $explicitCustom
Assert-LineCount $patchedCustom '^\s*Game\s+citadel[\\/]addons\s*$' 1 'Custom config must still receive the addons Game path.'
Assert-LineCount $patchedCustom '^\s*Mod\s+custom_mod\s*$' 1 'Existing explicit Mod path must be preserved.'
Assert-LineCount $patchedCustom '^\s*Write\s+custom_write\s*$' 1 'Existing explicit Write path must be preserved.'
Assert-LineCount $patchedCustom '^\s*Mod\s+citadel\s*$' 0 'Deadlimit must not add MOD citadel over an explicit custom Mod path.'
Assert-LineCount $patchedCustom '^\s*Write\s+citadel\s*$' 0 'Deadlimit must not add Write citadel over an explicit custom Write path.'

# A fully repaired block is idempotent, preventing repeated BUILD FOR TEST runs
# from continuously rewriting gameinfo.gi.
$alreadyRepaired = @"
            Game                citadel/addons
            Mod                 citadel
            Write               citadel
            Game                citadel
            Game                core
"@
$patchedAgain = Invoke-SearchPathPatch $alreadyRepaired
if (-not [string]::Equals($patchedAgain, $alreadyRepaired, [StringComparison]::Ordinal)) {
    throw "An already repaired SearchPaths block must remain byte-for-byte unchanged.`n$patchedAgain"
}
