using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

internal static class ImportedVpkRepackSmoke
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-vpk-repack-smoke-{Guid.NewGuid():N}");
        try
        {
            var projectsRoot = Path.Combine(root, "projects");
            var projectFolder = Path.Combine(projectsRoot, "Ivy");
            var sourceFolder = Path.Combine(root, "retail", "game", "citadel", "addons");
            Directory.CreateDirectory(projectFolder);
            Directory.CreateDirectory(sourceFolder);

            var sourceVpk = Path.Combine(sourceFolder, "pak42_dir.vpk");
            var originalModelBytes = new byte[] { 1, 2, 3, 4 };
            var repairedModelBytes = new byte[] { 9, 8, 7, 6, 5 };
            var materialBytes = new byte[] { 11, 12, 13 };
            WriteSourceVpk(sourceVpk, originalModelBytes, materialBytes);

            var candidate = VpkImportSourceValidator.Validate(sourceVpk);
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

            var extracted = ImportedVpkPayloadService.Extract(manifest, candidate);
            var original = ImportedVpkPayloadService.TryLoadSnapshot(projectFolder);
            if (original?.SourceVpkVersion != 1)
            {
                return 1;
            }

            const string modelPath = "models/heroes/ivy/ivy.vmdl_c";
            const string materialPath = "materials/models/heroes/ivy/ivy.vmat_c";
            var payloadModel = Path.Combine(extracted.PayloadFolder, modelPath.Replace('/', Path.DirectorySeparatorChar));
            var payloadMaterial = Path.Combine(extracted.PayloadFolder, materialPath.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllBytes(payloadModel, repairedModelBytes);

            var originalModel = original.Entries.Single(entry => entry.InternalPath == modelPath);
            var repairReportPath = Path.Combine(
                ProjectStore.GetMetadataFolder(projectFolder),
                ImportedVpkAnimationBindingRepairService.ReportFileName);
            AtomicFile.WriteJson(
                repairReportPath,
                new ImportedVpkAnimationBindingRepairSnapshot
                {
                    RepairedUtc = DateTimeOffset.UtcNow,
                    Entries =
                    [
                        new ImportedVpkAnimationBindingRepairEntry(
                            modelPath,
                            ImportedVpkRepairTargetStatus.BindingsDiffer,
                            Modified: true,
                            originalModel.Sha256,
                            ComputeSha256(repairedModelBytes),
                            "Synthetic Stage 9 repair provenance."),
                    ],
                },
                JsonOptions);

            var repacked = new ImportedVpkRepackService().RebuildAndVerify(manifest);
            if (repacked.OutputVpkVersion != 1
                || !repacked.SourceVersionPreserved
                || repacked.EntryCount != 2
                || repacked.ChangedEntryCount != 1
                || !File.Exists(repacked.OutputVpkPath))
            {
                return 2;
            }

            using (var package = new Package())
            {
                package.Read(repacked.OutputVpkPath);
                package.VerifyHashes();
                package.VerifyFileChecksums();
                if (package.Version != 1)
                {
                    return 3;
                }

                var archiveEntries = package.Entries!
                    .SelectMany(group => group.Value)
                    .ToDictionary(
                        entry => entry.GetFullPath().Replace('\\', '/'),
                        StringComparer.Ordinal);
                if (archiveEntries.Count != 2
                    || !archiveEntries.ContainsKey(modelPath)
                    || !archiveEntries.ContainsKey(materialPath)
                    || archiveEntries.Keys.Any(path => path.Contains(".deadlimit", StringComparison.OrdinalIgnoreCase)))
                {
                    return 4;
                }

                package.ReadEntry(archiveEntries[modelPath], out byte[] packedModel);
                package.ReadEntry(archiveEntries[materialPath], out byte[] packedMaterial);
                if (!packedModel.SequenceEqual(repairedModelBytes)
                    || !packedMaterial.SequenceEqual(materialBytes))
                {
                    return 5;
                }
            }

            File.WriteAllBytes(payloadMaterial, new byte[] { 99, 98 });
            try
            {
                _ = new ImportedVpkRepackService().RebuildAndVerify(manifest);
                return 6;
            }
            catch (InvalidOperationException)
            {
                // Expected: unrelated payload mutation has no repair provenance.
            }

            File.WriteAllBytes(payloadMaterial, materialBytes);
            File.WriteAllBytes(Path.Combine(extracted.PayloadFolder, "unexpected.bin"), new byte[] { 42 });
            try
            {
                _ = new ImportedVpkRepackService().RebuildAndVerify(manifest);
                return 7;
            }
            catch (InvalidOperationException)
            {
                // Expected: internal path set differs from the imported VPK snapshot.
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
                // Temp cleanup is not part of the repack assertion.
            }
        }
    }

    private static void WriteSourceVpk(string path, byte[] modelBytes, byte[] materialBytes)
    {
        using var package = new Package { Version = 1 };
        package.AddFile("models/heroes/ivy/ivy.vmdl_c", modelBytes);
        package.AddFile("materials/models/heroes/ivy/ivy.vmat_c", materialBytes);
        package.Write(path);
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
