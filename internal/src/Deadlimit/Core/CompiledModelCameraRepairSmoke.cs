using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal static class CompiledModelCameraRepairSmoke
{
    private const string ModelPath = "models/heroes_wip/camera_smoke/camera_smoke.vmdl_c";

    public static int Run()
    {
        var retail = Fixture(old: false);
        var old = Fixture(old: true, helper: false);
        var result = CompiledModelCameraRepair.Repair(old, retail, ModelPath);
        Require(result.Modified && result.MeshBlocks == 3 && result.RemappedBones.Count == 1, "Old frame and lobby adaptation");
        var again = CompiledModelCameraRepair.Repair(result.Bytes, retail, ModelPath);
        Require(!again.Modified && ReferenceEquals(again.Bytes, result.Bytes), "Idempotent camera repair");
        using (var input = CompiledModelIntegrity.Read(old, ModelPath))
        using (var output = CompiledModelIntegrity.Read(result.Bytes, ModelPath))
        {
            Require(((Model)input.DataBlock!).Data["m_name"].ToString() == ((Model)output.DataBlock!).Data["m_name"].ToString(), "Model marker preserved");
            var before = input.Blocks.OfType<Mesh>().ToArray();
            var after = output.Blocks.OfType<Mesh>().ToArray();
            for (var i = 0; i < before.Length; i++)
                Require(CompiledModelIntegrity.Fingerprint(before[i].Data["geometry_marker"])
                    == CompiledModelIntegrity.Fingerprint(after[i].Data["geometry_marker"]), "LOD geometry preserved");
        }
        Require(CompiledModelCameraRepair.Repair(Fixture(old: false), retail, ModelPath).Status == CameraRepairStatus.Current, "Current camera no-op");
        Require(CompiledModelCameraRepair.Repair(Fixture(old: false, customOffset: 4), retail, ModelPath).Status
            == CameraRepairStatus.ReviewNeeded, "Unrecognized author camera preserved");
        Require(CompiledModelCameraRepair.Repair(Fixture(old: true, helper: false, rootScale: 2), retail, ModelPath).Status
            == CameraRepairStatus.Unsupported, "Unsafe lobby bone adaptation refused");
        Require(CompiledModelCameraRepair.Repair(Fixture(old: false, staleLastLod: true), retail, ModelPath).Modified,
            "Stale attachment detected outside first LOD");
        Require(CompiledModelCameraRepair.Repair(Fixture(old: true, missingPivots: true), retail, ModelPath).Modified,
            "Missing gameplay pivots restored for recognized hero metadata");
        RunImportedFixture(retail, old);
        return 0;
    }

    private static void RunImportedFixture(byte[] retail, byte[] old)
    {
        var root = Path.Combine(Path.GetTempPath(), "deadlimit-camera-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var retailRoot = Path.Combine(root, "retail");
            var citadel = Path.Combine(retailRoot, "game", "citadel");
            Directory.CreateDirectory(Path.Combine(citadel, "addons"));
            WriteVpk(Path.Combine(citadel, "pak01_dir.vpk"), retail);
            var source = Path.Combine(citadel, "addons", "pak42_dir.vpk");
            WriteVpk(source, old);
            var manifest = Import(source, Path.Combine(root, "project"), ModelPath);
            var paths = new DeadlimitPaths(new ToolPathSettings { RetailDeadlockRoot = retailRoot });
            _ = new VpkSlotOwnershipService(paths).AdoptImportedSource(manifest);
            var route = new Deadlimit.App.BuildAndTestService(paths);
            var first = route.BuildAsync(manifest).GetAwaiter().GetResult();
            Require(!first.Ag2Applied, "Camera-only repair keeps AG2 result accurate");
            var report = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), ImportedVpkAnimationBindingRepairService.ReportFileName);
            var reportBytes = File.ReadAllBytes(report);
            var deployed = ReadEntry(source, ModelPath);
            Require(!CompiledModelCameraRepair.Repair(deployed, retail, ModelPath).Modified, "Imported BUILD & TEST repaired camera");
            _ = route.BuildAsync(manifest).GetAwaiter().GetResult();
            Require(File.ReadAllBytes(report).SequenceEqual(reportBytes) && ReadEntry(source, ModelPath).SequenceEqual(deployed),
                "Repeat BUILD & TEST preserves model and provenance");
            // A subsequent game update repairs an already repaired payload. The
            // combined report must still start at the original imported hash.
            using (var updated = CompiledModelIntegrity.Read(retail, ModelPath))
            {
                ((Model)updated.DataBlock!).Data["m_vecNmSkeletonRefs"] = KVObject.Array([
                    new KVObject("models/heroes_wip/camera_smoke/current.vnmskel") { Flag = KVFlag.Resource }]);
                var bytes = CompiledModelIntegrity.Serialize(updated, retail,
                    new HashSet<int> { updated.Blocks.IndexOf(updated.DataBlock!) });
                WriteVpk(Path.Combine(citadel, "pak01_dir.vpk"), bytes);
            }
            Require(route.BuildAsync(manifest).GetAwaiter().GetResult().Ag2Applied, "Later AG2 repair extends camera provenance");
            var extended = new ImportedVpkRepackService().RebuildAndVerify(manifest);
            Require(extended.ChangedEntryCount == 1, "Chained repairs retain original-to-final provenance");
            var payload = Path.Combine(ImportedVpkPayloadService.ResolveCompiledFolder(manifest.ProjectFolder), ModelPath.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(payload, "tampered");
            var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            try { _ = route.BuildAsync(manifest).GetAwaiter().GetResult(); throw new Exception("Tampered input was accepted"); }
            catch (InvalidDataException) { }
            Require(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash), "Tampering blocks deployment");
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // Runs against actual installed Ivy/Dynamo mods, but only writes isolated test outputs.
    public static string RunLive(string retailRoot)
    {
        var outputRoot = Path.Combine(Path.GetTempPath(), "deadlimit-camera-live-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(outputRoot);
        var results = new List<object>();
        foreach (var (hero, slot) in new[] { ("ivy", "01"), ("dynamo", "34") })
        {
            var source = Path.Combine(retailRoot, "game", "citadel", "addons", $"pak{slot}_dir.vpk");
            var sourceHash = SHA256.HashData(File.ReadAllBytes(source));
            var modelPath = $"models/heroes_wip/{hero}/{hero}.vmdl_c";
            var retail = ReadEntry(Path.Combine(retailRoot, "game", "citadel", "pak01_dir.vpk"), modelPath);
            var input = ReadEntry(source, modelPath);
            var repaired = CompiledModelCameraRepair.Repair(input, retail, modelPath);
            Require(repaired.Status is CameraRepairStatus.Repaired or CameraRepairStatus.Current,
                $"{hero}: recognized camera state: {repaired.Status}");
            Require(!CompiledModelCameraRepair.Repair(repaired.Bytes, retail, modelPath).Modified, $"{hero}: repeat repair no-op");
            var manifest = Import(source, Path.Combine(outputRoot, hero, "imported"), modelPath);
            var paths = new DeadlimitPaths(new ToolPathSettings { RetailDeadlockRoot = retailRoot });
            var service = new ImportedVpkAnimationBindingRepairService(paths);
            var first = service.Repair(manifest);
            Require(repaired.Modified ? first.CameraModifiedTargetCount > 0 : first.CameraModifiedTargetCount == 0,
                $"{hero}: imported camera repair agrees with input state");
            var repacked = new ImportedVpkRepackService().RebuildAndVerify(manifest);
            var reportBefore = File.Exists(first.ReportPath) ? File.ReadAllBytes(first.ReportPath) : null;
            Require(service.Repair(manifest).ModifiedTargetCount == 0
                    && (reportBefore is null ? !File.Exists(first.ReportPath)
                        : File.ReadAllBytes(first.ReportPath).SequenceEqual(reportBefore)),
                $"{hero}: combined AG2/camera provenance idempotent");
            _ = new ImportedVpkRepackService().RebuildAndVerify(manifest);
            var compiledRoot = Path.Combine(outputRoot, hero, "compiled");
            var compiledFile = Path.Combine(compiledRoot, modelPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(compiledFile)!);
            var binding = CompiledModelAnimationBindingRepair.Repair(input, retail, modelPath);
            File.WriteAllBytes(compiledFile, binding.Bytes);
            var log = new List<string>();
            var warnings = CompiledModelCameraRepairService.RepairBuildOutputs(compiledRoot, retailRoot, log.Add, default);
            Require(warnings.Count == 0, $"{hero}: authoring camera step warnings: {string.Join(", ", warnings)}");
            Require(File.ReadAllBytes(compiledFile).SequenceEqual(ReadEntry(repacked.OutputVpkPath, modelPath)),
                $"{hero}: authoring and imported model outputs agree");
            Require(SHA256.HashData(File.ReadAllBytes(source)).SequenceEqual(sourceHash), $"{hero}: installed source untouched");
            results.Add(new { hero, CameraStatus = repaired.Status.ToString(), repaired.MeshBlocks,
                repaired.RemappedBones, repacked.EntryCount, repacked.ChangedEntryCount,
                SourceVpkBytes = new FileInfo(source).Length, OutputVpkBytes = new FileInfo(repacked.OutputVpkPath).Length,
                SourceModelBytes = input.Length, OutputModelBytes = new FileInfo(compiledFile).Length,
                Vpk = repacked.OutputVpkPath, Log = log, first.ReportPath });
        }
        var json = JsonSerializer.Serialize(new { outputRoot, results, gameVerification = "pending" }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(outputRoot, "results.json"), json);
        return json;
    }

    private static ProjectManifest Import(string source, string folder, string modelPath)
    {
        Directory.CreateDirectory(folder);
        var candidate = VpkImportSourceValidator.Validate(source);
        var manifest = new ProjectManifest
        {
            Mode = ProjectMode.ImportedVpk, ProjectId = AddonIdentityService.CreateProjectId(),
            ProjectName = "Camera smoke", ProjectFolder = folder, ReleaseTarget = candidate.ReleaseTarget ?? "42",
            RetailMainModel = modelPath, ImportedVpk = new ImportedVpkMetadata
            {
                SourceVpkPath = source, SourceVpkFileName = candidate.SourceVpkFileName,
                SourceReleaseTarget = candidate.ReleaseTarget,
                OriginalVpkSha256 = candidate.SourceVpkSha256, SourceEntryCount = candidate.EntryCount,
                PrimaryModelResources = [modelPath],
            },
        };
        _ = ImportedVpkPayloadService.Extract(manifest, candidate);
        return manifest;
    }

    private static void WriteVpk(string path, byte[] model)
    {
        using var package = new Package { Version = 2 };
        package.AddFile(ModelPath, model);
        package.AddFile("materials/untouched.vmat_c", new byte[] { 3, 5, 8, 13 });
        package.Write(path);
    }

    private static byte[] ReadEntry(string path, string name)
    {
        using var package = new Package();
        package.Read(path);
        var entry = package.Entries!.SelectMany(group => group.Value).Single(entry => entry.GetFullPath().Replace('\\', '/') == name);
        package.ReadEntry(entry, out byte[] bytes);
        return bytes;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException("Camera smoke failed: " + message);
    }

    private static byte[] Fixture(bool old, bool helper = true, float rootScale = 1, float customOffset = 0,
        bool staleLastLod = false, bool missingPivots = false)
    {
        var metadata = KVObject.Collection();
        metadata["CCitadelHeroModelGameData_t"] = KVObject.Collection();
        metadata["untouched_game_metadata"] = new KVObject("preserve this");
        metadata["AttachmentCameraData"] = KVObject.Array();
        var camera = KVObject.Collection(); camera["attachment_name"] = new KVObject("portrait_camera_attach");
        metadata["AttachmentCameraData"].Add(camera);
        metadata["CitadelModelSceneData_t"] = KVObject.Collection();
        var data = KVObject.Collection();
        data["m_name"] = new KVObject("geometry/material/skeleton marker");
        var info = KVObject.Collection(); info["m_keyValueText"] = new KVObject(metadata.ToKV3String()); data["m_modelInfo"] = info;
        var skeleton = KVObject.Collection();
        skeleton["m_boneName"] = KVObject.Array(helper ? [new KVObject("root_motion"), new KVObject("attachWorld")] : [new KVObject("root_motion")]);
        skeleton["m_nParent"] = KVObject.Array(helper ? [new KVObject(-1), new KVObject(0)] : [new KVObject(-1)]);
        skeleton["m_nFlag"] = KVObject.Array(helper ? [new KVObject(0), new KVObject(0)] : [new KVObject(0)]);
        skeleton["m_bonePosParent"] = KVObject.Array(helper ? [Vector(0, 0, 0), Vector(0, 0, 0)] : [Vector(0, 0, 0)]);
        skeleton["m_boneRotParent"] = KVObject.Array(helper ? [Vector(0, 0, 0, 1), Vector(0, 0, 0, 1)] : [Vector(0, 0, 0, 1)]);
        skeleton["m_boneScaleParent"] = KVObject.Array(helper ? [new KVObject(rootScale), new KVObject(1f)] : [new KVObject(rootScale)]);
        data["m_modelSkeleton"] = skeleton;
        using var resource = new Resource(ResourceType.Model);
        resource.Blocks.Add(new WritableKv3(data, BlockType.DATA) { Resource = resource });
        for (var lod = 0; lod < 3; lod++)
        {
            var stale = old || (staleLastLod && lod == 2);
            var mesh = KVObject.Collection();
            var attachments = KVObject.Array();
            if (!missingPivots)
            {
                attachments.Add(Attachment("standing_pivot", "root_motion", stale, 94, customOffset));
                attachments.Add(Attachment("crouching_pivot", "root_motion", stale, 54, customOffset));
            }
            if (helper) attachments.Add(Attachment("portrait_camera_attach", "attachWorld", false, 0, 0));
            attachments.Add(Attachment("artist_attachment", "root_motion", false, 1, 0));
            mesh["m_attachments"] = attachments;
            mesh["geometry_marker"] = Vector(lod, 2, 3, 4);
            resource.Blocks.Add(new WritableKv3(mesh, BlockType.MDAT) { Resource = resource });
        }
        using var output = new MemoryStream(); resource.Serialize(output); return output.ToArray();
    }

    private static KVObject Vector(params float[] values) => KVObject.Array(values.Select(value => new KVObject(value)));
    private static KVObject Attachment(string name, string bone, bool old, float height, float customOffset)
    {
        var data = KVObject.Collection();
        data["m_name"] = new KVObject(name); data["m_nInfluences"] = new KVObject(1);
        data["m_bIgnoreRotation"] = new KVObject(false);
        data["m_influenceNames"] = KVObject.Array([new KVObject(bone)]);
        data["m_influenceWeights"] = KVObject.Array([new KVObject(1.0)]);
        data["m_bInfluenceRootTransform"] = KVObject.Array([new KVObject(false)]);
        data["m_vInfluenceOffsets"] = KVObject.Array([old ? Vector(0, height, 0) : Vector(customOffset, 0, height)]);
        data["m_vInfluenceRotations"] = KVObject.Array([old ? Vector(-0.5f, -0.5f, -0.5f, 0.5f) : Vector(0, 0, 0, 1)]);
        var entry = KVObject.Collection(); entry["key"] = new KVObject(name); entry["value"] = data; return entry;
    }

    private sealed class WritableKv3 : BinaryKV3
    {
        public WritableKv3(KVObject data, BlockType type) : base(type) { Data = data.ToKV3Document(); }
    }
}
