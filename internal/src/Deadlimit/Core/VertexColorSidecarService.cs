using System.Collections;
using Datamodel;
using Datamodel.Codecs;
using DmxColor = Datamodel.Color;

namespace Deadlimit.Core;

public enum VertexColorSidecarStatus
{
    Missing,
    Applied,
    Skipped,
}

public sealed record VertexColorSidecarResult(
    VertexColorSidecarStatus Status,
    string SidecarPath,
    int StreamCount,
    string Message);

public static class VertexColorSidecarService
{
    public const string FileSuffix = "_vertexcolor.dmx";

    public static bool IsSidecarPath(string path) =>
        Path.GetFileName(path).EndsWith(FileSuffix, StringComparison.OrdinalIgnoreCase);

    public static string GetSidecarPath(string artistDmxPath)
    {
        var directory = Path.GetDirectoryName(artistDmxPath)
            ?? throw new ArgumentException("Artist DMX path has no parent folder.", nameof(artistDmxPath));
        return Path.Combine(
            directory,
            Path.GetFileNameWithoutExtension(artistDmxPath) + FileSuffix);
    }

    public static VertexColorSidecarResult TryApply(string artistDmxPath, string preparedDmxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistDmxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedDmxPath);

        var sidecarPath = GetSidecarPath(artistDmxPath);
        if (!File.Exists(sidecarPath))
        {
            return new VertexColorSidecarResult(
                VertexColorSidecarStatus.Missing,
                sidecarPath,
                0,
                "No vertex-color sidecar was found.");
        }

        var temporaryPath = preparedDmxPath + $".deadlimit-vertexcolor-{Guid.NewGuid():N}.tmp";
        try
        {
            if (File.GetLastWriteTimeUtc(sidecarPath) < File.GetLastWriteTimeUtc(artistDmxPath))
            {
                return new VertexColorSidecarResult(
                    VertexColorSidecarStatus.Skipped,
                    sidecarPath,
                    0,
                    "The sidecar is older than the artist DMX. Export vertex color again after the latest Wall Worm export.");
            }

            using var prepared = Datamodel.Datamodel.Load(preparedDmxPath, DeferredMode.Disabled);
            using var sidecar = Datamodel.Datamodel.Load(sidecarPath, DeferredMode.Disabled);

            if (!string.Equals(prepared.Format, sidecar.Format, StringComparison.Ordinal)
                || prepared.FormatVersion != sidecar.FormatVersion)
            {
                return Skipped(sidecarPath, "DMX format or format version differs between the artist file and sidecar.");
            }

            var preparedMeshes = FindMeshBindings(prepared);
            var sidecarMeshes = FindMeshBindings(sidecar);
            if (preparedMeshes.Count == 0 || sidecarMeshes.Count == 0)
            {
                return Skipped(sidecarPath, "One of the DMX files contains no DmeMesh bindings.");
            }

            if (preparedMeshes.Count != sidecarMeshes.Count)
            {
                return Skipped(
                    sidecarPath,
                    $"Mesh count differs: artist {preparedMeshes.Count}, sidecar {sidecarMeshes.Count}.");
            }

            var matches = MatchMeshes(preparedMeshes, sidecarMeshes, out var mismatchReason);
            if (matches is null)
            {
                return Skipped(sidecarPath, mismatchReason);
            }

            var transferCount = 0;
            foreach (var (target, source) in matches)
            {
                if (!TryTransferMesh(target, source, out var transferred, out mismatchReason))
                {
                    return Skipped(sidecarPath, mismatchReason);
                }

                transferCount += transferred;
            }

            if (transferCount == 0)
            {
                return Skipped(sidecarPath, "The sidecar contains no usable color$0 streams.");
            }

            prepared.Save(temporaryPath, prepared.Encoding, prepared.EncodingVersion);
            File.Move(temporaryPath, preparedDmxPath, overwrite: true);

            return new VertexColorSidecarResult(
                VertexColorSidecarStatus.Applied,
                sidecarPath,
                transferCount,
                $"Transferred {transferCount} validated color stream(s).");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Skipped(sidecarPath, $"Could not validate or read the sidecar: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failed cleanup must not turn a rejected sidecar into a failed PREPARE transaction.
            }
        }
    }

    private static IReadOnlyList<MeshBinding> FindMeshBindings(Datamodel.Datamodel document)
    {
        var result = new List<MeshBinding>();
        foreach (var mesh in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal)))
        {
            var bindState = GetRequiredElement(mesh, "bindState");
            var currentState = GetRequiredElement(mesh, "currentState");
            if (bindState.ID != currentState.ID)
            {
                throw new InvalidDataException(
                    $"Mesh '{mesh.Name}' uses different bind/current vertex states; safe sidecar transfer is unavailable.");
            }

            var faceSets = GetRequiredArray<Element>(mesh, "faceSets").ToArray();
            result.Add(new MeshBinding(mesh.Name, bindState, faceSets));
        }

        return result;
    }

    private static IReadOnlyList<(MeshBinding Target, MeshBinding Source)>? MatchMeshes(
        IReadOnlyList<MeshBinding> targets,
        IReadOnlyList<MeshBinding> sources,
        out string mismatchReason)
    {
        var targetGroups = targets
            .GroupBy(binding => binding.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (targetGroups.Values.Any(group => group.Length != 1))
        {
            mismatchReason = "The artist DMX contains duplicate DmeMesh names; transfer would be ambiguous.";
            return null;
        }

        var matches = new List<(MeshBinding Target, MeshBinding Source)>();

        foreach (var source in sources)
        {
            if (!targetGroups.TryGetValue(source.Name, out var candidates))
            {
                mismatchReason = $"Sidecar mesh '{source.Name}' has no exact artist-DMX mesh-name match.";
                return null;
            }

            matches.Add((candidates[0], source));
            targetGroups.Remove(source.Name);
        }

        if (targetGroups.Count != 0)
        {
            mismatchReason = "Not every artist-DMX mesh has a same-named sidecar mesh.";
            return null;
        }

        mismatchReason = string.Empty;
        return matches;
    }

    private static bool TryTransferMesh(
        MeshBinding target,
        MeshBinding source,
        out int transferred,
        out string mismatchReason)
    {
        transferred = 0;
        if (target.FaceSets.Count != source.FaceSets.Count)
        {
            mismatchReason = $"Face-set count differs for mesh '{target.Name}'.";
            return false;
        }

        var targetFormat = GetRequiredArray<string>(target.VertexData, "vertexFormat");
        var sourceFormat = GetRequiredArray<string>(source.VertexData, "vertexFormat");
        var comparisonStreams = targetFormat
            .Where(name => !string.Equals(name, "color$0", StringComparison.Ordinal))
            .ToArray();
        if (comparisonStreams.Length == 0
            || comparisonStreams.Any(name => !sourceFormat.Contains(name, StringComparer.Ordinal)))
        {
            mismatchReason = $"Indexed vertex streams differ for mesh '{target.Name}'.";
            return false;
        }

        var targetColumns = comparisonStreams
            .Select(name => ReadStreamColumn(target.VertexData, name))
            .ToArray();
        var sourceColumns = comparisonStreams
            .Select(name => ReadStreamColumn(source.VertexData, name))
            .ToArray();
        var targetVertexCount = GetLogicalVertexCount(target.VertexData);
        var sourceVertexCount = GetLogicalVertexCount(source.VertexData);
        if (!ValidateColumns(targetColumns, targetVertexCount)
            || !ValidateColumns(
                sourceColumns.Where(column => column.IsIndexed).ToArray(),
                sourceVertexCount))
        {
            mismatchReason = $"Indexed stream lengths are inconsistent for mesh '{target.Name}'.";
            return false;
        }

        var identityColumnIndexes = Enumerable.Range(0, targetColumns.Length)
            .Where(index => targetColumns[index].IsIndexed)
            .ToArray();
        if (identityColumnIndexes.Length == 0
            || identityColumnIndexes.Any(index => !sourceColumns[index].IsIndexed))
        {
            mismatchReason = $"No compatible indexed identity streams exist for mesh '{target.Name}'.";
            return false;
        }

        var targetIdentityColumns = identityColumnIndexes.Select(index => targetColumns[index]).ToArray();
        var sourceIdentityColumns = identityColumnIndexes.Select(index => sourceColumns[index]).ToArray();
        var targetDirectColumns = targetColumns.Where(column => !column.IsIndexed).ToArray();
        var targetKeys = BuildVertexKeys(targetIdentityColumns, targetVertexCount);
        var sourceKeys = BuildVertexKeys(sourceIdentityColumns, sourceVertexCount);
        var targetLookup = new Dictionary<VertexKey, int>();
        for (var index = 0; index < targetKeys.Length; index++)
        {
            if (targetLookup.TryGetValue(targetKeys[index], out var existing)
                && !DirectPayloadEquals(targetDirectColumns, existing, index))
            {
                mismatchReason = $"Surface-identical vertices have different direct attributes in mesh '{target.Name}'.";
                return false;
            }

            targetLookup.TryAdd(targetKeys[index], index);
        }

        var sourceToTarget = new int[sourceKeys.Length];
        for (var index = 0; index < sourceKeys.Length; index++)
        {
            if (!targetLookup.TryGetValue(sourceKeys[index], out sourceToTarget[index]))
            {
                mismatchReason = $"Vertex attributes differ for mesh '{target.Name}'.";
                return false;
            }
        }

        for (var faceSetIndex = 0; faceSetIndex < target.FaceSets.Count; faceSetIndex++)
        {
            var targetFaceSet = target.FaceSets[faceSetIndex];
            var sourceFaceSet = source.FaceSets[faceSetIndex];
            if (!string.Equals(targetFaceSet.Name, sourceFaceSet.Name, StringComparison.Ordinal))
            {
                mismatchReason = $"Face-set material/order differs for mesh '{target.Name}'.";
                return false;
            }

            var targetFaces = GetRequiredArray<int>(targetFaceSet, "faces");
            var sourceFaces = GetRequiredArray<int>(sourceFaceSet, "faces");
            if (!FaceTopologyMatches(targetFaces, sourceFaces, targetKeys, sourceKeys))
            {
                mismatchReason = $"Polygon topology differs for mesh '{target.Name}', face set {faceSetIndex}.";
                return false;
            }
        }

        var hasColors = source.VertexData.ContainsKey("color$0")
            && source.VertexData.ContainsKey("color$0Indices");
        if (!hasColors)
        {
            mismatchReason = string.Empty;
            return true;
        }

        var colors = GetRequiredArray<DmxColor>(source.VertexData, "color$0");
        var colorIndices = GetRequiredArray<int>(source.VertexData, "color$0Indices");
        if (colors.Count == 0 || colorIndices.Count != sourceVertexCount)
        {
            mismatchReason = $"Color stream length differs for mesh '{target.Name}'.";
            return false;
        }

        if (colorIndices.Any(index => index < 0 || index >= colors.Count))
        {
            mismatchReason = $"A color index is outside the color array for mesh '{target.Name}'.";
            return false;
        }

        for (var streamIndex = 0; streamIndex < comparisonStreams.Length; streamIndex++)
        {
            var streamName = comparisonStreams[streamIndex];
            var targetColumn = targetColumns[streamIndex];
            if (targetColumn.IsIndexed)
            {
                var expandedValues = CreateDmxArray(
                    targetColumn.ArrayType,
                    sourceToTarget.Select(targetVertex =>
                        targetColumn.Values[targetColumn.Indices[targetVertex]]));
                target.VertexData[streamName] = expandedValues;
                target.VertexData[streamName + "Indices"] = new IntArray(
                    Enumerable.Range(0, sourceToTarget.Length));
            }
            else
            {
                var expandedValues = ExpandDirectArray(
                    targetColumn.Values,
                    targetColumn.ArrayType,
                    sourceToTarget,
                    targetColumn.Stride);
                target.VertexData[streamName] = expandedValues;
            }

            var expectedValueCount = sourceToTarget.Length * targetColumn.Stride;
            var actualValueCount = GetRequiredValues(target.VertexData, streamName).Length;
            if (actualValueCount != expectedValueCount)
            {
                throw new InvalidDataException(
                    $"Expanded stream '{streamName}' has {actualValueCount} values; expected {expectedValueCount}.");
            }
        }

        target.VertexData["color$0"] = new ColorArray(colors);
        target.VertexData["color$0Indices"] = new IntArray(colorIndices);
        EnsureVertexFormatEntry(target.VertexData, "color$0");

        for (var faceSetIndex = 0; faceSetIndex < target.FaceSets.Count; faceSetIndex++)
        {
            target.FaceSets[faceSetIndex]["faces"] = new IntArray(
                GetRequiredArray<int>(source.FaceSets[faceSetIndex], "faces"));
        }

        transferred = 1;
        mismatchReason = string.Empty;
        return true;
    }

    private static bool DirectPayloadEquals(
        IReadOnlyList<StreamColumn> columns,
        int firstVertex,
        int secondVertex)
    {
        foreach (var column in columns)
        {
            for (var offset = 0; offset < column.Stride; offset++)
            {
                var first = column.Values[(firstVertex * column.Stride) + offset];
                var second = column.Values[(secondVertex * column.Stride) + offset];
                if (!Equals(first, second))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static StreamColumn ReadStreamColumn(Element vertexData, string streamName)
    {
        var rawValues = vertexData[streamName];
        if (rawValues is not IEnumerable enumerable || rawValues is string)
        {
            throw new InvalidDataException(
                $"DmeVertexData attribute '{streamName}' is missing or is not an array.");
        }

        var values = enumerable.Cast<object>().ToArray();
        var indexName = streamName + "Indices";
        if (vertexData.ContainsKey(indexName))
        {
            return new StreamColumn(
                rawValues.GetType(),
                values,
                GetRequiredArray<int>(vertexData, indexName),
                isIndexed: true);
        }

        return new StreamColumn(rawValues.GetType(), values, Array.Empty<int>(), isIndexed: false);
    }

    private static object ExpandDirectArray(
        IReadOnlyList<object> source,
        Type arrayType,
        IReadOnlyList<int> sourceToTarget,
        int stride)
    {
        var values = new List<object>(sourceToTarget.Count * stride);
        foreach (var targetVertex in sourceToTarget)
        {
            for (var offset = 0; offset < stride; offset++)
            {
                values.Add(source[(targetVertex * stride) + offset]);
            }
        }

        return CreateDmxArray(arrayType, values);
    }

    private static object CreateDmxArray(Type arrayType, IEnumerable<object> values)
    {
        var elementType = FindDmxArrayElementType(arrayType)
            ?? throw new InvalidDataException($"Could not determine element type for DMX array '{arrayType.FullName}'.");
        var materialized = values.ToArray();
        var typedValues = System.Array.CreateInstance(elementType, materialized.Length);
        for (var index = 0; index < materialized.Length; index++)
        {
            typedValues.SetValue(materialized[index], index);
        }

        var enumerableType = typeof(IEnumerable<>).MakeGenericType(elementType);
        var constructor = arrayType.GetConstructor([enumerableType])
            ?? throw new InvalidDataException($"DMX array '{arrayType.FullName}' has no enumerable constructor.");
        return constructor.Invoke([typedValues]);
    }

    private static Type? FindDmxArrayElementType(Type arrayType)
    {
        for (var current = arrayType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType
                && current.GetGenericTypeDefinition() == typeof(Datamodel.Array<>))
            {
                return current.GetGenericArguments()[0];
            }
        }

        return null;
    }

    private static int GetLogicalVertexCount(Element vertexData)
    {
        if (vertexData.ContainsKey("position$0Indices"))
        {
            return GetRequiredArray<int>(vertexData, "position$0Indices").Count;
        }

        return GetRequiredValues(vertexData, "position$0").Length;
    }

    private static bool ValidateColumns(IReadOnlyList<StreamColumn> columns, int logicalVertexCount)
    {
        if (logicalVertexCount <= 0)
        {
            return false;
        }

        foreach (var column in columns)
        {
            if (column.IsIndexed)
            {
                if (column.Indices.Count != logicalVertexCount
                    || column.Indices.Any(index => index < 0 || index >= column.Values.Length))
                {
                    return false;
                }

                column.Stride = 1;
                continue;
            }

            if (column.Values.Length == 0 || column.Values.Length % logicalVertexCount != 0)
            {
                return false;
            }

            column.Stride = column.Values.Length / logicalVertexCount;
        }

        return true;
    }

    private static VertexKey[] BuildVertexKeys(
        IReadOnlyList<StreamColumn> columns,
        int logicalVertexCount)
    {
        var result = new VertexKey[logicalVertexCount];
        for (var vertex = 0; vertex < logicalVertexCount; vertex++)
        {
            var values = new List<object>();
            foreach (var column in columns)
            {
                if (column.IsIndexed)
                {
                    values.Add(column.Values[column.Indices[vertex]]!);
                    continue;
                }

                for (var offset = 0; offset < column.Stride; offset++)
                {
                    values.Add(column.Values[(vertex * column.Stride) + offset]!);
                }
            }

            result[vertex] = new VertexKey(values.ToArray());
        }

        return result;
    }

    private static bool FaceTopologyMatches(
        IList<int> targetFaces,
        IList<int> sourceFaces,
        IReadOnlyList<VertexKey> targetKeys,
        IReadOnlyList<VertexKey> sourceKeys)
    {
        if (targetFaces.Count != sourceFaces.Count)
        {
            return false;
        }

        for (var index = 0; index < targetFaces.Count; index++)
        {
            var targetVertex = targetFaces[index];
            var sourceVertex = sourceFaces[index];
            if (targetVertex < 0 || sourceVertex < 0)
            {
                if (targetVertex != sourceVertex)
                {
                    return false;
                }

                continue;
            }

            if (targetVertex >= targetKeys.Count
                || sourceVertex >= sourceKeys.Count
                || !targetKeys[targetVertex].Equals(sourceKeys[sourceVertex]))
            {
                return false;
            }
        }

        return true;
    }

    private static void EnsureVertexFormatEntry(Element vertexData, string streamName)
    {
        if (!vertexData.ContainsKey("vertexFormat"))
        {
            throw new InvalidOperationException("DmeVertexData has no vertexFormat array.");
        }

        var format = GetRequiredArray<string>(vertexData, "vertexFormat");
        if (format.Contains(streamName, StringComparer.Ordinal))
        {
            return;
        }

        var updated = format.ToList();
        var blendIndex = updated.FindIndex(value => value.StartsWith("blend", StringComparison.Ordinal));
        if (blendIndex >= 0)
        {
            updated.Insert(blendIndex, streamName);
        }
        else
        {
            updated.Add(streamName);
        }

        vertexData["vertexFormat"] = new StringArray(updated);
    }

    private static IList<T> GetRequiredArray<T>(Element element, string name) =>
        element.GetArray<T>(name)
        ?? throw new InvalidDataException($"DmeVertexData attribute '{name}' is missing or has an invalid type.");

    private static Element GetRequiredElement(Element element, string name) =>
        element.Get<Element>(name)
        ?? throw new InvalidDataException($"DMX element '{element.Name}' has no valid '{name}' reference.");

    private static object[] GetRequiredValues(Element element, string name)
    {
        var value = element[name];
        if (value is not IEnumerable enumerable || value is string)
        {
            throw new InvalidDataException(
                $"DmeVertexData attribute '{name}' is missing or is not an array.");
        }

        return enumerable.Cast<object>().ToArray();
    }

    private static VertexColorSidecarResult Skipped(string sidecarPath, string message) =>
        new(VertexColorSidecarStatus.Skipped, sidecarPath, 0, message);

    private sealed record MeshBinding(
        string Name,
        Element VertexData,
        IReadOnlyList<Element> FaceSets);

    private sealed class StreamColumn
    {
        public StreamColumn(Type arrayType, object[] values, IList<int> indices, bool isIndexed)
        {
            ArrayType = arrayType;
            Values = values;
            Indices = indices;
            IsIndexed = isIndexed;
        }

        public Type ArrayType { get; }

        public object[] Values { get; }

        public IList<int> Indices { get; }

        public bool IsIndexed { get; }

        public int Stride { get; set; }
    }

    private sealed class VertexKey : IEquatable<VertexKey>
    {
        private readonly object[] _values;

        public VertexKey(object[] values)
        {
            _values = values;
        }

        public bool Equals(VertexKey? other) =>
            other is not null
            && _values.Length == other._values.Length
            && _values.SequenceEqual(other._values);

        public override bool Equals(object? obj) => Equals(obj as VertexKey);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var value in _values)
            {
                hash.Add(value);
            }

            return hash.ToHashCode();
        }
    }
}
