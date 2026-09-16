namespace Deadlimit.Core;

internal static class RetailResourcePackagingPolicySmoke
{
    public static int Run()
    {
        if (!UnsupportedHeaderIsNonFatal()
            || !NonCompiledPayloadIsNotParsed()
            || !TruncatedCompiledResourceIsNonFatal()
            || !ChangedPreparedRetailTextureIsDetected())
        {
            return 1;
        }

        var addonFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["models/custom.vmdl_c"] = "custom-model",
            ["models/stock.vmdl_c"] = "stock-model",
            ["materials/custom.vmat_c"] = "custom-material",
            ["materials/stock.vmat_c"] = "stock-material",
            ["materials/custom.vtex_c"] = "custom-texture",
            ["materials/stock.vtex_c"] = "stock-texture",
            ["materials/overridden.vtex_c"] = "overridden-texture",
            ["materials/overridden-stock.vmat_c"] = "overridden-stock-material",
            ["materials/unreferenced-generated.vtex_c"] = "unreferenced-generated",
        };
        var retailResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "models/stock.vmdl_c",
            "materials/stock.vmat_c",
            "materials/stock.vtex_c",
            "materials/overridden.vtex_c",
            "materials/overridden-stock.vmat_c",
        };
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "models/custom.vmdl_c",
            "materials/overridden-stock.vmat_c",
        };
        var forcedMaterialDependencyOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "materials/overridden-stock.vmat_c",
        };
        var references = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["models/custom.vmdl_c"] = ["models/stock.vmdl", "materials/custom.vmat"],
            ["materials/custom.vmat_c"] = ["materials/custom.vtex", "materials/stock.vtex"],
            ["materials/overridden-stock.vmat_c"] = ["materials/overridden.vtex"],
        };

        var included = RetailResourcePackagingPolicy.ResolveClosureForSmoke(
            addonFiles,
            retailResources,
            roots,
            forcedMaterialDependencyOwners,
            references);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "models/custom.vmdl_c",
            "materials/custom.vmat_c",
            "materials/custom.vtex_c",
            "materials/overridden-stock.vmat_c",
            "materials/overridden.vtex_c",
        };

        return included.SetEquals(expected) ? 0 : 1;
    }

    private static bool ChangedPreparedRetailTextureIsDetected()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-packaging-texture-{Guid.NewGuid():N}");
        var extractedRoot = Path.Combine(root, "0source");
        var preparedRoot = Path.Combine(root, "content", "citadel_addons", "test");
        const string resourcePath = "materials/blends/ground_plants_01_color.png";
        var relativePath = resourcePath.Replace('/', Path.DirectorySeparatorChar);
        var extractedPath = Path.Combine(extractedRoot, relativePath);
        var preparedPath = Path.Combine(preparedRoot, relativePath);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(extractedPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(preparedPath)!);
            File.WriteAllBytes(extractedPath, [1, 2, 3, 4]);
            File.WriteAllBytes(preparedPath, [1, 2, 3, 4]);

            RetailTextureTarget[] targets =
            [
                new(
                    resourcePath,
                    "materials/particle/ground_nature_projected.vmat",
                    HasExtractedSourceFile: true),
            ];

            var identical = RetailResourcePackagingPolicy.ResolvePreparedTextureOverridesForSmoke(
                targets,
                extractedRoot,
                preparedRoot);
            if (identical.Count != 0)
            {
                return false;
            }

            File.WriteAllBytes(preparedPath, [9, 8, 7, 6]);
            var changed = RetailResourcePackagingPolicy.ResolvePreparedTextureOverridesForSmoke(
                targets,
                extractedRoot,
                preparedRoot);
            return changed.SetEquals([resourcePath]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static bool UnsupportedHeaderIsNonFatal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadlimit-packaging-{Guid.NewGuid():N}.vmdl_c");
        const string resourcePath = "models/unsupported-header.vmdl_c";
        var diagnostics = new List<string>();

        try
        {
            using (var stream = File.Create(path))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((uint)64);
                writer.Write((ushort)14);
            }

            var references = RetailResourcePackagingPolicy.ReadExternalReferencesForSmoke(
                path,
                resourcePath,
                diagnostics.Add);

            return references.Count == 0
                   && diagnostics.Count == 1
                   && diagnostics[0].Contains(resourcePath, StringComparison.OrdinalIgnoreCase)
                   && diagnostics[0].Contains("UnexpectedMagicException", StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool NonCompiledPayloadIsNotParsed()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadlimit-packaging-{Guid.NewGuid():N}.bin");
        const string resourcePath = "tools_asset_info.bin";
        var diagnostics = new List<string>();

        try
        {
            File.WriteAllBytes(path, [0x01]);
            var references = RetailResourcePackagingPolicy.ReadExternalReferencesForSmoke(
                path,
                resourcePath,
                diagnostics.Add);

            return references.Count == 0 && diagnostics.Count == 0;
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static bool TruncatedCompiledResourceIsNonFatal()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deadlimit-packaging-{Guid.NewGuid():N}.vmdl_c");
        const string resourcePath = "models/truncated.vmdl_c";
        var diagnostics = new List<string>();

        try
        {
            File.WriteAllBytes(path, [0x40, 0x00, 0x00, 0x00]);
            var references = RetailResourcePackagingPolicy.ReadExternalReferencesForSmoke(
                path,
                resourcePath,
                diagnostics.Add);

            return references.Count == 0
                   && diagnostics.Count == 1
                   && diagnostics[0].Contains(resourcePath, StringComparison.OrdinalIgnoreCase)
                   && diagnostics[0].Contains("EndOfStreamException", StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
