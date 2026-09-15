using System.Text;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed partial class HeroExtractionService
{
    private const string HeroesVdataResourcePath = "scripts/heroes.vdata_c";
    private const string AbilitiesVdataResourcePath = "scripts/abilities.vdata_c";

    private static readonly string[] AbilityVisualDependencyExtensions =
    [
        ".vpcf",
        ".vmdl",
        ".vmesh",
        ".vmat",
        ".vsnap",
        ".vphys",
        ".vanim",
        ".vagrp",
        ".vseq",
    ];

    private static void ExtractHeroAbilityDependencies(
        IReadOnlyList<string> vpkPaths,
        ModelCandidate candidate,
        string outputRoot,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var locations = ResolveHeroAbilityDependencies(
            vpkPaths,
            candidate,
            includeTextures,
            progress,
            cancellationToken);

        if (locations.Count > 0)
        {
            ExtractResourceLocations(locations, outputRoot, progress, cancellationToken);
        }
    }

    private static void ExtractHeroAbilityGltfResources(
        IReadOnlyList<string> vpkPaths,
        ModelCandidate candidate,
        string outputRoot,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var locations = ResolveHeroAbilityDependencies(
            vpkPaths,
            candidate,
            includeTextures: false,
            progress,
            cancellationToken);

        if (locations.Count == 0)
        {
            return;
        }

        ExtractGltfResourceLocations(
            vpkPaths,
            locations,
            outputRoot,
            includeTextures,
            progress,
            cancellationToken);
    }

    private static IReadOnlyList<ResourceLocation> ResolveHeroAbilityDependencies(
        IReadOnlyList<string> vpkPaths,
        ModelCandidate candidate,
        bool includeTextures,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new HeroExtractionProgress("Resolving hero ability resources..."));

        var vdataPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HeroesVdataResourcePath,
            AbilitiesVdataResourcePath,
        };
        var vdataLocations = ResolveResourceLocations(vpkPaths, vdataPaths, progress, cancellationToken);

        if (!vdataLocations.TryGetValue(HeroesVdataResourcePath, out var heroesLocation))
        {
            throw new InvalidOperationException(
                $"Current Deadlock VPKs do not contain {HeroesVdataResourcePath}.");
        }
        if (!vdataLocations.TryGetValue(AbilitiesVdataResourcePath, out var abilitiesLocation))
        {
            throw new InvalidOperationException(
                $"Current Deadlock VPKs do not contain {AbilitiesVdataResourcePath}.");
        }

        var heroesText = ReadDecompiledResourceText(heroesLocation, cancellationToken);
        var abilitiesText = ReadDecompiledResourceText(abilitiesLocation, cancellationToken);
        var selection = HeroAbilityVdataParser.Resolve(
            heroesText,
            abilitiesText,
            candidate.ResourcePath);

        if (selection.AbilityNames.Count == 0)
        {
            progress?.Report(new HeroExtractionProgress(
                "No bound abilities were found for the selected hero."));
            return [];
        }

        progress?.Report(new HeroExtractionProgress(
            $"Hero abilities: {selection.AbilityNames.Count}; visual roots: {selection.VisualResourcePaths.Count}."));

        var requested = selection.VisualResourcePaths
            .Where(path => IsAbilityVisualDependency(path, includeTextures))
            .Select(ToCompiledResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requested.Count == 0)
        {
            progress?.Report(new HeroExtractionProgress(
                "No enabled visual resource references were found in the selected hero abilities."));
            return [];
        }

        var resolved = ResolveResourceLocations(vpkPaths, requested, progress, cancellationToken);
        foreach (var missing in requested.Where(path => !resolved.ContainsKey(path)))
        {
            progress?.Report(new HeroExtractionProgress(
                $"Referenced ability visual resource was not found: {missing}"));
        }

        var pending = new Queue<ResourceLocation>(resolved.Values);
        var collected = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);

        void DrainPendingDependencies()
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = pending.Dequeue();
                if (!collected.TryAdd(location.ResourcePath, location))
                {
                    continue;
                }

                var dependencyPaths = ReadExternalReferences(location, cancellationToken)
                    .Where(path => IsAbilityVisualDependency(path, includeTextures))
                    .Select(ToCompiledResourcePath)
                    .Where(path => !collected.ContainsKey(path))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var materialPath in ReadModelMaterialGroupReferences(location, cancellationToken))
                {
                    var compiledMaterialPath = ToCompiledResourcePath(materialPath);
                    if (!collected.ContainsKey(compiledMaterialPath))
                    {
                        dependencyPaths.Add(compiledMaterialPath);
                    }
                }

                if (dependencyPaths.Count == 0)
                {
                    continue;
                }

                var dependencyLocations = ResolveResourceLocations(
                    vpkPaths,
                    dependencyPaths,
                    progress,
                    cancellationToken);
                foreach (var missing in dependencyPaths.Where(path => !dependencyLocations.ContainsKey(path)))
                {
                    progress?.Report(new HeroExtractionProgress(
                        $"Referenced ability dependency was not found: {missing}"));
                }

                foreach (var dependencyLocation in dependencyLocations.Values)
                {
                    if (!collected.ContainsKey(dependencyLocation.ResourcePath))
                    {
                        pending.Enqueue(dependencyLocation);
                    }
                }
            }
        }

        DrainPendingDependencies();

        var abilityNamespaces = ResolveAbilityNamespaces(selection.VisualResourcePaths);
        var namespaceResources = ResolveResourcesMatching(
            vpkPaths,
            path => IsAbilityNamespaceOwnedResource(path, abilityNamespaces),
            cancellationToken);
        var namespaceResourceCount = 0;
        foreach (var namespaceResource in namespaceResources.Values)
        {
            if (collected.ContainsKey(namespaceResource.ResourcePath))
            {
                continue;
            }

            if (IsParticleSystemReference(namespaceResource.ResourcePath))
            {
                collected.Add(namespaceResource.ResourcePath, namespaceResource);
            }
            else
            {
                pending.Enqueue(namespaceResource);
            }
            namespaceResourceCount++;
        }
        if (pending.Count > 0)
        {
            progress?.Report(new HeroExtractionProgress(
                $"Ability namespaces: added {namespaceResourceCount} hero-owned material/particle source(s)."));
            DrainPendingDependencies();
        }

        // Retail ability folders can contain alternate materials and particle graphs
        // selected by runtime state. Those siblings have no edge from the static graph
        // and would otherwise remain visible only through the read-only retail search
        // path in CSDK.
        var abilityMaterialDirectories = collected.Keys
            .Where(IsMaterialReference)
            .Select(GetResourceFolder)
            .Where(IsAbilityResourceDirectory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siblingMaterials = ResolveResourcesInDirectories(
            vpkPaths,
            abilityMaterialDirectories,
            IsMaterialReference,
            cancellationToken);
        var siblingCount = 0;
        foreach (var siblingMaterial in siblingMaterials.Values)
        {
            if (!collected.ContainsKey(siblingMaterial.ResourcePath))
            {
                pending.Enqueue(siblingMaterial);
                siblingCount++;
            }
        }

        var abilityParticleDirectories = collected.Keys
            .Where(IsParticleSystemReference)
            .Select(GetResourceFolder)
            .Where(IsAbilityResourceDirectory)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var siblingParticles = ResolveResourcesInDirectories(
            vpkPaths,
            abilityParticleDirectories,
            IsParticleSystemReference,
            cancellationToken);
        foreach (var siblingParticle in siblingParticles.Values)
        {
            if (!collected.ContainsKey(siblingParticle.ResourcePath))
            {
                collected.Add(siblingParticle.ResourcePath, siblingParticle);
                siblingCount++;
            }
        }

        if (pending.Count > 0)
        {
            progress?.Report(new HeroExtractionProgress(
                $"Ability resource folders: added {siblingCount} sibling material/particle source(s)."));
            DrainPendingDependencies();
        }

        var locations = collected.Values
            .OrderBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new HeroExtractionProgress(
            $"Ability visual dependencies: {locations.Length} resource(s)."));
        return locations;
    }

    private static IReadOnlyDictionary<string, ResourceLocation> ResolveResourcesInDirectories(
        IReadOnlyList<string> vpkPaths,
        IReadOnlySet<string> resourceDirectories,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);
        if (resourceDirectories.Count == 0)
        {
            return resolved;
        }

        foreach (var vpkPath in vpkPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var package = new Package();
            package.Read(vpkPath);
            var packageEntries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

            foreach (var entry in packageEntries.SelectMany(group => group.Value))
            {
                var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                if (!predicate(resourcePath)
                    || !resourceDirectories.Contains(GetResourceFolder(resourcePath)))
                {
                    continue;
                }

                resolved.TryAdd(resourcePath, new ResourceLocation(vpkPath, resourcePath));
            }
        }

        return resolved;
    }

    private static IReadOnlyDictionary<string, ResourceLocation> ResolveResourcesMatching(
        IReadOnlyList<string> vpkPaths,
        Func<string, bool> predicate,
        CancellationToken cancellationToken)
    {
        var resolved = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var vpkPath in vpkPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var package = new Package();
            package.Read(vpkPath);
            var packageEntries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

            foreach (var entry in packageEntries.SelectMany(group => group.Value))
            {
                var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                if (predicate(resourcePath))
                {
                    resolved.TryAdd(resourcePath, new ResourceLocation(vpkPath, resourcePath));
                }
            }
        }

        return resolved;
    }

    private static IReadOnlySet<string> ResolveAbilityNamespaces(IEnumerable<string> visualResourcePaths)
    {
        var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var visualResourcePath in visualResourcePaths)
        {
            var segments = NormalizeResourcePath(visualResourcePath)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index + 1 < segments.Length; index++)
            {
                if (string.Equals(segments[index], "abilities", StringComparison.OrdinalIgnoreCase))
                {
                    namespaces.Add(segments[index + 1]);
                }
            }
        }
        return namespaces;
    }

    private static bool IsAbilityNamespaceOwnedResource(
        string resourcePath,
        IReadOnlySet<string> abilityNamespaces)
    {
        if (abilityNamespaces.Count == 0
            || (!IsMaterialReference(resourcePath) && !IsParticleSystemReference(resourcePath)))
        {
            return false;
        }

        var normalized = NormalizeResourcePath(resourcePath);
        var sourcePath = normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^2]
            : normalized;
        var stem = Path.GetFileNameWithoutExtension(sourcePath);
        var pathWithBoundaries = "/" + sourcePath.Trim('/') + "/";

        return abilityNamespaces.Any(abilityNamespace =>
            stem.StartsWith(abilityNamespace + "_", StringComparison.OrdinalIgnoreCase)
            || pathWithBoundaries.Contains(
                $"/abilities/{abilityNamespace}/",
                StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAbilityResourceDirectory(string resourceDirectory)
    {
        var normalized = NormalizeResourcePath(resourceDirectory).TrimEnd('/');
        return normalized.Contains("/abilities/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsParticleSystemReference(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        return normalized.EndsWith(".vpcf", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".vpcf_c", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> ReadModelMaterialGroupReferences(
        ResourceLocation location,
        CancellationToken cancellationToken)
    {
        if (!location.ResourcePath.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

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
                $"Indexed retail model resource was not found: {location.ResourcePath}");
        }

        package.ReadEntry(entry, out byte[] rawData);
        using var stream = new MemoryStream(rawData, writable: false);
        using var resource = new Resource { FileName = location.ResourcePath };
        resource.Read(stream);
        if (resource.DataBlock is not Model model)
        {
            return [];
        }

        try
        {
            return model.GetMaterialGroups()
                .SelectMany(group => group.Materials ?? [])
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(NormalizeResourcePath)
                .Where(path => path.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".vmat_c", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (NullReferenceException)
        {
            // VRF 20.0 exposes material groups through a deferred enumerable that assumes
            // m_materialGroups exists. Models without that field simply have no extra
            // material-group dependencies to add.
            return [];
        }
    }

    private static string ReadDecompiledResourceText(
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
                $"Indexed retail resource was not found: {location.ResourcePath}");
        }

        package.ReadEntry(entry, out byte[] rawData);
        using var stream = new MemoryStream(rawData, writable: false);
        using var resource = new Resource { FileName = location.ResourcePath };
        resource.Read(stream);
        using var fileLoader = new GameFileLoader(package, package.FileName);
        using var content = FileExtract.Extract(resource, fileLoader, null);
        if (content.Data is null)
        {
            throw new InvalidDataException(
                $"ValveResourceFormat did not return decompiled text for {location.ResourcePath}.");
        }

        return Encoding.UTF8.GetString(content.Data);
    }

    private static bool IsAbilityVisualDependency(string resourcePath, bool includeTextures)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        if (IsTextureReference(normalized))
        {
            return includeTextures;
        }

        if (normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }

        return AbilityVisualDependencyExtensions.Any(extension =>
            normalized.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }
}
