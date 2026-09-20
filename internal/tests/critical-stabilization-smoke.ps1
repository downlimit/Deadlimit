$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) {
        throw "$Label contract is missing: $Pattern"
    }
}

function Assert-NotContains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -ge 0) {
        throw "$Label contains forbidden text: $Pattern"
    }
}

$toolchain = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ToolchainDependencyService.cs' -Raw
foreach ($required in @(
    'PinnedCsdkGeneration = 12',
    'CsdkPinnedDriveId = "1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS"',
    'DeadlockToolsPinnedTag = "v1.1.0"',
    'DeadlockToolsWindowsSha256 = "7E4668DA796E4CA67B1EE684CF03270E07FECEBECCF66D04DDF1F3A3E7409DCF"',
    'DepotDownloaderPinnedTag = "DepotDownloader_3.4.0"',
    'DepotDownloaderWindowsSha256 = "41C9E9F0DF54B3AD02E67A11726756E5C73283BD7C2E1B04ACFA5AE4C2ED3767"',
    'IncrementalHash.CreateHash(HashAlgorithmName.SHA256)',
    'ApplyOverlayTransaction(',
    'IsTrustedManagedDeadlockTools(',
    'IsTrustedDepotDownloaderCache(',
    'executableSha256'
)) {
    Assert-Contains $toolchain $required 'Toolchain hardening'
}
Assert-NotContains $toolchain 'repos/dotryen/DeadlockTools/releases/latest' 'Toolchain hardening'
Assert-NotContains $toolchain 'repos/SteamRE/DepotDownloader/releases/latest' 'Toolchain hardening'
Assert-NotContains $toolchain 'ReadCsdkPageAsync' 'Toolchain hardening'
Assert-NotContains $toolchain 'CsdkGenerationRegex' 'Toolchain hardening'

$vpk = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/VpkSlotOwnershipService.cs' -Raw
foreach ($required in @(
    'LegacyVpkOwnershipException',
    'AdoptLegacySlot(',
    'EnsureSlotUnchanged(',
    'ExistingFamilySha256'
)) {
    Assert-Contains $vpk $required 'VPK ownership hardening'
}

$build = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
Assert-Contains $build 'slotOwnership.EnsureSlotUnchanged(manifest, slotSnapshot);' 'Authoring VPK deployment'

$imported = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkBuildAndTestService.cs' -Raw
Assert-Contains $imported 'slotGuard.EnsureSlotUnchanged(manifest, slotCheck);' 'Imported VPK deployment'

$buildUi = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
foreach ($required in @(
    'ApplicationMutationCoordinator.Begin',
    'form.FormClosing',
    'CANCELLING BUILD',
    'CANCELLING PREPARATION'
)) {
    Assert-Contains $buildUi $required 'Pipeline mutation interlock'
}

$mainForm = Get-Content -LiteralPath 'internal/src/Deadlimit/App/MainForm.cs' -Raw
foreach ($required in @(
    'EnsureLegacyRootAuthoringMigrated',
    'The originals will be left unchanged',
    '_heroExtractionCancellation.Cancel()',
    'Project switching is locked while'
)) {
    Assert-Contains $mainForm $required 'Project migration and extraction shutdown'
}

$projectEntry = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ProjectCreationChoiceFeature.cs' -Raw
Assert-Contains $projectEntry 'Cannot create or import a project while' 'Project mutation interlock'

$authoring = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ProjectAuthoringLayout.cs' -Raw
Assert-Contains $authoring "More than one authoring model uses the filename" 'Authoring ambiguity guard'

Write-Host 'Critical stabilization contracts passed.'
