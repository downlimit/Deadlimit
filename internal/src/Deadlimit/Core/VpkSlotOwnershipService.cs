using System.Security.Cryptography;

namespace Deadlimit.Core;

public sealed record VpkSlotOwnershipCheck(
    string VpkPath,
    bool ExistingFilePresent,
    bool OwnedByProject,
    bool LegacyOwnershipAdopted);

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
                LegacyOwnershipAdopted: false);
        }

        var currentHash = ComputeSha256(vpkPath);
        var ownershipPath = GetOwnershipPath(manifest);
        var record = TryLoadRecord(ownershipPath);

        if (record is not null
            && record.ReleaseSlot == slot
            && string.Equals(NormalizePath(record.VpkPath), NormalizePath(vpkPath), StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(record.Sha256, currentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new VpkSlotOwnershipCheck(
                    vpkPath,
                    ExistingFilePresent: true,
                    OwnedByProject: true,
                    LegacyOwnershipAdopted: false);
            }

            throw new InvalidOperationException(
                $"Release ID {slot:D2} points to a VPK that Deadlimit previously deployed for this project, " +
                "but the file has been changed outside Deadlimit. The slot will not be overwritten automatically.\n\n" +
                $"{vpkPath}\n\n" +
                "Restore/remove that VPK manually or choose another Release ID.");
        }

        var legacyBuildState = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), BuildStateFileName);
        if (record is null && File.Exists(legacyBuildState))
        {
            return new VpkSlotOwnershipCheck(
                vpkPath,
                ExistingFilePresent: true,
                OwnedByProject: true,
                LegacyOwnershipAdopted: true);
        }

        throw new InvalidOperationException(
            $"Release ID {slot:D2} is already occupied by a VPK that is not known to this Deadlimit project.\n\n" +
            $"{vpkPath}\n\n" +
            "Deadlimit will not overwrite an unknown mod. Choose another Release ID or remove/move that VPK manually.");
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
        var previous = TryLoadRecord(ownershipPath);

        if (previous is not null
            && previous.ReleaseSlot != slot
            && !string.IsNullOrWhiteSpace(previous.VpkPath)
            && File.Exists(previous.VpkPath))
        {
            var previousHash = ComputeSha256(previous.VpkPath);
            if (string.Equals(previousHash, previous.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(previous.VpkPath);
            }
        }

        var record = new DeploymentRecord
        {
            SchemaVersion = 1,
            ReleaseSlot = slot,
            VpkPath = Path.GetFullPath(deployedVpkPath),
            Sha256 = ComputeSha256(deployedVpkPath),
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ownershipPath)!);
        File.WriteAllText(
            ownershipPath,
            JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
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
            return JsonSerializer.Deserialize<DeploymentRecord>(File.ReadAllText(path));
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
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private sealed class DeploymentRecord
    {
        public int SchemaVersion { get; set; } = 1;
        public int ReleaseSlot { get; set; }
        public string VpkPath { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
