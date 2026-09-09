namespace Deadlimit.Core;

internal sealed record ArtistDmxTargetMapping(
    string ArtistDmxPath,
    string TargetResourcePath);

internal static class ArtistDmxTargetResolver
{
    public static IReadOnlyList<ArtistDmxTargetMapping> Resolve(
        string preparedVmdlPath,
        string hero,
        IReadOnlyList<string> artistDmxFiles,
        string? extractedSourceRoot = null)
    {
        var renderMeshes = RetailVmdlInheritance.ReadRenderMeshes(preparedVmdlPath);
        if (renderMeshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The prepared VMDL has no RenderMeshFile entries, so ONLINE PREPARATION cannot map artist DMX files safely.");
        }

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mappings = new List<ArtistDmxTargetMapping>();

        foreach (var artistDmx in artistDmxFiles)
        {
            var artistFileName = Path.GetFileName(artistDmx);
            var exactMatches = renderMeshes
                .Where(entry => string.Equals(
                    Path.GetFileName(entry.Filename),
                    artistFileName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            string targetResourcePath;
            if (exactMatches.Length == 1)
            {
                targetResourcePath = NormalizeResourcePath(exactMatches[0].Filename);
            }
            else
            {
                var sourceTarget = ExtractedSourceAssetResolver.ResolveDmxTarget(
                    artistDmx,
                    extractedSourceRoot);
                if (sourceTarget is not null)
                {
                    targetResourcePath = NormalizeResourcePath(sourceTarget.ResourcePath);
                }
                else if (artistDmxFiles.Count == 1)
                {
                    var primary = ChoosePrimaryRenderMesh(renderMeshes, hero, artistFileName)
                        ?? throw new InvalidOperationException(
                            $"Could not identify a unique primary prepared render mesh for '{artistFileName}'. " +
                            "Use an original extracted source filename or rename the artist DMX to match the retail render-mesh source filename.");
                    targetResourcePath = NormalizeResourcePath(primary.Filename);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Artist DMX '{artistFileName}' does not uniquely match the main prepared VMDL and has no unique extracted-source target. " +
                        "Deadlimit will not guess which retail resource to replace.");
                }
            }

            if (!usedTargets.Add(targetResourcePath))
            {
                throw new InvalidOperationException(
                    $"More than one artist DMX resolved to the same prepared render mesh: {targetResourcePath}");
            }

            mappings.Add(new ArtistDmxTargetMapping(
                Path.GetFullPath(artistDmx),
                targetResourcePath));
        }

        return mappings;
    }

    private static RetailRenderMeshEntry? ChoosePrimaryRenderMesh(
        IReadOnlyList<RetailRenderMeshEntry> entries,
        string hero,
        string artistFileName)
    {
        var heroToken = NormalizeToken(hero);
        var artistToken = NormalizeToken(Path.GetFileNameWithoutExtension(artistFileName));

        var scored = entries
            .Select(entry =>
            {
                var nameToken = NormalizeToken(entry.Name);
                var fileToken = NormalizeToken(Path.GetFileNameWithoutExtension(entry.Filename));
                var searchable = $"{nameToken} {fileToken}";

                var score = 0;
                if (fileToken == artistToken)
                {
                    score += 1000;
                }
                if (heroToken.Length > 0 && nameToken == heroToken)
                {
                    score += 700;
                }
                if (heroToken.Length > 0 && fileToken.Contains(heroToken, StringComparison.Ordinal))
                {
                    score += 250;
                }
                if (searchable.Contains("lod", StringComparison.Ordinal))
                {
                    score -= 600;
                }
                if (searchable.Contains("gun", StringComparison.Ordinal)
                    || searchable.Contains("weapon", StringComparison.Ordinal))
                {
                    score -= 500;
                }

                return (Entry: entry, Score: score);
            })
            .OrderByDescending(item => item.Score)
            .ToArray();

        if (scored.Length == 0 || scored[0].Score <= 0)
        {
            return null;
        }

        if (scored.Length > 1 && scored[0].Score == scored[1].Score)
        {
            return null;
        }

        return scored[0].Entry;
    }

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
