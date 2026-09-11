using System.Text.Json;
using System.Text.Json.Nodes;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace Deadlimit.Core;

public sealed partial class HeroExtractionService
{
    private static void ExtractGltfResourceLocations(
        IReadOnlyList<string> vpkPaths,
        IReadOnlyList<ResourceLocation> locations,
        string outputRoot,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var packages = new List<Package>();
        GameFileLoader? fileLoader = null;

        try
        {
            foreach (var vpkPath in vpkPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var package = new Package();
                    package.Read(vpkPath);
                    packages.Add(package);
                }
                catch (Exception exception) when (exception is IOException
                                                  or UnauthorizedAccessException
                                                  or NotSupportedException)
                {
                    progress?.Report(new HeroExtractionProgress(
                        $"Skipping unreadable VPK during glTF export: {Path.GetFileName(vpkPath)}: {exception.Message}"));
                }
            }

            if (packages.Count == 0)
            {
                throw new InvalidOperationException(
                    "No current Deadlock VPK could be opened for glTF export.");
            }

            fileLoader = new GameFileLoader(packages[0], packages[0].FileName);
            foreach (var package in packages.Skip(1))
            {
                fileLoader.AddPackageToSearch(package);
            }

            var exporter = new GltfModelExporter(fileLoader)
            {
                ProgressReporter = new Progress<string>(message =>
                    progress?.Report(new HeroExtractionProgress(message))),
                ExportAnimations = true,
                ExportMaterials = includeTextures,
                AdaptTextures = true,
                SatelliteImages = true,
                ExportExtras = false,
                ComposeAdditiveAnimations = false,
            };

            var exportableCount = 0;
            var distinctLocations = locations
                .GroupBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            for (var index = 0; index < distinctLocations.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = distinctLocations[index];

                using var resource = fileLoader.LoadFile(location.ResourcePath);
                if (resource is null)
                {
                    progress?.Report(new HeroExtractionProgress(
                        $"Referenced retail resource could not be loaded for glTF export: {location.ResourcePath}"));
                    continue;
                }

                if (!GltfModelExporter.CanExport(resource))
                {
                    continue;
                }

                exportableCount++;
                var sourcePath = location.ResourcePath.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                    ? location.ResourcePath[..^2]
                    : location.ResourcePath;
                var relativeGltfPath = Path.ChangeExtension(sourcePath, ".gltf");
                var outputPath = SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(relativeGltfPath),
                    "Extracted glTF source file");
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

                progress?.Report(new HeroExtractionProgress(
                    $"Exporting glTF {index + 1}/{distinctLocations.Length}: {Path.GetFileName(relativeGltfPath)}"));
                exporter.Export(resource, outputPath, cancellationToken);

                if (!File.Exists(outputPath))
                {
                    throw new InvalidOperationException(
                        $"ValveResourceFormat completed glTF export without creating the expected file: {outputPath}");
                }

                NormalizeMixedPrimitiveVertexColors(outputPath);
                SplitGltfPrimitivesForDcc(outputPath);
                ValidateGltfSkinningContract(outputPath);
            }

            if (exportableCount == 0)
            {
                progress?.Report(new HeroExtractionProgress(
                    "The selected scope contained no resources supported by the glTF exporter."));
            }
        }
        finally
        {
            fileLoader?.Dispose();
            foreach (var package in packages)
            {
                package.Dispose();
            }
        }
    }

    private static void NormalizeMixedPrimitiveVertexColors(string gltfPath)
    {
        // glTF defines a missing COLOR_0 as a white base-color multiplier. The Khronos
        // Max importer concatenates primitives into one mesh but advances its color
        // offset only for primitives that contain COLOR_0. Materializing the implicit
        // white values keeps later colored primitives aligned with their Max vertices.
        var root = JsonNode.Parse(File.ReadAllText(gltfPath))?.AsObject()
            ?? throw new InvalidDataException($"glTF JSON is empty: {gltfPath}");
        if (root["meshes"] is not JsonArray meshes
            || root["accessors"] is not JsonArray accessors
            || root["bufferViews"] is not JsonArray bufferViews
            || root["buffers"] is not JsonArray buffers
            || buffers.Count == 0
            || buffers[0] is not JsonObject buffer)
        {
            return;
        }

        var missingColorPrimitives = new List<(JsonObject Attributes, int VertexCount)>();
        foreach (var meshNode in meshes)
        {
            if (meshNode is not JsonObject mesh
                || mesh["primitives"] is not JsonArray primitives)
            {
                continue;
            }

            var primitiveObjects = primitives.OfType<JsonObject>().ToArray();
            if (!primitiveObjects.Any(HasVertexColor)
                || primitiveObjects.All(HasVertexColor))
            {
                continue;
            }

            foreach (var primitive in primitiveObjects.Where(primitive => !HasVertexColor(primitive)))
            {
                if (primitive["attributes"] is not JsonObject attributes
                    || attributes["POSITION"]?.GetValue<int>() is not int positionAccessorIndex
                    || positionAccessorIndex < 0
                    || positionAccessorIndex >= accessors.Count
                    || accessors[positionAccessorIndex] is not JsonObject positionAccessor
                    || positionAccessor["count"]?.GetValue<int>() is not int vertexCount
                    || vertexCount <= 0)
                {
                    throw new InvalidDataException(
                        $"glTF contains a mixed vertex-color mesh with an invalid POSITION accessor in {Path.GetFileName(gltfPath)}.");
                }

                missingColorPrimitives.Add((attributes, vertexCount));
            }
        }

        if (missingColorPrimitives.Count == 0)
        {
            return;
        }

        var bufferUri = buffer["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(bufferUri)
            || Uri.TryCreate(bufferUri, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                $"glTF vertex-color normalization requires an external relative buffer in {Path.GetFileName(gltfPath)}.");
        }

        var gltfFolder = Path.GetDirectoryName(gltfPath)!;
        var bufferPath = SafePath.ResolveUnderRoot(
            gltfFolder,
            Uri.UnescapeDataString(bufferUri).Replace('/', Path.DirectorySeparatorChar),
            "glTF binary buffer");
        if (!File.Exists(bufferPath))
        {
            throw new FileNotFoundException("glTF binary buffer was not created.", bufferPath);
        }

        var whiteAccessorByVertexCount = new Dictionary<int, int>();
        using (var stream = new FileStream(bufferPath, FileMode.Append, FileAccess.Write, FileShare.None))
        {
            foreach (var (attributes, vertexCount) in missingColorPrimitives)
            {
                if (!whiteAccessorByVertexCount.TryGetValue(vertexCount, out var accessorIndex))
                {
                    while (stream.Position % 4 != 0)
                    {
                        stream.WriteByte(0);
                    }

                    var byteOffset = checked((int)stream.Position);
                    var whiteColors = new byte[checked(vertexCount * 4)];
                    Array.Fill(whiteColors, byte.MaxValue);
                    stream.Write(whiteColors);

                    var bufferViewIndex = bufferViews.Count;
                    bufferViews.Add(new JsonObject
                    {
                        ["buffer"] = 0,
                        ["byteOffset"] = byteOffset,
                        ["byteLength"] = whiteColors.Length,
                    });

                    accessorIndex = accessors.Count;
                    accessors.Add(new JsonObject
                    {
                        ["bufferView"] = bufferViewIndex,
                        ["componentType"] = 5121,
                        ["normalized"] = true,
                        ["count"] = vertexCount,
                        ["type"] = "VEC4",
                    });
                    whiteAccessorByVertexCount.Add(vertexCount, accessorIndex);
                }

                attributes["COLOR_0"] = accessorIndex;
            }

            buffer["byteLength"] = checked((int)stream.Length);
        }

        AtomicFile.WriteAllText(
            gltfPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool HasVertexColor(JsonObject primitive) =>
        primitive["attributes"] is JsonObject attributes
        && attributes.ContainsKey("COLOR_0");

    private static void SplitGltfPrimitivesForDcc(string gltfPath)
    {
        var root = JsonNode.Parse(File.ReadAllText(gltfPath))?.AsObject()
            ?? throw new InvalidDataException($"glTF JSON is empty: {gltfPath}");
        if (root["meshes"] is not JsonArray meshes
            || root["nodes"] is not JsonArray nodes
            || root["accessors"] is not JsonArray accessors
            || root["bufferViews"] is not JsonArray bufferViews
            || root["buffers"] is not JsonArray buffers
            || buffers.Count == 0
            || buffers[0] is not JsonObject buffer)
        {
            return;
        }

        var bufferUri = buffer["uri"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(bufferUri)
            || Uri.TryCreate(bufferUri, UriKind.Absolute, out _))
        {
            throw new InvalidDataException(
                $"glTF primitive splitting requires an external relative buffer in {Path.GetFileName(gltfPath)}.");
        }

        var bufferPath = SafePath.ResolveUnderRoot(
            Path.GetDirectoryName(gltfPath)!,
            Uri.UnescapeDataString(bufferUri).Replace('/', Path.DirectorySeparatorChar),
            "glTF binary buffer");
        var originalBuffer = File.ReadAllBytes(bufferPath);
        using var appendedBuffer = new FileStream(bufferPath, FileMode.Append, FileAccess.Write, FileShare.None);

        var materials = root["materials"] as JsonArray;
        var splitMeshes = new Dictionary<int, int[]>();
        var originalMeshCount = meshes.Count;
        for (var meshIndex = 0; meshIndex < originalMeshCount; meshIndex++)
        {
            if (meshes[meshIndex] is not JsonObject mesh
                || mesh["primitives"] is not JsonArray primitives
                || primitives.Count <= 1)
            {
                continue;
            }

            var baseName = mesh["name"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(baseName))
            {
                baseName = $"mesh_{meshIndex:D2}";
            }

            var replacementIndexes = new int[primitives.Count];
            for (var primitiveIndex = 0; primitiveIndex < primitives.Count; primitiveIndex++)
            {
                var primitive = primitives[primitiveIndex]?.DeepClone() as JsonObject
                    ?? throw new InvalidDataException(
                        $"glTF mesh {meshIndex} contains an empty primitive in {Path.GetFileName(gltfPath)}.");
                CompactPrimitiveAccessors(
                    primitive,
                    accessors,
                    bufferViews,
                    originalBuffer,
                    appendedBuffer,
                    gltfPath);
                var materialSuffix = GetPrimitiveMaterialSuffix(primitive, materials);
                var partName = $"{baseName}__part_{primitiveIndex:D2}{materialSuffix}";
                var splitMesh = new JsonObject
                {
                    ["name"] = partName,
                    ["primitives"] = new JsonArray(primitive),
                };
                if (mesh["weights"] is JsonNode weights)
                {
                    splitMesh["weights"] = weights.DeepClone();
                }
                if (mesh["extras"] is JsonNode extras)
                {
                    splitMesh["extras"] = extras.DeepClone();
                }

                replacementIndexes[primitiveIndex] = meshes.Count;
                meshes.Add(splitMesh);
            }

            splitMeshes.Add(meshIndex, replacementIndexes);
        }

        if (splitMeshes.Count == 0)
        {
            return;
        }

        var originalNodeCount = nodes.Count;
        var splitNodeChildren = new Dictionary<int, int[]>();
        for (var nodeIndex = 0; nodeIndex < originalNodeCount; nodeIndex++)
        {
            if (nodes[nodeIndex] is not JsonObject node
                || node["mesh"]?.GetValue<int>() is not int originalMeshIndex
                || !splitMeshes.TryGetValue(originalMeshIndex, out var replacementIndexes))
            {
                continue;
            }

            node.Remove("mesh");
            var skin = node["skin"]?.DeepClone();
            node.Remove("skin");
            var weights = node["weights"]?.DeepClone();
            node.Remove("weights");

            var children = node["children"] as JsonArray;
            if (children is null)
            {
                children = [];
                node["children"] = children;
            }

            var nodeName = node["name"]?.GetValue<string>() ?? $"node_{nodeIndex:D2}";
            var newChildren = new int[replacementIndexes.Length];
            for (var partIndex = 0; partIndex < replacementIndexes.Length; partIndex++)
            {
                var child = new JsonObject
                {
                    ["name"] = $"{nodeName}__part_{partIndex:D2}",
                    ["mesh"] = replacementIndexes[partIndex],
                };
                if (skin is not null)
                {
                    child["skin"] = skin.DeepClone();
                }
                if (weights is not null)
                {
                    child["weights"] = weights.DeepClone();
                }

                newChildren[partIndex] = nodes.Count;
                children.Add(nodes.Count);
                nodes.Add(child);
            }
            splitNodeChildren.Add(nodeIndex, newChildren);
        }

        RetargetSplitMorphAnimations(root, splitNodeChildren);

        var orderedMeshes = new JsonArray();
        var oldToNewMeshIndex = new Dictionary<int, int>();
        for (var meshIndex = 0; meshIndex < originalMeshCount; meshIndex++)
        {
            var sourceIndexes = splitMeshes.TryGetValue(meshIndex, out var replacements)
                ? replacements
                : [meshIndex];
            foreach (var sourceIndex in sourceIndexes)
            {
                oldToNewMeshIndex[sourceIndex] = orderedMeshes.Count;
                orderedMeshes.Add(meshes[sourceIndex]?.DeepClone());
            }
        }
        root["meshes"] = orderedMeshes;

        foreach (var node in nodes.OfType<JsonObject>())
        {
            if (node["mesh"]?.GetValue<int>() is not int meshIndex)
            {
                continue;
            }
            node["mesh"] = oldToNewMeshIndex[meshIndex];
        }

        buffer["byteLength"] = checked((int)appendedBuffer.Length);

        AtomicFile.WriteAllText(
            gltfPath,
            root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void RetargetSplitMorphAnimations(
        JsonObject root,
        IReadOnlyDictionary<int, int[]> splitNodeChildren)
    {
        if (splitNodeChildren.Count == 0 || root["animations"] is not JsonArray animations)
        {
            return;
        }

        foreach (var animation in animations.OfType<JsonObject>())
        {
            if (animation["channels"] is not JsonArray channels)
            {
                continue;
            }
            var updated = new JsonArray();
            foreach (var channelNode in channels)
            {
                if (channelNode is not JsonObject channel
                    || channel["target"] is not JsonObject target
                    || !string.Equals(target["path"]?.GetValue<string>(), "weights", StringComparison.Ordinal)
                    || target["node"]?.GetValue<int>() is not int nodeIndex
                    || !splitNodeChildren.TryGetValue(nodeIndex, out var childIndexes))
                {
                    updated.Add(channelNode?.DeepClone());
                    continue;
                }

                foreach (var childIndex in childIndexes)
                {
                    var clone = channel.DeepClone().AsObject();
                    clone["target"]!["node"] = childIndex;
                    updated.Add(clone);
                }
            }
            animation["channels"] = updated;
        }
    }

    private static void CompactPrimitiveAccessors(
        JsonObject primitive,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] sourceBuffer,
        FileStream destinationBuffer,
        string gltfPath)
    {
        if (primitive["indices"]?.GetValue<int>() is not int indexAccessorIndex
            || primitive["attributes"] is not JsonObject attributes)
        {
            throw new InvalidDataException(
                $"glTF primitive has no indexed geometry in {Path.GetFileName(gltfPath)}.");
        }

        var sourceIndices = ReadUnsignedAccessor(
            indexAccessorIndex,
            accessors,
            bufferViews,
            sourceBuffer,
            gltfPath);
        var oldToNew = new Dictionary<int, int>();
        var usedVertices = new List<int>();
        var compactIndices = new int[sourceIndices.Length];
        for (var index = 0; index < sourceIndices.Length; index++)
        {
            var sourceVertex = sourceIndices[index];
            if (!oldToNew.TryGetValue(sourceVertex, out var compactVertex))
            {
                compactVertex = usedVertices.Count;
                oldToNew.Add(sourceVertex, compactVertex);
                usedVertices.Add(sourceVertex);
            }
            compactIndices[index] = compactVertex;
        }

        foreach (var attribute in attributes.ToArray())
        {
            if (attribute.Value?.GetValue<int>() is int accessorIndex)
            {
                attributes[attribute.Key] = AppendCompactedAccessor(
                    accessorIndex,
                    usedVertices,
                    accessors,
                    bufferViews,
                    sourceBuffer,
                    destinationBuffer,
                    gltfPath);
            }
        }

        if (primitive["targets"] is JsonArray targets)
        {
            foreach (var target in targets.OfType<JsonObject>())
            {
                foreach (var attribute in target.ToArray())
                {
                    if (attribute.Value?.GetValue<int>() is int accessorIndex)
                    {
                        target[attribute.Key] = AppendCompactedAccessor(
                            accessorIndex,
                            usedVertices,
                            accessors,
                            bufferViews,
                            sourceBuffer,
                            destinationBuffer,
                            gltfPath);
                    }
                }
            }
        }

        Align4(destinationBuffer);
        var indexOffset = checked((int)destinationBuffer.Position);
        var useUInt32 = usedVertices.Count > ushort.MaxValue;
        foreach (var value in compactIndices)
        {
            destinationBuffer.Write(useUInt32
                ? BitConverter.GetBytes((uint)value)
                : BitConverter.GetBytes((ushort)value));
        }

        var indexView = bufferViews.Count;
        bufferViews.Add(new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = indexOffset,
            ["byteLength"] = checked(compactIndices.Length * (useUInt32 ? 4 : 2)),
            ["target"] = 34963,
        });
        var compactIndexAccessor = accessors.Count;
        accessors.Add(new JsonObject
        {
            ["bufferView"] = indexView,
            ["componentType"] = useUInt32 ? 5125 : 5123,
            ["count"] = compactIndices.Length,
            ["type"] = "SCALAR",
        });
        primitive["indices"] = compactIndexAccessor;
    }

    private static int AppendCompactedAccessor(
        int accessorIndex,
        IReadOnlyList<int> sourceVertices,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] sourceBuffer,
        FileStream destinationBuffer,
        string gltfPath)
    {
        var accessor = GetGltfObject(accessors, accessorIndex, "accessor", gltfPath);
        if (accessor.ContainsKey("sparse"))
        {
            throw new InvalidDataException(
                $"Sparse glTF accessors are unsupported during primitive splitting: {Path.GetFileName(gltfPath)}.");
        }

        var viewIndex = accessor["bufferView"]?.GetValue<int>() ?? -1;
        var view = GetGltfObject(bufferViews, viewIndex, "buffer view", gltfPath);
        if ((view["buffer"]?.GetValue<int>() ?? 0) != 0)
        {
            throw new InvalidDataException(
                $"Multiple glTF buffers are unsupported during primitive splitting: {Path.GetFileName(gltfPath)}.");
        }

        var componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
        var type = accessor["type"]?.GetValue<string>()
            ?? throw new InvalidDataException($"glTF accessor has no value type in {Path.GetFileName(gltfPath)}.");
        var count = accessor["count"]?.GetValue<int>() ?? 0;
        var elementSize = checked(GetGltfComponentSize(componentType) * GetGltfComponentCount(type));
        var stride = view["byteStride"]?.GetValue<int>() ?? elementSize;
        var start = checked((view["byteOffset"]?.GetValue<int>() ?? 0)
            + (accessor["byteOffset"]?.GetValue<int>() ?? 0));

        Align4(destinationBuffer);
        var destinationOffset = checked((int)destinationBuffer.Position);
        foreach (var sourceVertex in sourceVertices)
        {
            if (sourceVertex < 0 || sourceVertex >= count)
            {
                throw new InvalidDataException(
                    $"glTF index {sourceVertex} exceeds accessor {accessorIndex} in {Path.GetFileName(gltfPath)}.");
            }

            var sourceOffset = checked(start + (sourceVertex * stride));
            if (sourceOffset < 0 || sourceOffset + elementSize > sourceBuffer.Length)
            {
                throw new InvalidDataException(
                    $"glTF accessor {accessorIndex} exceeds its binary buffer in {Path.GetFileName(gltfPath)}.");
            }
            destinationBuffer.Write(sourceBuffer, sourceOffset, elementSize);
        }

        var destinationViewIndex = bufferViews.Count;
        bufferViews.Add(new JsonObject
        {
            ["buffer"] = 0,
            ["byteOffset"] = destinationOffset,
            ["byteLength"] = checked(sourceVertices.Count * elementSize),
            ["target"] = 34962,
        });
        var result = new JsonObject
        {
            ["bufferView"] = destinationViewIndex,
            ["componentType"] = componentType,
            ["count"] = sourceVertices.Count,
            ["type"] = type,
        };
        if (accessor["normalized"]?.GetValue<bool>() == true)
        {
            result["normalized"] = true;
        }

        var resultIndex = accessors.Count;
        accessors.Add(result);
        return resultIndex;
    }

    private static int[] ReadUnsignedAccessor(
        int accessorIndex,
        JsonArray accessors,
        JsonArray bufferViews,
        byte[] sourceBuffer,
        string gltfPath)
    {
        var accessor = GetGltfObject(accessors, accessorIndex, "accessor", gltfPath);
        var view = GetGltfObject(
            bufferViews,
            accessor["bufferView"]?.GetValue<int>() ?? -1,
            "buffer view",
            gltfPath);
        var componentType = accessor["componentType"]?.GetValue<int>() ?? 0;
        var componentSize = GetGltfComponentSize(componentType);
        var count = accessor["count"]?.GetValue<int>() ?? 0;
        var stride = view["byteStride"]?.GetValue<int>() ?? componentSize;
        var start = checked((view["byteOffset"]?.GetValue<int>() ?? 0)
            + (accessor["byteOffset"]?.GetValue<int>() ?? 0));
        var result = new int[count];
        for (var index = 0; index < count; index++)
        {
            var offset = checked(start + (index * stride));
            result[index] = componentType switch
            {
                5121 => sourceBuffer[offset],
                5123 => BitConverter.ToUInt16(sourceBuffer, offset),
                5125 => checked((int)BitConverter.ToUInt32(sourceBuffer, offset)),
                _ => throw new InvalidDataException(
                    $"Unsupported glTF index component type {componentType} in {Path.GetFileName(gltfPath)}."),
            };
        }
        return result;
    }

    private static JsonObject GetGltfObject(JsonArray array, int index, string kind, string gltfPath)
    {
        if (index < 0 || index >= array.Count || array[index] is not JsonObject value)
        {
            throw new InvalidDataException(
                $"glTF contains an invalid {kind} index {index} in {Path.GetFileName(gltfPath)}.");
        }
        return value;
    }

    private static void Align4(FileStream stream)
    {
        while (stream.Position % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    private static int GetGltfComponentSize(int componentType) => componentType switch
    {
        5120 or 5121 => 1,
        5122 or 5123 => 2,
        5125 or 5126 => 4,
        _ => throw new InvalidDataException($"Unsupported glTF component type {componentType}."),
    };

    private static int GetGltfComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT2" => 4,
        "MAT3" => 9,
        "MAT4" => 16,
        _ => throw new InvalidDataException($"Unsupported glTF accessor type '{type}'."),
    };

    private static string GetPrimitiveMaterialSuffix(JsonNode primitive, JsonArray? materials)
    {
        if (primitive is not JsonObject primitiveObject
            || primitiveObject["material"]?.GetValue<int>() is not int materialIndex
            || materials is null
            || materialIndex < 0
            || materialIndex >= materials.Count
            || materials[materialIndex] is not JsonObject material
            || string.IsNullOrWhiteSpace(material["name"]?.GetValue<string>()))
        {
            return string.Empty;
        }

        var name = material["name"]!.GetValue<string>();
        var safe = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' ? ch : '_').ToArray());
        return safe.Length == 0 ? string.Empty : $"__{safe}";
    }

    private static void ValidateGltfSkinningContract(string gltfPath)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(gltfPath));
        var root = document.RootElement;
        if (!root.TryGetProperty("meshes", out var meshes)
            || meshes.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var weightedMeshes = new HashSet<int>();
        var meshIndex = 0;
        foreach (var mesh in meshes.EnumerateArray())
        {
            if (mesh.TryGetProperty("primitives", out var primitives)
                && primitives.ValueKind == JsonValueKind.Array)
            {
                foreach (var primitive in primitives.EnumerateArray())
                {
                    if (!primitive.TryGetProperty("attributes", out var attributes)
                        || attributes.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var hasJoints = attributes.TryGetProperty("JOINTS_0", out _);
                    var hasWeights = attributes.TryGetProperty("WEIGHTS_0", out _);
                    if (hasJoints != hasWeights)
                    {
                        throw new InvalidDataException(
                            $"glTF mesh {meshIndex} has incomplete skinning attributes in {Path.GetFileName(gltfPath)}.");
                    }

                    if (hasJoints)
                    {
                        weightedMeshes.Add(meshIndex);
                    }
                }
            }

            meshIndex++;
        }

        if (weightedMeshes.Count == 0)
        {
            return;
        }

        if (!root.TryGetProperty("skins", out var skins)
            || skins.ValueKind != JsonValueKind.Array
            || skins.GetArrayLength() == 0)
        {
            throw new InvalidDataException(
                $"glTF export retained joint weights but omitted skeleton/Skin data in {Path.GetFileName(gltfPath)}.");
        }

        var skinnedMeshes = new HashSet<int>();
        if (root.TryGetProperty("nodes", out var nodes)
            && nodes.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in nodes.EnumerateArray())
            {
                if (!node.TryGetProperty("mesh", out var meshProperty)
                    || !meshProperty.TryGetInt32(out var referencedMesh)
                    || !weightedMeshes.Contains(referencedMesh))
                {
                    continue;
                }

                if (!node.TryGetProperty("skin", out var skinProperty)
                    || !skinProperty.TryGetInt32(out var referencedSkin))
                {
                    throw new InvalidDataException(
                        $"glTF node omitted the Skin binding for weighted mesh {referencedMesh} in {Path.GetFileName(gltfPath)}.");
                }

                if (referencedSkin < 0 || referencedSkin >= skins.GetArrayLength())
                {
                    throw new InvalidDataException(
                        $"glTF mesh {referencedMesh} references an invalid Skin in {Path.GetFileName(gltfPath)}.");
                }

                var skin = skins[referencedSkin];
                if (!skin.TryGetProperty("joints", out var joints)
                    || joints.ValueKind != JsonValueKind.Array
                    || joints.GetArrayLength() == 0)
                {
                    throw new InvalidDataException(
                        $"glTF mesh {referencedMesh} references an empty skeleton in {Path.GetFileName(gltfPath)}.");
                }

                skinnedMeshes.Add(referencedMesh);
            }
        }

        if (!weightedMeshes.SetEquals(skinnedMeshes))
        {
            var missing = weightedMeshes.Except(skinnedMeshes).Order().Select(index => index.ToString());
            throw new InvalidDataException(
                $"glTF export omitted Skin bindings for weighted mesh index(es) {string.Join(", ", missing)} in {Path.GetFileName(gltfPath)}.");
        }

    }
}
