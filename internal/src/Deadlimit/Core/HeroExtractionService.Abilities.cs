using System.Text;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

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
            return;
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
            return;
        }

        var resolved = ResolveResourceLocations(vpkPaths, requested, progress, cancellationToken);
        foreach (var missing in requested.Where(path => !resolved.ContainsKey(path)))
        {
            progress?.Report(new HeroExtractionProgress(
                $"Referenced ability visual resource was not found: {missing}"));
        }

        var pending = new Queue<ResourceLocation>(resolved.Values);
        var collected = new Dictionary<string, ResourceLocation>(StringComparer.OrdinalIgnoreCase);

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

        var locations = collected.Values
            .OrderBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new HeroExtractionProgress(
            $"Ability visual dependencies: {locations.Length} resource(s)."));
        ExtractResourceLocations(locations, outputRoot, progress, cancellationToken);
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
