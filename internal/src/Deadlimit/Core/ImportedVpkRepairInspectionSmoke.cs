using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

internal static class ImportedVpkRepairInspectionSmoke
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-repair-inspection-smoke-{Guid.NewGuid():N}");
        try
        {
            var retailRoot = Path.Combine(root, "retail");
            var retailCitadel = Path.Combine(retailRoot, "game", "citadel");
            var retailAddons = Path.Combine(retailCitadel, "addons");
            var projectFolder = Path.Combine(root, "project");
            Directory.CreateDirectory(retailAddons);
            Directory.CreateDirectory(projectFolder);

            var retailVpk = Path.Combine(retailCitadel, "pak01_dir.vpk");
            using (var package = new Package { Version = 2 })
            {
                package.AddFile("models/heroes/ivy/ivy.vmdl_c", [1, 2, 3, 4]);
                package.Write(retailVpk);
            }

            var addonVpk = Path.Combine(retailAddons, "pak42_dir.vpk");
            using (var package = new Package { Version = 2 })
            {
                package.AddFile("models/heroes/ivy/ivy.vmdl_c", [9, 9, 9, 9]);
                package.AddFile("models/heroes_wip/testmissing/testmissing.vmdl_c", [7, 7, 7]);
                package.AddFile("materials/models/heroes/ivy/ivy.vmat_c", [5, 6, 7]);
                package.Write(addonVpk);
            }

            var candidate = VpkImportSourceValidator.Validate(addonVpk);
            var manifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.ImportedVpk,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "Inspection",
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
                    PrimaryModelResources = ["models/heroes/ivy/ivy.vmdl_c"],
                },
            };

            _ = ImportedVpkPayloadService.Extract(manifest, candidate);

            var paths = new DeadlimitPaths(new ToolPathSettings
            {
                RetailDeadlockRoot = retailRoot,
                CsdkRoot = Path.Combine(root, "csdk"),
                DeadlockToolsRoot = Path.Combine(root, "tools"),
            });
            var result = new ImportedVpkRepairInspectionService(paths).InspectAndSave(manifest);

            if (result.Targets.Count != 2)
            {
                return 1;
            }

            var ivy = result.Targets.SingleOrDefault(target =>
                string.Equals(target.ResourcePath, "models/heroes/ivy/ivy.vmdl_c", StringComparison.OrdinalIgnoreCase));
            if (ivy is null
                || !ivy.RetailMatched
                || ivy.Status != ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable
                || !string.Equals(Path.GetFullPath(ivy.RetailVpkPath!), Path.GetFullPath(retailVpk), StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            var missing = result.Targets.SingleOrDefault(target =>
                string.Equals(
                    target.ResourcePath,
                    "models/heroes_wip/testmissing/testmissing.vmdl_c",
                    StringComparison.OrdinalIgnoreCase));
            if (missing is null
                || missing.RetailMatched
                || missing.Status != ImportedVpkRepairTargetStatus.MissingRetailCounterpart)
            {
                return 3;
            }

            if (result.Targets.Any(target => target.ResourcePath.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase)))
            {
                return 4;
            }

            if (!File.Exists(result.ReportPath))
            {
                return 5;
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
                // Temp cleanup is not part of the inspection assertions.
            }
        }
    }
}
