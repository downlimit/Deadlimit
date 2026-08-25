using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using DmxColor = Datamodel.Color;

namespace Deadlimit.Core;

internal sealed record FbxVertexColorPolygon(
    IReadOnlyList<int> ControlPoints,
    IReadOnlyList<DmxColor>? Colors,
    IReadOnlyList<Vector2>? Texcoords);

internal sealed record FbxVertexColorMesh(
    string Name,
    IReadOnlyList<Vector3> ControlPoints,
    IReadOnlyList<FbxVertexColorPolygon> Polygons)
{
    public bool HasColors => Polygons.Any(polygon => polygon.Colors is not null);
}

internal sealed record FbxModel(
    string Name,
    Matrix4x4 LocalTransform);

internal static partial class AsciiFbxVertexColorReader
{
    public static IReadOnlyList<FbxVertexColorMesh> Read(string path)
    {
        var text = File.ReadAllText(path);
        if (!text.StartsWith("; FBX", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Vertex-color FBX must use Autodesk ASCII FBX format.");
        }

        var models = ParseModels(text);
        var meshModelIds = MeshModelRegex().Matches(text)
            .Cast<Match>()
            .Select(match => ParseInt64(match.Groups[1].Value, "mesh model id"))
            .ToHashSet();
        var geometryMatches = GeometryHeaderRegex().Matches(text).Cast<Match>().ToArray();
        var geometryIds = geometryMatches
            .Select(match => ParseInt64(match.Groups[1].Value, "geometry id"))
            .ToHashSet();
        var geometryToModel = ParseGeometryConnections(text, meshModelIds, geometryIds);
        var modelParents = ParseModelParents(text, models.Keys);
        var worldTransforms = BuildWorldTransforms(models, modelParents);
        var result = new List<FbxVertexColorMesh>();

        foreach (var match in geometryMatches)
        {
            var geometryId = ParseInt64(match.Groups[1].Value, "geometry id");
            if (!geometryToModel.TryGetValue(geometryId, out var modelId)
                || !models.TryGetValue(modelId, out var model)
                || !worldTransforms.TryGetValue(modelId, out var worldTransform))
            {
                continue;
            }

            var block = ExtractBlock(text, match.Index + match.Length - 1);
            result.Add(ParseGeometry(model.Name, block, worldTransform));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("FBX contains no connected mesh geometry.");
        }

        return result;
    }

    private static Dictionary<long, FbxModel> ParseModels(string text)
    {
        var result = new Dictionary<long, FbxModel>();
        foreach (Match match in ModelRegex().Matches(text))
        {
            var id = ParseInt64(match.Groups[1].Value, "model id");
            var block = ExtractBlock(text, match.Index + match.Length - 1);
            var translation = ReadOptionalModelVector(block, "Lcl Translation", Vector3.Zero);
            var rotation = ReadOptionalModelVector(block, "Lcl Rotation", Vector3.Zero);
            var scaling = ReadOptionalModelVector(block, "Lcl Scaling", Vector3.One);
            var preRotation = ReadOptionalModelVector(block, "PreRotation", Vector3.Zero);
            var localTransform = Matrix4x4.CreateScale(scaling)
                * CreateEulerXyz(rotation)
                * CreateEulerXyz(preRotation)
                * Matrix4x4.CreateTranslation(translation);
            if (!result.TryAdd(id, new FbxModel(match.Groups[2].Value, localTransform)))
            {
                throw new InvalidDataException($"FBX contains duplicate model id {id}.");
            }
        }

        return result;
    }

    private static Dictionary<long, long> ParseModelParents(
        string text,
        IEnumerable<long> modelIds)
    {
        var models = modelIds.ToHashSet();
        var result = new Dictionary<long, long>();
        foreach (Match match in ObjectConnectionRegex().Matches(text))
        {
            var child = ParseInt64(match.Groups[1].Value, "connection child id");
            var parent = ParseInt64(match.Groups[2].Value, "connection parent id");
            if (!models.Contains(child) || !models.Contains(parent))
            {
                continue;
            }

            if (!result.TryAdd(child, parent))
            {
                throw new InvalidDataException($"FBX model {child} has more than one parent.");
            }
        }

        return result;
    }

    private static Dictionary<long, Matrix4x4> BuildWorldTransforms(
        IReadOnlyDictionary<long, FbxModel> models,
        IReadOnlyDictionary<long, long> parents)
    {
        var result = new Dictionary<long, Matrix4x4>();
        var visiting = new HashSet<long>();

        Matrix4x4 Resolve(long id)
        {
            if (result.TryGetValue(id, out var existing))
            {
                return existing;
            }

            if (!visiting.Add(id))
            {
                throw new InvalidDataException("FBX model hierarchy contains a cycle.");
            }

            var world = models[id].LocalTransform;
            if (parents.TryGetValue(id, out var parentId))
            {
                world *= Resolve(parentId);
            }

            visiting.Remove(id);
            result.Add(id, world);
            return world;
        }

        foreach (var id in models.Keys)
        {
            Resolve(id);
        }

        return result;
    }

    private static Dictionary<long, long> ParseGeometryConnections(
        string text,
        IEnumerable<long> meshModelIds,
        IReadOnlySet<long> geometryIds)
    {
        var meshModels = meshModelIds.ToHashSet();
        var result = new Dictionary<long, long>();
        foreach (Match match in ObjectConnectionRegex().Matches(text))
        {
            var child = ParseInt64(match.Groups[1].Value, "connection child id");
            var parent = ParseInt64(match.Groups[2].Value, "connection parent id");
            if (!meshModels.Contains(parent) || !geometryIds.Contains(child))
            {
                continue;
            }

            if (!result.TryAdd(child, parent))
            {
                throw new InvalidDataException($"FBX geometry {child} is connected more than once.");
            }
        }

        return result;
    }

    private static FbxVertexColorMesh ParseGeometry(
        string name,
        string block,
        Matrix4x4 worldTransform)
    {
        var vertexValues = ParseDoubleArray(block, "Vertices");
        if (vertexValues.Length == 0 || vertexValues.Length % 3 != 0)
        {
            throw new InvalidDataException($"FBX mesh '{name}' has an invalid Vertices array.");
        }

        var controlPoints = new Vector3[vertexValues.Length / 3];
        for (var index = 0; index < controlPoints.Length; index++)
        {
            var localPosition = new Vector3(
                checked((float)vertexValues[index * 3]),
                checked((float)vertexValues[(index * 3) + 1]),
                checked((float)vertexValues[(index * 3) + 2]));
            var worldPosition = Vector3.Transform(localPosition, worldTransform);
            controlPoints[index] = new Vector3(
                worldPosition.X,
                -worldPosition.Z,
                worldPosition.Y);
        }

        var encodedPolygonIndices = ParseIntArray(block, "PolygonVertexIndex");
        var polygonControlPoints = DecodePolygons(name, encodedPolygonIndices, controlPoints.Length);
        var polygonColors = ParseColors(name, block, polygonControlPoints);
        var polygonTexcoords = ParseTexcoords(name, block, polygonControlPoints);
        var polygons = new FbxVertexColorPolygon[polygonControlPoints.Count];
        for (var index = 0; index < polygons.Length; index++)
        {
            polygons[index] = new FbxVertexColorPolygon(
                polygonControlPoints[index],
                polygonColors?[index],
                polygonTexcoords?[index]);
        }

        return new FbxVertexColorMesh(name, controlPoints, polygons);
    }

    private static IReadOnlyList<int[]> DecodePolygons(
        string meshName,
        IReadOnlyList<int> encoded,
        int controlPointCount)
    {
        var polygons = new List<int[]>();
        var current = new List<int>();
        foreach (var encodedIndex in encoded)
        {
            var isLast = encodedIndex < 0;
            var controlPoint = isLast ? (-encodedIndex) - 1 : encodedIndex;
            if (controlPoint < 0 || controlPoint >= controlPointCount)
            {
                throw new InvalidDataException(
                    $"FBX mesh '{meshName}' has a polygon index outside its control-point array.");
            }

            current.Add(controlPoint);
            if (!isLast)
            {
                continue;
            }

            if (current.Count < 3)
            {
                throw new InvalidDataException($"FBX mesh '{meshName}' contains a degenerate polygon.");
            }

            polygons.Add(current.ToArray());
            current.Clear();
        }

        if (current.Count != 0)
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' has an unterminated polygon-index array.");
        }

        return polygons;
    }

    private static IReadOnlyList<DmxColor[]>? ParseColors(
        string meshName,
        string geometryBlock,
        IReadOnlyList<int[]> polygons)
    {
        var header = LayerElementColorRegex().Match(geometryBlock);
        if (!header.Success)
        {
            return null;
        }

        var colorBlock = ExtractBlock(geometryBlock, header.Index + header.Length - 1);
        var mapping = ReadRequiredString(colorBlock, "MappingInformationType");
        var reference = ReadRequiredString(colorBlock, "ReferenceInformationType");
        if (mapping is not ("ByPolygonVertex" or "ByControlPoint"))
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' uses unsupported color mapping '{mapping}'.");
        }

        if (reference is not ("Direct" or "IndexToDirect"))
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' uses unsupported color reference '{reference}'.");
        }

        var colorValues = ParseDoubleArray(colorBlock, "Colors");
        if (colorValues.Length == 0 || colorValues.Length % 4 != 0)
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' has an invalid Colors array.");
        }

        var directColors = new DmxColor[colorValues.Length / 4];
        for (var index = 0; index < directColors.Length; index++)
        {
            directColors[index] = new DmxColor(
                ToColorByte(colorValues[index * 4]),
                ToColorByte(colorValues[(index * 4) + 1]),
                ToColorByte(colorValues[(index * 4) + 2]),
                ToColorByte(colorValues[(index * 4) + 3]));
        }

        var colorIndices = reference == "IndexToDirect"
            ? ParseIntArray(colorBlock, "ColorIndex")
            : Array.Empty<int>();
        var result = new List<DmxColor[]>(polygons.Count);
        var polygonVertexIndex = 0;
        foreach (var polygon in polygons)
        {
            var colors = new DmxColor[polygon.Length];
            for (var corner = 0; corner < polygon.Length; corner++)
            {
                var mappingIndex = mapping == "ByPolygonVertex"
                    ? polygonVertexIndex
                    : polygon[corner];
                var directIndex = reference == "Direct"
                    ? mappingIndex
                    : GetColorIndex(meshName, colorIndices, mappingIndex);
                if (directIndex < 0 || directIndex >= directColors.Length)
                {
                    throw new InvalidDataException(
                        $"FBX mesh '{meshName}' has a color index outside its Colors array.");
                }

                colors[corner] = directColors[directIndex];
                polygonVertexIndex++;
            }

            result.Add(colors);
        }

        return result;
    }

    private static IReadOnlyList<Vector2[]>? ParseTexcoords(
        string meshName,
        string geometryBlock,
        IReadOnlyList<int[]> polygons)
    {
        var header = LayerElementUvRegex().Match(geometryBlock);
        if (!header.Success)
        {
            return null;
        }

        var uvBlock = ExtractBlock(geometryBlock, header.Index + header.Length - 1);
        var mapping = ReadRequiredString(uvBlock, "MappingInformationType");
        var reference = ReadRequiredString(uvBlock, "ReferenceInformationType");
        if (mapping is not ("ByPolygonVertex" or "ByControlPoint"))
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' uses unsupported UV mapping '{mapping}'.");
        }

        if (reference is not ("Direct" or "IndexToDirect"))
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' uses unsupported UV reference '{reference}'.");
        }

        var uvValues = ParseDoubleArray(uvBlock, "UV");
        if (uvValues.Length == 0 || uvValues.Length % 2 != 0)
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' has an invalid UV array.");
        }

        var directUvs = new Vector2[uvValues.Length / 2];
        for (var index = 0; index < directUvs.Length; index++)
        {
            directUvs[index] = new Vector2(
                checked((float)uvValues[index * 2]),
                checked((float)uvValues[(index * 2) + 1]));
        }

        var uvIndices = reference == "IndexToDirect"
            ? ParseIntArray(uvBlock, "UVIndex")
            : Array.Empty<int>();
        var result = new List<Vector2[]>(polygons.Count);
        var polygonVertexIndex = 0;
        foreach (var polygon in polygons)
        {
            var texcoords = new Vector2[polygon.Length];
            for (var corner = 0; corner < polygon.Length; corner++)
            {
                var mappingIndex = mapping == "ByPolygonVertex"
                    ? polygonVertexIndex
                    : polygon[corner];
                var directIndex = reference == "Direct"
                    ? mappingIndex
                    : GetUvIndex(meshName, uvIndices, mappingIndex);
                if (directIndex < 0 || directIndex >= directUvs.Length)
                {
                    throw new InvalidDataException(
                        $"FBX mesh '{meshName}' has a UV index outside its UV array.");
                }

                texcoords[corner] = directUvs[directIndex];
                polygonVertexIndex++;
            }

            result.Add(texcoords);
        }

        return result;
    }

    private static int GetColorIndex(string meshName, IReadOnlyList<int> indices, int index)
    {
        if (index < 0 || index >= indices.Count)
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' has an incomplete ColorIndex array.");
        }

        return indices[index];
    }

    private static int GetUvIndex(string meshName, IReadOnlyList<int> indices, int index)
    {
        if (index < 0 || index >= indices.Count)
        {
            throw new InvalidDataException(
                $"FBX mesh '{meshName}' has an incomplete UVIndex array.");
        }

        return indices[index];
    }

    private static byte ToColorByte(double value)
    {
        if (!double.IsFinite(value) || value < -0.0001 || value > 1.0001)
        {
            throw new InvalidDataException($"FBX contains an invalid color component '{value}'.");
        }

        return checked((byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero));
    }

    private static string ReadRequiredString(string block, string propertyName)
    {
        var match = Regex.Match(
            block,
            $@"(?m)^\s*{Regex.Escape(propertyName)}:\s*""([^""]+)""\s*$",
            RegexOptions.CultureInvariant);
        return match.Success
            ? match.Groups[1].Value
            : throw new InvalidDataException($"FBX property '{propertyName}' is missing.");
    }

    private static Vector3 ReadOptionalModelVector(
        string block,
        string propertyName,
        Vector3 fallback)
    {
        var match = Regex.Match(
            block,
            $@"(?m)^\s*P:\s*""{Regex.Escape(propertyName)}"".*?,([-+\d.eE]+),([-+\d.eE]+),([-+\d.eE]+)\s*$",
            RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return fallback;
        }

        return new Vector3(
            checked((float)double.Parse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            checked((float)double.Parse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture)),
            checked((float)double.Parse(match.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture)));
    }

    private static Matrix4x4 CreateEulerXyz(Vector3 degrees)
    {
        var radians = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationZ(radians.Z);
    }

    private static double[] ParseDoubleArray(string block, string propertyName) =>
        ExtractArrayPayload(block, propertyName)
            .Split([',', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture))
            .ToArray();

    private static int[] ParseIntArray(string block, string propertyName) =>
        ExtractArrayPayload(block, propertyName)
            .Split([',', '\r', '\n', '\t', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .ToArray();

    private static string ExtractArrayPayload(string block, string propertyName)
    {
        var header = Regex.Match(
            block,
            $@"(?m)^\s*{Regex.Escape(propertyName)}:\s*\*\d+\s*\{{",
            RegexOptions.CultureInvariant);
        if (!header.Success)
        {
            throw new InvalidDataException($"FBX array '{propertyName}' is missing.");
        }

        var arrayBlock = ExtractBlock(block, header.Index + header.Length - 1);
        var payload = Regex.Match(
            arrayBlock,
            @"(?s)^\{\s*a:\s*(.*?)\s*\}$",
            RegexOptions.CultureInvariant);
        return payload.Success
            ? payload.Groups[1].Value
            : throw new InvalidDataException($"FBX array '{propertyName}' has no payload.");
    }

    private static string ExtractBlock(string text, int openingBraceIndex)
    {
        if (openingBraceIndex < 0
            || openingBraceIndex >= text.Length
            || text[openingBraceIndex] != '{')
        {
            throw new InvalidDataException("FBX block does not start with an opening brace.");
        }

        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var index = openingBraceIndex; index < text.Length; index++)
        {
            var character = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (character == '\\')
                {
                    escaped = true;
                }
                else if (character == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (character == '"')
            {
                inString = true;
            }
            else if (character == '{')
            {
                depth++;
            }
            else if (character == '}' && --depth == 0)
            {
                return text.Substring(openingBraceIndex, index - openingBraceIndex + 1);
            }
        }

        throw new InvalidDataException("FBX contains an unterminated block.");
    }

    private static long ParseInt64(string value, string description) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new InvalidDataException($"FBX contains an invalid {description} '{value}'.");

    [GeneratedRegex(
        @"(?m)^\s*Geometry:\s*(-?\d+),\s*""Geometry::[^""]*"",\s*""Mesh""\s*\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex GeometryHeaderRegex();

    [GeneratedRegex(
        @"(?m)^\s*Model:\s*(-?\d+),\s*""Model::([^""]+)"",\s*""Mesh""\s*\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex MeshModelRegex();

    [GeneratedRegex(
        @"(?m)^\s*Model:\s*(-?\d+),\s*""Model::([^""]+)"",\s*""[^""]+""\s*\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex ModelRegex();

    [GeneratedRegex(
        @"(?m)^\s*C:\s*""OO"",\s*(-?\d+),\s*(-?\d+)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ObjectConnectionRegex();

    [GeneratedRegex(
        @"(?m)^\s*LayerElementColor:\s*0\s*\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex LayerElementColorRegex();

    [GeneratedRegex(
        @"(?m)^\s*LayerElementUV:\s*0\s*\{",
        RegexOptions.CultureInvariant)]
    private static partial Regex LayerElementUvRegex();
}
