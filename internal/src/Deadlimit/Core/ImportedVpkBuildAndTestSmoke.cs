using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal static class ImportedVpkBuildAndTestSmoke
{
    private const string ResourcePath = "models/heroes/ivy/ivy.vmdl_c";
    private const string MaterialPath = "materials/models/heroes/ivy/ivy.vmat_c";
    private const string RetailGraph = "animgraphs/animgraph2/hero/hero.vnmgraph+ivy.vnmgraph";
    private const string RetailUiGraph = "animgraphs/animgraph2/hero/hero_ui.vnmgraph+ivy.vnmgraph";
    private const string RetailSkeleton = "models/heroes_wip/ivy/ivy.vnmskel";

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-import-build-smoke-{Guid.NewGuid():N}");
        try
        {
            var retailRoot = Path.Combine(root, "retail");
            var retailCitadel = Path.Combine(retailRoot, "game", "citadel");
            var addonsRoot = Path.Combine(retailCitadel, "addons");
            var projectsRoot = Path.Combine(root, "projects");
            var projectFolder = Path.Combine(projectsRoot, "IvyRepair");
            Directory.CreateDirectory(addonsRoot);
            Directory.CreateDirectory(projectFolder);

            var retailModel = CreateCompiledModel(RetailGraph, RetailUiGraph, RetailSkeleton);
            var staleModel = CreateCompiledModel(
                "animgraphs/animgraph2/old/old.vnmgraph",
                "animgraphs/animgraph2/old/old_ui.vnmgraph",
                "models/heroes_wip/ivy/old.vnmskel");
            var materialBytes = new byte[] { 21, 22, 23, 24 };

            WriteVpk(Path.Combine(retailCitadel, "pak01_dir.vpk"), retailModel, materialBytes);
            var importedSource = Path.Combine(addonsRoot, "pak42_dir.vpk");
            WriteVpk(importedSource, staleModel, materialBytes);

            var candidate = VpkImportSourceValidator.Validate(importedSource);
            var manifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.ImportedVpk,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "Ivy Repair",
                ProjectFolder = projectFolder,
                Hero = "ivy",
                ReleaseTarget = "42",
                RetailMainModel = ResourcePath,
                ImportedVpk = new ImportedVpkMetadata
                {
                    SourceVpkFileName = candidate.SourceVpkFileName,
                    SourceVpkPath = candidate.SourceVpkPath,
                    SourceReleaseTarget = candidate.ReleaseTarget,
                    OriginalVpkSha256 = candidate.SourceVpkSha256,
                    SourceEntryCount = candidate.EntryCount,
                    PrimaryModelResources = [ResourcePath],
                },
            };
            _ = ImportedVpkPayloadService.Extract(manifest, candidate);

            var missingCsdkRoot = Path.Combine(root, "csdk-must-not-be-created");
            var paths = new DeadlimitPaths(new ToolPathSettings
            {
                ProjectsRoot = projectsRoot,
                RetailDeadlockRoot = retailRoot,
                CsdkRoot = missingCsdkRoot,
                DeadlockToolsRoot = Path.Combine(root, "deadlock-tools-must-not-be-used"),
            });
            _ = new VpkSlotOwnershipService(paths).AdoptImportedSource(manifest);

            var routedService = new Deadlimit.App.BuildAndTestService(paths);
            var first = routedService.BuildAsync(manifest).GetAwaiter().GetResult();
            if (first.CompiledSourceCount != 0
                || !first.Ag2Applied
                || !string.Equals(first.VpkPath, importedSource, StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }
            if (Directory.Exists(missingCsdkRoot)
                || File.Exists(Path.Combine(
                    ProjectStore.GetMetadataFolder(projectFolder),
                    "build-test-state.json")))
            {
                return 2;
            }

            var firstDeployed = ReadEntry(importedSource, ResourcePath);
            var firstSnapshot = CompiledModelAnimationBindingRepair.ReadSnapshot(firstDeployed, ResourcePath);
            var retailSnapshot = CompiledModelAnimationBindingRepair.ReadSnapshot(retailModel, ResourcePath);
            if (!CompiledModelAnimationBindingRepair.SnapshotsEqual(firstSnapshot, retailSnapshot)
                || !ReadEntry(importedSource, MaterialPath).SequenceEqual(materialBytes))
            {
                return 3;
            }

            var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
            var repairReport = Path.Combine(
                metadataFolder,
                ImportedVpkAnimationBindingRepairService.ReportFileName);
            if (!File.Exists(repairReport))
            {
                return 4;
            }
            var provenanceBeforeSecond = File.ReadAllBytes(repairReport);
            var payloadModelPath = SafePath.ResolveUnderRoot(
                Path.Combine(projectFolder, ImportedVpkPayloadService.PayloadFolderName),
                ResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Imported Build smoke payload model");
            var payloadBeforeSecond = File.ReadAllBytes(payloadModelPath);

            var second = routedService.BuildAsync(manifest).GetAwaiter().GetResult();
            if (second.CompiledSourceCount != 0 || second.Ag2Applied)
            {
                return 5;
            }
            if (!payloadBeforeSecond.SequenceEqual(File.ReadAllBytes(payloadModelPath))
                || !provenanceBeforeSecond.SequenceEqual(File.ReadAllBytes(repairReport)))
            {
                return 6;
            }

            var secondDeployed = ReadEntry(importedSource, ResourcePath);
            if (!secondDeployed.SequenceEqual(firstDeployed)
                || !ReadEntry(importedSource, MaterialPath).SequenceEqual(materialBytes))
            {
                return 7;
            }

            var authoringFolder = Path.Combine(projectsRoot, "AuthoringControl");
            Directory.CreateDirectory(authoringFolder);
            var authoring = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.Authoring,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "Authoring Control",
                ProjectFolder = authoringFolder,
                ReleaseTarget = "43",
            };
            try
            {
                _ = routedService.BuildAsync(authoring).GetAwaiter().GetResult();
                return 8;
            }
            catch (DirectoryNotFoundException exception)
                when (exception.Message.Contains("content", StringComparison.OrdinalIgnoreCase)
                    || exception.Message.Contains("csdk", StringComparison.OrdinalIgnoreCase))
            {
                // Expected: Authoring still enters the unchanged Core CSDK path.
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
                // Temp cleanup is not part of the Build/Test route assertion.
            }
        }
    }

    private static byte[] CreateCompiledModel(string? graph, string? uiGraph, string? skeleton)
    {
        var root = KVObject.Collection();
        root["m_name"] = new KVObject("stage10-marker");

        if (graph is not null && uiGraph is not null)
        {
            var graphs = KVObject.Array(2);
            graphs.Add(CreateGraphRef(string.Empty, graph));
            graphs.Add(CreateGraphRef("ui", uiGraph));
            root["m_animGraph2Refs"] = graphs;
        }

        if (skeleton is not null)
        {
            var skeletons = KVObject.Array(1);
            skeletons.Add(new KVObject(skeleton) { Flag = KVFlag.Resource });
            root["m_vecNmSkeletonRefs"] = skeletons;
        }

        using var resource = new Resource(ResourceType.Model);
        resource.Blocks.Add(new WritableBinaryKv3(root) { Resource = resource });
        using var output = new MemoryStream();
        resource.Serialize(output);
        return output.ToArray();
    }

    private static KVObject CreateGraphRef(string identifier, string path)
    {
        var result = KVObject.Collection(2);
        result["m_sIdentifier"] = new KVObject(identifier);
        result["m_hGraph"] = new KVObject(path) { Flag = KVFlag.Resource };
        return result;
    }

    private static void WriteVpk(string path, byte[] modelBytes, byte[] materialBytes)
    {
        using var package = new Package { Version = 2 };
        package.AddFile(ResourcePath, modelBytes);
        package.AddFile(MaterialPath, materialBytes);
        package.Write(path);
    }

    private static byte[] ReadEntry(string vpkPath, string resourcePath)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var match = package.Entries!
            .SelectMany(group => group.Value)
            .Single(entry => string.Equals(
                entry.GetFullPath().Replace('\\', '/'),
                resourcePath,
                StringComparison.Ordinal));
        package.ReadEntry(match, out byte[] bytes);
        return bytes;
    }

    private sealed class WritableBinaryKv3 : BinaryKV3
    {
        public WritableBinaryKv3(KVObject root)
        {
            Data = root.ToKV3Document();
        }
    }
}
