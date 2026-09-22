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
    IReadOnlyList<string> AuthoringResourcePaths,
    int AuthoringResourceCreatedCount,
    int AuthoringResourcePreservedCount,
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

        // Hammer can display compiled resources from the addon game folder, but
        // ResourceCompiler needs editable model sources while rebuilding a VMAP.
        // World-node prop models are stored inside the prefab VPK, so publish
        // their decompiled VMDL and embedded mesh sidecars into addon content.
        var authoringResourcePaths = new List<string>();
        var authoringResourceCreated = 0;
        var authoringResourcePreserved = 0;
        var sceneResourcePrefix = $"{HeroPrefabContentFolder}/{heroPrefabId}/";
        var authoringModelEntries = packageFiles
            .Where(item => item.Path.StartsWith(sceneResourcePrefix, StringComparison.OrdinalIgnoreCase))
            .Where(item => item.Path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var (entry, resourcePath) in authoringModelEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            package.ReadEntry(entry, out byte[] rawData);

            using var stream = new MemoryStream(rawData, writable: false);
            using var resource = new Resource { FileName = resourcePath };
            resource.Read(stream);

            var outputExtension = FileExtract.GetExtension(resource) ?? entry.TypeName[..^2];
            var relativeOutputPath = Path.ChangeExtension(resourcePath, outputExtension);
            var outputPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                relativeOutputPath.Replace('/', Path.DirectorySeparatorChar),
                "Prepared hero-select authoring resource");

            using var contentFile = FileExtract.Extract(resource, fileLoader, null);
            PublishContentFilePreserving(
                addonContentRoot,
                outputPath,
                contentFile,
                authoringResourcePaths,
                ref authoringResourceCreated,
                ref authoringResourcePreserved);
        }

        // A compiled Source 2 map is delivered as a VPK package. Its internal
        // *.vmap_c directory entry must not be published as a loose game file:
        // the tools treat that path as a packed-map store and report it as a
        // corrupt VPK directory. Remove files created by the older PREPARE
        // behavior before publishing the remaining runtime support resources.
        RemoveLegacyLooseCompiledMaps(addonContentRoot, addonGameRoot);

        var runtimeCreated = 0;
        var runtimePreserved = 0;
        var runtimeResourcePaths = new List<string>(packageFiles.Length);
        foreach (var (entry, resourcePath) in packageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsLooseCompiledMapResource(resourcePath))
            {
                continue;
            }

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
            authoringResourcePaths,
            authoringResourceCreated,
            authoringResourcePreserved,
            runtimeResourcePaths,
            runtimeCreated,
            runtimePreserved);
    }

    internal static int RemoveLegacyLooseCompiledMaps(
        ProjectManifest manifest,
        DeadlimitPaths paths)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(paths);

        if (manifest.Mode == ProjectMode.ImportedVpk)
        {
            return 0;
        }

        var addonId = AddonIdentityService.ResolveInitialAddonId(manifest, manifest.ProjectName);
        var addonContentRoot = Path.Combine(paths.CsdkContentRoot, "citadel_addons", addonId);
        var addonGameRoot = Path.Combine(paths.CsdkGameRoot, "citadel_addons", addonId);
        return RemoveLegacyLooseCompiledMaps(addonContentRoot, addonGameRoot);
    }

    internal static int RemoveLegacyLooseCompiledMaps(
        string addonContentRoot,
        string addonGameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(addonContentRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonGameRoot);

        var sceneFolder = Path.Combine(
            addonContentRoot,
            HeroPrefabContentFolder.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(sceneFolder))
        {
            return 0;
        }

        var removed = 0;
        foreach (var scenePath in Directory.EnumerateFiles(
                     sceneFolder,
                     "*.vmap",
                     SearchOption.TopDirectoryOnly))
        {
            var sceneId = Path.GetFileNameWithoutExtension(scenePath);
            var looseCompiledPath = SafePath.ResolveUnderRoot(
                addonGameRoot,
                Path.Combine(
                    HeroPrefabContentFolder.Replace('/', Path.DirectorySeparatorChar),
                    sceneId + ".vmap_c"),
                "Legacy loose hero-select compiled map");
            if (!File.Exists(looseCompiledPath))
            {
                continue;
            }

            File.Delete(looseCompiledPath);
            removed++;
        }

        return removed;
    }

    private static bool IsLooseCompiledMapResource(string resourcePath) =>
        NormalizeResourcePath(resourcePath).EndsWith(
            ".vmap_c",
            StringComparison.OrdinalIgnoreCase);

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

    private static void PublishContentFilePreserving(
        string outputRoot,
        string outputPath,
        ContentFile contentFile,
        List<string> publishedPaths,
        ref int created,
        ref int preserved)
    {
        if (contentFile.Data is not null)
        {
            PublishContentBytesPreserving(outputPath, contentFile.Data, publishedPaths, ref created, ref preserved);
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalFileName = NormalizeResourcePath(additionalFile.FileName);
            var preserveTextureResourceDirectory = additionalFile is TextureContentFile
                && additionalFileName.Contains('/');
            var additionalPath = additionalFile.KeepFullPath || preserveTextureResourceDirectory
                ? SafePath.ResolveUnderRoot(
                    outputRoot,
                    additionalFileName.Replace('/', Path.DirectorySeparatorChar),
                    "Additional prepared hero-select authoring resource")
                : Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileName(additionalFileName));

            PublishContentFilePreserving(
                outputRoot,
                additionalPath,
                additionalFile,
                publishedPaths,
                ref created,
                ref preserved);
        }

        foreach (var subFile in contentFile.SubFiles)
        {
            var data = subFile.Extract?.Invoke();
            if (data is null)
            {
                continue;
            }

            var parent = SafePath.EnsureUnderRoot(
                outputRoot,
                Path.GetDirectoryName(outputPath)!,
                "Prepared hero-select subfile parent");
            var subFilePath = SafePath.ResolveUnderRoot(
                parent,
                Path.GetFileName(subFile.FileName),
                "Prepared hero-select subfile");
            PublishContentBytesPreserving(subFilePath, data, publishedPaths, ref created, ref preserved);
        }
    }

    private static void PublishContentBytesPreserving(
        string outputPath,
        ReadOnlyMemory<byte> data,
        List<string> publishedPaths,
        ref int created,
        ref int preserved)
    {
        publishedPaths.Add(outputPath);
        if (File.Exists(outputPath))
        {
            preserved++;
            return;
        }

        if (WriteNewFileAtomically(outputPath, data.ToArray()))
        {
            created++;
        }
        else
        {
            preserved++;
        }
    }

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
