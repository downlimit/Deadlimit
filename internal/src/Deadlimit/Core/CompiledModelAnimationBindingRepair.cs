using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

public sealed record CompiledModelAnimationBindingRepairResult(
    bool Modified,
    byte[] Bytes,
    ModelAnimationBindingSnapshot Before,
    ModelAnimationBindingSnapshot Retail,
    ModelAnimationBindingSnapshot After);

internal static class CompiledModelAnimationBindingRepair
{
    private const string AnimGraph2Field = "m_animGraph2Refs";
    private const string NmSkeletonField = "m_vecNmSkeletonRefs";

    public static ModelAnimationBindingSnapshot ReadSnapshot(byte[] bytes, string resourcePath)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        using var stream = new MemoryStream(bytes, writable: false);
        using var resource = ReadModelResource(stream, resourcePath);
        return ReadSnapshot(GetModel(resource).Data);
    }

    public static CompiledModelAnimationBindingRepairResult Repair(
        byte[] importedBytes,
        byte[] retailBytes,
        string resourcePath)
    {
        ArgumentNullException.ThrowIfNull(importedBytes);
        ArgumentNullException.ThrowIfNull(retailBytes);

        using var importedStream = new MemoryStream(importedBytes, writable: false);
        using var retailStream = new MemoryStream(retailBytes, writable: false);
        using var importedResource = ReadModelResource(importedStream, resourcePath);
        using var retailResource = ReadModelResource(retailStream, resourcePath);

        var importedData = GetModel(importedResource).Data;
        var retailData = GetModel(retailResource).Data;
        var before = ReadSnapshot(importedData);
        var retail = ReadSnapshot(retailData);

        if (SnapshotsEqual(before, retail))
        {
            return new CompiledModelAnimationBindingRepairResult(
                Modified: false,
                importedBytes,
                before,
                retail,
                before);
        }

        CopyAuthoritativeField(retailData, importedData, AnimGraph2Field);
        CopyAuthoritativeField(retailData, importedData, NmSkeletonField);

        var inMemoryAfter = ReadSnapshot(importedData);
        if (!SnapshotsEqual(inMemoryAfter, retail))
        {
            throw new InvalidOperationException(
                "Compiled model binding mutation did not reproduce the current retail AG2/NmSkeleton values in memory.");
        }

        using var output = new MemoryStream(capacity: Math.Max(importedBytes.Length, 4096));
        importedResource.Serialize(output);
        var repairedBytes = output.ToArray();
        if (repairedBytes.Length == 0)
        {
            throw new InvalidDataException("ValveResourceFormat serialized an empty compiled model.");
        }

        var serializedAfter = ReadSnapshot(repairedBytes, resourcePath);
        if (!SnapshotsEqual(serializedAfter, retail))
        {
            throw new InvalidDataException(
                "The serialized compiled model did not preserve the authoritative current-retail AG2/NmSkeleton bindings.");
        }

        return new CompiledModelAnimationBindingRepairResult(
            Modified: true,
            repairedBytes,
            before,
            retail,
            serializedAfter);
    }

    public static bool SnapshotsEqual(
        ModelAnimationBindingSnapshot left,
        ModelAnimationBindingSnapshot right) =>
        left.HasAnimGraph2Field == right.HasAnimGraph2Field
        && left.HasNmSkeletonField == right.HasNmSkeletonField
        && left.AnimGraph2Refs.SequenceEqual(right.AnimGraph2Refs, StringComparer.OrdinalIgnoreCase)
        && left.NmSkeletonRefs.SequenceEqual(right.NmSkeletonRefs, StringComparer.OrdinalIgnoreCase);

    private static Resource ReadModelResource(Stream stream, string resourcePath)
    {
        var resource = new Resource { FileName = resourcePath };
        try
        {
            resource.Read(stream, verifyFileSize: true, leaveOpen: true);
            if (resource.ResourceType != ResourceType.Model || resource.DataBlock is not Model)
            {
                throw new InvalidDataException($"Resource is not a compiled model: {resourcePath}");
            }
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    private static Model GetModel(Resource resource) =>
        resource.DataBlock as Model
        ?? throw new InvalidDataException("Compiled model DATA block was not available.");

    private static ModelAnimationBindingSnapshot ReadSnapshot(KVObject data)
    {
        var hasGraphField = data.TryGetValue(AnimGraph2Field, out var graphArray);
        var graphRefs = new List<string>();
        if (hasGraphField)
        {
            if (!graphArray.IsArray)
            {
                throw new InvalidDataException($"{AnimGraph2Field} exists but is not an array.");
            }

            foreach (var graphRef in graphArray.Values)
            {
                if (graphRef.ValueType != KVValueType.Collection)
                {
                    throw new InvalidDataException($"{AnimGraph2Field} contains a non-object entry.");
                }

                var graphPath = graphRef.GetStringProperty("m_hGraph", null);
                if (string.IsNullOrWhiteSpace(graphPath))
                {
                    throw new InvalidDataException($"{AnimGraph2Field} contains an entry without m_hGraph.");
                }

                var identifier = graphRef.GetStringProperty("m_sIdentifier", string.Empty) ?? string.Empty;
                graphRefs.Add(identifier + "|" + NormalizeResourcePath(graphPath));
            }
        }

        var hasSkeletonField = data.TryGetValue(NmSkeletonField, out var skeletonArray);
        var skeletonRefs = new List<string>();
        if (hasSkeletonField)
        {
            if (!skeletonArray.IsArray)
            {
                throw new InvalidDataException($"{NmSkeletonField} exists but is not an array.");
            }

            foreach (var skeletonRef in skeletonArray.Values)
            {
                if (skeletonRef.ValueType != KVValueType.String)
                {
                    throw new InvalidDataException($"{NmSkeletonField} contains a non-string entry.");
                }
                skeletonRefs.Add(NormalizeResourcePath((string)skeletonRef));
            }
        }

        return new ModelAnimationBindingSnapshot(
            hasGraphField,
            graphRefs,
            hasSkeletonField,
            skeletonRefs);
    }

    private static void CopyAuthoritativeField(KVObject source, KVObject destination, string fieldName)
    {
        if (!source.TryGetValue(fieldName, out var sourceValue))
        {
            destination.Remove(fieldName);
            return;
        }

        destination[fieldName] = CloneBindingValue(sourceValue, fieldName);
    }

    private static KVObject CloneBindingValue(KVObject value, string fieldName)
    {
        KVObject clone = value.ValueType switch
        {
            KVValueType.String => new KVObject((string)value),
            KVValueType.Array => CloneArray(value, fieldName),
            KVValueType.Collection => CloneCollection(value, fieldName),
            KVValueType.Null => KVObject.Null(),
            _ => throw new InvalidDataException(
                $"Unsupported value type inside {fieldName}: {value.ValueType}."),
        };
        clone.Flag = value.Flag;
        return clone;
    }

    private static KVObject CloneArray(KVObject source, string fieldName)
    {
        var result = KVObject.Array(source.Count);
        foreach (var child in source.Values)
        {
            result.Add(CloneBindingValue(child, fieldName));
        }
        return result;
    }

    private static KVObject CloneCollection(KVObject source, string fieldName)
    {
        var result = KVObject.Collection(source.Count);
        foreach (var child in source.Children)
        {
            result[child.Key] = CloneBindingValue(child.Value, fieldName);
        }
        return result;
    }

    private static string NormalizeResourcePath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "Source 2 resource path")
            .TrimStart('/');
}
