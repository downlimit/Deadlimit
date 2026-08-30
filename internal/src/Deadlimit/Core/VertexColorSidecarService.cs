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

    public static string GetArtistDmxPath(string sidecarPath)
    {
        if (!IsSidecarPath(sidecarPath))
        {
            throw new ArgumentException("Path is not a Deadlimit Vertex Color sidecar.", nameof(sidecarPath));
        }

        var directory = Path.GetDirectoryName(sidecarPath)
            ?? throw new ArgumentException("Vertex Color sidecar has no parent folder.", nameof(sidecarPath));
        var fileName = Path.GetFileName(sidecarPath);
        return Path.Combine(directory, fileName[..^FileSuffix.Length] + ".dmx");
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
            var defaultGrayCount = 0;
            var priorityTargets = preparedMeshes.Where(mesh => mesh.UsesVertexColorMaterial).ToArray();
            if (priorityTargets.Length == 0)
            {
                return Skipped(
                    sidecarPath,
                    "No DMX mesh uses a material whose name contains 'vertexcolor'.");
            }

            var sidecarByName = sidecarMeshes
                .GroupBy(mesh => mesh.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (sidecarByName.Values.Any(group => group.Length != 1))
            {
                return Skipped(sidecarPath, "The FBX contains duplicate mesh node names.");
            }

            var priorityFailures = new List<string>();
            foreach (var target in priorityTargets)
            {
                var fbxName = GetFbxMeshName(target.Name);
                if (!sidecarByName.TryGetValue(fbxName, out var candidates))
                {
                    priorityFailures.Add($"{fbxName}: mesh is missing from the FBX");
                    continue;
                }

                if (!TryTransferMeshFromFbx(
                        target,
                        candidates[0],
                        out var transferred,
                        out var mismatchReason))
                {
                    priorityFailures.Add($"{fbxName}: {mismatchReason}");
                    continue;
                }

                if (transferred == 0)
                {
                    priorityFailures.Add($"{fbxName}: FBX has no Vertex Color channel 0");
                    continue;
                }

                transferCount += transferred;
                if (!candidates[0].HasColors)
                {
                    defaultGrayCount++;
                }
                sidecarByName.Remove(fbxName);
            }

            if (priorityFailures.Count != 0)
            {
                return Skipped(
                    sidecarPath,
                    "Vertex Color was not written because required mesh(es) failed:\n- "
                    + string.Join("\n- ", priorityFailures));
            }

            prepared.Save(temporaryPath, prepared.Encoding, prepared.EncodingVersion);
            if (!ValidateWrittenColorStreams(temporaryPath, transferCount, out var validationReason))
            {
                return Skipped(sidecarPath, $"Written DMX verification failed: {validationReason}");
            }

            File.Move(temporaryPath, preparedDmxPath, overwrite: true);

            return new VertexColorSidecarResult(
                VertexColorSidecarStatus.Applied,
                sidecarPath,
                transferCount,
                $"Transferred {transferCount} validated color stream(s)"
                + (defaultGrayCount == 0
                    ? "."
                    : $"; wrote neutral gray for {defaultGrayCount} mesh(es) without channel 0."));
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
            || positionColumn.Values.Any(value => value is not Vector3))
        {
            mismatchReason =
                $"Position representation is unsupported for mesh '{target.Name}'.";
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

        if (!source.HasColors)
        {
            if (targetPolygons.Zip(source.Polygons).Any(pair =>
                    pair.First.ControlPoints.Count != pair.Second.ControlPoints.Count))
            {
                mismatchReason = $"Polygon corner counts differ for mesh '{target.Name}'.";
                return false;
            }

            target.VertexData["color$0"] = new ColorArray([
                new DmxColor(128, 128, 128, 255),
            ]);
            target.VertexData["color$0Indices"] = new IntArray(
                Enumerable.Repeat(0, logicalVertexCount));
            EnsureVertexFormatEntry(target.VertexData, "color$0");
            transferred = 1;
            mismatchReason = string.Empty;
            return true;
        }

        var texcoordColumnIndex = Array.IndexOf(streamNames, "texcoord$0");
        var texcoordColumn = texcoordColumnIndex >= 0 ? columns[texcoordColumnIndex] : null;
        if (!TryMatchPolygonColors(
                target.Name,
                targetPolygons,
                source.Polygons,
                texcoordColumn,
                positionColumn.Values.Cast<Vector3>().ToArray(),
                source.ControlPoints,
                out var colors,
                out mismatchReason))
        {
            return false;
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
        StreamColumn? targetTexcoords,
        IReadOnlyList<Vector3> targetControlPoints,
        IReadOnlyList<Vector3> sourceControlPoints,
        out IReadOnlyList<DmxColor> colors,
        out string mismatchReason)
    {
        if (targets.Count != sources.Count)
        {
            colors = Array.Empty<DmxColor>();
            mismatchReason = $"Polygon count differs for mesh '{meshName}'.";
            return false;
        }

        if (sources.Any(polygon =>
                polygon.Colors is null
                || polygon.Colors.Count != polygon.ControlPoints.Count))
        {
            colors = Array.Empty<DmxColor>();
            mismatchReason = $"FBX mesh '{meshName}' has an incomplete Vertex Color layer.";
            return false;
        }

        var targetCornerCount = targets.Sum(polygon => polygon.ControlPoints.Count);
        var sourceCornerColors = sources
            .SelectMany(polygon => polygon.Colors!)
            .ToArray();
        if (targetCornerCount != sourceCornerColors.Length || sourceCornerColors.Length == 0)
        {
            colors = Array.Empty<DmxColor>();
            mismatchReason = $"Polygon corner count differs for mesh '{meshName}'.";
            return false;
        }

        var firstColor = sourceCornerColors[0];
        if (sourceCornerColors.All(color =>
                EqualityComparer<DmxColor>.Default.Equals(color, firstColor)))
        {
            colors = Enumerable.Repeat(firstColor, targetCornerCount).ToArray();
            mismatchReason = string.Empty;
            return true;
        }

        int[] sourcePolygonIndexes = [];
        int[]?[] cornerMappings = [];
        var polygonsMatched = false;
        var polygonMismatchReason =
            $"Control-point count differs for mesh '{meshName}': DMX {targetControlPoints.Count}, FBX {sourceControlPoints.Count}.";

        // A topology-preserving modifier may legitimately move or non-uniformly deform
        // vertices between the DMX and Vertex Color FBX exports. When both exporters
        // retained the same control-point numbering and polygon connectivity, that
        // connectivity is a stronger ownership proof than absolute positions.
        if (targetControlPoints.Count == sourceControlPoints.Count)
        {
            var identityControlPointMap = Enumerable.Range(0, targetControlPoints.Count).ToArray();
            polygonsMatched = TryMatchPolygonsFromControlPointMap(
                meshName,
                targets,
                sources,
                identityControlPointMap,
                out sourcePolygonIndexes,
                out cornerMappings,
                out polygonMismatchReason);
        }

        // Position-based matching remains the fallback for exporters that renumber
        // control points while keeping the same geometric surface.
        if (!polygonsMatched)
        {
            polygonsMatched = TryMatchPolygonsByPositions(
                meshName,
                targets,
                sources,
                targetControlPoints,
                sourceControlPoints,
                out sourcePolygonIndexes,
                out cornerMappings,
                out polygonMismatchReason);
        }

        if (!polygonsMatched)
        {
            polygonsMatched = TryMatchSplitControlPointPolygons(
                meshName,
                targets,
                sources,
                targetControlPoints,
                sourceControlPoints,
                out sourcePolygonIndexes,
                out cornerMappings,
                out polygonMismatchReason);
        }

        if (polygonsMatched)
        {
            var result = new List<DmxColor>(targetCornerCount);
            for (var index = 0; index < targets.Count; index++)
            {
                var sourcePolygon = sources[sourcePolygonIndexes[index]];
                var sourceColors = sourcePolygon.Colors!;
                var cornerMap = cornerMappings[index]
                    ?? Enumerable.Range(0, sourcePolygon.ControlPoints.Count).ToArray();
                result.AddRange(cornerMap.Select(sourceCorner => sourceColors[sourceCorner]));
            }

            colors = result;
            mismatchReason = string.Empty;
            return true;
        }

        if (TryMatchColorsByControlPoints(
                meshName,
                targets,
                sources,
                targetControlPoints,
                sourceControlPoints,
                out colors,
                out var positionColorMismatchReason))
        {
            mismatchReason = string.Empty;
            return true;
        }

        colors = Array.Empty<DmxColor>();
        mismatchReason =
            $"Vertex Color correspondence is ambiguous for multi-color mesh '{meshName}'. " +
            $"Geometry polygon match: {polygonMismatchReason} " +
            $"Position/color match: {positionColorMismatchReason} " +
            "UV-only transfer is intentionally disabled because it cannot prove polygon ownership.";
        return false;
    }

    private static bool TryMatchColorsByTexcoords(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        StreamColumn? targetTexcoords,
        out IReadOnlyList<DmxColor> colors,
        out string mismatchReason)
    {
        colors = Array.Empty<DmxColor>();
        if (targetTexcoords is null
            || !targetTexcoords.IsIndexed
            || targetTexcoords.Values.Any(value => value is not Vector2)
            || sources.Any(polygon => polygon.Texcoords is null || polygon.Colors is null))
        {
            mismatchReason = $"Mesh '{meshName}' has no complete UV/color correspondence.";
            return false;
        }

        var targetValues = targets
            .SelectMany(polygon => polygon.LogicalVertices)
            .Select(vertex => (Vector2)targetTexcoords.Values[targetTexcoords.Indices[vertex]])
            .ToArray();
        var targetKeys = targetValues
            .Select(texcoord => MakeTexcoordKey(texcoord))
            .ToHashSet();

        foreach (var uvTransform in new[] { 0, 1, 2 })
        {
            var sourceByUv = new Dictionary<(long X, long Y), DmxColor>();
            var conflicted = false;
            foreach (var polygon in sources)
            {
                for (var corner = 0; corner < polygon.ControlPoints.Count; corner++)
                {
                    var key = MakeTexcoordKey(TransformTexcoord(polygon.Texcoords![corner], uvTransform));
                    var color = polygon.Colors![corner];
                    if (sourceByUv.TryGetValue(key, out var existing)
                        && !EqualityComparer<DmxColor>.Default.Equals(existing, color))
                    {
                        conflicted = true;
                        break;
                    }

                    sourceByUv[key] = color;
                }

                if (conflicted)
                {
                    break;
                }
            }

            if (conflicted
                || sourceByUv.Keys.Any(key => !targetKeys.Contains(key))
                || targetValues.Any(texcoord => !sourceByUv.ContainsKey(MakeTexcoordKey(texcoord))))
            {
                continue;
            }

            colors = targetValues
                .Select(texcoord => sourceByUv[MakeTexcoordKey(texcoord)])
                .ToArray();
            mismatchReason = string.Empty;
            return true;
        }

        mismatchReason = $"UV/color values differ for mesh '{meshName}'.";
        return false;
    }

    private static bool TryMatchColorsByControlPoints(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        IReadOnlyList<Vector3> targetControlPoints,
        IReadOnlyList<Vector3> sourceControlPoints,
        out IReadOnlyList<DmxColor> colors,
        out string mismatchReason)
    {
        colors = Array.Empty<DmxColor>();
        if (!TryMapSplitControlPoints(
                targetControlPoints,
                sourceControlPoints,
                out var targetToSource,
                out mismatchReason))
        {
            mismatchReason = $"Control-point validation failed for mesh '{meshName}': {mismatchReason}";
            return false;
        }

        var sourceColors = new DmxColor?[sourceControlPoints.Count];
        foreach (var polygon in sources)
        {
            if (polygon.Colors is null)
            {
                mismatchReason = $"FBX mesh '{meshName}' has an incomplete Vertex Color layer.";
                return false;
            }

            for (var corner = 0; corner < polygon.ControlPoints.Count; corner++)
            {
                var controlPoint = polygon.ControlPoints[corner];
                var color = polygon.Colors[corner];
                if (sourceColors[controlPoint] is DmxColor existing
                    && !EqualityComparer<DmxColor>.Default.Equals(existing, color))
                {
                    mismatchReason =
                        $"FBX mesh '{meshName}' stores different colors on one geometric point; polygon topology must match exactly.";
                    return false;
                }

                sourceColors[controlPoint] = color;
            }
        }

        if (sourceColors.Any(color => color is null))
        {
            mismatchReason = $"FBX mesh '{meshName}' has uncolored control points.";
            return false;
        }

        var sourceMin = GetBoundsMin(sourceControlPoints);
        var sourceMax = GetBoundsMax(sourceControlPoints);
        var positionTolerance = Math.Max(
            0.0002f,
            Vector3.Distance(sourceMin, sourceMax) * 0.00002f);
        var coincidentGroups = new List<List<int>>();
        for (var index = 0; index < sourceControlPoints.Count; index++)
        {
            var group = coincidentGroups.FirstOrDefault(candidate =>
                Vector3.Distance(
                    sourceControlPoints[candidate[0]],
                    sourceControlPoints[index]) <= positionTolerance);
            if (group is null)
            {
                group = new List<int>();
                coincidentGroups.Add(group);
            }

            group.Add(index);
        }

        foreach (var group in coincidentGroups)
        {
            var groupColors = group
                .Select(index => sourceColors[index]!.Value)
                .Distinct()
                .ToArray();
            if (groupColors.Length > 1)
            {
                mismatchReason =
                    $"FBX mesh '{meshName}' stores different colors on coincident geometric points; polygon topology must match exactly.";
                return false;
            }

            foreach (var index in group)
            {
                sourceColors[index] = groupColors[0];
            }
        }

        colors = targets
            .SelectMany(polygon => polygon.ControlPoints)
            .Select(controlPoint => sourceColors[targetToSource[controlPoint]]!.Value)
            .ToArray();
        mismatchReason = string.Empty;
        return true;
    }

    private static bool TryMatchPolygonsByTexcoords(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        StreamColumn? targetTexcoords,
        IReadOnlyList<Vector3> targetControlPoints,
        out int[] sourcePolygonIndexes,
        out int[]?[] cornerMappings,
        out string mismatchReason)
    {
        sourcePolygonIndexes = Array.Empty<int>();
        cornerMappings = Array.Empty<int[]?>();
        if (targetTexcoords is null
            || !targetTexcoords.IsIndexed
            || targetTexcoords.Values.Any(value => value is not Vector2)
            || sources.Any(polygon => polygon.Texcoords is null))
        {
            mismatchReason = $"Mesh '{meshName}' has no comparable UV layer.";
            return false;
        }

        var targetPolygonUvs = targets
            .Select(polygon => polygon.LogicalVertices
                .Select(vertex => (Vector2)targetTexcoords.Values[targetTexcoords.Indices[vertex]])
                .ToArray())
            .ToArray();
        var sourcePolygonUvs = sources
            .Select(polygon => polygon.Texcoords!.ToArray())
            .ToArray();

        foreach (var uvTransform in new[] { 0, 1, 2 })
        {
            var targetByUv = targetPolygonUvs
                .Select((texcoords, index) =>
                    (Key: MakeTexcoordPolygonKey(texcoords, uvTransform: 0), Index: index))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Index).ToArray(),
                    StringComparer.Ordinal);
            var sourceByUv = sourcePolygonUvs
                .Select((texcoords, index) =>
                    (Key: MakeTexcoordPolygonKey(texcoords, uvTransform), Index: index))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.Index).ToArray(),
                    StringComparer.Ordinal);

            var targetToSource = Enumerable.Repeat(-1, targetControlPoints.Count).ToArray();
            var hasUvAnchors = false;
            var conflicted = false;
            foreach (var (key, targetIndexes) in targetByUv)
            {
                if (targetIndexes.Length != 1
                    || !sourceByUv.TryGetValue(key, out var sourceIndexes)
                    || sourceIndexes.Length != 1
                    || !TryMapPolygonCornersByTexcoords(
                        targetPolygonUvs[targetIndexes[0]],
                        sourcePolygonUvs[sourceIndexes[0]],
                        uvTransform,
                        out var cornerMap))
                {
                    continue;
                }

                hasUvAnchors = true;
                var targetPolygon = targets[targetIndexes[0]];
                var sourcePolygon = sources[sourceIndexes[0]];
                for (var targetCorner = 0; targetCorner < targetPolygon.ControlPoints.Count; targetCorner++)
                {
                    var targetControlPoint = targetPolygon.ControlPoints[targetCorner];
                    var sourceControlPoint = sourcePolygon.ControlPoints[cornerMap[targetCorner]];
                    if (targetToSource[targetControlPoint] >= 0
                        && targetToSource[targetControlPoint] != sourceControlPoint)
                    {
                        conflicted = true;
                        break;
                    }

                    targetToSource[targetControlPoint] = sourceControlPoint;
                }

                if (conflicted)
                {
                    break;
                }
            }

            if (!hasUvAnchors || conflicted
                || !TryPropagateSplitPositionMappings(targetControlPoints, targetToSource))
            {
                continue;
            }

            if (TryMatchPolygonsFromControlPointMap(
                    meshName,
                    targets,
                    sources,
                    targetToSource,
                    out sourcePolygonIndexes,
                    out cornerMappings,
                    out mismatchReason))
            {
                return true;
            }
        }

        mismatchReason = $"UV polygon surface differs for mesh '{meshName}'.";
        return false;
    }

    private static string MakeTexcoordPolygonKey(IReadOnlyList<Vector2> texcoords, int uvTransform)
    {
        string? best = null;
        for (var start = 0; start < texcoords.Count; start++)
        {
            for (var direction = -1; direction <= 1; direction += 2)
            {
                var parts = new string[texcoords.Count];
                for (var offset = 0; offset < parts.Length; offset++)
                {
                    var index = (start + (direction * offset)) % texcoords.Count;
                    if (index < 0)
                    {
                        index += texcoords.Count;
                    }

                    var texcoord = texcoords[index];
                    var transformed = TransformTexcoord(texcoord, uvTransform);
                    parts[offset] = $"{QuantizeTexcoord(transformed.X)}:{QuantizeTexcoord(transformed.Y)}";
                }

                var candidate = string.Join(',', parts);
                if (best is null || string.CompareOrdinal(candidate, best) < 0)
                {
                    best = candidate;
                }
            }
        }

        return best ?? string.Empty;
    }

    private static bool TryMapPolygonCornersByTexcoords(
        IReadOnlyList<Vector2> target,
        IReadOnlyList<Vector2> source,
        int uvTransform,
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
                    var sourceTexcoord = TransformTexcoord(source[sourceCorner], uvTransform);
                    if (QuantizeTexcoord(target[targetCorner].X) != QuantizeTexcoord(sourceTexcoord.X)
                        || QuantizeTexcoord(target[targetCorner].Y) != QuantizeTexcoord(sourceTexcoord.Y))
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

    private static long QuantizeTexcoord(float value) =>
        checked((long)Math.Round(value * 10000));

    private static (long X, long Y) MakeTexcoordKey(Vector2 texcoord) =>
        (QuantizeTexcoord(texcoord.X), QuantizeTexcoord(texcoord.Y));

    private static Vector2 TransformTexcoord(Vector2 texcoord, int transform) =>
        transform switch
        {
            1 => new Vector2(texcoord.X, -texcoord.Y),
            2 => new Vector2(texcoord.X, 1 - texcoord.Y),
            _ => texcoord,
        };

    private static bool TryPropagateSplitPositionMappings(
        IReadOnlyList<Vector3> targetControlPoints,
        int[] targetToSource)
    {
        var targetMin = GetBoundsMin(targetControlPoints);
        var targetMax = GetBoundsMax(targetControlPoints);
        var tolerance = Math.Max(0.00001f, Vector3.Distance(targetMin, targetMax) * 0.000001f);
        foreach (var group in targetControlPoints
                     .Select((position, index) => (Key: QuantizePosition(position, tolerance), Index: index))
                     .GroupBy(item => item.Key))
        {
            var mappedSources = group
                .Select(item => targetToSource[item.Index])
                .Where(source => source >= 0)
                .Distinct()
                .ToArray();
            if (mappedSources.Length > 1)
            {
                return false;
            }

            if (mappedSources.Length == 1)
            {
                foreach (var item in group)
                {
                    targetToSource[item.Index] = mappedSources[0];
                }
            }
        }

        return targetToSource.All(source => source >= 0);
    }

    private static bool TryMatchPolygonsByPositions(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        IReadOnlyList<Vector3> targetControlPoints,
        IReadOnlyList<Vector3> sourceControlPoints,
        out int[] sourcePolygonIndexes,
        out int[]?[] cornerMappings,
        out string mismatchReason)
    {
        sourcePolygonIndexes = Array.Empty<int>();
        cornerMappings = Array.Empty<int[]?>();
        if (targetControlPoints.Count == 0 || sourceControlPoints.Count == 0)
        {
            mismatchReason = $"Position polygon validation failed for mesh '{meshName}': one position set is empty.";
            return false;
        }

        var targetMin = GetBoundsMin(targetControlPoints);
        var targetMax = GetBoundsMax(targetControlPoints);
        var sourceMin = GetBoundsMin(sourceControlPoints);
        var sourceMax = GetBoundsMax(sourceControlPoints);
        var targetExtent = targetMax - targetMin;
        var sourceExtent = sourceMax - sourceMin;
        var scales = new List<float>();
        foreach (var (targetAxis, sourceAxis) in new[]
                 {
                     (targetExtent.X, sourceExtent.X),
                     (targetExtent.Y, sourceExtent.Y),
                     (targetExtent.Z, sourceExtent.Z),
                 })
        {
            if (Math.Abs(targetAxis) <= 0.000001f && Math.Abs(sourceAxis) <= 0.000001f)
            {
                continue;
            }

            if (Math.Abs(targetAxis) <= 0.000001f || Math.Abs(sourceAxis) <= 0.000001f)
            {
                mismatchReason = $"Position polygon validation failed for mesh '{meshName}': position bounds differ by axis.";
                return false;
            }

            scales.Add(sourceAxis / targetAxis);
        }

        if (scales.Count == 0 || scales.Any(scale => !float.IsFinite(scale) || scale <= 0))
        {
            mismatchReason = $"Position polygon validation failed for mesh '{meshName}': a valid uniform scale could not be determined.";
            return false;
        }

        var scale = scales.Average();
        if (scales.Any(axisScale => Math.Abs(axisScale - scale) > Math.Max(0.00001f, scale * 0.0001f)))
        {
            mismatchReason = $"Position polygon validation failed for mesh '{meshName}': position bounds do not share one uniform scale.";
            return false;
        }

        var translation = sourceMin - (targetMin * scale);
        var tolerance = Math.Max(
            0.0002f,
            Vector3.Distance(sourceMin, sourceMax) * 0.00002f);
        var sourcePolygonPositions = sources
            .Select(polygon => polygon.ControlPoints
                .Select(controlPoint => sourceControlPoints[controlPoint])
                .ToArray())
            .ToArray();

        var matchedSourceIndexes = new int[targets.Count];
        var matchedCornerMappings = new int[]?[targets.Count];
        var consumed = new HashSet<int>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var targetPositions = targets[targetIndex].ControlPoints
                .Select(controlPoint => (targetControlPoints[controlPoint] * scale) + translation)
                .ToArray();
            var matches = new List<(int Index, int[] CornerMap)>();
            for (var candidate = 0; candidate < sources.Count; candidate++)
            {
                if (consumed.Contains(candidate))
                {
                    continue;
                }

                if (TryMapPolygonCornersByPositions(
                        targetPositions,
                        sourcePolygonPositions[candidate],
                        tolerance,
                        out var cornerMap))
                {
                    matches.Add((candidate, cornerMap));
                }
            }

            if (matches.Count == 0)
            {
                mismatchReason = $"Position polygon surface differs for mesh '{meshName}'.";
                return false;
            }

            var referenceColors = matches[0].CornerMap
                .Select(corner => sources[matches[0].Index].Colors![corner])
                .ToArray();
            if (matches.Skip(1).Any(match => !referenceColors.SequenceEqual(
                    match.CornerMap.Select(corner => sources[match.Index].Colors![corner]))))
            {
                mismatchReason = $"Mesh '{meshName}' has coincident polygons with different Vertex Colors.";
                return false;
            }

            matchedSourceIndexes[targetIndex] = matches[0].Index;
            matchedCornerMappings[targetIndex] = matches[0].CornerMap;
            consumed.Add(matches[0].Index);
        }

        if (consumed.Count != sources.Count)
        {
            mismatchReason = $"Not every FBX position polygon matched mesh '{meshName}'.";
            return false;
        }

        sourcePolygonIndexes = matchedSourceIndexes;
        cornerMappings = matchedCornerMappings;
        mismatchReason = string.Empty;
        return true;
    }

    private static bool TryMatchSplitControlPointPolygons(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        IReadOnlyList<Vector3> targetControlPoints,
        IReadOnlyList<Vector3> sourceControlPoints,
        out int[] sourcePolygonIndexes,
        out int[]?[] cornerMappings,
        out string mismatchReason)
    {
        sourcePolygonIndexes = Array.Empty<int>();
        cornerMappings = Array.Empty<int[]?>();

        if (!TryMapSplitControlPoints(
                targetControlPoints,
                sourceControlPoints,
                out var targetToSource,
                out mismatchReason))
        {
            mismatchReason = $"Split control-point validation failed for mesh '{meshName}': {mismatchReason}";
            return false;
        }

        return TryMatchPolygonsFromControlPointMap(
            meshName,
            targets,
            sources,
            targetToSource,
            out sourcePolygonIndexes,
            out cornerMappings,
            out mismatchReason);
    }

    private static bool TryMatchPolygonsFromControlPointMap(
        string meshName,
        IReadOnlyList<TargetPolygon> targets,
        IReadOnlyList<FbxVertexColorPolygon> sources,
        IReadOnlyList<int> targetToSource,
        out int[] sourcePolygonIndexes,
        out int[]?[] cornerMappings,
        out string mismatchReason)
    {
        sourcePolygonIndexes = Array.Empty<int>();
        cornerMappings = Array.Empty<int[]?>();
        var sourceByPolygon = sources
            .Select((polygon, index) => (Key: MakePolygonKey(polygon.ControlPoints), Index: index))
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Index).ToArray(), StringComparer.Ordinal);
        if (sourceByPolygon.Values.Any(matches => matches.Length != 1))
        {
            mismatchReason = $"FBX mesh '{meshName}' contains duplicate geometric polygons.";
            return false;
        }

        var matchedSourceIndexes = new int[targets.Count];
        var matchedCornerMappings = new int[]?[targets.Count];
        var consumed = new HashSet<int>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var mappedControlPoints = targets[targetIndex].ControlPoints
                .Select(controlPoint => targetToSource[controlPoint])
                .ToArray();
            var key = MakePolygonKey(mappedControlPoints);
            if (!sourceByPolygon.TryGetValue(key, out var candidates)
                || !consumed.Add(candidates[0])
                || !TryMapPolygonCorners(
                    mappedControlPoints,
                    sources[candidates[0]].ControlPoints,
                    out var cornerMap))
            {
                mismatchReason = $"Polygon surface differs for mesh '{meshName}'.";
                return false;
            }

            matchedSourceIndexes[targetIndex] = candidates[0];
            matchedCornerMappings[targetIndex] = cornerMap;
        }

        if (consumed.Count != sources.Count)
        {
            mismatchReason = $"Not every FBX polygon matched mesh '{meshName}'.";
            return false;
        }

        sourcePolygonIndexes = matchedSourceIndexes;
        cornerMappings = matchedCornerMappings;
        mismatchReason = string.Empty;
        return true;
    }

    private static bool TryMapSplitControlPoints(
        IReadOnlyList<Vector3> targets,
        IReadOnlyList<Vector3> sources,
        out int[] targetToSource,
        out string mismatchReason)
    {
        targetToSource = Array.Empty<int>();
        if (targets.Count == 0 || sources.Count == 0)
        {
            mismatchReason = "One position set is empty.";
            return false;
        }

        var targetMin = GetBoundsMin(targets);
        var targetMax = GetBoundsMax(targets);
        var sourceMin = GetBoundsMin(sources);
        var sourceMax = GetBoundsMax(sources);
        var targetExtent = targetMax - targetMin;
        var sourceExtent = sourceMax - sourceMin;
        var scales = new List<float>();
        foreach (var (targetAxis, sourceAxis) in new[]
                 {
                     (targetExtent.X, sourceExtent.X),
                     (targetExtent.Y, sourceExtent.Y),
                     (targetExtent.Z, sourceExtent.Z),
                 })
        {
            if (Math.Abs(targetAxis) <= 0.000001f && Math.Abs(sourceAxis) <= 0.000001f)
            {
                continue;
            }

            if (Math.Abs(targetAxis) <= 0.000001f || Math.Abs(sourceAxis) <= 0.000001f)
            {
                mismatchReason = "Position bounds differ by axis.";
                return false;
            }

            scales.Add(sourceAxis / targetAxis);
        }

        if (scales.Count == 0 || scales.Any(scale => !float.IsFinite(scale) || scale <= 0))
        {
            mismatchReason = "A valid uniform position scale could not be determined.";
            return false;
        }

        var scale = scales.Average();
        if (scales.Any(axisScale => Math.Abs(axisScale - scale) > Math.Max(0.00001f, scale * 0.0001f)))
        {
            mismatchReason = "Position bounds do not share one uniform scale.";
            return false;
        }

        var translation = sourceMin - (targetMin * scale);
        var sourceDiagonal = Vector3.Distance(sourceMin, sourceMax);
        var tolerance = Math.Max(0.0002f, sourceDiagonal * 0.00002f);
        var buckets = sources
            .Select((position, index) => (Key: QuantizePosition(position, tolerance), Index: index))
            .GroupBy(item => item.Key)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Index).ToArray());
        var mapping = new int[targets.Count];
        var usedSources = new HashSet<int>();
        for (var targetIndex = 0; targetIndex < targets.Count; targetIndex++)
        {
            var transformed = (targets[targetIndex] * scale) + translation;
            var key = QuantizePosition(transformed, tolerance);
            var bestSource = -1;
            var bestDistance = float.PositiveInfinity;
            for (var x = -1; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
            for (var z = -1; z <= 1; z++)
            {
                if (!buckets.TryGetValue((key.X + x, key.Y + y, key.Z + z), out var candidates))
                {
                    continue;
                }

                foreach (var candidate in candidates)
                {
                    var distance = Vector3.Distance(transformed, sources[candidate]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestSource = candidate;
                    }
                }
            }

            if (bestSource < 0 || bestDistance > tolerance)
            {
                mismatchReason = "A transformed DMX position has no exact FBX position match.";
                return false;
            }

            mapping[targetIndex] = bestSource;
            usedSources.Add(bestSource);
        }

        var transformedTargets = targets
            .Select(position => (position * scale) + translation)
            .ToArray();
        var unmatchedSourceCount = sources.Count(source =>
            !transformedTargets.Any(target => Vector3.Distance(target, source) <= tolerance));
        if (unmatchedSourceCount != 0)
        {
            mismatchReason =
                $"FBX has {unmatchedSourceCount} position(s) with no exact DMX match.";
            return false;
        }

        targetToSource = mapping;
        mismatchReason = string.Empty;
        return true;
    }

    private static Vector3 GetBoundsMin(IReadOnlyList<Vector3> positions) =>
        new(
            positions.Min(position => position.X),
            positions.Min(position => position.Y),
            positions.Min(position => position.Z));

    private static Vector3 GetBoundsMax(IReadOnlyList<Vector3> positions) =>
        new(
            positions.Max(position => position.X),
            positions.Max(position => position.Y),
            positions.Max(position => position.Z));

    private static (long X, long Y, long Z) QuantizePosition(Vector3 position, float tolerance) =>
        (
            checked((long)Math.Round(position.X / tolerance)),
            checked((long)Math.Round(position.Y / tolerance)),
            checked((long)Math.Round(position.Z / tolerance)));

    private static bool TryMapPolygonCornersByPositions(
        IReadOnlyList<Vector3> target,
        IReadOnlyList<Vector3> source,
        float tolerance,
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
                    if (Vector3.Distance(target[targetCorner], source[sourceCorner]) > tolerance)
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
