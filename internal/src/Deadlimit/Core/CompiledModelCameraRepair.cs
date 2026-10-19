using System.Numerics;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.ModelData.Attachments;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

internal enum CameraRepairStatus { Current, Repaired, NotApplicable, ReviewNeeded, Unsupported }

internal sealed record CameraRepairResult(
    byte[] Bytes, CameraRepairStatus Status, int MeshBlocks, IReadOnlyList<string> RemappedBones, string Detail)
{
    public bool Modified => Status == CameraRepairStatus.Repaired;
}

/// <summary>Repairs the recognized pre-Violet attachment frame using the installed game's model.</summary>
internal static class CompiledModelCameraRepair
{
    private static readonly string[] MetadataSections = ["AttachmentCameraData", "CitadelModelSceneData_t"];
    private static readonly string[] Pivots = ["standing_pivot", "crouching_pivot"];

    public static CameraRepairResult Repair(byte[] bytes, byte[] retailBytes, string path)
    {
        using var source = CompiledModelIntegrity.Read(bytes, path);
        using var retail = CompiledModelIntegrity.Read(retailBytes, path);
        var model = (Model)source.DataBlock!;
        var original = (Model)retail.DataBlock!;
        var reference = Entries(retail).GroupBy(entry => (string)entry["key"], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        CameraRepairResult Skip(CameraRepairStatus status, string detail) => new(bytes, status, 0, [], detail);
        if (!Pivots.All(reference.ContainsKey))
        {
            return Skip(CameraRepairStatus.NotApplicable, "Retail counterpart has no supported hero camera pivots.");
        }

        // The reference is authoritative only when all its LODs agree.
        foreach (var entry in Entries(retail))
        {
            if (CompiledModelIntegrity.Fingerprint(entry) != CompiledModelIntegrity.Fingerprint(reference[(string)entry["key"]]))
            {
                return Skip(CameraRepairStatus.Unsupported, "Retail LODs contain conflicting attachment definitions.");
            }
        }
        var meshes = source.Blocks.OfType<Mesh>().Where(mesh => mesh.Data.ContainsKey("m_attachments")).ToArray();
        if (meshes.Length == 0) return Skip(CameraRepairStatus.Unsupported, "Model has no embedded attachment blocks.");
        var required = new HashSet<string>(Pivots, StringComparer.Ordinal);
        if (reference.ContainsKey("portrait_camera")) required.Add("portrait_camera");
        if (original.KeyValues.TryGetValue("AttachmentCameraData", out var cameras))
        {
            foreach (var camera in cameras.Values) required.Add((string)camera["attachment_name"]);
        }
        if (required.Any(name => !reference.ContainsKey(name)))
        {
            return Skip(CameraRepairStatus.Unsupported, "A camera referenced by retail metadata has no embedded attachment.");
        }
        var bones = model.Skeleton.Bones.Select(bone => bone.Name).ToHashSet(StringComparer.Ordinal);
        var plans = new Dictionary<string, KVObject>(StringComparer.Ordinal);
        var remapped = new List<string>();
        foreach (var pair in reference)
        {
            var value = pair.Value["value"];
            var influences = new Attachment(pair.Value);
            if (!required.Contains(pair.Key) && !influences.Any(influence => influence.Weight > 0 && influence.Name == "root_motion")) continue;
            var planned = pair.Value;
            if (!bones.Contains("attachWorld") && CanUseRootForLobby(model, original, pair.Key, value))
            {
                planned = KVObject.Collection(pair.Value.Children);
                var replacement = KVObject.Collection(value.Children);
                replacement["m_influenceNames"] = KVObject.Array(value["m_influenceNames"].Values
                    .Select((name, index) => index == 0 ? new KVObject("root_motion") : name));
                planned["value"] = replacement;
                remapped.Add(pair.Key + ":attachWorld->root_motion");
                influences = new Attachment(planned);
            }
            var missing = influences.Where(influence => influence.Weight > 0 && !string.IsNullOrEmpty(influence.Name)
                && !bones.Contains(influence.Name)).Select(influence => influence.Name).Distinct().ToArray();
            if (missing.Length > 0)
            {
                if (required.Contains(pair.Key)) return Skip(CameraRepairStatus.Unsupported,
                    $"Required camera '{pair.Key}' references missing bones: {string.Join(", ", missing)}.");
                continue;
            }
            plans.Add(pair.Key, planned);
        }

        var knownOldFrame = false;
        var missingPivots = false;
        var rootDifference = false;
        var anyAttachmentDifference = false;
        foreach (var mesh in meshes)
        {
            var ours = mesh.Data["m_attachments"].Values.ToDictionary(entry => (string)entry["key"], StringComparer.Ordinal);
            knownOldFrame |= Pivots.All(name => ours.TryGetValue(name, out var entry) && OldFrame(entry, reference[name]));
            missingPivots |= Pivots.All(name => !ours.ContainsKey(name)) && model.KeyValues.ContainsKey("CCitadelHeroModelGameData_t");
            foreach (var pair in plans)
            {
                if (ours.TryGetValue(pair.Key, out var entry) && Equal(entry["value"], pair.Value["value"])) continue;
                anyAttachmentDifference = true;
                if (new Attachment(pair.Value).Any(influence => influence.Weight > 0 && influence.Name == "root_motion")
                    && !pair.Key.StartsWith("portrait_camera", StringComparison.Ordinal)) rootDifference = true;
            }
        }
        var metadata = KVObject.Collection(model.KeyValues.Children);
        var metadataChanged = false;
        foreach (var section in MetadataSections)
        {
            if (!original.KeyValues.TryGetValue(section, out var expected)) continue;
            if (!metadata.TryGetValue(section, out var actual) || !Equal(actual, expected)) metadataChanged = true;
            metadata[section] = expected;
        }
        if (!anyAttachmentDifference && !metadataChanged) return Skip(CameraRepairStatus.Current, "Camera data already matches current retail.");
        if (!knownOldFrame && !missingPivots && rootDifference)
        {
            return Skip(CameraRepairStatus.ReviewNeeded, "Attachment differences do not match the known old camera frame; author data preserved.");
        }

        var preservedModel = CompiledModelIntegrity.Fingerprint(model.Data, OmitMetadataText);
        var preservedMetadata = CompiledModelIntegrity.Fingerprint(model.KeyValues, OmitCameraMetadata);
        var preservedMeshes = meshes.Select(mesh => PreservedMesh(mesh, plans.Keys)).ToArray();
        var edited = new HashSet<int>();
        for (var i = 0; i < source.Blocks.Count; i++)
        {
            if (source.Blocks[i] is not Mesh mesh || !meshes.Contains(mesh)) continue;
            var array = mesh.Data["m_attachments"];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in array.Values)
            {
                var name = (string)entry["key"];
                if (!plans.TryGetValue(name, out var replacement)) continue;
                entry["value"] = replacement["value"];
                seen.Add(name);
            }
            foreach (var pair in plans.Where(pair => !seen.Contains(pair.Key))) array.Add(pair.Value);
            edited.Add(i);
        }
        if (metadataChanged)
        {
            if (!model.Data.TryGetValue("m_modelInfo", out var info))
            {
                return Skip(CameraRepairStatus.Unsupported, "Model has no metadata container for camera settings.");
            }
            info["m_keyValueText"] = new KVObject(metadata.ToKV3String());
            edited.Add(source.Blocks.IndexOf(source.DataBlock!));
        }
        var result = CompiledModelIntegrity.Serialize(source, bytes, edited);
        using var reopened = CompiledModelIntegrity.Read(result, path);
        var checkedModel = (Model)reopened.DataBlock!;
        CompiledModelIntegrity.VerifyUneditedBlocks(source, bytes, reopened, result, edited);
        if (preservedModel != CompiledModelIntegrity.Fingerprint(checkedModel.Data, OmitMetadataText)
            || preservedMetadata != CompiledModelIntegrity.Fingerprint(checkedModel.KeyValues, OmitCameraMetadata))
        {
            throw new InvalidDataException("Camera repair changed unrelated model or game metadata.");
        }
        var checkedMeshes = reopened.Blocks.OfType<Mesh>().Where(mesh => mesh.Data.ContainsKey("m_attachments")).ToArray();
        if (checkedMeshes.Length != meshes.Length) throw new InvalidDataException("Camera repair changed the mesh count.");
        for (var i = 0; i < meshes.Length; i++)
        {
            if (preservedMeshes[i] != PreservedMesh(checkedMeshes[i], plans.Keys)
                || !Equal(meshes[i].Data, checkedMeshes[i].Data))
            {
                throw new InvalidDataException($"Camera repair failed mesh/geometry/material preservation check at LOD block {i}.");
            }
        }
        foreach (var section in MetadataSections)
        {
            if (original.KeyValues.TryGetValue(section, out var expected)
                && (!checkedModel.KeyValues.TryGetValue(section, out var actual) || !Equal(actual, expected)))
                throw new InvalidDataException($"Camera metadata did not survive serialization: {section}.");
        }
        return new(result, CameraRepairStatus.Repaired, meshes.Length, remapped,
            $"Camera/aim attachments verified in {meshes.Length} mesh blocks; unrelated model data and resource blocks preserved.");
    }

    private static bool OmitMetadataText(string path) => path == "m_modelInfo/m_keyValueText";
    private static bool OmitCameraMetadata(string path) => MetadataSections.Contains(path, StringComparer.Ordinal);
    private static bool Equal(KVObject a, KVObject b) => CompiledModelIntegrity.Fingerprint(a) == CompiledModelIntegrity.Fingerprint(b);
    private static IEnumerable<KVObject> Entries(Resource resource) => resource.Blocks.OfType<Mesh>()
        .Where(mesh => mesh.Data.ContainsKey("m_attachments")).SelectMany(mesh => mesh.Data["m_attachments"].Values);

    private static string PreservedMesh(Mesh mesh, IEnumerable<string> changedNames)
    {
        var names = changedNames.ToHashSet(StringComparer.Ordinal);
        var preserved = KVObject.Collection(mesh.Data.Children);
        preserved["m_attachments"] = KVObject.Array(mesh.Data["m_attachments"].Values.Where(entry => !names.Contains((string)entry["key"])));
        return CompiledModelIntegrity.Fingerprint(preserved);
    }

    private static bool OldFrame(KVObject ours, KVObject retail)
    {
        var a = new Attachment(ours);
        var b = new Attachment(retail);
        if (a.Length != 1 || b.Length != 1 || a[0].Name != "root_motion" || b[0].Name != "root_motion") return false;
        var left = a[0];
        var right = b[0];
        var dot = Math.Abs(Quaternion.Dot(Quaternion.Normalize(left.Rotation), Quaternion.Normalize(right.Rotation)));
        return float.IsFinite(dot) && dot < 0.95f && Math.Abs(right.Offset.Z) > 20 && Math.Abs(left.Offset.Y) > 20
            && Math.Abs(left.Offset.Z) < 5 && Math.Abs(Math.Abs(left.Offset.Y) - Math.Abs(right.Offset.Z)) < 10;
    }

    private static bool CanUseRootForLobby(Model model, Model retail, string name, KVObject value)
    {
        const string prefix = "portrait_camera_attach";
        if (!name.StartsWith(prefix, StringComparison.Ordinal) || !name[prefix.Length..].All(char.IsAsciiDigit)
            || (int)value["m_nInfluences"] != 1 || (bool)value["m_bIgnoreRotation"]
            || (string)value["m_influenceNames"][0] != "attachWorld"
            || Math.Abs((double)value["m_influenceWeights"][0] - 1) > 0.00001
            || (bool)value["m_bInfluenceRootTransform"][0]) return false;
        var helper = retail.Skeleton.Bones.FirstOrDefault(bone => bone.Name == "attachWorld");
        var root = retail.Skeleton.Bones.FirstOrDefault(bone => bone.Name == "root_motion");
        var target = model.Skeleton.Bones.FirstOrDefault(bone => bone.Name == "root_motion");
        if (helper is null || root is null || target is null || helper.Parent != root || root.Parent is not null || target.Parent is not null) return false;
        return UnitScale(retail, helper.Index) && UnitScale(retail, root.Index) && UnitScale(model, target.Index)
            && helper.Position.Length() < 0.0001f && Math.Abs(Quaternion.Dot(helper.Angle, Quaternion.Identity)) > 0.99999f
            && Vector3.Distance(root.Position, target.Position) < 0.0001f && Math.Abs(Quaternion.Dot(root.Angle, target.Angle)) > 0.99999f;
    }

    private static bool UnitScale(Model model, int index) =>
        Math.Abs((double)model.Data["m_modelSkeleton"]["m_boneScaleParent"][index] - 1) < 0.00001;
}
