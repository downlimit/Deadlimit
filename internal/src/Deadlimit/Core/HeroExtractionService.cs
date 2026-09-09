using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed record HeroExtractionProgress(string Message);

public sealed record HeroExtractionResult(
    string MainModelResourcePath,
    string SourceVpkPath,
    string OutputFolder,
    int ExtractedFileCount,
    string? Source2ViewerVersion,
    int CsdkMaterialCopiedCount = 0,
    int CsdkAbilityFxCopiedCount = 0,
    string? CsdkEditingBackupFolder = null);

public sealed partial class HeroExtractionService
{
    private readonly DeadlimitPaths _paths;

    public HeroExtractionService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<HeroExtractionResult> ExtractAsync(
        ProjectManifest manifest,
        IProgress<HeroExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        ExtractAsync(manifest, HeroExtractionOptions.SourceOnly, progress, cancellationToken);

    public Task<HeroExtractionResult> ExtractAsync(
        ProjectManifest manifest,
        HeroExtractionOptions options,
        IProgress<HeroExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return Task.Run(() => Extract(manifest, options, progress, cancellationToken), cancellationToken);
    }

    private HeroExtractionResult Extract(
        ProjectManifest manifest,
        HeroExtractionOptions options,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        if (!options.HasAnyExtractionScope)
        {
            throw new InvalidOperationException(
                "Select at least one extraction scope: hero, abilities, or portraits/UI.");
        }
        if (options.CopyAbilityFxToCsdkForEditing && !options.ExtractAbilities)
        {
            throw new InvalidOperationException(
                "Copy ability FX to CSDK for editing requires Extract abilities.");
        }
        if (options.CopyMaterialsToCsdkForEditing
            && !options.ExtractHero
            && !options.ExtractAbilities)
        {
            throw new InvalidOperationException(
                "Copy materials to CSDK for editing requires Extract hero and/or Extract abilities.");
        }

        var hero = manifest.Hero.Trim();
        if (hero.Length == 0)
        {
            throw new InvalidOperationException("Project hero is empty.");
        }

        var retailGameRoot = Path.Combine(_paths.RetailDeadlockRoot, "game");
        if (!Directory.Exists(retailGameRoot))
        {
            throw new DirectoryNotFoundException($"Retail Deadlock game folder was not found: {retailGameRoot}");
        }

        var vrfVersion = typeof(Resource).Assembly.GetName().Version?.ToString();
        var vpkPaths = GetVpkPaths(retailGameRoot);

        progress?.Report(new HeroExtractionProgress("Locating current retail hero model..."));
        var candidate = FindMainModel(vpkPaths, hero, progress, cancellationToken);
        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"Could not find a retail .vmdl_c candidate for hero '{hero}' in the current Deadlock VPKs.");
        }

        var resourceFolder = GetResourceFolder(candidate.ResourcePath);
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var stagingFolder = Path.Combine(metadataFolder, "source-extract-staging");
        var outputFolder = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-extraction folder");
        var previousFolder = Path.Combine(metadataFolder, "0source.previous");

        Directory.CreateDirectory(metadataFolder);
        DeleteDirectoryIfExists(stagingFolder);
        Directory.CreateDirectory(stagingFolder);

        try
        {
            if (options.ExtractHero)
            {
                progress?.Report(new HeroExtractionProgress($"Decompiling {resourceFolder}..."));
                ExtractResourceFolder(
                    candidate.VpkPath,
                    resourceFolder,
                    stagingFolder,
                    true,
                    progress,
                    cancellationToken);
            }

            if (options.ExtractHero
                && (options.ExtractTextures || options.CopyMaterialsToCsdkForEditing))
            {
                ExtractHeroTextureDependencies(
                    vpkPaths,
                    candidate,
                    stagingFolder,
                    options.ExtractTextures || options.CopyMaterialsToCsdkForEditing,
                    progress,
                    cancellationToken);
            }

            if (options.ExtractAbilities)
            {
                ExtractHeroAbilityDependencies(
                    vpkPaths,
                    candidate,
                    stagingFolder,
                    options.ExtractTextures
                    || options.CopyAbilityFxToCsdkForEditing
                    || options.CopyMaterialsToCsdkForEditing,
                    progress,
                    cancellationToken);
            }

            if (options.ExtractPortraitsAndUi)
            {
                ExtractHeroUiResources(
                    vpkPaths,
                    candidate,
                    stagingFolder,
                    progress,
                    cancellationToken);
            }

            var extractedFileCount = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories).Count();
            if (extractedFileCount == 0)
            {
                throw new InvalidOperationException(
                    "ValveResourceFormat completed without an error, but no files were written to the extraction folder.");
            }

            progress?.Report(new HeroExtractionProgress("Publishing refreshed 0source..."));
            PublishRefreshedSource(
                stagingFolder,
                outputFolder,
                previousFolder,
                progress);

            manifest.SchemaVersion = Math.Max(manifest.SchemaVersion, 2);
            manifest.RetailMainModel = candidate.ResourcePath;
            manifest.RetailSourceVpk = candidate.VpkPath;
            manifest.LastSourceExtractionUtc = DateTimeOffset.UtcNow;
            manifest.Source2ViewerVersion = vrfVersion is null ? "ValveResourceFormat" : $"ValveResourceFormat {vrfVersion}";
            manifest.ExtractedSourceFileCount = extractedFileCount;
            manifest.LastSourceExtractionIncludedTextures = options.ExtractTextures;
            manifest.LastSourceExtractionIncludedAbilities = options.ExtractAbilities;

            var csdkCopy = new CsdkEditableAssetCopyService(_paths).Copy(
                manifest,
                outputFolder,
                options,
                progress,
                cancellationToken);

            ProjectStore.Save(manifest);

            var completionMessage = (options.ExtractTextures, options.ExtractAbilities) switch
            {
                (true, true) => "Hero source, dependency textures and abilities extraction complete.",
                (true, false) => "Hero source and dependency texture extraction complete.",
                (false, true) => "Hero source and abilities extraction complete.",
                _ => "Hero source extraction complete.",
            };
            progress?.Report(new HeroExtractionProgress(completionMessage));

            return new HeroExtractionResult(
                candidate.ResourcePath,
                candidate.VpkPath,
                outputFolder,
                extractedFileCount,
                manifest.Source2ViewerVersion,
                csdkCopy.MaterialCopiedCount,
                csdkCopy.AbilityFxCopiedCount,
                csdkCopy.BackupFolder);
        }
        catch
        {
            DeleteDirectoryIfExists(stagingFolder);
            throw;
        }
    }

    private static IReadOnlyList<string> GetVpkPaths(string retailGameRoot)
    {
        var primaryVpk = Path.Combine(retailGameRoot, "citadel", "pak01_dir.vpk");
        var vpks = new List<string>();

        if (File.Exists(primaryVpk))
        {
            vpks.Add(primaryVpk);
        }

        foreach (var vpk in Directory.EnumerateFiles(retailGameRoot, "*_dir.vpk", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!vpks.Contains(vpk, StringComparer.OrdinalIgnoreCase))
            {
                vpks.Add(vpk);
            }
        }

        return vpks;
    }

    private ModelCandidate? FindMainModel(
        IReadOnlyList<string> vpks,
        string hero,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        ModelCandidate? best = null;
        foreach (var vpkPath in vpks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new HeroExtractionProgress($"Scanning {Path.GetFileName(vpkPath)}..."));

            try
            {
                using var package = new Package();
                package.Read(vpkPath);
                var packageEntries = package.Entries
                    ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

                foreach (var entry in packageEntries.SelectMany(group => group.Value))
                {
                    var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                    if (!IsHeroModelPath(resourcePath))
                    {
                        continue;
                    }

                    var score = ScoreModelCandidate(resourcePath, hero);
                    if (score <= 0)
                    {
                        continue;
                    }

                    var candidate = new ModelCandidate(vpkPath, resourcePath, score);
                    if (best is null || candidate.Score > best.Score)
                    {
                        best = candidate;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Skipping unreadable VPK {Path.GetFileName(vpkPath)}: {ex.Message}"));
            }

            if (best is { Score: >= 1000 })
            {
                break;
            }
        }

        return best;
    }

    private static void ExtractHeroTextureDependencies(
        IReadOnlyList<string> vpkPaths,
        ModelCandidate candidate,
        string outputRoot,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new HeroExtractionProgress(
            includeTextures
                ? "Resolving hero model, material and texture dependencies..."
                : "Resolving hero model and material dependencies..."));

        var pendingBridges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var texturePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        CollectTextureDependencyReferences(
            ReadExternalReferences(
                new ResourceLocation(candidate.VpkPath, candidate.ResourcePath),
                cancellationToken),
            pendingBridges,
            pendingMaterials,
            texturePaths);

        var processedBridges = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            candidate.ResourcePath,
        };

        while (pendingBridges.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = pendingBridges
                .Where(path => !processedBridges.Contains(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            pendingBridges.Clear();

            if (request.Count == 0)
            {
                break;
            }

            var locations = ResolveResourceLocations(vpkPaths, request, progress, cancellationToken);
            foreach (var missing in request.Where(path => !locations.ContainsKey(path)))
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Referenced retail model/mesh bridge was not found: {missing}"));
                processedBridges.Add(missing);
            }

            foreach (var location in locations.Values)
            {
                if (!processedBridges.Add(location.ResourcePath))
                {
                    continue;
                }

                CollectTextureDependencyReferences(
                    ReadExternalReferences(location, cancellationToken),
                    pendingBridges,
                    pendingMaterials,
                    texturePaths);
            }
        }

        var resolvedMaterials = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);
        var processedMaterials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (pendingMaterials.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = pendingMaterials
                .Where(path => !processedMaterials.Contains(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            pendingMaterials.Clear();

            if (request.Count == 0)
            {
                break;
            }

            var locations = ResolveResourceLocations(vpkPaths, request, progress, cancellationToken);
            foreach (var missing in request.Where(path => !locations.ContainsKey(path)))
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Referenced retail material was not found: {missing}"));
                processedMaterials.Add(missing);
            }

            foreach (var location in locations.Values)
            {
                if (!processedMaterials.Add(location.ResourcePath))
                {
                    continue;
                }

                resolvedMaterials[location.ResourcePath] = location;
                foreach (var reference in ReadExternalReferences(location, cancellationToken))
                {
                    if (IsTextureReference(reference))
                    {
                        texturePaths.Add(ToCompiledResourcePath(reference));
                    }
                    else if (IsMaterialReference(reference))
                    {
                        var materialPath = ToCompiledResourcePath(reference);
                        if (!processedMaterials.Contains(materialPath))
                        {
                            pendingMaterials.Add(materialPath);
                        }
                    }
                }
            }
        }

        IReadOnlyDictionary<string, ResourceLocation> textureLocations =
            new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);
        if (includeTextures)
        {
            textureLocations = ResolveResourceLocations(
                vpkPaths,
                texturePaths,
                progress,
                cancellationToken);

            var missingTextures = texturePaths
                .Where(path => !textureLocations.ContainsKey(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var missing in missingTextures)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Referenced retail texture was not found: {missing}"));
            }
        }

        var dependencies = resolvedMaterials.Values
            .Concat(textureLocations.Values)
            .GroupBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new HeroExtractionProgress(
            $"Hero dependencies: {processedBridges.Count - 1} model/mesh bridge(s), " +
            $"{resolvedMaterials.Count} material(s), {textureLocations.Count} texture resource(s)."));

        if (dependencies.Length == 0)
        {
            progress?.Report(new HeroExtractionProgress(
                "No external hero material or texture dependencies were found."));
            return;
        }

        ExtractResourceLocations(
            dependencies,
            outputRoot,
            progress,
            cancellationToken);
    }

    private static void CollectTextureDependencyReferences(
        IEnumerable<string> references,
        HashSet<string> pendingBridges,
        HashSet<string> pendingMaterials,
        HashSet<string> texturePaths)
    {
        foreach (var reference in references)
        {
            if (IsTextureReference(reference))
            {
                texturePaths.Add(ToCompiledResourcePath(reference));
            }
            else if (IsMaterialReference(reference))
            {
                pendingMaterials.Add(ToCompiledResourcePath(reference));
            }
            else if (IsTextureDependencyBridgeReference(reference))
            {
                pendingBridges.Add(ToCompiledResourcePath(reference));
            }
        }
    }

    private static IReadOnlyDictionary<string, ResourceLocation> ResolveResourceLocations(
        IReadOnlyList<string> vpkPaths,
        IReadOnlySet<string> requestedResourcePaths,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var remaining = requestedResourcePaths
            .Select(NormalizeResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resolved = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);

        if (remaining.Count == 0)
        {
            return resolved;
        }

        foreach (var vpkPath in vpkPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (remaining.Count == 0)
            {
                break;
            }

            try
            {
                using var package = new Package();
                package.Read(vpkPath);
                var packageEntries = package.Entries
                    ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

                foreach (var entry in packageEntries.SelectMany(group => group.Value))
                {
                    var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                    if (!remaining.Remove(resourcePath))
                    {
                        continue;
                    }

                    resolved[resourcePath] = new ResourceLocation(vpkPath, resourcePath);
                    if (remaining.Count == 0)
                    {
                        break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Skipping unreadable VPK {Path.GetFileName(vpkPath)} while resolving dependencies: {ex.Message}"));
            }
        }

        return resolved;
    }

    private static IReadOnlyList<string> ReadExternalReferences(
        ResourceLocation location,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var package = new Package();
        package.Read(location.VpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {location.VpkPath}");

        var entry = packageEntries
            .SelectMany(group => group.Value)
            .FirstOrDefault(candidate => string.Equals(
                NormalizeResourcePath(candidate.GetFullPath()),
                location.ResourcePath,
                StringComparison.OrdinalIgnoreCase));

        if (entry is null)
        {
            throw new InvalidOperationException(
                $"Referenced retail resource was not found in its indexed VPK: {location.ResourcePath}");
        }

        package.ReadEntry(entry, out byte[] rawData);
        using var stream = new MemoryStream(rawData, writable: false);
        using var resource = new Resource { FileName = location.ResourcePath };
        resource.Read(stream);

        return resource.ExternalReferences?.ResourceRefInfoList
            .Select(reference => NormalizeResourcePath(reference.Name))
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray()
            ?? Array.Empty<string>();
    }

    private static void ExtractResourceLocations(
        IReadOnlyList<ResourceLocation> locations,
        string outputRoot,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var groups = locations
            .GroupBy(location => location.VpkPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var completed = 0;
        var total = locations.Count;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var package = new Package();
            package.Read(group.Key);
            var packageEntries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {group.Key}");
            using var fileLoader = new GameFileLoader(package, package.FileName);

            var requested = group
                .Select(location => location.ResourcePath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var entries = packageEntries
                .SelectMany(packageGroup => packageGroup.Value)
                .Select(entry => (Entry: entry, Path: NormalizeResourcePath(entry.GetFullPath())))
                .Where(item => requested.Contains(item.Path))
                .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var (entry, filePath) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                completed++;
                progress?.Report(new HeroExtractionProgress(
                    $"Decompiling hero dependency {completed}/{total}: {Path.GetFileName(filePath)}"));

                try
                {
                    package.ReadEntry(entry, out byte[] rawData);

                    if (!entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                    {
                        WriteFile(
                            SafePath.ResolveUnderRoot(outputRoot, ToWindowsPath(filePath), "Extracted VPK dependency"),
                            rawData);
                        continue;
                    }

                    using var stream = new MemoryStream(rawData, writable: false);
                    using var resource = new Resource { FileName = filePath };
                    resource.Read(stream);

                    var outputExtension = FileExtract.GetExtension(resource) ?? entry.TypeName[..^2];
                    var decompiledPath = Path.ChangeExtension(filePath, outputExtension);
                    var outputPath = SafePath.ResolveUnderRoot(
                        outputRoot,
                        ToWindowsPath(decompiledPath),
                        "Decompiled VPK dependency");

                    using var contentFile = resource.ResourceType == ResourceType.Texture
                        ? new TextureExtract(resource).ToContentFile()
                        : FileExtract.Extract(resource, fileLoader, null);

                    DumpContentFile(outputRoot, outputPath, contentFile);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"ValveResourceFormat failed while decompiling dependency '{filePath}': {ex.Message}",
                        ex);
                }
            }
        }
    }

    private static void ExtractResourceFolder(
        string vpkPath,
        string resourceFolder,
        string outputRoot,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        using var fileLoader = new GameFileLoader(package, package.FileName);

        var entries = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, Path: NormalizeResourcePath(entry.GetFullPath())))
            .Where(item => item.Path.StartsWith(resourceFolder, StringComparison.OrdinalIgnoreCase))
            .Where(item => includeTextures || !IsTextureReference(item.Path))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"No VPK entries matched '{resourceFolder}'.");
        }

        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (entry, filePath) = entries[index];

            if (index == 0 || (index + 1) % 25 == 0 || index == entries.Length - 1)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Decompiling {index + 1}/{entries.Length}: {Path.GetFileName(filePath)}"));
            }

            try
            {
                package.ReadEntry(entry, out byte[] rawData);

                if (!entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    WriteFile(
                        SafePath.ResolveUnderRoot(outputRoot, ToWindowsPath(filePath), "Extracted VPK resource"),
                        rawData);
                    continue;
                }

                using var stream = new MemoryStream(rawData, writable: false);
                using var resource = new Resource { FileName = filePath };
                resource.Read(stream);

                var outputExtension = FileExtract.GetExtension(resource) ?? entry.TypeName[..^2];
                var decompiledPath = Path.ChangeExtension(filePath, outputExtension);
                var outputPath = SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(decompiledPath),
                    "Decompiled VPK resource");

                using var contentFile = resource.ResourceType == ResourceType.Texture
                    ? new TextureExtract(resource).ToContentFile()
                    : FileExtract.Extract(resource, fileLoader, null);

                DumpContentFile(outputRoot, outputPath, contentFile);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ValveResourceFormat failed while decompiling '{filePath}': {ex.Message}",
                    ex);
            }
        }
    }

    private static void DumpContentFile(string outputRoot, string path, ContentFile contentFile)
    {
        if (contentFile.Data is not null)
        {
            WriteFile(path, contentFile.Data);
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalFileName = NormalizeResourcePath(additionalFile.FileName);
            var preserveTextureResourceDirectory = additionalFile is TextureContentFile
                && additionalFileName.Contains('/');
            var additionalPath = additionalFile.KeepFullPath || preserveTextureResourceDirectory
                ? SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(additionalFileName),
                    "Additional extracted resource")
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFileName));

            DumpContentFile(outputRoot, additionalPath, additionalFile);
        }

        foreach (var subFile in contentFile.SubFiles)
        {
            var data = subFile.Extract?.Invoke();
            if (data is not null)
            {
                var parent = SafePath.EnsureUnderRoot(outputRoot, Path.GetDirectoryName(path)!, "Extracted subfile parent");
                WriteFile(
                    SafePath.ResolveUnderRoot(parent, Path.GetFileName(subFile.FileName), "Extracted subfile"),
                    data);
            }
        }
    }

    private static void WriteFile(string path, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static bool IsHeroModelPath(string path) =>
        path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase));

    private static bool IsTextureDependencyBridgeReference(string path)
    {
        var normalized = NormalizeResourcePath(path);
        return normalized.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vmesh", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vmesh_c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMaterialReference(string path)
    {
        var normalized = NormalizeResourcePath(path);
        return normalized.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextureReference(string path)
    {
        var normalized = NormalizeResourcePath(path);
        return normalized.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase);
    }

    private static string ToCompiledResourcePath(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        return normalized.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + GameFileLoader.CompiledFileSuffix;
    }

    private static int ScoreModelCandidate(string resourcePath, string hero)
    {
        var normalizedHero = NormalizeToken(hero);
        if (normalizedHero.Length == 0)
        {
            return 0;
        }

        var path = NormalizeResourcePath(resourcePath);
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        var normalizedFileName = NormalizeToken(fileName);
        var normalizedPath = NormalizeToken(path);

        var score = 0;
        if (string.Equals(normalizedFileName, normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }
        else if (normalizedFileName.Contains(normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 350;
        }

        if (normalizedPath.Contains(normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else if (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (path.Contains("/lod", StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        return score;
    }

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').Trim().Trim('"').TrimStart('/');

    private static string GetResourceFolder(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
    }

    private static string ToWindowsPath(string resourcePath) =>
        resourcePath.Replace('/', Path.DirectorySeparatorChar);

    private static void PublishRefreshedSource(
        string stagingFolder,
        string outputFolder,
        string previousFolder,
        IProgress<HeroExtractionProgress>? progress)
    {
        DeleteDirectoryIfExists(previousFolder);

        if (!Directory.Exists(outputFolder))
        {
            Directory.Move(stagingFolder, outputFolder);
            return;
        }

        try
        {
            Directory.Move(outputFolder, previousFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            progress?.Report(new HeroExtractionProgress(
                "The existing 0source folder is busy; refreshing its contents in place..."));
            PublishRefreshedSourceInPlace(stagingFolder, outputFolder, previousFolder);
            return;
        }

        try
        {
            Directory.Move(stagingFolder, outputFolder);
        }
        catch
        {
            if (!Directory.Exists(outputFolder) && Directory.Exists(previousFolder))
            {
                Directory.Move(previousFolder, outputFolder);
            }

            throw;
        }
    }

    private static void PublishRefreshedSourceInPlace(
        string stagingFolder,
        string outputFolder,
        string previousFolder)
    {
        CopyDirectoryFiles(outputFolder, previousFolder, "back up current 0source");

        var stagedRelativeFiles = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stagingFolder, path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var existingFile in Directory.EnumerateFiles(outputFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(outputFolder, existingFile);
            if (stagedRelativeFiles.Contains(relative))
            {
                continue;
            }

            try
            {
                File.Delete(existingFile);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not remove stale extracted source file because it is in use or access is denied: {existingFile}",
                    ex);
            }
        }

        foreach (var stagedFile in Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(stagingFolder, stagedFile);
            var destination = SafePath.ResolveUnderRoot(
                outputFolder,
                relative,
                "Refreshed extracted source file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(stagedFile, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not refresh extracted source file because it is in use or access is denied: {destination}",
                    ex);
            }
        }

        foreach (var directory in Directory.EnumerateDirectories(outputFolder, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A process may hold an otherwise empty folder open. Empty stale folders are harmless.
            }
        }

        DeleteDirectoryIfExists(stagingFolder);
    }

    private static void CopyDirectoryFiles(string sourceFolder, string destinationFolder, string operation)
    {
        Directory.CreateDirectory(destinationFolder);
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, sourceFile);
            var destination = SafePath.ResolveUnderRoot(
                destinationFolder,
                relative,
                "Source extraction backup file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

            try
            {
                File.Copy(sourceFile, destination, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Could not {operation}; a source file is in use or access is denied: {sourceFile}",
                    ex);
            }
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record ModelCandidate(string VpkPath, string ResourcePath, int Score);

    private sealed record ResourceLocation(string VpkPath, string ResourcePath);
}
