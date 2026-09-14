using Assimp;
using Datamodel.Codecs;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using DmxDocument = Datamodel.Datamodel;
using DmxElement = Datamodel.Element;

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

    // Preserve authored vertex-color data on the generated shell. Some
    // Deadlock materials use color0 as part of their appearance contract, and
    // dropping it makes those slots render black after the preview FBX is
    // re-imported by Painter.
    for (var channel = 0; channel < source.VertexColorChannels.Length; channel++)
        if (source.HasVertexColors(channel))
            outline.VertexColorChannels[channel].AddRange(source.VertexColorChannels[channel]);

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

static string CanonicalMaterial(string value) =>
    value.Replace('\\', '/').Trim().ToLowerInvariant().Replace(".vmat_c", ".vmat");

static int ApplyDmxVertexColors(Scene scene, string path)
{
    using var document = DmxDocument.Load(path, DeferredMode.Automatic);
    var transfers = 0;
    foreach (var element in document.AllElements.Where(element => element.ClassName == "DmeMesh"))
    {
        var vertexState = element.ContainsKey("currentState") && element["currentState"] is DmxElement
            ? element.Get<DmxElement>("currentState")
            : element.ContainsKey("bindState") && element["bindState"] is DmxElement
                ? element.Get<DmxElement>("bindState")
                : null;
        if (vertexState is null || !vertexState.ContainsKey("color$0") ||
            !vertexState.ContainsKey("color$0Indices") || !element.ContainsKey("faceSets"))
            continue;

        var positions = vertexState.GetArray<Vector3>("position$0") ?? Array.Empty<Vector3>();
        var positionIndices = vertexState.GetArray<int>("position$0Indices") ?? Array.Empty<int>();
        var colors = vertexState.GetArray<Vector4>("color$0") ?? Array.Empty<Vector4>();
        var colorIndices = vertexState.GetArray<int>("color$0Indices") ?? Array.Empty<int>();
        var faceSets = element.GetArray<DmxElement>("faceSets") ?? Array.Empty<DmxElement>();
        if (positions.Count == 0 || positionIndices.Count == 0 || colors.Count == 0 || colorIndices.Count == 0)
            continue;

        foreach (var faceSet in faceSets)
        {
            var materialElement = faceSet.Get<DmxElement>("material") ?? throw new InvalidDataException(
                "DMX face set has no material element.");
            var materialName = materialElement.Get<string>("mtlName") ?? throw new InvalidDataException(
                "DMX face set material has no mtlName.");
            var material = CanonicalMaterial(materialName);
            var faceEntries = faceSet.GetArray<int>("faces") ?? Array.Empty<int>();
            var dmxFaces = new List<int[]>();
            var pending = new List<int>(3);
            foreach (var entry in faceEntries)
            {
                if (entry >= 0)
                {
                    pending.Add(entry);
                    continue;
                }
                if (pending.Count != 3)
                    throw new InvalidDataException($"DMX material '{material}' contains a non-triangle face.");
                dmxFaces.Add(pending.ToArray());
                pending.Clear();
            }
            if (pending.Count != 0)
                throw new InvalidDataException($"DMX material '{material}' has an unterminated face.");

            var candidates = scene.Meshes.Where(mesh =>
                    CanonicalMaterial(scene.Materials[mesh.MaterialIndex].Name) == material &&
                    mesh.Faces.Count == dmxFaces.Count)
                .ToArray();
            // Source DMX can contain technical/helper face sets that are not
            // present in the Painter FBX. They carry no transferable target
            // geometry and should not make the whole preview fail.
            if (candidates.Length == 0)
                continue;
            if (candidates.Length > 1)
                throw new InvalidDataException(
                    $"DMX vertex colors for '{material}' matched {candidates.Length} FBX meshes; expected exactly one.");
            var target = candidates[0];
            var transferred = Enumerable.Repeat(new Vector4(float.NaN), target.VertexCount).ToArray();
            const float positionToleranceSquared = 1e-6f;

            for (var faceIndex = 0; faceIndex < dmxFaces.Count; faceIndex++)
            {
                var targetFace = target.Faces[faceIndex];
                if (targetFace.IndexCount != 3)
                    throw new InvalidDataException($"FBX material '{material}' contains a non-triangle face.");
                var dmxFace = dmxFaces[faceIndex];
                var matchedDmxCorners = new bool[3];
                for (var targetCorner = 0; targetCorner < 3; targetCorner++)
                {
                    var targetVertexIndex = targetFace.Indices[targetCorner];
                    var targetPosition = target.Vertices[targetVertexIndex];
                    var matchingCorner = -1;
                    for (var dmxCorner = 0; dmxCorner < 3; dmxCorner++)
                    {
                        if (matchedDmxCorners[dmxCorner])
                            continue;
                        var streamIndex = dmxFace[dmxCorner];
                        var position = positions[positionIndices[streamIndex]];
                        if (Vector3.DistanceSquared(position, targetPosition) <= positionToleranceSquared)
                        {
                            matchingCorner = dmxCorner;
                            break;
                        }
                    }
                    if (matchingCorner < 0)
                        throw new InvalidDataException(
                            $"DMX vertex-color topology for '{material}' diverges at face {faceIndex}.");
                    matchedDmxCorners[matchingCorner] = true;
                    var matchedStreamIndex = dmxFace[matchingCorner];
                    var color = colors[colorIndices[matchedStreamIndex]];
                    if (!float.IsNaN(transferred[targetVertexIndex].X) &&
                        Vector4.DistanceSquared(transferred[targetVertexIndex], color) > 1e-8f)
                        throw new InvalidDataException(
                            $"DMX vertex-color seam for '{material}' cannot be represented by the FBX topology.");
                    transferred[targetVertexIndex] = color;
                }
            }
            if (transferred.Any(color => float.IsNaN(color.X)))
                throw new InvalidDataException($"DMX vertex-color transfer for '{material}' left vertices unassigned.");
            target.VertexColorChannels[0].Clear();
            target.VertexColorChannels[0].AddRange(transferred);
            transfers++;
        }
    }
    return transfers;
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
                    firstVertex = scene.Meshes[meshIndex].Vertices.Count > 0
                        ? new[]
                        {
                            scene.Meshes[meshIndex].Vertices[0].X,
                            scene.Meshes[meshIndex].Vertices[0].Y,
                            scene.Meshes[meshIndex].Vertices[0].Z
                        }
                        : null,
                    vertexColorChannels = Enumerable.Range(0, scene.Meshes[meshIndex].VertexColorChannels.Length)
                        .Where(scene.Meshes[meshIndex].HasVertexColors)
                        .Select(channel => new
                        {
                            channel,
                            values = scene.Meshes[meshIndex].VertexColorChannels[channel].Count,
                            minimum = new[]
                            {
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Min(color => color.X),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Min(color => color.Y),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Min(color => color.Z),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Min(color => color.W)
                            },
                            maximum = new[]
                            {
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Max(color => color.X),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Max(color => color.Y),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Max(color => color.Z),
                                scene.Meshes[meshIndex].VertexColorChannels[channel].Max(color => color.W)
                            }
                        }).ToArray(),
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

    var vertexColorTransfers = 0;
    if (options.TryGetValue("vertex-color-dmx", out var vertexColorDmx))
    {
        var resolvedDmx = Path.GetFullPath(vertexColorDmx);
        if (!File.Exists(resolvedDmx))
            throw new FileNotFoundException("Vertex-color DMX was not found.", resolvedDmx);
        vertexColorTransfers = ApplyDmxVertexColors(scene, resolvedDmx);
        if (vertexColorTransfers == 0)
            throw new InvalidDataException("Vertex-color DMX contained no transferable color streams.");
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
        vertexColorTransfers,
        sourceBounds
    }));
    return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.ToString());
    return 1;
}