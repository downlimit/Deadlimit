using System.Globalization;
using System.IO.Compression;
using System.Numerics;
using System.Text;
using DmxColor = Datamodel.Color;

namespace Deadlimit.Core;

internal static class BinaryFbxVertexColorReader
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\x1a\0");

    public static bool IsBinary(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length < Header.Length)
        {
            return false;
        }

        Span<byte> actual = stackalloc byte[Header.Length];
        stream.ReadExactly(actual);
        return actual.SequenceEqual(Header);
    }

    public static IReadOnlyList<FbxVertexColorMesh> Read(string path)
    {
        var roots = ReadRoots(path);

        var objects = roots.FirstOrDefault(node => node.Name == "Objects")
            ?? throw new InvalidDataException("Binary FBX has no Objects section.");
        var connections = roots.FirstOrDefault(node => node.Name == "Connections")
            ?? throw new InvalidDataException("Binary FBX has no Connections section.");

        var modelNodes = objects.Children
            .Where(node => node.Name == "Model" && node.Properties.Count >= 3)
            .ToArray();
        var models = modelNodes.ToDictionary(
            node => ReadInt64(node.Properties[0], "model id"),
            ParseModel);

        var meshModelIds = modelNodes
            .Where(node => string.Equals(ReadString(node.Properties[2], "model type"), "Mesh", StringComparison.Ordinal))
            .Select(node => ReadInt64(node.Properties[0], "mesh model id"))
            .ToHashSet();

        var geometryNodes = objects.Children
            .Where(node => node.Name == "Geometry"
                && node.Properties.Count >= 3
                && string.Equals(ReadString(node.Properties[2], "geometry type"), "Mesh", StringComparison.Ordinal))
            .ToArray();
        var geometryIds = geometryNodes
            .Select(node => ReadInt64(node.Properties[0], "geometry id"))
            .ToHashSet();

        var objectConnections = connections.Children
            .Where(node => node.Name == "C"
                && node.Properties.Count >= 3
                && string.Equals(ReadString(node.Properties[0], "connection type"), "OO", StringComparison.Ordinal))
            .Select(node => (
                Child: ReadInt64(node.Properties[1], "connection child id"),
                Parent: ReadInt64(node.Properties[2], "connection parent id")))
            .ToArray();

        var geometryToModel = new Dictionary<long, long>();
        foreach (var connection in objectConnections)
        {
            if (!geometryIds.Contains(connection.Child) || !meshModelIds.Contains(connection.Parent))
            {
                continue;
            }

            if (!geometryToModel.TryAdd(connection.Child, connection.Parent))
            {
                throw new InvalidDataException($"FBX geometry {connection.Child} is connected more than once.");
            }
        }

        var modelParents = new Dictionary<long, long>();
        foreach (var connection in objectConnections)
        {
            if (!models.ContainsKey(connection.Child) || !models.ContainsKey(connection.Parent))
            {
                continue;
            }

            if (!modelParents.TryAdd(connection.Child, connection.Parent))
            {
                throw new InvalidDataException($"FBX model {connection.Child} has more than one parent.");
            }
        }

        var worldTransforms = BuildWorldTransforms(models, modelParents);
        var result = new List<FbxVertexColorMesh>();
        foreach (var geometry in geometryNodes)
        {
            var geometryId = ReadInt64(geometry.Properties[0], "geometry id");
            if (!geometryToModel.TryGetValue(geometryId, out var modelId)
                || !models.TryGetValue(modelId, out var model)
                || !worldTransforms.TryGetValue(modelId, out var worldTransform))
            {
                continue;
            }

            result.Add(ParseGeometry(model.Name, geometry, worldTransform));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("FBX contains no connected mesh geometry.");
        }

        return result;
    }

    public static IReadOnlySet<string> ReadJointNames(string path)
    {
        var roots = ReadRoots(path);
        var objects = roots.FirstOrDefault(node => node.Name == "Objects")
            ?? throw new InvalidDataException("Binary FBX has no Objects section.");
        var connections = roots.FirstOrDefault(node => node.Name == "Connections");

        var modelNodes = objects.Children
            .Where(node => node.Name == "Model" && node.Properties.Count >= 3)
            .ToArray();
        var modelNamesById = modelNodes.ToDictionary(
            node => ReadInt64(node.Properties[0], "model id"),
            node => NormalizeObjectName(ReadString(node.Properties[1], "model name"), "Model::"));
        var names = modelNodes
            .Where(node =>
            {
                var modelType = ReadString(node.Properties[2], "model type");
                return string.Equals(modelType, "LimbNode", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(modelType, "Root", StringComparison.OrdinalIgnoreCase);
            })
            .Select(node => NormalizeObjectName(ReadString(node.Properties[1], "model name"), "Model::"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.Ordinal);

        if (connections is null)
        {
            return names;
        }

        var clusterIds = objects.Children
            .Where(node => node.Name == "Deformer"
                && node.Properties.Count >= 3
                && string.Equals(
                    ReadString(node.Properties[2], "deformer type"),
                    "Cluster",
                    StringComparison.OrdinalIgnoreCase))
            .Select(node => ReadInt64(node.Properties[0], "cluster deformer id"))
            .ToHashSet();
        if (clusterIds.Count == 0)
        {
            return names;
        }

        foreach (var connection in connections.Children.Where(node =>
                     node.Name == "C"
                     && node.Properties.Count >= 3
                     && string.Equals(
                         ReadString(node.Properties[0], "connection type"),
                         "OO",
                         StringComparison.Ordinal)))
        {
            var child = ReadInt64(connection.Properties[1], "connection child id");
            var parent = ReadInt64(connection.Properties[2], "connection parent id");
            if (clusterIds.Contains(parent)
                && modelNamesById.TryGetValue(child, out var modelName)
                && !string.IsNullOrWhiteSpace(modelName))
            {
                names.Add(modelName);
            }
        }

        return names;
    }

    private static IReadOnlyList<FbxNode> ReadRoots(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

        var header = reader.ReadBytes(Header.Length);
        if (!header.AsSpan().SequenceEqual(Header))
        {
            throw new InvalidDataException("FBX is not an Autodesk Binary FBX file.");
        }

        var version = reader.ReadUInt32();
        var roots = new List<FbxNode>();
        while (stream.Position < stream.Length)
        {
            var node = ReadNode(reader, version);
            if (node is null)
            {
                break;
            }

            roots.Add(node);
        }

        return roots;
    }

    private static FbxNode? ReadNode(BinaryReader reader, uint version)
    {
        var stream = reader.BaseStream;
        var wide = version >= 7500;
        var endOffset = wide ? reader.ReadUInt64() : reader.ReadUInt32();
        var propertyCount = wide ? reader.ReadUInt64() : reader.ReadUInt32();
        _ = wide ? reader.ReadUInt64() : reader.ReadUInt32();
        var nameLength = reader.ReadByte();

        if (endOffset == 0)
        {
            return null;
        }

        if (endOffset > checked((ulong)stream.Length) || endOffset < checked((ulong)stream.Position))
        {
            throw new InvalidDataException("Binary FBX node has an invalid end offset.");
        }

        var name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        var properties = new object[checked((int)propertyCount)];
        for (var index = 0; index < properties.Length; index++)
        {
            properties[index] = ReadProperty(reader);
        }

        var children = new List<FbxNode>();
        while (checked((ulong)stream.Position) < endOffset)
        {
            var child = ReadNode(reader, version);
            if (child is null)
            {
                break;
            }

            children.Add(child);
        }

        stream.Position = checked((long)endOffset);
        return new FbxNode(name, properties, children);
    }

    private static object ReadProperty(BinaryReader reader)
    {
        var type = (char)reader.ReadByte();
        return type switch
        {
            'Y' => reader.ReadInt16(),
            'C' => reader.ReadByte() != 0,
            'I' => reader.ReadInt32(),
            'F' => reader.ReadSingle(),
            'D' => reader.ReadDouble(),
            'L' => reader.ReadInt64(),
            'S' => Encoding.UTF8.GetString(reader.ReadBytes(checked((int)reader.ReadUInt32()))),
            'R' => reader.ReadBytes(checked((int)reader.ReadUInt32())),
            'i' => ReadArray(reader, sizeof(int), bytes => ToInt32Array(bytes)),
            'l' => ReadArray(reader, sizeof(long), bytes => ToInt64Array(bytes)),
            'f' => ReadArray(reader, sizeof(float), bytes => ToSingleArray(bytes)),
            'd' => ReadArray(reader, sizeof(double), bytes => ToDoubleArray(bytes)),
            'b' or 'c' => ReadArray(reader, sizeof(byte), bytes => bytes),
            _ => throw new InvalidDataException($"Binary FBX contains unsupported property type '{type}'."),
        };
    }

    private static T ReadArray<T>(BinaryReader reader, int elementSize, Func<byte[], T> materialize)
    {
        var elementCount = reader.ReadUInt32();
        var encoding = reader.ReadUInt32();
        var storedLength = reader.ReadUInt32();
        var stored = reader.ReadBytes(checked((int)storedLength));
        var expectedLength = checked((int)elementCount * elementSize);

        byte[] raw;
        if (encoding == 0)
        {
            raw = stored;
        }
        else if (encoding == 1)
        {
            using var source = new MemoryStream(stored, writable: false);
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var target = new MemoryStream(expectedLength);
            zlib.CopyTo(target);
            raw = target.ToArray();
        }
        else
        {
            throw new InvalidDataException($"Binary FBX uses unsupported array encoding {encoding}.");
        }

        if (raw.Length != expectedLength)
        {
            throw new InvalidDataException(
                $"Binary FBX array decoded to {raw.Length} bytes; expected {expectedLength}.");
        }

        return materialize(raw);
    }

    private static int[] ToInt32Array(byte[] bytes)
    {
        var result = new int[bytes.Length / sizeof(int)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static long[] ToInt64Array(byte[] bytes)
    {
        var result = new long[bytes.Length / sizeof(long)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static float[] ToSingleArray(byte[] bytes)
    {
        var result = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static double[] ToDoubleArray(byte[] bytes)
    {
        var result = new double[bytes.Length / sizeof(double)];
        Buffer.BlockCopy(bytes, 0, result, 0, bytes.Length);
        return result;
    }

    private static FbxModel ParseModel(FbxNode node)
    {
        var name = NormalizeObjectName(ReadString(node.Properties[1], "model name"), "Model::");
        var properties70 = node.Children.FirstOrDefault(child => child.Name == "Properties70");
        var translation = ReadModelVector(properties70, "Lcl Translation", Vector3.Zero);
        var rotation = ReadModelVector(properties70, "Lcl Rotation", Vector3.Zero);
        var scaling = ReadModelVector(properties70, "Lcl Scaling", Vector3.One);
        var preRotation = ReadModelVector(properties70, "PreRotation", Vector3.Zero);
        var localTransform = Matrix4x4.CreateScale(scaling)
            * CreateEulerXyz(rotation)
            * CreateEulerXyz(preRotation)
            * Matrix4x4.CreateTranslation(translation);
        return new FbxModel(name, localTransform);
    }

    private static Vector3 ReadModelVector(FbxNode? properties70, string propertyName, Vector3 fallback)
    {
        if (properties70 is null)
        {
            return fallback;
        }

        var property = properties70.Children.FirstOrDefault(node =>
            node.Name == "P"
            && node.Properties.Count >= 5
            && string.Equals(ReadString(node.Properties[0], "model property name"), propertyName, StringComparison.Ordinal));
        if (property is null || property.Properties.Count < 7)
        {
            return fallback;
        }

        var values = property.Properties
            .Skip(4)
            .Select(TryReadDouble)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .TakeLast(3)
            .ToArray();
        return values.Length == 3
            ? new Vector3(checked((float)values[0]), checked((float)values[1]), checked((float)values[2]))
            : fallback;
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

    private static FbxVertexColorMesh ParseGeometry(
        string name,
        FbxNode geometry,
        Matrix4x4 worldTransform)
    {
        var vertexValues = ReadDoubleArray(FindChild(geometry, "Vertices"), "Vertices");
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
            controlPoints[index] = new Vector3(worldPosition.X, -worldPosition.Z, worldPosition.Y);
        }

        var polygonIndices = ReadIntArray(FindChild(geometry, "PolygonVertexIndex"), "PolygonVertexIndex");
        var polygonControlPoints = DecodePolygons(name, polygonIndices, controlPoints.Length);
        var polygonColors = ParseColors(name, geometry, polygonControlPoints);
        var polygonTexcoords = ParseTexcoords(name, geometry, polygonControlPoints);
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
        FbxNode geometry,
        IReadOnlyList<int[]> polygons)
    {
        var layer = geometry.Children.FirstOrDefault(node => node.Name == "LayerElementColor");
        if (layer is null)
        {
            return null;
        }

        var mapping = ReadNodeString(layer, "MappingInformationType");
        var reference = ReadNodeString(layer, "ReferenceInformationType");
        if (mapping is not ("ByPolygonVertex" or "ByControlPoint"))
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' uses unsupported color mapping '{mapping}'.");
        }

        if (reference is not ("Direct" or "IndexToDirect"))
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' uses unsupported color reference '{reference}'.");
        }

        var colorValues = ReadDoubleArray(FindChild(layer, "Colors"), "Colors");
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
            ? ReadIntArray(FindChild(layer, "ColorIndex"), "ColorIndex")
            : Array.Empty<int>();
        return MapLayerValues(
            meshName,
            polygons,
            mapping,
            reference,
            directColors,
            colorIndices,
            "color");
    }

    private static IReadOnlyList<Vector2[]>? ParseTexcoords(
        string meshName,
        FbxNode geometry,
        IReadOnlyList<int[]> polygons)
    {
        var layer = geometry.Children.FirstOrDefault(node => node.Name == "LayerElementUV");
        if (layer is null)
        {
            return null;
        }

        var mapping = ReadNodeString(layer, "MappingInformationType");
        var reference = ReadNodeString(layer, "ReferenceInformationType");
        if (mapping is not ("ByPolygonVertex" or "ByControlPoint"))
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' uses unsupported UV mapping '{mapping}'.");
        }

        if (reference is not ("Direct" or "IndexToDirect"))
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' uses unsupported UV reference '{reference}'.");
        }

        var uvValues = ReadDoubleArray(FindChild(layer, "UV"), "UV");
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
            ? ReadIntArray(FindChild(layer, "UVIndex"), "UVIndex")
            : Array.Empty<int>();
        return MapLayerValues(
            meshName,
            polygons,
            mapping,
            reference,
            directUvs,
            uvIndices,
            "UV");
    }

    private static IReadOnlyList<T[]> MapLayerValues<T>(
        string meshName,
        IReadOnlyList<int[]> polygons,
        string mapping,
        string reference,
        IReadOnlyList<T> directValues,
        IReadOnlyList<int> indices,
        string label)
    {
        var result = new List<T[]>(polygons.Count);
        var polygonVertexIndex = 0;
        foreach (var polygon in polygons)
        {
            var values = new T[polygon.Length];
            for (var corner = 0; corner < polygon.Length; corner++)
            {
                var mappingIndex = mapping == "ByPolygonVertex"
                    ? polygonVertexIndex
                    : polygon[corner];
                var directIndex = reference == "Direct"
                    ? mappingIndex
                    : ReadLayerIndex(meshName, indices, mappingIndex, label);
                if (directIndex < 0 || directIndex >= directValues.Count)
                {
                    throw new InvalidDataException(
                        $"FBX mesh '{meshName}' has a {label} index outside its direct array.");
                }

                values[corner] = directValues[directIndex];
                polygonVertexIndex++;
            }

            result.Add(values);
        }

        return result;
    }

    private static int ReadLayerIndex(
        string meshName,
        IReadOnlyList<int> indices,
        int index,
        string label)
    {
        if (index < 0 || index >= indices.Count)
        {
            throw new InvalidDataException($"FBX mesh '{meshName}' has an incomplete {label} index array.");
        }

        return indices[index];
    }

    private static FbxNode FindChild(FbxNode parent, string name) =>
        parent.Children.FirstOrDefault(child => child.Name == name)
        ?? throw new InvalidDataException($"Binary FBX node '{name}' is missing.");

    private static string ReadNodeString(FbxNode parent, string name)
    {
        var node = FindChild(parent, name);
        if (node.Properties.Count == 0)
        {
            throw new InvalidDataException($"Binary FBX property '{name}' is empty.");
        }

        return ReadString(node.Properties[0], name);
    }

    private static double[] ReadDoubleArray(FbxNode node, string description)
    {
        if (node.Properties.Count == 0)
        {
            throw new InvalidDataException($"Binary FBX array '{description}' is empty.");
        }

        return node.Properties[0] switch
        {
            double[] values => values,
            float[] values => values.Select(value => (double)value).ToArray(),
            _ => throw new InvalidDataException($"Binary FBX array '{description}' has an unexpected type."),
        };
    }

    private static int[] ReadIntArray(FbxNode node, string description)
    {
        if (node.Properties.Count == 0)
        {
            throw new InvalidDataException($"Binary FBX array '{description}' is empty.");
        }

        return node.Properties[0] switch
        {
            int[] values => values,
            long[] values => values.Select(value => checked((int)value)).ToArray(),
            _ => throw new InvalidDataException($"Binary FBX array '{description}' has an unexpected type."),
        };
    }

    private static string NormalizeObjectName(string value, string prefix)
    {
        var nul = value.IndexOf('\0');
        if (nul >= 0)
        {
            value = value[..nul];
        }

        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }

    private static string ReadString(object value, string description) =>
        value as string
        ?? throw new InvalidDataException($"Binary FBX {description} is not a string.");

    private static long ReadInt64(object value, string description) =>
        value switch
        {
            long result => result,
            int result => result,
            _ => throw new InvalidDataException($"Binary FBX {description} is not an integer."),
        };

    private static double? TryReadDouble(object value) =>
        value switch
        {
            double result => result,
            float result => result,
            long result => result,
            int result => result,
            short result => result,
            _ => null,
        };

    private static byte ToColorByte(double value)
    {
        if (!double.IsFinite(value) || value < -0.0001 || value > 1.0001)
        {
            throw new InvalidDataException(
                $"FBX contains an invalid color component '{value.ToString(CultureInfo.InvariantCulture)}'.");
        }

        return checked((byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero));
    }

    private static Matrix4x4 CreateEulerXyz(Vector3 degrees)
    {
        var radians = degrees * (MathF.PI / 180f);
        return Matrix4x4.CreateRotationX(radians.X)
            * Matrix4x4.CreateRotationY(radians.Y)
            * Matrix4x4.CreateRotationZ(radians.Z);
    }

    private sealed record FbxNode(
        string Name,
        IReadOnlyList<object> Properties,
        IReadOnlyList<FbxNode> Children);
}
