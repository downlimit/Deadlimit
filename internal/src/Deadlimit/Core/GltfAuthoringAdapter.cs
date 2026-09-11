using System.Numerics;
using System.Text;
using System.Text.Json;
using Datamodel;
using Datamodel.Codecs;

namespace Deadlimit.Core;

internal sealed record GltfAuthoringOverlayResult(
    int GltfFileCount,
    int PreparedDmxCount,
    int PrimitiveCount,
    IReadOnlyList<string> MaterialReferences,
    IReadOnlyList<string> PreparedResources);

internal static class GltfAuthoringAdapter
{
    private const float MetersToSourceUnits = 39.37007874015748f;

    public static GltfAuthoringOverlayResult Overlay(
        ProjectManifest manifest,
        RetailModelSourceCopyResult sourceCopy,
        string addonContentRoot,
        IReadOnlyList<string> artistGltfFiles,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (artistGltfFiles.Count == 0)
        {
            return new GltfAuthoringOverlayResult(0, 0, 0, [], []);
        }

        var sourceRoot = ExtractedSourceLayout.SelectOwningRoot(manifest, sourceCopy.SourceVmdlPath);
        var preparedDmx = 0;
        var primitiveCount = 0;
        var materials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var artistPath in artistGltfFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var referencePath = ResolveReferenceGltf(sourceRoot, sourceCopy.SourceVmdlPath, artistPath);
            var sourceVmdlPath = Path.ChangeExtension(referencePath, ".vmdl");
            if (!File.Exists(sourceVmdlPath))
            {
                throw new InvalidOperationException(
                    $"The extracted glTF reference has no companion VMDL: {Path.GetRelativePath(sourceRoot, referencePath)}. " +
                    "Refresh this hero with glTF extraction before PREPARE.");
            }

            using var reference = GltfBinaryDocument.Load(referencePath);
            using var artist = GltfBinaryDocument.Load(artistPath);
            var referencePrimitives = reference.ReadPrimitives();
            var artistPrimitives = artist.ReadPrimitives();
            if (referencePrimitives.Count != artistPrimitives.Count)
            {
                throw new InvalidOperationException(
                    $"Artist glTF '{Path.GetFileName(artistPath)}' has {artistPrimitives.Count} render primitives; " +
                    $"the extracted reference has {referencePrimitives.Count}. Keep the extracted primitive/object split when exporting from the DCC.");
            }

            var mappedArtistPrimitives = MapArtistPrimitives(referencePrimitives, artistPrimitives, log);
            var renderMeshes = RetailVmdlInheritance.ReadRenderMeshes(sourceVmdlPath);
            var targets = ReadDmxTargets(sourceRoot, renderMeshes);
            var faceSets = targets.SelectMany(target => target.Meshes.SelectMany(mesh => mesh.FaceIndexCounts)).ToArray();
            if (faceSets.Length != referencePrimitives.Count)
            {
                throw new InvalidOperationException(
                    $"The extracted glTF/DMX companion set is inconsistent for '{Path.GetFileName(artistPath)}': " +
                    $"{referencePrimitives.Count} glTF primitives and {faceSets.Length} DMX face sets. Refresh glTF extraction.");
            }

            for (var index = 0; index < faceSets.Length; index++)
            {
                if (faceSets[index] != referencePrimitives[index].Indices.Length)
                {
                    throw new InvalidOperationException(
                        $"The extracted glTF primitive order no longer matches its DMX companion at primitive {index}. Refresh glTF extraction.");
                }
            }

            var cursor = 0;
            foreach (var target in targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var meshPrimitiveGroups = new List<IReadOnlyList<GltfPrimitiveData>>(target.Meshes.Count);
                foreach (var mesh in target.Meshes)
                {
                    var group = mappedArtistPrimitives
                        .Skip(cursor)
                        .Take(mesh.FaceIndexCounts.Count)
                        .ToArray();
                    cursor += group.Length;
                    meshPrimitiveGroups.Add(group);
                }

                var destinationPath = SafePath.ResolveUnderRoot(
                    addonContentRoot,
                    target.ResourcePath.Replace('/', Path.DirectorySeparatorChar),
                    "Prepared glTF-to-DMX destination");
                PatchDmx(destinationPath, meshPrimitiveGroups);
                preparedDmx++;
                resources.Add(target.ResourcePath);
                log.AppendLine($"  glTF adapter -> {target.ResourcePath} ({target.Meshes.Count} mesh object(s))");
            }

            foreach (var material in mappedArtistPrimitives
                         .Select(primitive => primitive.MaterialName)
                         .Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                materials.Add(NormalizeMaterialReference(material!));
            }

            primitiveCount += mappedArtistPrimitives.Count;
            log.AppendLine(
                $"Artist glTF overlay: {Path.GetFileName(artistPath)} | reference={Path.GetRelativePath(sourceRoot, referencePath)} | " +
                $"primitives={mappedArtistPrimitives.Count} | preparedDMX={targets.Count}");
        }

        return new GltfAuthoringOverlayResult(
            artistGltfFiles.Count,
            preparedDmx,
            primitiveCount,
            materials.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(),
            resources.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string ResolveReferenceGltf(string sourceRoot, string mainVmdlPath, string artistPath)
    {
        var mainCandidate = Path.ChangeExtension(mainVmdlPath, ".gltf");
        if (string.Equals(
                Path.GetFileNameWithoutExtension(mainCandidate),
                Path.GetFileNameWithoutExtension(artistPath),
                StringComparison.OrdinalIgnoreCase)
            && File.Exists(mainCandidate))
        {
            return mainCandidate;
        }

        var desiredStem = Path.GetFileNameWithoutExtension(artistPath);
        string[] matches = [];
        var roots = ExtractedSourceLayout.GetOrderedRoots(sourceRoot);
        for (var rootIndex = 0; rootIndex < roots.Count && matches.Length == 0; rootIndex++)
        {
            var root = roots[rootIndex];
            if (!Directory.Exists(root))
            {
                continue;
            }
            matches = Directory.EnumerateFiles(root, "*.gltf", SearchOption.AllDirectories)
                .Where(path => rootIndex != 0 || !ExtractedSourceLayout.IsInsideNestedPipeline(root, path))
                .Where(path => string.Equals(Path.GetFileNameWithoutExtension(path), desiredStem, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                $"No extracted glTF reference matches project-root '{Path.GetFileName(artistPath)}'. " +
                "Export from a Deadlimit glTF source and keep its filename."),
            _ => throw new InvalidOperationException(
                $"More than one extracted glTF reference matches '{Path.GetFileName(artistPath)}'. Keep a unique extracted filename."),
        };
    }

    private static IReadOnlyList<GltfPrimitiveData> MapArtistPrimitives(
        IReadOnlyList<GltfPrimitiveData> reference,
        IReadOnlyList<GltfPrimitiveData> artist,
        StringBuilder log)
    {
        var artistByName = artist
            .GroupBy(value => NormalizeName(value.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var mapped = new GltfPrimitiveData[reference.Count];
        var used = new HashSet<GltfPrimitiveData>();
        var exactCount = 0;

        for (var index = 0; index < reference.Count; index++)
        {
            var key = NormalizeName(reference[index].Name);
            if (key.Length > 0
                && artistByName.TryGetValue(key, out var matches)
                && matches.Length == 1
                && used.Add(matches[0]))
            {
                mapped[index] = matches[0];
                exactCount++;
            }
        }

        for (var index = 0; index < reference.Count; index++)
        {
            if (mapped[index] is not null)
            {
                continue;
            }

            var fallback = artist.FirstOrDefault(candidate => !used.Contains(candidate))
                ?? throw new InvalidOperationException("Artist glTF primitive mapping became incomplete.");
            used.Add(fallback);
            mapped[index] = fallback;
        }

        for (var index = 0; index < mapped.Length; index++)
        {
            var artistMaterial = mapped[index].MaterialName;
            var referenceMaterial = reference[index].MaterialName;
            if (!string.IsNullOrWhiteSpace(artistMaterial)
                && !string.IsNullOrWhiteSpace(referenceMaterial)
                && !artistMaterial.Contains('/')
                && string.Equals(
                    NormalizeName(Path.GetFileNameWithoutExtension(artistMaterial)),
                    NormalizeName(Path.GetFileNameWithoutExtension(referenceMaterial.Replace('/', Path.DirectorySeparatorChar))),
                    StringComparison.Ordinal))
            {
                mapped[index] = mapped[index] with { MaterialName = referenceMaterial };
            }
        }

        log.AppendLine($"glTF primitive mapping: exact names={exactCount}, stable order fallback={reference.Count - exactCount}");
        return mapped;
    }

    private static IReadOnlyList<DmxTargetDescriptor> ReadDmxTargets(
        string sourceRoot,
        IReadOnlyList<RetailRenderMeshEntry> renderMeshes)
    {
        var targets = new List<DmxTargetDescriptor>();
        foreach (var renderMesh in renderMeshes)
        {
            if (!Path.GetExtension(renderMesh.Filename).Equals(".dmx", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (ContainsLodToken(renderMesh.Name)
                || ContainsLodToken(Path.GetFileNameWithoutExtension(renderMesh.Filename)))
            {
                continue;
            }

            var sourcePath = SafePath.ResolveUnderRoot(
                sourceRoot,
                renderMesh.Filename.Replace('/', Path.DirectorySeparatorChar),
                "Extracted glTF companion DMX");
            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("Extracted glTF companion DMX was not found.", sourcePath);
            }

            using var document = Datamodel.Datamodel.Load(sourcePath, DeferredMode.Disabled);
            var jointShapes = DmxSkeletonShapeFilter.FindJointShapeMeshIds(document);
            var meshes = document.AllElements
                .Where(element => string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal))
                .Where(element => !DmxSkeletonShapeFilter.IsJointShape(element, jointShapes))
                .Select(element => new DmxMeshDescriptor(
                    GetRequiredArray<Element>(element, "faceSets")
                        .Select(faceSet => GetRequiredArray<int>(faceSet, "faces").Count(value => value >= 0))
                        .ToArray()))
                .ToArray();
            targets.Add(new DmxTargetDescriptor(renderMesh.Filename, meshes));
        }
        return targets;
    }

    private static void PatchDmx(
        string destinationPath,
        IReadOnlyList<IReadOnlyList<GltfPrimitiveData>> meshPrimitiveGroups)
    {
        if (!File.Exists(destinationPath))
        {
            throw new FileNotFoundException("Prepared retail DMX target was not staged.", destinationPath);
        }

        var temporaryPath = destinationPath + $".deadlimit-gltf-{Guid.NewGuid():N}.tmp";
        try
        {
            using var document = Datamodel.Datamodel.Load(destinationPath, DeferredMode.Disabled);
            var jointShapes = DmxSkeletonShapeFilter.FindJointShapeMeshIds(document);
            var meshes = document.AllElements
                .Where(element => string.Equals(element.ClassName, "DmeMesh", StringComparison.Ordinal))
                .Where(element => !DmxSkeletonShapeFilter.IsJointShape(element, jointShapes))
                .ToArray();
            var skeletonJointCount = document.AllElements.Count(element =>
                string.Equals(element.ClassName, "DmeJoint", StringComparison.Ordinal));
            if (meshes.Length != meshPrimitiveGroups.Count)
            {
                throw new InvalidDataException(
                    $"Prepared DMX mesh count changed before glTF adaptation: {destinationPath}.");
            }

            for (var meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
            {
                PatchDmxMesh(meshes[meshIndex], meshPrimitiveGroups[meshIndex], skeletonJointCount);
            }

            document.Save(temporaryPath, document.Encoding, document.EncodingVersion);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void PatchDmxMesh(
        Element mesh,
        IReadOnlyList<GltfPrimitiveData> primitives,
        int skeletonJointCount)
    {
        var state = mesh.Get<Element>("currentState")
            ?? throw new InvalidDataException($"DMX mesh '{mesh.Name}' has no currentState.");
        var faceSets = GetRequiredArray<Element>(mesh, "faceSets");
        if (faceSets.Count != primitives.Count)
        {
            throw new InvalidDataException($"DMX mesh '{mesh.Name}' face-set count does not match glTF primitives.");
        }

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var tangents = new List<Vector4>();
        var texcoords = new List<Vector2>();
        var colors = new List<Vector4>();
        var blendIndices = new List<int>();
        var blendWeights = new List<float>();
        var hasColor = primitives.Any(primitive => primitive.Colors is not null)
            || state.ContainsKey("color$0");

        for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
        {
            var primitive = primitives[primitiveIndex];
            ValidatePrimitive(primitive, mesh.Name, skeletonJointCount);
            var baseVertex = positions.Count;
            if (!Matrix4x4.Invert(primitive.Transform, out var inverseTransform))
            {
                throw new InvalidDataException($"glTF primitive '{primitive.Name}' has a singular node transform.");
            }
            var normalTransform = Matrix4x4.Transpose(inverseTransform);
            for (var vertex = 0; vertex < primitive.Positions.Length; vertex++)
            {
                var position = Vector3.Transform(primitive.Positions[vertex], primitive.Transform);
                var normal = Vector3.Normalize(Vector3.TransformNormal(primitive.Normals[vertex], normalTransform));
                var tangent3 = Vector3.Normalize(Vector3.TransformNormal(
                    new Vector3(primitive.Tangents[vertex].X, primitive.Tangents[vertex].Y, primitive.Tangents[vertex].Z),
                    primitive.Transform));
                positions.Add(ToSourcePosition(position));
                normals.Add(ToSourceDirection(normal));
                tangents.Add(new Vector4(ToSourceDirection(tangent3), primitive.Tangents[vertex].W));
                texcoords.Add(primitive.Texcoords[vertex]);
                colors.Add(primitive.Colors?[vertex] ?? Vector4.One);
                for (var influence = 0; influence < 4; influence++)
                {
                    blendIndices.Add(primitive.Joints[(vertex * 4) + influence]);
                    blendWeights.Add(primitive.Weights[(vertex * 4) + influence]);
                }
            }

            var faces = new List<int>(primitive.Indices.Length + (primitive.Indices.Length / 3));
            for (var index = 0; index < primitive.Indices.Length; index += 3)
            {
                faces.Add(baseVertex + primitive.Indices[index]);
                faces.Add(baseVertex + primitive.Indices[index + 1]);
                faces.Add(baseVertex + primitive.Indices[index + 2]);
                faces.Add(-1);
            }
            faceSets[primitiveIndex]["faces"] = new IntArray(faces);
            if (!string.IsNullOrWhiteSpace(primitive.MaterialName))
            {
                var materialReference = NormalizeMaterialReference(primitive.MaterialName!);
                var materialName = Path.GetFileNameWithoutExtension(
                    materialReference.Replace('/', Path.DirectorySeparatorChar));
                var owner = faceSets[primitiveIndex].Owner
                    ?? throw new InvalidDataException("DMX face set has no owning document.");
                var material = new Element(
                    owner,
                    materialName,
                    null,
                    "DmeMaterial");
                material["mtlName"] = materialReference;
                faceSets[primitiveIndex]["material"] = material;
            }
        }

        SetIndexedStream(state, "position$0", new Vector3Array(positions), positions.Count);
        SetIndexedStream(state, "normal$0", new Vector3Array(normals), positions.Count);
        SetIndexedStream(state, "tangent$0", new Vector4Array(tangents), positions.Count);
        SetIndexedStream(state, "texcoord$0", new Vector2Array(texcoords), positions.Count);
        state["blendindices$0"] = new IntArray(blendIndices);
        state["blendweights$0"] = new FloatArray(blendWeights);

        if (hasColor)
        {
            SetIndexedStream(state, "color$0", new Vector4Array(colors), positions.Count);
            EnsureVertexFormat(state, "color$0");
        }
        else
        {
            state.Remove("color$0");
            state.Remove("color$0Indices");
        }
    }

    private static void ValidatePrimitive(GltfPrimitiveData primitive, string meshName, int jointCount)
    {
        var count = primitive.Positions.Length;
        if (count == 0
            || primitive.Normals.Length != count
            || primitive.Tangents.Length != count
            || primitive.Texcoords.Length != count
            || primitive.Joints.Length != count * 4
            || primitive.Weights.Length != count * 4
            || primitive.Indices.Length == 0
            || primitive.Indices.Length % 3 != 0)
        {
            throw new InvalidDataException(
                $"glTF primitive '{primitive.Name}' has incomplete Source 2 mesh streams for DMX '{meshName}'.");
        }
        if (primitive.Colors is not null && primitive.Colors.Length != count)
        {
            throw new InvalidDataException($"glTF primitive '{primitive.Name}' has an invalid COLOR_0 stream.");
        }
        if (primitive.Indices.Any(index => index < 0 || index >= count))
        {
            throw new InvalidDataException($"glTF primitive '{primitive.Name}' contains an invalid vertex index.");
        }
        if (primitive.Joints.Any(index => index < 0 || index >= jointCount))
        {
            throw new InvalidDataException(
                $"glTF primitive '{primitive.Name}' references a joint outside the retail skeleton ({jointCount} joints). " +
                "Keep the original skin joint order when exporting from the DCC.");
        }
    }

    private static void SetIndexedStream(Element state, string name, object values, int count)
    {
        state[name] = values;
        state[name + "Indices"] = new IntArray(Enumerable.Range(0, count));
    }

    private static void EnsureVertexFormat(Element state, string stream)
    {
        var format = GetRequiredArray<string>(state, "vertexFormat");
        if (format.Contains(stream, StringComparer.Ordinal))
        {
            return;
        }
        var updated = format.ToList();
        var blend = updated.FindIndex(value => value.StartsWith("blend", StringComparison.Ordinal));
        updated.Insert(blend < 0 ? updated.Count : blend, stream);
        state["vertexFormat"] = new StringArray(updated);
    }

    private static Vector3 ToSourcePosition(Vector3 value) =>
        new(value.Z * MetersToSourceUnits, value.X * MetersToSourceUnits, value.Y * MetersToSourceUnits);

    private static Vector3 ToSourceDirection(Vector3 value) => new(value.Z, value.X, value.Y);

    private static string NormalizeMaterialReference(string value)
    {
        var normalized = value.Replace('\\', '/').Trim().TrimStart('/');
        if (!normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            normalized = "materials/" + normalized;
        }
        return normalized;
    }

    private static string NormalizeName(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool ContainsLodToken(string value)
    {
        var tokens = value.Split(['_', '-', '.', ' '], StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token => string.Equals(token, "lod", StringComparison.OrdinalIgnoreCase)
            || (token.Length > 3
                && token.StartsWith("lod", StringComparison.OrdinalIgnoreCase)
                && token[3..].All(char.IsDigit)));
    }

    private static IList<T> GetRequiredArray<T>(Element element, string name) =>
        element.GetArray<T>(name)
        ?? throw new InvalidDataException($"DMX element '{element.Name}' has no required '{name}' array.");

    private sealed record DmxTargetDescriptor(string ResourcePath, IReadOnlyList<DmxMeshDescriptor> Meshes);
    private sealed record DmxMeshDescriptor(IReadOnlyList<int> FaceIndexCounts);
}

internal sealed record GltfPrimitiveData(
    string Name,
    string? MaterialName,
    Vector3[] Positions,
    Vector3[] Normals,
    Vector4[] Tangents,
    Vector2[] Texcoords,
    Vector4[]? Colors,
    int[] Joints,
    float[] Weights,
    int[] Indices,
    Matrix4x4 Transform);

internal sealed class GltfBinaryDocument : IDisposable
{
    private readonly JsonDocument _json;
    private readonly byte[][] _buffers;

    private GltfBinaryDocument(JsonDocument json, byte[][] buffers)
    {
        _json = json;
        _buffers = buffers;
    }

    public static GltfBinaryDocument Load(string path)
    {
        return Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase)
            ? LoadGlb(path)
            : LoadGltf(path);
    }

    public IReadOnlyList<GltfPrimitiveData> ReadPrimitives()
    {
        var root = _json.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes)
            || meshes.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var materials = root.TryGetProperty("materials", out var materialArray)
            && materialArray.ValueKind == JsonValueKind.Array
            ? materialArray
            : default;
        var transforms = ComputeMeshTransforms(root, meshes.GetArrayLength());
        var result = new List<GltfPrimitiveData>();
        var meshIndex = 0;
        foreach (var mesh in meshes.EnumerateArray())
        {
            var meshName = mesh.TryGetProperty("name", out var nameProperty)
                ? nameProperty.GetString() ?? $"mesh_{meshIndex:D2}"
                : $"mesh_{meshIndex:D2}";
            if (!mesh.TryGetProperty("primitives", out var primitives)
                || primitives.ValueKind != JsonValueKind.Array)
            {
                meshIndex++;
                continue;
            }

            var primitiveIndex = 0;
            foreach (var primitive in primitives.EnumerateArray())
            {
                var mode = primitive.TryGetProperty("mode", out var modeProperty)
                    ? modeProperty.GetInt32()
                    : 4;
                if (mode != 4)
                {
                    throw new InvalidDataException($"glTF primitive '{meshName}' is not a triangle list.");
                }
                var attributes = primitive.GetProperty("attributes");
                var displayName = primitives.GetArrayLength() == 1
                    ? meshName
                    : $"{meshName}__part_{primitiveIndex:D2}";
                var materialName = ReadMaterialName(primitive, materials);
                result.Add(new GltfPrimitiveData(
                    displayName,
                    materialName,
                    ReadVector3(attributes.GetProperty("POSITION").GetInt32()),
                    ReadVector3(attributes.GetProperty("NORMAL").GetInt32()),
                    ReadVector4(attributes.GetProperty("TANGENT").GetInt32()),
                    ReadVector2(attributes.GetProperty("TEXCOORD_0").GetInt32()),
                    attributes.TryGetProperty("COLOR_0", out var color) ? ReadColor(color.GetInt32()) : null,
                    ReadIntegerVector4(attributes.GetProperty("JOINTS_0").GetInt32()),
                    ReadFloatVector4Flat(attributes.GetProperty("WEIGHTS_0").GetInt32()),
                    ReadScalarIntegers(primitive.GetProperty("indices").GetInt32()),
                    transforms[meshIndex]));
                primitiveIndex++;
            }
            meshIndex++;
        }
        return result;
    }

    public void Dispose() => _json.Dispose();

    private static GltfBinaryDocument LoadGltf(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var json = JsonDocument.Parse(bytes);
        var buffers = new List<byte[]>();
        foreach (var buffer in json.RootElement.GetProperty("buffers").EnumerateArray())
        {
            var uri = buffer.GetProperty("uri").GetString()
                ?? throw new InvalidDataException($"glTF buffer URI is empty: {path}");
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                var comma = uri.IndexOf(',');
                if (comma < 0)
                {
                    throw new InvalidDataException($"glTF data URI is malformed: {path}");
                }
                buffers.Add(Convert.FromBase64String(uri[(comma + 1)..]));
                continue;
            }
            var bufferPath = SafePath.ResolveUnderRoot(
                Path.GetDirectoryName(path)!,
                Uri.UnescapeDataString(uri).Replace('/', Path.DirectorySeparatorChar),
                "Artist glTF buffer");
            buffers.Add(File.ReadAllBytes(bufferPath));
        }
        return new GltfBinaryDocument(json, buffers.ToArray());
    }

    private static GltfBinaryDocument LoadGlb(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67)
        {
            throw new InvalidDataException($"Invalid GLB header: {path}");
        }
        var offset = 12;
        byte[]? jsonBytes = null;
        var binaryChunks = new List<byte[]>();
        while (offset + 8 <= bytes.Length)
        {
            var length = checked((int)BitConverter.ToUInt32(bytes, offset));
            var type = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (offset + length > bytes.Length)
            {
                throw new InvalidDataException($"GLB chunk exceeds file length: {path}");
            }
            var chunk = bytes.AsSpan(offset, length).ToArray();
            if (type == 0x4E4F534A)
            {
                jsonBytes = chunk;
            }
            else if (type == 0x004E4942)
            {
                binaryChunks.Add(chunk);
            }
            offset += length;
        }
        if (jsonBytes is null)
        {
            throw new InvalidDataException($"GLB has no JSON chunk: {path}");
        }
        var json = JsonDocument.Parse(jsonBytes.AsSpan().TrimEnd((byte)0x20, (byte)0x00).ToArray());
        return new GltfBinaryDocument(json, binaryChunks.ToArray());
    }

    private Matrix4x4[] ComputeMeshTransforms(JsonElement root, int meshCount)
    {
        var result = Enumerable.Repeat(Matrix4x4.Identity, meshCount).ToArray();
        if (!root.TryGetProperty("nodes", out var nodes) || nodes.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        var parent = Enumerable.Repeat(-1, nodes.GetArrayLength()).ToArray();
        for (var index = 0; index < nodes.GetArrayLength(); index++)
        {
            if (!nodes[index].TryGetProperty("children", out var children))
            {
                continue;
            }
            foreach (var child in children.EnumerateArray())
            {
                parent[child.GetInt32()] = index;
            }
        }
        var local = nodes.EnumerateArray().Select(ReadNodeTransform).ToArray();
        var world = new Matrix4x4[nodes.GetArrayLength()];
        var computed = new bool[nodes.GetArrayLength()];
        Matrix4x4 Resolve(int index)
        {
            if (computed[index])
            {
                return world[index];
            }
            world[index] = parent[index] < 0 ? local[index] : local[index] * Resolve(parent[index]);
            computed[index] = true;
            return world[index];
        }
        for (var index = 0; index < nodes.GetArrayLength(); index++)
        {
            var node = nodes[index];
            if (node.TryGetProperty("mesh", out var meshProperty))
            {
                result[meshProperty.GetInt32()] = Resolve(index);
            }
        }
        return result;
    }

    private static Matrix4x4 ReadNodeTransform(JsonElement node)
    {
        if (node.TryGetProperty("matrix", out var matrix))
        {
            var values = matrix.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            return new Matrix4x4(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
        }
        var translation = node.TryGetProperty("translation", out var t)
            ? new Vector3(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle())
            : Vector3.Zero;
        var rotation = node.TryGetProperty("rotation", out var r)
            ? new Quaternion(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle())
            : Quaternion.Identity;
        var scale = node.TryGetProperty("scale", out var s)
            ? new Vector3(s[0].GetSingle(), s[1].GetSingle(), s[2].GetSingle())
            : Vector3.One;
        return Matrix4x4.CreateScale(scale)
            * Matrix4x4.CreateFromQuaternion(rotation)
            * Matrix4x4.CreateTranslation(translation);
    }

    private static string? ReadMaterialName(JsonElement primitive, JsonElement materials)
    {
        if (materials.ValueKind != JsonValueKind.Array
            || !primitive.TryGetProperty("material", out var materialIndex))
        {
            return null;
        }
        var material = materials[materialIndex.GetInt32()];
        return material.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    private Vector2[] ReadVector2(int accessorIndex) => ReadFloatAccessor(accessorIndex)
        .Select(value => new Vector2(value[0], value[1])).ToArray();

    private Vector3[] ReadVector3(int accessorIndex) => ReadFloatAccessor(accessorIndex)
        .Select(value => new Vector3(value[0], value[1], value[2])).ToArray();

    private Vector4[] ReadVector4(int accessorIndex) => ReadFloatAccessor(accessorIndex)
        .Select(value => new Vector4(value[0], value[1], value[2], value[3])).ToArray();

    private Vector4[] ReadColor(int accessorIndex) => ReadFloatAccessor(accessorIndex)
        .Select(value => value.Length == 3
            ? new Vector4(value[0], value[1], value[2], 1f)
            : new Vector4(value[0], value[1], value[2], value[3]))
        .ToArray();

    private float[] ReadFloatVector4Flat(int accessorIndex) => ReadFloatAccessor(accessorIndex)
        .SelectMany(value => value).ToArray();

    private int[] ReadIntegerVector4(int accessorIndex) => ReadIntegerAccessor(accessorIndex)
        .SelectMany(value => value).ToArray();

    private int[] ReadScalarIntegers(int accessorIndex) => ReadIntegerAccessor(accessorIndex)
        .Select(value => value[0]).ToArray();

    private float[][] ReadFloatAccessor(int accessorIndex)
    {
        var (accessor, view, buffer, start, stride, componentCount, componentSize) = GetAccessor(accessorIndex);
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var normalized = accessor.TryGetProperty("normalized", out var normalizedProperty)
            && normalizedProperty.GetBoolean();
        var count = accessor.GetProperty("count").GetInt32();
        var result = new float[count][];
        for (var index = 0; index < count; index++)
        {
            result[index] = new float[componentCount];
            for (var component = 0; component < componentCount; component++)
            {
                result[index][component] = ReadFloatComponent(
                    buffer,
                    start + (index * stride) + (component * componentSize),
                    componentType,
                    normalized);
            }
        }
        return result;
    }

    private int[][] ReadIntegerAccessor(int accessorIndex)
    {
        var (accessor, _, buffer, start, stride, componentCount, componentSize) = GetAccessor(accessorIndex);
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var count = accessor.GetProperty("count").GetInt32();
        var result = new int[count][];
        for (var index = 0; index < count; index++)
        {
            result[index] = new int[componentCount];
            for (var component = 0; component < componentCount; component++)
            {
                result[index][component] = ReadIntegerComponent(
                    buffer,
                    start + (index * stride) + (component * componentSize),
                    componentType);
            }
        }
        return result;
    }

    private (JsonElement Accessor, JsonElement View, byte[] Buffer, int Start, int Stride, int Components, int ComponentSize)
        GetAccessor(int accessorIndex)
    {
        var root = _json.RootElement;
        var accessor = root.GetProperty("accessors")[accessorIndex];
        if (accessor.TryGetProperty("sparse", out _))
        {
            throw new InvalidDataException("Sparse glTF accessors are unsupported for CSDK preparation.");
        }
        var view = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
        var buffer = _buffers[view.GetProperty("buffer").GetInt32()];
        var componentType = accessor.GetProperty("componentType").GetInt32();
        var componentSize = ComponentSize(componentType);
        var componentCount = ComponentCount(accessor.GetProperty("type").GetString()!);
        var elementSize = checked(componentSize * componentCount);
        var start = (view.TryGetProperty("byteOffset", out var viewOffset) ? viewOffset.GetInt32() : 0)
            + (accessor.TryGetProperty("byteOffset", out var accessorOffset) ? accessorOffset.GetInt32() : 0);
        var stride = view.TryGetProperty("byteStride", out var strideProperty)
            ? strideProperty.GetInt32()
            : elementSize;
        return (accessor, view, buffer, start, stride, componentCount, componentSize);
    }

    private static float ReadFloatComponent(byte[] buffer, int offset, int type, bool normalized) => type switch
    {
        5120 => normalized ? Math.Max((sbyte)buffer[offset] / 127f, -1f) : (sbyte)buffer[offset],
        5121 => normalized ? buffer[offset] / 255f : buffer[offset],
        5122 => normalized ? Math.Max(BitConverter.ToInt16(buffer, offset) / 32767f, -1f) : BitConverter.ToInt16(buffer, offset),
        5123 => normalized ? BitConverter.ToUInt16(buffer, offset) / 65535f : BitConverter.ToUInt16(buffer, offset),
        5125 => normalized ? BitConverter.ToUInt32(buffer, offset) / (float)uint.MaxValue : BitConverter.ToUInt32(buffer, offset),
        5126 => BitConverter.ToSingle(buffer, offset),
        _ => throw new InvalidDataException($"Unsupported glTF component type {type}."),
    };

    private static int ReadIntegerComponent(byte[] buffer, int offset, int type) => type switch
    {
        5120 => (sbyte)buffer[offset],
        5121 => buffer[offset],
        5122 => BitConverter.ToInt16(buffer, offset),
        5123 => BitConverter.ToUInt16(buffer, offset),
        5125 => checked((int)BitConverter.ToUInt32(buffer, offset)),
        _ => throw new InvalidDataException($"Unsupported integer glTF component type {type}."),
    };

    private static int ComponentSize(int type) => type switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"Unsupported glTF component type {type}."),
    };

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        _ => throw new InvalidDataException($"Unsupported glTF vector type '{type}'."),
    };
}

internal static class SpanByteTrimExtensions
{
    public static ReadOnlySpan<byte> TrimEnd(this ReadOnlySpan<byte> value, params byte[] trim)
    {
        var end = value.Length;
        while (end > 0 && trim.Contains(value[end - 1]))
        {
            end--;
        }
        return value[..end];
    }
}
