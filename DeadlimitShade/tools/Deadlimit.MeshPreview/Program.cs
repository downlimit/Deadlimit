using Assimp;
using System.Globalization;
using System.Numerics;
using System.Text.Json;

static string RequireOption(Dictionary<string, string> options, string name)
{
    if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        throw new ArgumentException($"Missing required option --{name}.");
    return value;
}

static IEnumerable<Node> Descendants(Node node)
{
    yield return node;
    foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
}

static Mesh CreateOutlineMesh(Mesh source, float width)
{
    if (!source.HasNormals || source.Normals.Count != source.VertexCount)
        throw new InvalidDataException($"Mesh '{source.Name}' has no per-vertex render normals.");

    var outline = new Mesh($"__deadlimit_outline_{source.Name}", PrimitiveType.Triangle)
    {
        MaterialIndex = 0
    };
    for (var index = 0; index < source.VertexCount; index++)
    {
        var normal = source.Normals[index];
        normal = Vector3.Normalize(normal);
        outline.Vertices.Add(source.Vertices[index] + normal * width);
        outline.Normals.Add(-normal);
    }

    if (source.HasTextureCoords(0))
        outline.TextureCoordinateChannels[0].AddRange(source.TextureCoordinateChannels[0]);
    else
        for (var index = 0; index < source.VertexCount; index++)
            outline.TextureCoordinateChannels[0].Add(new Vector3());
    outline.UVComponentCount[0] = 2;

    foreach (var face in source.Faces)
    {
        if (face.IndexCount != 3)
            throw new InvalidDataException($"Mesh '{source.Name}' contains a non-triangle face.");
        outline.Faces.Add(new Face(new[] { face.Indices[0], face.Indices[2], face.Indices[1] }));
    }
    return outline;
}

static object GetWorldBounds(Scene scene)
{
    var minimum = new Vector3(float.PositiveInfinity);
    var maximum = new Vector3(float.NegativeInfinity);
    var vertexCount = 0;

    void Visit(Node node, Matrix4x4 parentTransform)
    {
        var worldTransform = parentTransform * node.Transform;
        foreach (var meshIndex in node.MeshIndices)
        {
            foreach (var vertex in scene.Meshes[meshIndex].Vertices)
            {
                var world = Vector3.Transform(vertex, worldTransform);
                minimum.X = Math.Min(minimum.X, world.X);
                minimum.Y = Math.Min(minimum.Y, world.Y);
                minimum.Z = Math.Min(minimum.Z, world.Z);
                maximum.X = Math.Max(maximum.X, world.X);
                maximum.Y = Math.Max(maximum.Y, world.Y);
                maximum.Z = Math.Max(maximum.Z, world.Z);
                vertexCount++;
            }
        }
        foreach (var child in node.Children)
            Visit(child, worldTransform);
    }

    Visit(scene.RootNode, Matrix4x4.Identity);
    return new
    {
        minimum = new[] { minimum.X, minimum.Y, minimum.Z },
        maximum = new[] { maximum.X, maximum.Y, maximum.Z },
        size = new[] { maximum.X - minimum.X, maximum.Y - minimum.Y, maximum.Z - minimum.Z },
        vertexCount
    };
}

try
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < args.Length; index += 2)
    {
        if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Options must use --name value pairs.");
        options[args[index][2..]] = args[index + 1];
    }

    var input = Path.GetFullPath(RequireOption(options, "input"));
    var inputExtension = Path.GetExtension(input).ToLowerInvariant();
    var inspectOnly = options.TryGetValue("inspect-only", out var inspectText) &&
        bool.Parse(inspectText);
    var output = inspectOnly ? string.Empty : Path.GetFullPath(RequireOption(options, "output"));
    var widthMillimeters = inspectOnly ? 0f : float.Parse(RequireOption(options, "width-mm"), CultureInfo.InvariantCulture);
    var sourceUnitsPerMillimeter = options.TryGetValue("source-units-per-mm", out var scaleText)
        ? float.Parse(scaleText, CultureInfo.InvariantCulture)
        : inputExtension is ".glb" or ".gltf" ? 0.001f : 0.1f;
    if (!inspectOnly && (widthMillimeters <= 0 || sourceUnitsPerMillimeter <= 0))
        throw new ArgumentOutOfRangeException(nameof(widthMillimeters), "Width and source scale must be positive.");
    if (!inspectOnly && string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
        throw new ArgumentException("Input and output paths must differ.");
    if (!inspectOnly && File.Exists(output))
        throw new IOException($"Refusing to overwrite '{output}'.");

    using var context = new AssimpContext();
    var scene = context.ImportFile(input, PostProcessSteps.Triangulate | PostProcessSteps.ValidateDataStructure);
    if (scene is null || scene.RootNode is null || scene.MeshCount == 0)
        throw new InvalidDataException("Assimp imported no scene geometry.");

    var sourceBounds = GetWorldBounds(scene);
    if (inspectOnly)
    {
        var meshNodes = Descendants(scene.RootNode)
            .Where(node => node.HasMeshes)
            .Select(node => new
            {
                node.Name,
                meshes = node.MeshIndices.Select(meshIndex => new
                {
                    index = meshIndex,
                    scene.Meshes[meshIndex].Name,
                    vertices = scene.Meshes[meshIndex].VertexCount,
                    materialIndex = scene.Meshes[meshIndex].MaterialIndex,
                    material = scene.Materials[scene.Meshes[meshIndex].MaterialIndex].Name,
                    normalLengthMinimum = scene.Meshes[meshIndex].HasNormals
                        ? scene.Meshes[meshIndex].Normals.Min(normal => normal.Length())
                        : (float?)null,
                    normalLengthMaximum = scene.Meshes[meshIndex].HasNormals
                        ? scene.Meshes[meshIndex].Normals.Max(normal => normal.Length())
                        : (float?)null
                }).ToArray(),
                transform = node.Transform.ToString()
            }).ToArray();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            input,
            meshes = scene.MeshCount,
            materials = scene.MaterialCount,
            rootTransform = scene.RootNode.Transform.ToString(),
            bounds = sourceBounds,
            meshNodes
        }));
        return 0;
    }

    var outlineMaterial = new Material { Name = "__deadlimit_outline" };
    outlineMaterial.ColorDiffuse = new Vector4(0, 0, 0, 1);
    scene.Materials.Add(outlineMaterial);
    var outlineMaterialIndex = scene.MaterialCount - 1;

    var outlinedNodes = 0;
    var outlinedMeshes = 0;
    foreach (var node in Descendants(scene.RootNode))
    {
        if (!node.HasMeshes)
            continue;
        var sourceIndices = node.MeshIndices.ToArray();
        foreach (var sourceIndex in sourceIndices)
        {
            var outline = CreateOutlineMesh(scene.Meshes[sourceIndex], widthMillimeters * sourceUnitsPerMillimeter);
            outline.MaterialIndex = outlineMaterialIndex;
            scene.Meshes.Add(outline);
            node.MeshIndices.Add(scene.MeshCount - 1);
            outlinedMeshes++;
        }
        if (node.MeshIndices.Count > sourceIndices.Length)
            outlinedNodes++;
    }
    if (outlinedMeshes == 0)
        throw new InvalidDataException("The source scene contains no mesh geometry to outline.");

    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    var outputExtension = Path.GetExtension(output);
    var format = outputExtension.Equals(".glb", StringComparison.OrdinalIgnoreCase)
        ? "glb"
        : outputExtension.Equals(".gltf", StringComparison.OrdinalIgnoreCase)
            ? "gltf2"
            : "fbx";
    // FBX scene units are centimetres while glTF/GLB units are metres. Same-
    // format Apply needs no root conversion. Keep cross-format CLI use at the
    // same physical size as the imported FBX.
    if (inputExtension == ".fbx" && (format is "glb" or "gltf2"))
        scene.RootNode.Transform = Matrix4x4.CreateScale(0.01f) * scene.RootNode.Transform;
    var exported = context.ExportFile(scene, output, format);
    if (!exported || !File.Exists(output) || new FileInfo(output).Length == 0)
    {
        var supported = string.Join(", ", context.GetSupportedExportFormats().Select(item => item.FormatId));
        throw new IOException($"Assimp did not create the preview scene as '{format}'. Supported exporters: {supported}");
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        input,
        output,
        format,
        sourceMeshes = scene.MeshCount - outlinedMeshes,
        sourceMaterials = scene.MaterialCount - 1,
        outlinedNodes,
        outlinedMeshes,
        widthMillimeters,
        sourceBounds
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}
