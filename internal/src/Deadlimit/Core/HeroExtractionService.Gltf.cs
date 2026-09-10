using System.Text.Json;
using System.Text.Json.Nodes;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;

namespace Deadlimit.Core;

public sealed partial class HeroExtractionService
{
    private const string SkeletonOnlyAnimationFilter = "__deadlimit_skeleton_only_no_animation_clips__";

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
                {
                    if (!message.Contains(SkeletonOnlyAnimationFilter, StringComparison.Ordinal))
                    {
                        progress?.Report(new HeroExtractionProgress(message));
                    }
                }),
                // ValveResourceFormat currently uses ExportAnimations to gate both animation
                // channels and the skeleton/Skin objects. Keep it enabled, then use an exact
                // impossible animation name so DCC exports retain skinning without copying the
                // retail animation library into every character source file.
                ExportAnimations = true,
                ExportMaterials = includeTextures,
                AdaptTextures = true,
                SatelliteImages = true,
                ExportExtras = true,
                ComposeAdditiveAnimations = false,
            };
            exporter.AnimationFilter.Add(SkeletonOnlyAnimationFilter);

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
        // The Max importer concatenates primitives into one mesh but advances its color
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

        if (root.TryGetProperty("animations", out var animations)
            && animations.ValueKind == JsonValueKind.Array
            && animations.GetArrayLength() > 0)
        {
            throw new InvalidDataException(
                $"Skeleton-only glTF export unexpectedly included animation clips in {Path.GetFileName(gltfPath)}.");
        }
    }
}
