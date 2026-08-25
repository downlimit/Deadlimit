using System.Collections;
using System.Numerics;
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
    public const string FileSuffix = "_vertexcolor.fbx";

    public static VertexColorSidecarResult TryApplyInPlace(string artistDmxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistDmxPath);

        var fullArtistPath = Path.GetFullPath(artistDmxPath);
        var directory = Path.GetDirectoryName(fullArtistPath)
            ?? throw new ArgumentException("Artist DMX path has no parent folder.", nameof(artistDmxPath));
        var stagedPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullArtistPath)}.deadlimit-vertexcolor-{Guid.NewGuid():N}.tmp");
        var sidecarPath = GetSidecarPath(fullArtistPath);

        try
        {
            if (!File.Exists(fullArtistPath))
            {
                return Skipped(sidecarPath, "The artist DMX does not exist.");
            }

            File.Copy(fullArtistPath, stagedPath, overwrite: false);
            var result = TryApply(fullArtistPath, stagedPath);
            if (result.Status != VertexColorSidecarStatus.Applied)
            {
                return result;
            }

            if (!ValidateWrittenColorStreams(stagedPath, result.StreamCount, out var validationReason))
            {
                return Skipped(sidecarPath, $"Written DMX verification failed: {validationReason}");
            }

            File.Move(stagedPath, fullArtistPath, overwrite: true);
            return result with
            {
                Message = $"{result.Message} Verified and wrote the artist DMX atomically.",
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Skipped(sidecarPath, $"Could not update the artist DMX: {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The artist DMX has already remained untouched or been atomically replaced.
            }
        }
    }

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
            var sidecarMeshes = AsciiFbxVertexColorReader.Read(sidecarPath);

            var preparedMeshes = FindMeshBindings(prepared);
            if (preparedMeshes.Count == 0 || sidecarMeshes.Count == 0)
            {
                return Skipped(sidecarPath, "The DMX or FBX contains no mesh bindings.");
            }

            var transferCount = 0;
            var priorityTargets = preparedMeshes.Where(mesh => mesh.UsesVertexColorMaterial).ToArray();
            var sidecarByName = sidecarMeshes
                .GroupBy(mesh => mesh.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (sidecarByName.Values.Any(group => group.Length != 1))
            {
                return Skipped(sidecarPath, "The FBX contains duplicate mesh node names.");
            }

            foreach (var target in priorityTargets)
            {
                var fbxName = GetFbxMeshName(target.Name);
                if (!sidecarByName.TryGetValue(fbxName, out var candidates))
                {
                    return Skipped(
                        sidecarPath,
                        $"Priority Vertex Color mesh '{fbxName}' is missing from the FBX.");
                }

                if (!TryTransferMeshFromFbx(
                        target,
                        candidates[0],
                        out var transferred,
                        out var mismatchReason))
                {
                    return Skipped(sidecarPath, $"Priority mesh '{fbxName}' failed validation: {mismatchReason}");
                }

                if (transferred == 0)
                {
                    return Skipped(
                        sidecarPath,
                        $"Priority mesh '{fbxName}' has no Vertex Color layer in the FBX.");
                }

                transferCount += transferred;
                sidecarByName.Remove(fbxName);
            }

            foreach (var target in preparedMeshes.Where(mesh => !mesh.UsesVertexColorMaterial))
            {
                var fbxName = GetFbxMeshName(target.Name);
                if (sidecarByName.TryGetValue(fbxName, out var candidates)
                    && TryTransferMeshFromFbx(
                        target,
                        candidates[0],
                        out var transferred,
                        out _))
                {
                    transferCount += transferred;
                }
            }

            if (transferCount == 0)
            {
                return Skipped(sidecarPath, "The FBX contains no usable Vertex Color layers.");
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

    private static bool ValidateWrittenColorStreams(
        string dmxPath,
        int expectedStreamCount,
        out string validationReason)
    {
        using var document = Datamodel.Datamodel.Load(dmxPath, DeferredMode.Disabled);
        var validStreamCount = 0;
        foreach (var binding in FindMeshBindings(document))
        {
            var vertexData = binding.VertexData;
            if (!vertexData.ContainsKey("color$0") && !vertexData.ContainsKey("color$0Indices"))
            {
                continue;
            }

            if (!vertexData.ContainsKey("color$0") || !vertexData.ContainsKey("color$0Indices"))
            {
                validationReason = $"Mesh '{binding.Name}' has an incomplete color$0 stream.";
                return false;
            }

            var colors = GetRequiredArray<DmxColor>(vertexData, "color$0");
            var colorIndices = GetRequiredArray<int>(vertexData, "color$0Indices");
            var logicalVertexCount = GetLogicalVertexCount(vertexData);
            var vertexFormat = GetRequiredArray<string>(vertexData, "vertexFormat");
            if (colors.Count == 0
                || colorIndices.Count != logicalVertexCount
                || colorIndices.Any(index => index < 0 || index >= colors.Count)
                || !vertexFormat.Contains("color$0", StringComparer.Ordinal))
            {
                validationReason = $"Mesh '{binding.Name}' has an invalid color$0 stream after reload.";
                return false;
            }

            validStreamCount++;
        }

        if (validStreamCount < expectedStreamCount)
        {
            validationReason =
                $"Expected at least {expectedStreamCount} color stream(s), reloaded {validStreamCount}.";
            return false;
        }

        validationReason = string.Empty;
        return true;
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
            result.Add(new MeshBinding(
                mesh.Name,
                bindState,
                faceSets,
                faceSets.Any(UsesVertexColorMaterial)));
        }

        return result;
    }

    private static bool UsesVertexColorMaterial(Element faceSet)
    {
        if (!faceSet.ContainsKey("material"))
        {
            return false;
        }

        var material = faceSet.Get<Element>("material");
        if (material is null)
        {
            return false;
        }

        if (ContainsVertexColorToken(material.Name))
        {
            return true;
        }

        return material.ContainsKey("mtlName")
            && ContainsVertexColorToken(material.Get<string>("mtlName"));
    }

    private static bool ContainsVertexColorToken(string? value) =>
        value?.Contains("vertexcolor", StringComparison.OrdinalIgnoreCase) == true;

    private static IReadOnlyList<(MeshBinding Target, FbxVertexColorMesh Source)>? MatchMeshes(
        IReadOnlyList<MeshBinding> targets,
        IReadOnlyList<FbxVertexColorMesh> sources,
        out string mismatchReason)
    {
        var targetGroups = targets
            .GroupBy(binding => GetFbxMeshName(binding.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (targetGroups.Values.Any(group => group.Length != 1))
        {
            mismatchReason = "The DMX contains duplicate mesh names after removing the _mesh suffix.";
            return null;
        }

        var sourceGroups = sources
            .GroupBy(binding => binding.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        if (sourceGroups.Values.Any(group => group.Length != 1))
        {
            mismatchReason = "The FBX contains duplicate mesh node names.";
            return null;
        }

        var matches = new List<(MeshBinding Target, FbxVertexColorMesh Source)>();
        foreach (var source in sources)
        {
            if (!targetGroups.TryGetValue(source.Name, out var candidates))
            {
                mismatchReason = $"FBX mesh '{source.Name}' has no exact DMX mesh-name match.";
                return null;
            }

            matches.Add((candidates[0], source));
            targetGroups.Remove(source.Name);
        }

        if (targetGroups.Count != 0)
        {
            mismatchReason = "Not every DMX mesh has a same-named FBX mesh.";
            return null;
        }

        mismatchReason = string.Empty;
        return matches;
    }

    private static string GetFbxMeshName(string dmxMeshName) =>
        dmxMeshName.EndsWith("_mesh", StringComparison.Ordinal)
            ? dmxMeshName[..^"_mesh".Length]
            : dmxMeshName;

    private static bool TryTransferMeshFromFbx(
        MeshBinding target,
        FbxVertexColorMesh source,
        out int transferred,
        out string mismatchReason)
    {
        transferred = 0;
        var targetFormat = GetRequiredArray<string>(target.VertexData, "vertexFormat");
        var streamNames = targetFormat
            .Where(name => !string.Equals(name, "color$0", StringComparison.Ordinal))
            .ToArray();
        if (streamNames.Length == 0 || !streamNames.Contains("position$0", StringComparer.Ordinal))
        {
            mismatchReason = $"DMX mesh '{target.Name}' has no position$0 stream.";
            return false;
        }

        var columns = streamNames.Select(name => ReadStreamColumn(target.VertexData, name)).ToArray();
        var logicalVertexCount = GetLogicalVertexCount(target.VertexData);
        if (!ValidateColumns(columns, logicalVertexCount))
        {
            mismatchReason = $"DMX stream lengths are inconsistent for mesh '{target.Name}'.";
            return false;
        }

        var positionColumn = columns[Array.IndexOf(streamNames, "position$0")];
        if (!positionColumn.IsIndexed
            || positionColumn.Values.Length != source.ControlPoints.Count
            || positionColumn.Values.Any(value => value is not Vector3))
        {
            mismatchReason =
                $"Control-point count or position representation differs for mesh '{target.Name}': DMX {positionColumn.Values.Length}, FBX {source.ControlPoints.Count}.";
            return false;
        }

        if (!TryReadTargetPolygons(
                target,
                positionColumn.Indices,
                logicalVertexCount,
                out var targetPolygons,
                out mismatchReason))
        {
            return false;
        }

        if (targetPolygons.Count != source.Polygons.Count)
        {
            mismatchReason =
                $"Polygon count differs for mesh '{target.Name}': DMX {targetPolygons.Count}, FBX {source.Polygons.Count}.";
            return false;
        }

        if (!TryMatchPolygonColors(target.Name, targetPolygons, source.Polygons, out var colors, out mismatchReason))
        {
            return false;
        }

        if (!source.HasColors)
        {
            mismatchReason = string.Empty;
            return true;
        }

        var logicalVerticesByCorner = targetPolygons
            .SelectMany(polygon => polygon.LogicalVertices)
            .ToArray();
        if (colors.Count != logicalVerticesByCorner.Length)
        {
            mismatchReason = $"Color corner count differs for mesh '{target.Name}'.";
            return false;
        }

        for (var streamIndex = 0; streamIndex < streamNames.Length; streamIndex++)
        {
            var streamName = streamNames[streamIndex];
            var column = columns[streamIndex];
            if (column.IsIndexed)
            {
                target.VertexData[streamName] = CreateDmxArray(
                    column.ArrayType,
                    logicalVerticesByCorner.Select(vertex => column.Values[column.Indices[vertex]]));
                target.VertexData[streamName + "Indices"] = new IntArray(
                    Enumerable.Range(0, logicalVerticesByCorner.Length));
            }
            else
            {
                target.VertexData[streamName] = ExpandDirectArray(
                    column.Values,
                    column.ArrayType,
                    logicalVerticesByCorner,
                    column.Stride);
            }
        }

        target.VertexData["color$0"] = new ColorArray(colors);
        target.VertexData["color$0Indices"] = new IntArray(Enumerable.Range(0, colors.Count));
        EnsureVertexFormatEntry(target.VertexData, "color$0");

        var nextCorner = 0;
        foreach (var faceSet in target.FaceSets)
        {
            var rewritten = GetRequiredArray<int>(faceSet, "faces")
                .Select(value => value < 0 ? value : nextCorner++)
                .ToArray();
            faceSet["faces"] = new IntArray(rewritten);
        }

        if (nextCorner != colors.Count)
        {
            mismatchReason = $"Rewritten topology has an unexpected corner count for mesh '{target.Name}'.";
            return false;
        }

        transferred = 1;
        mismatchReason = string.Empty;
        return true;
    }

    private static bool TryReadTargetPolygons(
        MeshBinding target,
        IList<int> positionIndices,
        int logicalVertexCount,
        out IReadOnlyList<TargetPolygon> polygons,
        out string mismatchReason)
    {
        var result = new List<TargetPolygon>();
        foreach (var faceSet in target.FaceSets)
        {
            var currentLogical = new List<int>();
            var currentControlPoints = new List<int>();
            foreach (var value in GetRequiredArray<int>(faceSet, "faces"))
            {
                if (value >= 0)
                {
                    if (value >= logicalVertexCount)
                    {
                        polygons = Array.Empty<TargetPolygon>();
                        mismatchReason = $"DMX mesh '{target.Name}' has a face index outside its vertex streams.";
                        return false;
                    }

                    currentLogical.Add(value);
                    currentControlPoints.Add(positionIndices[value]);
                    continue;
                }

                if (value != -1 || currentLogical.Count < 3)
                {
                    polygons = Array.Empty<TargetPolygon>();
                    mismatchReason = $"DMX mesh '{target.Name}' contains invalid polygon termination.";
                    return false;
                }

                result.Add(new TargetPolygon(currentLogical.ToArray(), currentControlPoints.ToArray()));
                currentLogical.Clear();
                currentControlPoints.Clear();
            }

            if (currentLogical.Count != 0)
            {
                polygons = Array.Empty<TargetPolygon>();
                mismatchReason = $"DMX mesh '{target.Name}' contains an unterminated polygon.";
                return false;
            }
        }

        polygons = result;
        mismatchReason = string.Empty;
        return true;
    }

    private static bool TryMatchPolygonColors(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        out IReadOnlyList<DmxColor> colors,
        out string mismatchReason)
    {
        if (targets.Count != sources.Count)
        {
            colors = Array.Empty<DmxColor>();
            mismatchReason = $"Polygon count differs for mesh '{meshName}'.";
            return false;
        }

        var exactMappings = new int[]?[targets.Count];
        var exactMatchCount = 0;
        for (var index = 0; index < targets.Count; index++)
        {
            if (targets[index].ControlPoints.Count != sources[index].ControlPoints.Count)
            {
                colors = Array.Empty<DmxColor>();
                mismatchReason = $"Polygon corner count or order differs for mesh '{meshName}'.";
                return false;
            }

            if (TryMapPolygonCorners(
                    targets[index].ControlPoints,
                    sources[index].ControlPoints,
                    out var cornerMap))
            {
                exactMappings[index] = cornerMap;
                exactMatchCount++;
            }
        }

        var requiredAnchors = Math.Max(1, (int)Math.Ceiling(targets.Count * 0.9));
        if (exactMatchCount < requiredAnchors)
        {
            colors = Array.Empty<DmxColor>();
            mismatchReason =
                $"Polygon topology/order differs for mesh '{meshName}': {exactMatchCount} of {targets.Count} polygons retained their control-point topology.";
            return false;
        }

        var result = new List<DmxColor>();
        for (var index = 0; index < targets.Count; index++)
        {
            var sourceColors = sources[index].Colors;
            if (sourceColors is null)
            {
                continue;
            }

            var cornerMap = exactMappings[index]
                ?? Enumerable.Range(0, sources[index].ControlPoints.Count).ToArray();
            result.AddRange(cornerMap.Select(sourceCorner => sourceColors[sourceCorner]));
        }

        colors = result;
        mismatchReason = string.Empty;
        return true;
    }

    private static string MakePolygonKey(IReadOnlyList<int> controlPoints)
    {
        string? best = null;
        for (var start = 0; start < controlPoints.Count; start++)
        {
            for (var direction = -1; direction <= 1; direction += 2)
            {
                var sequence = new int[controlPoints.Count];
                for (var offset = 0; offset < sequence.Length; offset++)
                {
                    var index = (start + (direction * offset)) % controlPoints.Count;
                    if (index < 0)
                    {
                        index += controlPoints.Count;
                    }

                    sequence[offset] = controlPoints[index];
                }

                var candidate = string.Join(',', sequence);
                if (best is null || string.CompareOrdinal(candidate, best) < 0)
                {
                    best = candidate;
                }
            }
        }

        return best ?? string.Empty;
    }

    private static bool TryMapPolygonCorners(
        IReadOnlyList<int> target,
        IReadOnlyList<int> source,
        out int[] cornerMap)
    {
        if (target.Count != source.Count)
        {
            cornerMap = Array.Empty<int>();
            return false;
        }

        for (var start = 0; start < source.Count; start++)
        {
            for (var direction = -1; direction <= 1; direction += 2)
            {
                var candidate = new int[target.Count];
                var matches = true;
                for (var targetCorner = 0; targetCorner < target.Count; targetCorner++)
                {
                    var sourceCorner = (start + (direction * targetCorner)) % source.Count;
                    if (sourceCorner < 0)
                    {
                        sourceCorner += source.Count;
                    }

                    candidate[targetCorner] = sourceCorner;
                    if (target[targetCorner] != source[sourceCorner])
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    cornerMap = candidate;
                    return true;
                }
            }
        }

        cornerMap = Array.Empty<int>();
        return false;
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
        IReadOnlyList<Element> FaceSets,
        bool UsesVertexColorMaterial);

    private sealed record TargetPolygon(
        IReadOnlyList<int> LogicalVertices,
        IReadOnlyList<int> ControlPoints);

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
