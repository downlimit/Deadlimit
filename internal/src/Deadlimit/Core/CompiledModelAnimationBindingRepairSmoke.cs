using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal static class CompiledModelAnimationBindingRepairSmoke
{
    private const string ResourcePath = "models/heroes/ivy/ivy.vmdl_c";
    private const string RetailGraph = "animgraphs/animgraph2/hero/hero.vnmgraph+ivy.vnmgraph";
    private const string RetailUiGraph = "animgraphs/animgraph2/hero/hero_ui.vnmgraph+ivy.vnmgraph";
    private const string RetailSkeleton = "models/heroes_wip/ivy/ivy.vnmskel";

    public static int Run()
    {
        var retail = CreateCompiledModel(RetailGraph, RetailUiGraph, RetailSkeleton);
        var missing = CreateCompiledModel(null, null, null);
        var stale = CreateCompiledModel(
            "animgraphs/animgraph2/old/old.vnmgraph",
            "animgraphs/animgraph2/old/old_ui.vnmgraph",
            "models/heroes_wip/ivy/old.vnmskel");
        var current = CreateCompiledModel(RetailGraph, RetailUiGraph, RetailSkeleton);

        var missingRepair = CompiledModelAnimationBindingRepair.Repair(missing, retail, ResourcePath);
        if (!missingRepair.Modified
            || !CompiledModelAnimationBindingRepair.SnapshotsEqual(missingRepair.After, missingRepair.Retail)
            || !string.Equals(ReadModelName(missingRepair.Bytes), "unchanged-marker", StringComparison.Ordinal))
        {
            return 1;
        }

        var staleRepair = CompiledModelAnimationBindingRepair.Repair(stale, retail, ResourcePath);
        if (!staleRepair.Modified
            || !CompiledModelAnimationBindingRepair.SnapshotsEqual(staleRepair.After, staleRepair.Retail)
            || !string.Equals(ReadModelName(staleRepair.Bytes), "unchanged-marker", StringComparison.Ordinal))
        {
            return 2;
        }

        var currentRepair = CompiledModelAnimationBindingRepair.Repair(current, retail, ResourcePath);
        if (currentRepair.Modified
            || !currentRepair.Bytes.AsSpan().SequenceEqual(current)
            || !ReferenceEquals(currentRepair.Bytes, current))
        {
            return 3;
        }

        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-binding-repair-smoke-{Guid.NewGuid():N}");
        try
        {
            var retailRoot = Path.Combine(root, "retail");
            var retailCitadel = Path.Combine(retailRoot, "game", "citadel");
            var addonsRoot = Path.Combine(retailCitadel, "addons");
            var projectFolder = Path.Combine(root, "project");
            Directory.CreateDirectory(addonsRoot);
            Directory.CreateDirectory(projectFolder);

            var retailVpk = Path.Combine(retailCitadel, "pak01_dir.vpk");
            WriteVpk(retailVpk, retail);
            var addonVpk = Path.Combine(addonsRoot, "pak42_dir.vpk");
            WriteVpk(addonVpk, stale);

            var candidate = VpkImportSourceValidator.Validate(addonVpk);
            var manifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.ImportedVpk,
                ProjectId = AddonIdentityService.CreateProjectId(),
                ProjectName = "Repair",
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

            var paths = new DeadlimitPaths(new ToolPathSettings
            {
                RetailDeadlockRoot = retailRoot,
                CsdkRoot = Path.Combine(root, "csdk"),
                DeadlockToolsRoot = Path.Combine(root, "tools"),
            });
            var service = new ImportedVpkAnimationBindingRepairService(paths);
            var first = service.Repair(manifest);
            if (first.EligibleTargetCount != 1
                || first.ModifiedTargetCount != 1
                || !File.Exists(first.ReportPath))
            {
                return 4;
            }

            var payloadPath = SafePath.ResolveUnderRoot(
                Path.Combine(projectFolder, ImportedVpkPayloadService.PayloadFolderName),
                ResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Smoke repaired payload model");
            var repairedPayload = File.ReadAllBytes(payloadPath);
            var repairedSnapshot = CompiledModelAnimationBindingRepair.ReadSnapshot(repairedPayload, ResourcePath);
            var retailSnapshot = CompiledModelAnimationBindingRepair.ReadSnapshot(retail, ResourcePath);
            if (!CompiledModelAnimationBindingRepair.SnapshotsEqual(repairedSnapshot, retailSnapshot)
                || !string.Equals(ReadModelName(repairedPayload), "unchanged-marker", StringComparison.Ordinal))
            {
                return 5;
            }

            var beforeSecond = File.ReadAllBytes(payloadPath);
            var second = service.Repair(manifest);
            var afterSecond = File.ReadAllBytes(payloadPath);
            if (second.EligibleTargetCount != 0
                || second.ModifiedTargetCount != 0
                || !beforeSecond.AsSpan().SequenceEqual(afterSecond))
            {
                return 6;
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
                // Temp cleanup is not part of the repair assertions.
            }
        }
    }

    private static byte[] CreateCompiledModel(
        string? graph,
        string? uiGraph,
        string? skeleton)
    {
        var root = KVObject.Collection();
        root["m_name"] = new KVObject("unchanged-marker");

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
            var skeletonValue = new KVObject(skeleton)
            {
                Flag = KVFlag.Resource,
            };
            skeletons.Add(skeletonValue);
            root["m_vecNmSkeletonRefs"] = skeletons;
        }

        using var resource = new Resource(ResourceType.Model);
        var block = new WritableBinaryKv3(root)
        {
            Resource = resource,
        };
        resource.Blocks.Add(block);

        using var output = new MemoryStream();
        resource.Serialize(output);
        return output.ToArray();
    }

    private static KVObject CreateGraphRef(string identifier, string path)
    {
        var result = KVObject.Collection(2);
        result["m_sIdentifier"] = new KVObject(identifier);
        result["m_hGraph"] = new KVObject(path)
        {
            Flag = KVFlag.Resource,
        };
        return result;
    }

    private static string ReadModelName(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var resource = new Resource { FileName = ResourcePath };
        resource.Read(stream, verifyFileSize: true, leaveOpen: true);
        if (resource.DataBlock is not Model model)
        {
            throw new InvalidDataException("Smoke compiled model did not deserialize as Model.");
        }
        return model.Data.GetStringProperty("m_name", string.Empty);
    }

    private static void WriteVpk(string path, byte[] model)
    {
        using var package = new Package { Version = 2 };
        package.AddFile(ResourcePath, model);
        package.AddFile("materials/models/heroes/ivy/ivy.vmat_c", [1, 2, 3]);
        package.Write(path);
    }

    private sealed class WritableBinaryKv3 : BinaryKV3
    {
        public WritableBinaryKv3(KVObject root)
        {
            Data = root.ToKV3Document();
        }
    }
}
