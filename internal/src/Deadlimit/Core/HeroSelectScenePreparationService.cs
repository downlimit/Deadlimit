using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Deadlimit.Core;

public sealed record HeroSelectScenePreparationResult(
    string HeroPrefabId,
    string SourceVpkPath,
    IReadOnlyList<string> ScenePaths,
    int CreatedCount,
    int PreservedCount,
    IReadOnlyList<string> RuntimeResourcePaths,
    int RuntimeCreatedCount,
    int RuntimePreservedCount);

public sealed class HeroSelectScenePreparationService
{
    private const string HeroPrefabRelativeFolder = "citadel/maps/ui/hero_prefabs";
    private const string HeroPrefabContentFolder = "maps/ui/hero_prefabs";

    private readonly DeadlimitPaths _paths;

    public HeroSelectScenePreparationService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public HeroSelectScenePreparationResult Prepare(
        ProjectManifest manifest,
        string addonContentRoot,
        string addonGameRoot,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var prefabFolder = Path.Combine(
            _paths.CsdkGameRoot,
            HeroPrefabRelativeFolder.Replace('/', Path.DirectorySeparatorChar));
        var (heroPrefabId, sourceVpkPath) = ResolveHeroPrefabVpk(manifest, prefabFolder);

        using var package = new Package();
        package.Read(sourceVpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"Hero-select VPK contains no entries: {sourceVpkPath}");
        using var fileLoader = new GameFileLoader(package, package.FileName);

        var packageFiles = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, Path: NormalizeResourcePath(entry.GetFullPath())))
            .Where(item => item.Path.Length > 0)
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var mapEntries = packageFiles
            .Where(item => item.Path.EndsWith(".vmap_c", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (mapEntries.Length == 0)
        {
            throw new InvalidDataException(
                $"No compiled VMAP resources were found in the hero-select VPK: {sourceVpkPath}");
        }

        var preparedScenes = new List<(string Path, byte[]? Data)>();
        foreach (var (entry, resourcePath) in mapEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativeScenePath = ResolveSceneRelativePath(resourcePath);
            var outputPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                relativeScenePath.Replace('/', Path.DirectorySeparatorChar),
                "Prepared hero-select VMAP");
            if (File.Exists(outputPath))
            {
                preparedScenes.Add((outputPath, null));
                continue;
            }

            package.ReadEntry(entry, out byte[] rawData);

            using var stream = new MemoryStream(rawData, writable: false);
            using var resource = new Resource { FileName = resourcePath };
            resource.Read(stream);
            using var contentFile = FileExtract.Extract(resource, fileLoader, null);

            if (contentFile.Data is null || contentFile.Data.Length == 0)
            {
                throw new InvalidDataException(
                    $"ValveResourceFormat returned no editable VMAP data for '{resourcePath}'.");
            }

            preparedScenes.Add((outputPath, contentFile.Data.ToArray()));
        }

        var created = 0;
        var preserved = 0;
        var scenePaths = new List<string>(preparedScenes.Count);
        foreach (var (outputPath, data) in preparedScenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            scenePaths.Add(outputPath);
            if (data is null || File.Exists(outputPath))
            {
                preserved++;
                continue;
            }

            if (WriteNewFileAtomically(outputPath, data))
            {
                created++;
            }
            else
            {
                preserved++;
            }
        }

        var runtimeCreated = 0;
        var runtimePreserved = 0;
        var runtimeResourcePaths = new List<string>(packageFiles.Length);
        foreach (var (entry, resourcePath) in packageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outputPath = SafePath.ResolveUnderRoot(
                addonGameRoot,
                resourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Prepared hero-select runtime resource");
            runtimeResourcePaths.Add(outputPath);
            if (File.Exists(outputPath))
            {
                runtimePreserved++;
                continue;
            }

            package.ReadEntry(entry, out byte[] rawData);
            if (WriteNewFileAtomically(outputPath, rawData))
            {
                runtimeCreated++;
            }
            else
            {
                runtimePreserved++;
            }
        }

        return new HeroSelectScenePreparationResult(
            heroPrefabId,
            sourceVpkPath,
            scenePaths,
            created,
            preserved,
            runtimeResourcePaths,
            runtimeCreated,
            runtimePreserved);
    }

    private static (string HeroPrefabId, string VpkPath) ResolveHeroPrefabVpk(
        ProjectManifest manifest,
        string prefabFolder)
    {
        if (!Directory.Exists(prefabFolder))
        {
            throw new DirectoryNotFoundException(
                $"CSDK hero-select prefab folder was not found: {prefabFolder}");
        }

        var vpkPaths = Directory.EnumerateFiles(prefabFolder, "*.vpk", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (vpkPaths.Length == 0)
        {
            throw new FileNotFoundException(
                $"No hero-select VPK files were found in the configured CSDK: {prefabFolder}");
        }

        var candidates = GetHeroPrefabIdCandidates(manifest).ToArray();
        foreach (var candidate in candidates)
        {
            var exact = vpkPaths.FirstOrDefault(path => string.Equals(
                Path.GetFileNameWithoutExtension(path),
                candidate,
                StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return (Path.GetFileNameWithoutExtension(exact), exact);
            }
        }

        foreach (var candidate in candidates)
        {
            var suffixMatches = vpkPaths
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .EndsWith("_" + candidate, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (suffixMatches.Length == 1)
            {
                return (Path.GetFileNameWithoutExtension(suffixMatches[0]), suffixMatches[0]);
            }
        }

        throw new FileNotFoundException(
            $"Could not match hero '{manifest.Hero}' to a hero-select VPK in '{prefabFolder}'. " +
            $"Tried prefab IDs: {string.Join(", ", candidates)}. Refresh the hero catalog and verify the configured CSDK.");
    }

    private static IEnumerable<string> GetHeroPrefabIdCandidates(ProjectManifest manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var retailModel = NormalizeResourcePath(manifest.RetailMainModel ?? string.Empty);
        if (retailModel.EndsWith("_c", StringComparison.OrdinalIgnoreCase))
        {
            retailModel = retailModel[..^2];
        }

        var catalogMatch = HeroCatalogService.LoadCached().FirstOrDefault(hero =>
            string.Equals(NormalizeResourcePath(hero.ModelResourcePath), retailModel, StringComparison.OrdinalIgnoreCase)
            || string.Equals(hero.DisplayName, manifest.Hero, StringComparison.OrdinalIgnoreCase)
            || string.Equals(hero.LookupName, manifest.Hero, StringComparison.OrdinalIgnoreCase));

        if (catalogMatch is not null)
        {
            var minimapStem = Path.GetFileNameWithoutExtension(
                NormalizeResourcePath(catalogMatch.MinimapImageResourcePath));
            if (minimapStem.EndsWith("_mm", StringComparison.OrdinalIgnoreCase))
            {
                minimapStem = minimapStem[..^3];
            }
            if (TryNormalizeId(minimapStem, out var minimapId) && seen.Add(minimapId))
            {
                yield return minimapId;
            }

            if (TryNormalizeId(catalogMatch.LookupName, out var lookupId) && seen.Add(lookupId))
            {
                yield return lookupId;
            }
        }

        var modelStem = Path.GetFileNameWithoutExtension(retailModel);
        if (TryNormalizeId(modelStem, out var modelId) && seen.Add(modelId))
        {
            yield return modelId;
        }

        if (TryNormalizeId(manifest.Hero, out var heroId) && seen.Add(heroId))
        {
            yield return heroId;
        }
    }

    private static string ResolveSceneRelativePath(string resourcePath)
    {
        var relative = Path.ChangeExtension(NormalizeResourcePath(resourcePath), ".vmap");
        var fileName = Path.GetFileNameWithoutExtension(relative);
        if (fileName.EndsWith("_d", StringComparison.OrdinalIgnoreCase))
        {
            relative = NormalizeResourcePath(Path.Combine(
                Path.GetDirectoryName(relative) ?? string.Empty,
                fileName[..^2] + ".vmap"));
        }

        if (!relative.StartsWith(HeroPrefabContentFolder + "/", StringComparison.OrdinalIgnoreCase))
        {
            relative = HeroPrefabContentFolder + "/" + Path.GetFileName(relative);
        }

        return relative;
    }

    private static bool TryNormalizeId(string value, out string normalized)
    {
        normalized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character is '_' or '-' ? character : '_')
            .ToArray())
            .Trim('_');
        return normalized.Length > 0;
    }

    private static string NormalizeResourcePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static bool WriteNewFileAtomically(string outputPath, byte[] data)
    {
        var outputFolder = Path.GetDirectoryName(outputPath)
            ?? throw new InvalidOperationException($"Hero-select VMAP has no parent folder: {outputPath}");
        Directory.CreateDirectory(outputFolder);

        var temporaryPath = Path.Combine(outputFolder, $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, data);
            File.Move(temporaryPath, outputPath, overwrite: false);
            return true;
        }
        catch (IOException) when (File.Exists(outputPath))
        {
            // A concurrent PREPARE published the same scene. Preserve that completed file.
            return false;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
