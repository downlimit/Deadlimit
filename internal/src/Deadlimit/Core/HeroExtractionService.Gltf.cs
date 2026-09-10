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
                // Skin authoring reuses retail animation resources. Exporting the full retail
                // animation closure is brittle because current VMDLs can reference clips that
                // are absent from the shipped VPK set.
                ExportAnimations = false,
                ExportMaterials = includeTextures,
                AdaptTextures = true,
                SatelliteImages = true,
                ExportExtras = true,
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
}
