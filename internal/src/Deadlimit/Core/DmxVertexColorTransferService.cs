using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using Datamodel;
using Datamodel.Codecs;
using DmxColor = Datamodel.Color;

namespace Deadlimit.Core;

public sealed record DmxVertexColorTransferExportResult(
    string MeshName,
    int VertexCount,
    int FaceCount,
    int CornerCount,
    string OutputPath);

public static class DmxVertexColorTransferService
{
    public static DmxVertexColorTransferExportResult ExportMaxScriptPayload(
        string dmxPath,
        string targetMeshName,
        int targetVertexCount,
        int targetFaceCount,
        string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dmxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetMeshName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        if (targetVertexCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetVertexCount));
        }
        if (targetFaceCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetFaceCount));
        }
        if (!File.Exists(dmxPath))
        {
            throw new FileNotFoundException("Source DMX was not found.", dmxPath);
        }

        using var document = Datamodel.Datamodel.Load(dmxPath, DeferredMode.Disabled);
        var jointShapeMeshIds = DmxSkeletonShapeFilter.FindJointShapeMeshIds(document);
        var readableMeshes = new List<SourceMesh>();
        var rejectedMeshes = new List<string>();

        foreach (var mesh in document.AllElements.Where(element =>
                     string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal)))
        {
            if (DmxSkeletonShapeFilter.IsJointShape(mesh, jointShapeMeshIds))
            {
                continue;
            }

            try
            {
                readableMeshes.Add(ReadSourceMesh(mesh));
            }
            catch (InvalidDataException ex)
            {
                rejectedMeshes.Add($"{mesh.Name}: {ex.Message}");
            }
        }

        var geometryMatches = readableMeshes
            .Where(mesh => mesh.VertexCount == targetVertexCount && mesh.Polygons.Count == targetFaceCount)
            .ToArray();

        if (geometryMatches.Length == 0)
        {
            var available = readableMeshes.Count == 0
                ? "none"
                : string.Join(", ", readableMeshes.Select(mesh =>
                    $"{mesh.Name} ({mesh.VertexCount} verts, {mesh.Polygons.Count} faces)"));
            var rejected = rejectedMeshes.Count == 0
                ? string.Empty
                : $" Rejected DMX meshes: {string.Join(" | ", rejectedMeshes)}";
            throw new InvalidDataException(
                $"No DMX mesh matches selected '{targetMeshName}' ({targetVertexCount} verts, {targetFaceCount} faces). " +
                $"Readable meshes: {available}.{rejected}");
        }

        var nameMatches = geometryMatches
            .Where(mesh => NamesMatch(mesh.Name, targetMeshName))
            .ToArray();

        SourceMesh selected;
        if (nameMatches.Length == 1)
        {
            selected = nameMatches[0];
        }
        else if (nameMatches.Length > 1)
        {
            throw new InvalidDataException(
                $"More than one DMX mesh matches the selected name and topology: {string.Join(", ", nameMatches.Select(mesh => mesh.Name))}.");
        }
        else if (geometryMatches.Length == 1)
        {
            selected = geometryMatches[0];
        }
        else
        {
            throw new InvalidDataException(
                $"Selected mesh name '{targetMeshName}' did not identify one DMX mesh, and vertex/face counts are ambiguous: " +
                string.Join(", ", geometryMatches.Select(mesh => mesh.Name)) + ".");
        }

        var payload = BuildMaxScriptPayload(selected);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(fullOutputPath)
            ?? throw new ArgumentException("Output path has no parent folder.", nameof(outputPath));
        Directory.CreateDirectory(parent);
        AtomicFile.WriteAllText(
            fullOutputPath,
            payload,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new DmxVertexColorTransferExportResult(
            selected.Name,
            selected.VertexCount,
            selected.Polygons.Count,
            selected.Polygons.Sum(polygon => polygon.ControlPoints.Length),
            fullOutputPath);
    }

    private static SourceMesh ReadSourceMesh(Element mesh)
    {
        var bindState = GetRequiredElement(mesh, "bindState");
        var currentState = GetRequiredElement(mesh, "currentState");
        if (bindState.ID != currentState.ID)
        {
            throw new InvalidDataException("bindState/currentState differ; exact transfer is unsafe.");
        }

        var positions = GetRequiredArray<Vector3>(bindState, "position$0");
        var positionIndices = GetRequiredArray<int>(bindState, "position$0Indices");
        var colors = GetRequiredArray<DmxColor>(bindState, "color$0");
        var colorIndices = GetRequiredArray<int>(bindState, "color$0Indices");
        var vertexFormat = GetRequiredArray<string>(bindState, "vertexFormat");
        if (!vertexFormat.Contains("position$0", StringComparer.Ordinal)
            || !vertexFormat.Contains("color$0", StringComparer.Ordinal))
        {
            throw new InvalidDataException("position$0 or color$0 is absent from vertexFormat.");
        }
        if (positions.Count == 0 || colors.Count == 0 || positionIndices.Count == 0)
        {
            throw new InvalidDataException("position$0/color$0 stream is empty.");
        }
        if (colorIndices.Count != positionIndices.Count)
        {
            throw new InvalidDataException("position/color logical vertex streams have different lengths.");
        }
        if (positionIndices.Any(index => index < 0 || index >= positions.Count))
        {
            throw new InvalidDataException("position$0Indices contains an invalid control-point index.");
        }
        if (colorIndices.Any(index => index < 0 || index >= colors.Count))
        {
            throw new InvalidDataException("color$0Indices contains an invalid color index.");
        }

        var faceSets = GetRequiredArray<Element>(mesh, "faceSets");
        var polygons = new List<SourcePolygon>();
        foreach (var faceSet in faceSets)
        {
            var currentControlPoints = new List<int>();
            var currentColors = new List<Rgb>();
            foreach (var logicalVertex in GetRequiredArray<int>(faceSet, "faces"))
            {
                if (logicalVertex >= 0)
                {
                    if (logicalVertex >= positionIndices.Count)
                    {
                        throw new InvalidDataException("faceSet references a logical vertex outside the vertex streams.");
                    }

                    currentControlPoints.Add(positionIndices[logicalVertex] + 1);
                    currentColors.Add(ReadRgb(colors[colorIndices[logicalVertex]]));
                    continue;
                }

                if (logicalVertex != -1 || currentControlPoints.Count < 3)
                {
                    throw new InvalidDataException("faceSet contains invalid polygon termination.");
                }
                if (currentControlPoints.Distinct().Count() != currentControlPoints.Count)
                {
                    throw new InvalidDataException("polygon contains repeated control-point indices.");
                }

                polygons.Add(new SourcePolygon(currentControlPoints.ToArray(), currentColors.ToArray()));
                currentControlPoints.Clear();
                currentColors.Clear();
            }

            if (currentControlPoints.Count != 0)
            {
                throw new InvalidDataException("faceSet contains an unterminated polygon.");
            }
        }

        if (polygons.Count == 0)
        {
            throw new InvalidDataException("mesh has no polygons.");
        }

        return new SourceMesh(mesh.Name, positions.Count, polygons);
    }

    private static string BuildMaxScriptPayload(SourceMesh mesh)
    {
        var sb = new StringBuilder();
        sb.AppendLine("global deadlimitDmxVertexColorTransferPayload");
        sb.AppendLine("deadlimitDmxVertexColorTransferPayload = #(");
        sb.Append("    @\"").Append(EscapeMaxScriptVerbatimString(mesh.Name)).AppendLine("\",");
        sb.Append("    ").Append(mesh.VertexCount).AppendLine(",");
        sb.Append("    ").Append(mesh.Polygons.Count).AppendLine(",");
        sb.AppendLine("    #(");
        for (var polygonIndex = 0; polygonIndex < mesh.Polygons.Count; polygonIndex++)
        {
            var polygon = mesh.Polygons[polygonIndex];
            sb.Append("        #(")
                .Append(string.Join(",", polygon.ControlPoints))
                .Append(')');
            sb.AppendLine(polygonIndex + 1 == mesh.Polygons.Count ? string.Empty : ",");
        }
        sb.AppendLine("    ),");
        sb.AppendLine("    #(");
        for (var polygonIndex = 0; polygonIndex < mesh.Polygons.Count; polygonIndex++)
        {
            var polygon = mesh.Polygons[polygonIndex];
            sb.Append("        #(");
            for (var cornerIndex = 0; cornerIndex < polygon.Colors.Length; cornerIndex++)
            {
                if (cornerIndex > 0)
                {
                    sb.Append(',');
                }
                var color = polygon.Colors[cornerIndex];
                sb.Append('[')
                    .Append(FormatFloat(color.R)).Append(',')
                    .Append(FormatFloat(color.G)).Append(',')
                    .Append(FormatFloat(color.B)).Append(']');
            }
            sb.Append(')');
            sb.AppendLine(polygonIndex + 1 == mesh.Polygons.Count ? string.Empty : ",");
        }
        sb.AppendLine("    )");
        sb.AppendLine(")");
        return sb.ToString();
    }

    private static bool NamesMatch(string dmxName, string targetName) =>
        string.Equals(NormalizeMeshName(dmxName), NormalizeMeshName(targetName), StringComparison.Ordinal);

    private static string NormalizeMeshName(string value)
    {
        var normalized = value.Trim();
        if (normalized.EndsWith("_mesh", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^"_mesh".Length];
        }

        return new string(normalized
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static Rgb ReadRgb(DmxColor color) => new(
        NormalizeColorComponent(ReadColorComponent(color, "R", "Red")),
        NormalizeColorComponent(ReadColorComponent(color, "G", "Green")),
        NormalizeColorComponent(ReadColorComponent(color, "B", "Blue")));

    private static double ReadColorComponent(DmxColor color, params string[] names)
    {
        var boxed = (object)color;
        var type = boxed.GetType();
        foreach (var name in names)
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property is not null)
            {
                return Convert.ToDouble(property.GetValue(boxed), CultureInfo.InvariantCulture);
            }

            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field is not null)
            {
                return Convert.ToDouble(field.GetValue(boxed), CultureInfo.InvariantCulture);
            }
        }

        throw new InvalidDataException($"Unsupported Datamodel.Color layout ({type.FullName}).");
    }

    private static double NormalizeColorComponent(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new InvalidDataException("Vertex Color contains a non-finite component.");
        }

        var normalized = value > 1.000001 ? value / 255.0 : value;
        return Math.Clamp(normalized, 0.0, 1.0);
    }

    private static string FormatFloat(double value) =>
        value.ToString("0.#########", CultureInfo.InvariantCulture);

    private static string EscapeMaxScriptVerbatimString(string value) =>
        value.Replace("\"", "\"\"", StringComparison.Ordinal);

    private static IList<T> GetRequiredArray<T>(Element element, string name) =>
        element.GetArray<T>(name)
        ?? throw new InvalidDataException($"DMX element '{element.Name}' has no required '{name}' array.");

    private static Element GetRequiredElement(Element element, string name) =>
        element.Get<Element>(name)
        ?? throw new InvalidDataException($"DMX element '{element.Name}' has no required '{name}' element.");

    private sealed record SourceMesh(string Name, int VertexCount, IReadOnlyList<SourcePolygon> Polygons);
    private sealed record SourcePolygon(int[] ControlPoints, Rgb[] Colors);
    private readonly record struct Rgb(double R, double G, double B);
}
