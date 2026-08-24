namespace Deadlimit.Core;

internal sealed record ArtistDmxTargetMapping(
    string ArtistDmxPath,
    string TargetResourcePath);

internal static class ArtistDmxTargetResolver
{
    public static IReadOnlyList<ArtistDmxTargetMapping> Resolve(
        string preparedVmdlPath,
        string hero,
        IReadOnlyList<string> artistDmxFiles)
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

            RetailRenderMeshEntry target;
            if (exactMatches.Length == 1)
            {
                target = exactMatches[0];
            }
            else if (artistDmxFiles.Count == 1)
            {
                target = ChoosePrimaryRenderMesh(renderMeshes, hero, artistFileName)
                    ?? throw new InvalidOperationException(
                        $"Could not identify a unique primary prepared render mesh for '{artistFileName}'. " +
                        "Rename the artist DMX to match the retail render-mesh source filename.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Artist DMX '{artistFileName}' does not uniquely match a prepared RenderMeshFile. " +
                    "For multi-DMX projects, keep the original retail DMX filenames.");
            }

            if (!usedTargets.Add(target.Filename))
            {
                throw new InvalidOperationException(
                    $"More than one artist DMX resolved to the same prepared render mesh: {target.Filename}");
            }

            mappings.Add(new ArtistDmxTargetMapping(
                Path.GetFullPath(artistDmx),
                NormalizeResourcePath(target.Filename)));
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
