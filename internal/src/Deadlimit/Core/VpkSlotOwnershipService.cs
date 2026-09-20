using System.Security.Cryptography;

namespace Deadlimit.Core;

public sealed record VpkSlotOwnershipCheck(
    string VpkPath,
    bool ExistingFilePresent,
    bool OwnedByProject,
    bool LegacyOwnershipAdopted,
    string? ExistingFamilySha256 = null);

public sealed class LegacyVpkOwnershipException : InvalidOperationException
{
    public LegacyVpkOwnershipException(string vpkPath, string legacyStatePath)
        : base(
            "Deadlimit found a VPK in this Release ID together with legacy build state, " +
            "but the old state does not contain a cryptographic identity for the deployed VPK. " +
            "Automatic ownership adoption is blocked.\n\n" +
            $"VPK: {vpkPath}\nLegacy state: {legacyStatePath}")
    {
        VpkPath = vpkPath;
        LegacyStatePath = legacyStatePath;
    }

    public string VpkPath { get; }
    public string LegacyStatePath { get; }
}

public sealed class VpkSlotOwnershipService
{
    private const string OwnershipFileName = "vpk-deployment.json";
    private const string BuildStateFileName = "build-test-state.json";

    private readonly DeadlimitPaths _paths;

    public VpkSlotOwnershipService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public VpkSlotOwnershipCheck EnsureSlotAvailable(ProjectManifest manifest)
    {
        var slot = ParseReleaseSlot(manifest.ReleaseTarget);
        var vpkPath = GetVpkPath(slot);
        if (!File.Exists(vpkPath))
        {
            return new VpkSlotOwnershipCheck(
                vpkPath,
                ExistingFilePresent: false,
                OwnedByProject: false,
                LegacyOwnershipAdopted: false,
                ExistingFamilySha256: null);
        }

        var ownershipPath = GetOwnershipPath(manifest);
        var ownershipFileExists = File.Exists(ownershipPath);
        var record = TryLoadRecord(ownershipPath);

        if (ownershipFileExists && record is null)
        {
            throw new InvalidOperationException(
                $"Deadlimit Manager found a VPK ownership file for this project but could not read it safely:\n\n{ownershipPath}\n\n" +
                "The retail VPK slot will not be overwritten until that state is repaired or removed intentionally.");
        }

        if (record is not null
            && record.ReleaseSlot == slot
            && string.Equals(NormalizePath(record.VpkPath), NormalizePath(vpkPath), StringComparison.OrdinalIgnoreCase))
        {
            if (RecordMatchesCurrentArchive(record, vpkPath))
            {
                return new VpkSlotOwnershipCheck(
                    vpkPath,
                    ExistingFilePresent: true,
                    OwnedByProject: true,
                    LegacyOwnershipAdopted: false,
                    ExistingFamilySha256: ComputeFamilySha256(vpkPath));
            }

            throw new InvalidOperationException(
                $"Release ID {slot:D2} points to a VPK that Deadlimit Manager previously adopted or deployed for this project, " +
                "but the VPK family has been changed outside Deadlimit Manager. The slot will not be overwritten automatically.\n\n" +
                $"{vpkPath}\n\n" +
                "Restore/remove that VPK manually or choose another Release ID.");
        }

        var legacyBuildState = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), BuildStateFileName);
        if (manifest.Mode == ProjectMode.Authoring
            && !ownershipFileExists
            && File.Exists(legacyBuildState))
        {
            throw new LegacyVpkOwnershipException(vpkPath, legacyBuildState);
        }

        throw new InvalidOperationException(
            $"Release ID {slot:D2} is already occupied by a VPK that is not known to this Deadlimit Manager project.\n\n" +
            $"{vpkPath}\n\n" +
            "Deadlimit Manager will not overwrite an unknown mod. Choose another Release ID or remove/move that VPK manually.");
    }

    public VpkSlotOwnershipCheck AdoptLegacySlot(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.Authoring)
        {
            throw new InvalidOperationException(
                "Legacy VPK slot adoption is only valid for an authoring project.");
        }

        var slot = ParseReleaseSlot(manifest.ReleaseTarget);
        var vpkPath = GetVpkPath(slot);
        if (!File.Exists(vpkPath))
        {
            throw new FileNotFoundException(
                $"Release ID {slot:D2} cannot be adopted because its retail VPK is no longer present.",
                vpkPath);
        }

        var legacyBuildState = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            BuildStateFileName);
        if (!File.Exists(legacyBuildState))
        {
            throw new InvalidOperationException(
                "Legacy VPK ownership cannot be adopted because build-test-state.json is missing.");
        }

        var ownershipPath = GetOwnershipPath(manifest);
        if (File.Exists(ownershipPath))
        {
            return EnsureSlotAvailable(manifest);
        }

        var familyHash = ComputeFamilySha256(vpkPath);
        WriteRecord(
            ownershipPath,
            new DeploymentRecord
            {
                SchemaVersion = 2,
                ReleaseSlot = slot,
                VpkPath = Path.GetFullPath(vpkPath),
                Sha256 = ComputeSha256(vpkPath),
                FamilySha256 = familyHash,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });

        return new VpkSlotOwnershipCheck(
            vpkPath,
            ExistingFilePresent: true,
            OwnedByProject: true,
            LegacyOwnershipAdopted: true,
            ExistingFamilySha256: familyHash);
    }

    public void EnsureSlotUnchanged(
        ProjectManifest manifest,
        VpkSlotOwnershipCheck snapshot)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(snapshot);

        var slot = ParseReleaseSlot(manifest.ReleaseTarget);
        var expectedPath = GetVpkPath(slot);
        if (!string.Equals(
                NormalizePath(snapshot.VpkPath),
                NormalizePath(expectedPath),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The VPK deployment target changed while BUILD FOR TEST was running.");
        }

        var existsNow = File.Exists(expectedPath);
        if (existsNow != snapshot.ExistingFilePresent)
        {
            throw new InvalidOperationException(
                $"Release ID {slot:D2} changed while BUILD FOR TEST was running. " +
                "Deployment was stopped before replacing the retail VPK.");
        }

        if (!existsNow)
        {
            return;
        }

        var currentFamilyHash = ComputeFamilySha256(expectedPath);
        if (string.IsNullOrWhiteSpace(snapshot.ExistingFamilySha256)
            || !string.Equals(
                snapshot.ExistingFamilySha256,
                currentFamilyHash,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Release ID {slot:D2} was modified while BUILD FOR TEST was running. " +
                "Deployment was stopped before replacing the changed VPK family.");
        }
    }

    public VpkSlotOwnershipCheck AdoptImportedSource(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "VPK slot adoption is only valid for an ImportedVpk project.");
        }

        var slot = ParseReleaseSlot(manifest.ReleaseTarget);
        if (!int.TryParse(manifest.ImportedVpk.SourceReleaseTarget?.Trim(), out var sourceSlot)
            || sourceSlot != slot)
        {
            throw new InvalidOperationException(
                "The imported VPK does not provide a retail Release ID that matches this project's Release ID. " +
                "Deadlimit Manager will not adopt a retail slot by inference.");
        }

        var vpkPath = GetVpkPath(slot);
        if (!File.Exists(vpkPath))
        {
            throw new FileNotFoundException(
                $"Release ID {slot:D2} cannot be adopted because its retail VPK is no longer present.",
                vpkPath);
        }

        var snapshot = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidOperationException(
                "The imported project's original-vpk.json snapshot is missing or unreadable. " +
                "Deadlimit Manager will not adopt a retail slot without the original entry identity.");

        if (!string.Equals(
                snapshot.SourceVpkSha256,
                manifest.ImportedVpk.OriginalVpkSha256,
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                snapshot.SourceReleaseTarget,
                manifest.ImportedVpk.SourceReleaseTarget,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Imported VPK metadata and original-vpk.json disagree. The retail slot was not adopted.");
        }

        var comparison = VpkArchiveIdentityService.CompareToSnapshot(vpkPath, snapshot);
        if (!comparison.Matches)
        {
            throw new InvalidOperationException(
                $"Release ID {slot:D2} is occupied by a VPK that does not match the archive imported into this project.\n\n" +
                $"{comparison.Reason}\n\n{vpkPath}\n\n" +
                "Deadlimit Manager will not claim or overwrite this slot.");
        }

        var ownershipPath = GetOwnershipPath(manifest);
        var ownershipFileExists = File.Exists(ownershipPath);
        var previous = TryLoadRecord(ownershipPath);
        if (ownershipFileExists && previous is null)
        {
            throw new InvalidOperationException(
                $"Deadlimit Manager found a malformed VPK ownership file and will not replace it automatically:\n{ownershipPath}");
        }

        if (previous is not null)
        {
            var sameSlot = previous.ReleaseSlot == slot;
            var samePath = string.Equals(
                NormalizePath(previous.VpkPath),
                NormalizePath(vpkPath),
                StringComparison.OrdinalIgnoreCase);
            if (sameSlot && samePath && RecordMatchesCurrentArchive(previous, vpkPath))
            {
                return new VpkSlotOwnershipCheck(
                    vpkPath,
                    ExistingFilePresent: true,
                    OwnedByProject: true,
                    LegacyOwnershipAdopted: false,
                    ExistingFamilySha256: ComputeFamilySha256(vpkPath));
            }

            throw new InvalidOperationException(
                "This imported project already contains VPK ownership state for a different archive or slot. " +
                "Deadlimit Manager will not replace that ownership state automatically.");
        }

        WriteRecord(
            ownershipPath,
            new DeploymentRecord
            {
                SchemaVersion = 2,
                ReleaseSlot = slot,
                VpkPath = Path.GetFullPath(vpkPath),
                Sha256 = ComputeSha256(vpkPath),
                FamilySha256 = ComputeFamilySha256(vpkPath),
                ImportedSourceSha256 = manifest.ImportedVpk.OriginalVpkSha256,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });

        return new VpkSlotOwnershipCheck(
            vpkPath,
            ExistingFilePresent: true,
            OwnedByProject: true,
            LegacyOwnershipAdopted: false,
            ExistingFamilySha256: ComputeFamilySha256(vpkPath));
    }

    public void RecordSuccessfulDeployment(ProjectManifest manifest, string deployedVpkPath)
    {
        if (!File.Exists(deployedVpkPath))
        {
            throw new FileNotFoundException(
                "Cannot record VPK ownership because the deployed VPK does not exist.",
                deployedVpkPath);
        }

        var slot = ParseReleaseSlot(manifest.ReleaseTarget);
        var ownershipPath = GetOwnershipPath(manifest);
        var ownershipFileExists = File.Exists(ownershipPath);
        var previous = TryLoadRecord(ownershipPath);

        if (ownershipFileExists && previous is null)
        {
            throw new InvalidOperationException(
                $"Deadlimit Manager cannot update the malformed VPK ownership file safely:\n{ownershipPath}");
        }

        if (previous is not null
            && previous.ReleaseSlot != slot
            && !string.IsNullOrWhiteSpace(previous.VpkPath)
            && File.Exists(previous.VpkPath))
        {
            TryRemovePreviouslyOwnedFamily(previous);
        }

        WriteRecord(
            ownershipPath,
            new DeploymentRecord
            {
                SchemaVersion = 2,
                ReleaseSlot = slot,
                VpkPath = Path.GetFullPath(deployedVpkPath),
                Sha256 = ComputeSha256(deployedVpkPath),
                FamilySha256 = ComputeFamilySha256(deployedVpkPath),
                ImportedSourceSha256 = manifest.Mode == ProjectMode.ImportedVpk
                    ? manifest.ImportedVpk?.OriginalVpkSha256 ?? string.Empty
                    : string.Empty,
                UpdatedUtc = DateTimeOffset.UtcNow,
            });
    }

    private void TryRemovePreviouslyOwnedFamily(DeploymentRecord previous)
    {
        var retailAddonsRoot = Path.Combine(
            _paths.RetailDeadlockRoot,
            "game",
            "citadel",
            "addons");
        var expectedPreviousName = $"pak{previous.ReleaseSlot:D2}_dir.vpk";
        var safePreviousPath = SafePath.EnsureUnderRoot(
            retailAddonsRoot,
            previous.VpkPath,
            "Previous VPK deployment path");
        if (!string.Equals(
                Path.GetFileName(safePreviousPath),
                expectedPreviousName,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Previous VPK ownership path has an unexpected filename: {safePreviousPath}");
        }

        if (!string.IsNullOrWhiteSpace(previous.FamilySha256))
        {
            var previousFamilyHash = ComputeFamilySha256(safePreviousPath);
            if (!string.Equals(
                    previousFamilyHash,
                    previous.FamilySha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            foreach (var familyFile in VpkArchiveIdentityService.EnumerateFamily(safePreviousPath))
            {
                File.Delete(familyFile);
            }
            return;
        }

        // Schema-1 ownership only proved the directory VPK. Keep the historical
        // conservative behavior instead of deleting unverified chunk files.
        var previousHash = ComputeSha256(safePreviousPath);
        if (string.Equals(previousHash, previous.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(safePreviousPath);
        }
    }

    private static bool RecordMatchesCurrentArchive(DeploymentRecord record, string vpkPath)
    {
        if (!string.IsNullOrWhiteSpace(record.FamilySha256))
        {
            var currentFamilyHash = ComputeFamilySha256(vpkPath);
            return string.Equals(
                record.FamilySha256,
                currentFamilyHash,
                StringComparison.OrdinalIgnoreCase);
        }

        var currentHash = ComputeSha256(vpkPath);
        return string.Equals(record.Sha256, currentHash, StringComparison.OrdinalIgnoreCase);
    }

    private string GetVpkPath(int slot) =>
        Path.Combine(
            _paths.RetailDeadlockRoot,
            "game",
            "citadel",
            "addons",
            $"pak{slot:D2}_dir.vpk");

    private static string GetOwnershipPath(ProjectManifest manifest) =>
        Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), OwnershipFileName);

    private static DeploymentRecord? TryLoadRecord(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var record = JsonSerializer.Deserialize<DeploymentRecord>(File.ReadAllText(path));
            if (record is null
                || record.SchemaVersion is < 1 or > 2
                || record.ReleaseSlot is < 1 or > 99
                || string.IsNullOrWhiteSpace(record.VpkPath)
                || string.IsNullOrWhiteSpace(record.Sha256))
            {
                return null;
            }
            return record;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void WriteRecord(string ownershipPath, DeploymentRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ownershipPath)!);
        AtomicFile.WriteJson(
            ownershipPath,
            record,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static int ParseReleaseSlot(string? releaseTarget)
    {
        if (!int.TryParse(releaseTarget?.Trim(), out var slot) || slot is < 1 or > 99)
        {
            throw new InvalidOperationException(
                "BUILD & TEST needs Release ID 01-99. It maps to pak##_dir.vpk in retail Deadlock addons.");
        }
        return slot;
    }

    private static string ComputeSha256(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                options: FileOptions.SequentialScan);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateLockedVpkException(path, ex);
        }
    }

    private static string ComputeFamilySha256(string path)
    {
        try
        {
            return VpkArchiveIdentityService.ComputeFamilySha256(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw CreateLockedVpkException(path, ex);
        }
    }

    private static InvalidOperationException CreateLockedVpkException(string path, Exception inner) =>
        new(
            $"The retail VPK is currently locked by another process and cannot be inspected or replaced:\n\n{path}\n\n" +
            "Close Deadlock and any VPK viewer using this archive, then run BUILD & TEST again.",
            inner);

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class DeploymentRecord
    {
        public int SchemaVersion { get; set; } = 2;
        public int ReleaseSlot { get; set; }
        public string VpkPath { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public string FamilySha256 { get; set; } = string.Empty;
        public string ImportedSourceSha256 { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
