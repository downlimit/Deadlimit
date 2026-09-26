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
            ProjectAuthoringLayout.EnsureStructure(projectFolder);
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

            var progressUpdates = new List<ImportedVpkImportProgress>();
            var extracted = ImportedVpkPayloadService.Extract(
                manifest,
                candidate,
                new InlineProgress<ImportedVpkImportProgress>(progressUpdates.Add));
            if (!string.Equals(
                    extracted.AuthoringFolder,
                    Path.Combine(projectFolder, ProjectAuthoringLayout.AuthoringFolderName),
                    StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(Path.Combine(projectFolder, "payload"))
                || !string.Equals(
                    extracted.CompiledFolder,
                    Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), ImportedVpkPayloadService.CompiledFolderName),
                    StringComparison.OrdinalIgnoreCase)
                || ProjectAuthoringLayout.ArtistFolderNames.Any(folderName =>
                    !Directory.Exists(Path.Combine(projectFolder, folderName))))
            {
                return 8;
            }
            if (!progressUpdates.Any(update => update.Percent is >= 26 and <= 80)
                || progressUpdates.Any(update => update.Percent is < 0 or > 100))
            {
                return 9;
            }
            var original = ImportedVpkPayloadService.TryLoadSnapshot(projectFolder);
            if (original?.SourceVpkVersion != 1)
            {
                return 1;
            }

            const string modelPath = "models/heroes/ivy/ivy.vmdl_c";
            const string materialPath = "materials/models/heroes/ivy/ivy.vmat_c";
            var payloadModel = Path.Combine(extracted.CompiledFolder, modelPath.Replace('/', Path.DirectorySeparatorChar));
            var payloadMaterial = Path.Combine(extracted.CompiledFolder, materialPath.Replace('/', Path.DirectorySeparatorChar));
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
                if (package.Version != 1)
                {
                    return 3;
                }
                package.VerifyFileChecksums();

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

            const string addedPath = "materials/models/heroes/ivy/ivy_custom.vmat_c";
            var rebuiltMaterialBytes = new byte[] { 21, 22, 23, 24 };
            var addedBytes = new byte[] { 31, 32 };
            var builtFolder = Path.Combine(
                ProjectStore.GetMetadataFolder(projectFolder),
                ImportedVpkPayloadService.BuiltCompiledFolderName);
            var builtModel = Path.Combine(builtFolder, modelPath.Replace('/', Path.DirectorySeparatorChar));
            var builtMaterial = Path.Combine(builtFolder, materialPath.Replace('/', Path.DirectorySeparatorChar));
            var builtAdded = Path.Combine(builtFolder, addedPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(builtModel)!);
            Directory.CreateDirectory(Path.GetDirectoryName(builtMaterial)!);
            File.WriteAllBytes(builtModel, repairedModelBytes);
            File.WriteAllBytes(builtMaterial, rebuiltMaterialBytes);
            File.WriteAllBytes(builtAdded, addedBytes);

            var originalMaterial = original.Entries.Single(entry => entry.InternalPath == materialPath);
            var authoringReportPath = Path.Combine(
                ProjectStore.GetMetadataFolder(projectFolder),
                ImportedVpkAuthoringBuildService.ReportFileName);
            AtomicFile.WriteJson(
                authoringReportPath,
                new ImportedVpkAuthoringBuildSnapshot
                {
                    BuiltUtc = DateTimeOffset.UtcNow,
                    Entries =
                    [
                        new ImportedVpkAuthoringBuildEntry(
                            materialPath,
                            originalMaterial.Sha256,
                            ComputeSha256(rebuiltMaterialBytes),
                            rebuiltMaterialBytes.LongLength),
                        new ImportedVpkAuthoringBuildEntry(
                            addedPath,
                            null,
                            ComputeSha256(addedBytes),
                            addedBytes.LongLength),
                    ],
                },
                JsonOptions);

            var authoringRepack = new ImportedVpkRepackService().RebuildAndVerify(manifest);
            if (authoringRepack.EntryCount != 3
                || authoringRepack.ChangedEntryCount != 3
                || authoringRepack.Entries.Count(entry =>
                    entry.Status == ImportedVpkRepackEntryStatus.RebuiltFromAuthoring) != 2
                || authoringRepack.Entries.Count(entry =>
                    entry.Status == ImportedVpkRepackEntryStatus.Repaired) != 1)
            {
                return 10;
            }

            Directory.Delete(builtFolder, recursive: true);
            File.Delete(authoringReportPath);

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
            File.WriteAllBytes(Path.Combine(extracted.CompiledFolder, "unexpected.bin"), new byte[] { 42 });
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
