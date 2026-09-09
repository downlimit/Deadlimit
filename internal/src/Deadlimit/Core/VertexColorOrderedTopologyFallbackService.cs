using System.Numerics;
using Datamodel;
using Datamodel.Codecs;
using DmxColor = Datamodel.Color;

namespace Deadlimit.Core;

internal static class VertexColorTransferService
{
    internal static VertexColorSidecarResult TryApply(string artistDmxPath, string preparedDmxPath)
    {
        var primary = VertexColorSidecarService.TryApply(artistDmxPath, preparedDmxPath);
        if (primary.Status is VertexColorSidecarStatus.Applied or VertexColorSidecarStatus.Missing)
        {
            return primary;
        }

        var orderedTopology = VertexColorOrderedTopologyFallbackService.TryApply(
            artistDmxPath,
            preparedDmxPath);
        return orderedTopology.Status == VertexColorSidecarStatus.Applied
            ? orderedTopology
            : primary;
    }
}

internal static class VertexColorOrderedTopologyFallbackService
{
    internal static VertexColorSidecarResult TryApply(
        string artistDmxPath,
        string preparedDmxPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistDmxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preparedDmxPath);

        var sidecarPath = VertexColorSidecarService.GetSidecarPath(artistDmxPath);
        if (!File.Exists(sidecarPath))
        {
            return Skipped(sidecarPath, "Ordered-topology fallback has no Vertex Color FBX sidecar.");
        }

        if (File.GetLastWriteTimeUtc(sidecarPath) < File.GetLastWriteTimeUtc(artistDmxPath))
        {
            return Skipped(sidecarPath, "Ordered-topology fallback rejected a stale Vertex Color FBX sidecar.");
        }

        var temporaryPath = preparedDmxPath + $".deadlimit-ordered-topology-{Guid.NewGuid():N}.tmp";
        try
        {
            using var prepared = Datamodel.Datamodel.Load(preparedDmxPath, DeferredMode.Disabled);
            var sources = AsciiFbxVertexColorReader.Read(sidecarPath);
            var sourceByName = sources
                .GroupBy(source => source.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            if (sourceByName.Values.Any(group => group.Length != 1))
            {
                return Skipped(sidecarPath, "Ordered-topology fallback rejected duplicate FBX mesh names.");
            }

            var requiredMeshes = FindRequiredMeshes(prepared);
            if (requiredMeshes.Count == 0)
            {
                return Skipped(sidecarPath, "Ordered-topology fallback found no Vertex Color meshes in the DMX.");
            }

            var transferred = 0;
            var expectedMeshNames = new List<string>();
            foreach (var target in requiredMeshes)
            {
                var sourceName = GetFbxMeshName(target.Name);
                if (!sourceByName.TryGetValue(sourceName, out var candidates))
                {
                    return Skipped(
                        sidecarPath,
                        $"Ordered-topology fallback could not find FBX mesh '{sourceName}'.");
                }

                var source = candidates[0];
                if (!TryApplyMesh(target, source, out var reason))
                {
                    return Skipped(
                        sidecarPath,
                        $"Ordered-topology fallback rejected mesh '{target.Name}': {reason}");
                }

                transferred++;
                expectedMeshNames.Add(target.Name);
            }

            prepared.Save(temporaryPath, prepared.Encoding, prepared.EncodingVersion);
            if (!ValidateWrittenColorStreams(temporaryPath, expectedMeshNames, out var validationReason))
            {
                return Skipped(
                    sidecarPath,
                    $"Ordered-topology fallback wrote an invalid DMX: {validationReason}");
            }

            File.Move(temporaryPath, preparedDmxPath, overwrite: true);
            return new VertexColorSidecarResult(
                VertexColorSidecarStatus.Applied,
                sidecarPath,
                transferred,
                $"Transferred {transferred} validated color stream(s) using ordered split-topology correspondence.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Skipped(sidecarPath, $"Ordered-topology fallback could not validate the source pair: {ex.Message}");
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
                // Best-effort cleanup only; a rejected fallback must not touch the prepared DMX.
            }
        }
    }

    internal static bool HasOrderedSplitTopologyCorrespondence(
        int[][] targetPolygons,
        int[][] sourcePolygons)
    {
        ArgumentNullException.ThrowIfNull(targetPolygons);
        ArgumentNullException.ThrowIfNull(sourcePolygons);
        return TryBuildOrderedControlPointMap(
            targetPolygons,
            sourcePolygons,
            out _,
            out _);
    }

    private static bool TryApplyMesh(
        TargetMesh target,
        FbxVertexColorMesh source,
        out string reason)
    {
        if (!source.HasColors)
        {
            reason = "FBX has no Vertex Color channel 0.";
            return false;
        }

        var targetControlPointPolygons = target.Polygons
            .Select(polygon => polygon.ControlPoints.ToArray())
            .ToArray();
        var sourceControlPointPolygons = source.Polygons
            .Select(polygon => polygon.ControlPoints.ToArray())
            .ToArray();
        if (!TryBuildOrderedControlPointMap(
                targetControlPointPolygons,
                sourceControlPointPolygons,
                out var targetToSource,
                out reason))
        {
            return false;
        }

        if (!ValidateSplitPositionOwnership(
                target.Positions,
                targetToSource,
                out reason))
        {
            return false;
        }

        var logicalVertexCount = target.PositionIndices.Count;
        var logicalColors = new DmxColor[logicalVertexCount];
        var assigned = new bool[logicalVertexCount];
        for (var polygonIndex = 0; polygonIndex < target.Polygons.Count; polygonIndex++)
        {
            var targetPolygon = target.Polygons[polygonIndex];
            var sourcePolygon = source.Polygons[polygonIndex];
            if (sourcePolygon.Colors is null
                || sourcePolygon.Colors.Count != targetPolygon.LogicalVertices.Count)
            {
                reason = $"FBX polygon {polygonIndex} has incomplete Vertex Color data.";
                return false;
            }

            for (var corner = 0; corner < targetPolygon.LogicalVertices.Count; corner++)
            {
                var logicalVertex = targetPolygon.LogicalVertices[corner];
                if (logicalVertex < 0 || logicalVertex >= logicalVertexCount)
                {
                    reason = "DMX polygon references a logical vertex outside position$0Indices.";
                    return false;
                }

                var color = sourcePolygon.Colors[corner];
                if (assigned[logicalVertex]
                    && !EqualityComparer<DmxColor>.Default.Equals(logicalColors[logicalVertex], color))
                {
                    reason =
                        $"FBX polygon order is not color-consistent for DMX logical vertex {logicalVertex}.";
                    return false;
                }

                logicalColors[logicalVertex] = color;
                assigned[logicalVertex] = true;
            }
        }

        if (assigned.Any(value => !value))
        {
            reason = "Not every DMX logical vertex is owned by the ordered polygon surface.";
            return false;
        }

        target.VertexData["color$0"] = new ColorArray(logicalColors);
        target.VertexData["color$0Indices"] = new IntArray(Enumerable.Range(0, logicalVertexCount));
        EnsureVertexFormatEntry(target.VertexData, "color$0");

        reason = string.Empty;
        return true;
    }

    private static bool TryBuildOrderedControlPointMap(
        IReadOnlyList<int[]> targetPolygons,
        IReadOnlyList<int[]> sourcePolygons,
        out IReadOnlyDictionary<int, int> targetToSource,
        out string reason)
    {
        targetToSource = new Dictionary<int, int>();
        if (targetPolygons.Count == 0 || sourcePolygons.Count == 0)
        {
            reason = "One polygon surface is empty.";
            return false;
        }
        if (targetPolygons.Count != sourcePolygons.Count)
        {
            reason = $"Polygon count differs: DMX {targetPolygons.Count}, FBX {sourcePolygons.Count}.";
            return false;
        }

        var mapping = new Dictionary<int, int>();
        var totalCorners = 0;
        var repeatedOwnershipChecks = 0;
        for (var polygonIndex = 0; polygonIndex < targetPolygons.Count; polygonIndex++)
        {
            var target = targetPolygons[polygonIndex];
            var source = sourcePolygons[polygonIndex];
            if (target.Length != source.Length)
            {
                reason =
                    $"Polygon {polygonIndex} corner count differs: DMX {target.Length}, FBX {source.Length}.";
                return false;
            }

            for (var corner = 0; corner < target.Length; corner++)
            {
                totalCorners++;
                var targetControlPoint = target[corner];
                var sourceControlPoint = source[corner];
                if (targetControlPoint < 0 || sourceControlPoint < 0)
                {
                    reason = "A polygon contains a negative control-point index.";
                    return false;
                }

                if (mapping.TryGetValue(targetControlPoint, out var existing))
                {
                    repeatedOwnershipChecks++;
                    if (existing != sourceControlPoint)
                    {
                        reason =
                            $"Ordered polygon traversal maps DMX control point {targetControlPoint} to multiple FBX control points.";
                        return false;
                    }
                }
                else
                {
                    mapping.Add(targetControlPoint, sourceControlPoint);
                }
            }
        }

        var minimumRepeatedChecks = Math.Min(64, Math.Max(2, totalCorners / 20));
        if (repeatedOwnershipChecks < minimumRepeatedChecks)
        {
            reason =
                $"Ordered polygon traversal has only {repeatedOwnershipChecks} repeated control-point ownership checks; " +
                $"at least {minimumRepeatedChecks} are required to prove correspondence.";
            return false;
        }

        targetToSource = mapping;
        reason = string.Empty;
        return true;
    }

    private static bool ValidateSplitPositionOwnership(
        IList<Vector3> targetPositions,
        IReadOnlyDictionary<int, int> targetToSource,
        out string reason)
    {
        if (targetPositions.Count == 0)
        {
            reason = "DMX position$0 is empty.";
            return false;
        }

        var min = new Vector3(
            targetPositions.Min(position => position.X),
            targetPositions.Min(position => position.Y),
            targetPositions.Min(position => position.Z));
        var max = new Vector3(
            targetPositions.Max(position => position.X),
            targetPositions.Max(position => position.Y),
            targetPositions.Max(position => position.Z));
        var tolerance = Math.Max(0.00001f, Vector3.Distance(min, max) * 0.000001f);
        var sourceRepresentativePosition = new Dictionary<int, Vector3>();
        foreach (var (targetControlPoint, sourceControlPoint) in targetToSource)
        {
            if (targetControlPoint < 0 || targetControlPoint >= targetPositions.Count)
            {
                reason = $"DMX control point {targetControlPoint} is outside position$0.";
                return false;
            }

            var position = targetPositions[targetControlPoint];
            if (sourceRepresentativePosition.TryGetValue(sourceControlPoint, out var existing))
            {
                if (Vector3.Distance(existing, position) > tolerance)
                {
                    reason =
                        $"FBX control point {sourceControlPoint} would merge distinct DMX positions.";
                    return false;
                }
            }
            else
            {
                sourceRepresentativePosition.Add(sourceControlPoint, position);
            }
        }

        reason = string.Empty;
        return true;
    }

    private static IReadOnlyList<TargetMesh> FindRequiredMeshes(Datamodel.Datamodel document)
    {
        var result = new List<TargetMesh>();
        var jointShapeMeshIds = DmxSkeletonShapeFilter.FindJointShapeMeshIds(document);
        foreach (var mesh in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal)))
        {
            if (DmxSkeletonShapeFilter.IsJointShape(mesh, jointShapeMeshIds))
            {
                continue;
            }

            var faceSets = mesh.GetArray<Element>("faceSets");
            if (faceSets is null || !faceSets.Any(UsesVertexColorMaterial))
            {
                continue;
            }

            var bindState = mesh.Get<Element>("bindState")
                ?? throw new InvalidDataException($"Vertex Color mesh '{mesh.Name}' has no bindState.");
            var currentState = mesh.Get<Element>("currentState")
                ?? throw new InvalidDataException($"Vertex Color mesh '{mesh.Name}' has no currentState.");
            if (bindState.ID != currentState.ID)
            {
                throw new InvalidDataException(
                    $"Vertex Color mesh '{mesh.Name}' uses different bind/current vertex states.");
            }

            var positionIndices = bindState.GetArray<int>("position$0Indices")
                ?? throw new InvalidDataException(
                    $"Vertex Color mesh '{mesh.Name}' has no indexed position$0 stream.");
            var positions = bindState.GetArray<Vector3>("position$0")
                ?? throw new InvalidDataException(
                    $"Vertex Color mesh '{mesh.Name}' has no position$0 array.");
            if (positionIndices.Any(index => index < 0 || index >= positions.Count))
            {
                throw new InvalidDataException(
                    $"Vertex Color mesh '{mesh.Name}' has a position index outside position$0.");
            }

            var polygons = ReadTargetPolygons(mesh.Name, faceSets, positionIndices);
            result.Add(new TargetMesh(
                mesh.Name,
                bindState,
                positionIndices,
                positions,
                polygons));
        }

        return result;
    }

    private static IReadOnlyList<TargetPolygon> ReadTargetPolygons(
        string meshName,
        IList<Element> faceSets,
        IList<int> positionIndices)
    {
        var polygons = new List<TargetPolygon>();
        foreach (var faceSet in faceSets)
        {
            var faces = faceSet.GetArray<int>("faces")
                ?? throw new InvalidDataException($"Mesh '{meshName}' has an invalid faces array.");
            var logicalVertices = new List<int>();
            var controlPoints = new List<int>();
            foreach (var value in faces)
            {
                if (value >= 0)
                {
                    if (value >= positionIndices.Count)
                    {
                        throw new InvalidDataException(
                            $"Mesh '{meshName}' has a face index outside position$0Indices.");
                    }

                    logicalVertices.Add(value);
                    controlPoints.Add(positionIndices[value]);
                    continue;
                }

                if (value != -1 || logicalVertices.Count < 3)
                {
                    throw new InvalidDataException(
                        $"Mesh '{meshName}' contains invalid polygon termination.");
                }

                polygons.Add(new TargetPolygon(
                    logicalVertices.ToArray(),
                    controlPoints.ToArray()));
                logicalVertices.Clear();
                controlPoints.Clear();
            }

            if (logicalVertices.Count != 0)
            {
                throw new InvalidDataException(
                    $"Mesh '{meshName}' contains an unterminated polygon.");
            }
        }

        return polygons;
    }

    private static bool ValidateWrittenColorStreams(
        string path,
        IReadOnlyCollection<string> expectedMeshNames,
        out string reason)
    {
        using var document = Datamodel.Datamodel.Load(path, DeferredMode.Disabled);
        var validated = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mesh in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal)
                     && expectedMeshNames.Contains(element.Name)))
        {
            var vertexData = mesh.Get<Element>("bindState");
            if (vertexData is null)
            {
                reason = $"Mesh '{mesh.Name}' has no bindState after reload.";
                return false;
            }

            var colors = vertexData.GetArray<DmxColor>("color$0");
            var colorIndices = vertexData.GetArray<int>("color$0Indices");
            var positions = vertexData.GetArray<int>("position$0Indices");
            var vertexFormat = vertexData.GetArray<string>("vertexFormat");
            if (colors is null
                || colorIndices is null
                || positions is null
                || vertexFormat is null
                || colors.Count == 0
                || colorIndices.Count != positions.Count
                || colorIndices.Any(index => index < 0 || index >= colors.Count)
                || !vertexFormat.Contains("color$0", StringComparer.Ordinal))
            {
                reason = $"Mesh '{mesh.Name}' has an invalid color$0 stream after reload.";
                return false;
            }

            validated.Add(mesh.Name);
        }

        if (validated.Count != expectedMeshNames.Count)
        {
            reason =
                $"Expected {expectedMeshNames.Count} Vertex Color mesh(es), reloaded {validated.Count}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static void EnsureVertexFormatEntry(Element vertexData, string streamName)
    {
        var format = vertexData.GetArray<string>("vertexFormat")
            ?? throw new InvalidDataException("DmeVertexData has no valid vertexFormat array.");
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

    private static bool UsesVertexColorMaterial(Element faceSet)
    {
        var material = faceSet.Get<Element>("material");
        if (material is null)
        {
            return false;
        }

        if (material.Name?.Contains("vertexcolor", StringComparison.OrdinalIgnoreCase) == true)
        {
            return true;
        }

        return material.ContainsKey("mtlName")
            && material.Get<string>("mtlName")?.Contains(
                "vertexcolor",
                StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string GetFbxMeshName(string dmxMeshName) =>
        dmxMeshName.EndsWith("_mesh", StringComparison.Ordinal)
            ? dmxMeshName[..^"_mesh".Length]
            : dmxMeshName;

    private static VertexColorSidecarResult Skipped(string sidecarPath, string message) =>
        new(VertexColorSidecarStatus.Skipped, sidecarPath, 0, message);

    private sealed record TargetMesh(
        string Name,
        Element VertexData,
        IList<int> PositionIndices,
        IList<Vector3> Positions,
        IReadOnlyList<TargetPolygon> Polygons);

    private sealed record TargetPolygon(
        IReadOnlyList<int> LogicalVertices,
        IReadOnlyList<int> ControlPoints);
}
