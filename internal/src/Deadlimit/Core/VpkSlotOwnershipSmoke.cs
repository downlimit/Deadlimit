using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

internal static class VpkSlotOwnershipSmoke
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-vpk-ownership-smoke-{Guid.NewGuid():N}");
        try
        {
            var retailRoot = Path.Combine(root, "retail");
            var addonsRoot = Path.Combine(retailRoot, "game", "citadel", "addons");
            var projectsRoot = Path.Combine(root, "projects");
            var projectFolder = Path.Combine(projectsRoot, "Ivy");
            Directory.CreateDirectory(addonsRoot);
            Directory.CreateDirectory(projectFolder);

            var vpkPath = Path.Combine(addonsRoot, "pak42_dir.vpk");
            WriteVpk(vpkPath, [1, 2, 3, 4]);

            var candidate = VpkImportSourceValidator.Validate(vpkPath);
            if (!string.Equals(candidate.ReleaseTarget, "42", StringComparison.Ordinal))
            {
                return 1;
            }

            var manifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.ImportedVpk,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "Ivy",
                ProjectFolder = projectFolder,
                Hero = "ivy",
                ReleaseTarget = "42",
                ImportedVpk = new ImportedVpkMetadata
                {
                    SourceVpkFileName = candidate.SourceVpkFileName,
                    SourceVpkPath = candidate.SourceVpkPath,
                    SourceReleaseTarget = candidate.ReleaseTarget,
                    OriginalVpkSha256 = candidate.SourceVpkSha256,
                    SourceEntryCount = candidate.EntryCount,
                },
            };

            _ = ImportedVpkPayloadService.Extract(manifest, candidate);

            var paths = new DeadlimitPaths(new ToolPathSettings
            {
                ProjectsRoot = projectsRoot,
                RetailDeadlockRoot = retailRoot,
                CsdkRoot = Path.Combine(root, "csdk"),
                DeadlockToolsRoot = Path.Combine(root, "tools"),
            });
            var ownership = new VpkSlotOwnershipService(paths);

            var adopted = ownership.AdoptImportedSource(manifest);
            if (!adopted.OwnedByProject || !adopted.ExistingFilePresent)
            {
                return 2;
            }

            var available = ownership.EnsureSlotAvailable(manifest);
            if (!available.OwnedByProject)
            {
                return 3;
            }

            foreach (var familyFile in VpkArchiveIdentityService.EnumerateFamily(vpkPath))
            {
                File.Delete(familyFile);
            }
            WriteVpk(vpkPath, [9, 8, 7, 6]);

            try
            {
                _ = ownership.EnsureSlotAvailable(manifest);
                return 4;
            }
            catch (InvalidOperationException)
            {
                // Expected: external replacement invalidates the recorded family fingerprint.
            }

            var legacyProjectFolder = Path.Combine(projectsRoot, "LegacyIvy");
            Directory.CreateDirectory(ProjectStore.GetMetadataFolder(legacyProjectFolder));
            var legacyManifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.Authoring,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "LegacyIvy",
                ProjectFolder = legacyProjectFolder,
                Hero = "ivy",
                ReleaseTarget = "43",
            };
            var legacyVpk = Path.Combine(addonsRoot, "pak43_dir.vpk");
            WriteVpk(legacyVpk, [3, 3, 3, 3]);
            File.WriteAllText(
                Path.Combine(ProjectStore.GetMetadataFolder(legacyProjectFolder), "build-test-state.json"),
                "{}");

            try
            {
                _ = ownership.EnsureSlotAvailable(legacyManifest);
                return 5;
            }
            catch (LegacyVpkOwnershipException)
            {
                // Expected: legacy state alone cannot silently claim an existing VPK.
            }

            var adoptedLegacy = ownership.AdoptLegacySlot(legacyManifest);
            if (!adoptedLegacy.OwnedByProject
                || !adoptedLegacy.LegacyOwnershipAdopted
                || string.IsNullOrWhiteSpace(adoptedLegacy.ExistingFamilySha256))
            {
                return 6;
            }

            var deploymentSnapshot = ownership.EnsureSlotAvailable(legacyManifest);
            foreach (var familyFile in VpkArchiveIdentityService.EnumerateFamily(legacyVpk))
            {
                File.Delete(familyFile);
            }
            WriteVpk(legacyVpk, [4, 4, 4, 4]);
            try
            {
                ownership.EnsureSlotUnchanged(legacyManifest, deploymentSnapshot);
                return 7;
            }
            catch (InvalidOperationException)
            {
                // Expected: TOCTOU replacement is detected immediately before deployment.
            }

            var emptyProjectFolder = Path.Combine(projectsRoot, "EmptySlot");
            Directory.CreateDirectory(emptyProjectFolder);
            var emptyManifest = new ProjectManifest
            {
                SchemaVersion = 5,
                Mode = ProjectMode.Authoring,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "EmptySlot",
                ProjectFolder = emptyProjectFolder,
                Hero = "ivy",
                ReleaseTarget = "44",
            };
            var emptySnapshot = ownership.EnsureSlotAvailable(emptyManifest);
            var appearedVpk = Path.Combine(addonsRoot, "pak44_dir.vpk");
            WriteVpk(appearedVpk, [5, 5, 5, 5]);
            try
            {
                ownership.EnsureSlotUnchanged(emptyManifest, emptySnapshot);
                return 8;
            }
            catch (InvalidOperationException)
            {
                // Expected: a file appearing in a previously empty slot blocks deployment.
            }

            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Temp cleanup is not part of the ownership assertion.
            }
        }
    }

    private static void WriteVpk(string path, byte[] modelBytes)
    {
        using var package = new Package { Version = 2 };
        package.AddFile("models/heroes/ivy/ivy.vmdl_c", modelBytes);
        package.AddFile("materials/models/heroes/ivy/ivy.vmat_c", [5, 6, 7]);
        package.Write(path);
    }
}
