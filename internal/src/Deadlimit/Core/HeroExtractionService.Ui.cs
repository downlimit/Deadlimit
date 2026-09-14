namespace Deadlimit.Core;

public sealed partial class HeroExtractionService
{
    private static void ExtractHeroUiResources(
        IReadOnlyList<string> vpkPaths,
        ModelCandidate candidate,
        string outputRoot,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new HeroExtractionProgress("Resolving hero portraits and UI images..."));

        var requestedVdata = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            HeroesVdataResourcePath,
        };
        var vdataLocations = ResolveResourceLocations(vpkPaths, requestedVdata, progress, cancellationToken);
        if (!vdataLocations.TryGetValue(HeroesVdataResourcePath, out var heroesLocation))
        {
            throw new InvalidOperationException($"Current Deadlock VPKs do not contain {HeroesVdataResourcePath}.");
        }

        var heroesText = ReadDecompiledResourceText(heroesLocation, cancellationToken);
        var selection = HeroAbilityVdataParser.ResolveUiImages(heroesText, candidate.ResourcePath);
        var requestedImages = selection.ImageResourcePaths
            .Select(ToCompiledResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedImages.Count == 0)
        {
            progress?.Report(new HeroExtractionProgress("No portrait or UI image resources were found for the selected hero."));
            return;
        }

        var resolved = ResolveResourceLocations(vpkPaths, requestedImages, progress, cancellationToken);
        foreach (var missing in requestedImages.Where(path => !resolved.ContainsKey(path)))
        {
            progress?.Report(new HeroExtractionProgress($"Referenced hero UI image was not found: {missing}"));
        }

        var locations = resolved.Values
            .OrderBy(location => location.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        progress?.Report(new HeroExtractionProgress($"Hero portraits and UI: {locations.Length} image resource(s)."));
        if (locations.Length > 0)
        {
            ExtractResourceLocations(locations, outputRoot, progress, cancellationToken);
        }
    }
}
