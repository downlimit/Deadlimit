using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed record VpkImportIdentity(
    string ProjectName,
    string SuggestedFolderName,
    string? HeroLookupName,
    string? HeroDisplayName,
    IReadOnlyList<string> DetectedHeroLookupNames,
    IReadOnlyList<string> PrimaryModelResources,
    bool UsedFallbackName);

public static class VpkImportIdentityService
{
    private const int ExactRetailModelScore = 5000;
    private const int ConfidentHeroScore = 900;
    private const int RequiredHeroLead = 250;

    public static VpkImportIdentity Infer(
        VpkImportCandidate candidate,
        IReadOnlyList<HeroCatalogEntry>? heroCatalog = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var models = ReadCompiledModelPaths(candidate.SourceVpkPath);
        var heroes = (heroCatalog ?? HeroCatalogService.LoadCached())
            .Where(hero => !string.IsNullOrWhiteSpace(hero.LookupName)
                && !string.IsNullOrWhiteSpace(hero.ModelResourcePath))
            .GroupBy(hero => hero.LookupName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var scored = new List<ScoredHeroModel>();
        foreach (var hero in heroes)
        {
            foreach (var model in models)
            {
                var score = ScoreHeroModel(model, hero);
                if (score > 0)
                {
                    scored.Add(new ScoredHeroModel(hero, model, score));
                }
            }
        }

        var exactHeroes = scored
            .Where(item => item.Score >= ExactRetailModelScore)
            .GroupBy(item => item.Hero.LookupName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .OrderBy(item => item.Hero.LookupName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ScoredHeroModel? selectedHero = null;
        if (exactHeroes.Length == 1)
        {
            selectedHero = exactHeroes[0];
        }
        else if (exactHeroes.Length == 0)
        {
            var bestByHero = scored
                .GroupBy(item => item.Hero.LookupName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderByDescending(item => item.Score)
                    .ThenBy(item => item.ModelPath, StringComparer.OrdinalIgnoreCase)
                    .First())
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.Hero.LookupName, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (bestByHero.Length > 0
                && bestByHero[0].Score >= ConfidentHeroScore
                && (bestByHero.Length == 1
                    || bestByHero[0].Score - bestByHero[1].Score >= RequiredHeroLead))
            {
                selectedHero = bestByHero[0];
            }
        }

        var detectedHeroes = exactHeroes.Length > 0
            ? exactHeroes.Select(item => item.Hero.LookupName).ToArray()
            : scored
                .Where(item => item.Score >= ConfidentHeroScore)
                .Select(item => item.Hero.LookupName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (selectedHero is not null)
        {
            var projectName = SanitizeProjectName(selectedHero.Hero.DisplayName);
            return new VpkImportIdentity(
                projectName,
                projectName,
                selectedHero.Hero.LookupName,
                selectedHero.Hero.DisplayName,
                detectedHeroes,
                [selectedHero.ModelPath],
                UsedFallbackName: false);
        }

        var dominantModel = TryFindDominantModel(models);
        if (dominantModel is not null)
        {
            var modelName = SanitizeProjectName(GetModelIdentityName(dominantModel));
            return new VpkImportIdentity(
                modelName,
                modelName,
                HeroLookupName: null,
                HeroDisplayName: null,
                detectedHeroes,
                [dominantModel],
                UsedFallbackName: false);
        }

        var fallback = SanitizeProjectName(GetVpkIdentityName(candidate.SourceVpkFileName));
        return new VpkImportIdentity(
            fallback,
            fallback,
            HeroLookupName: null,
            HeroDisplayName: null,
            detectedHeroes,
            Array.Empty<string>(),
            UsedFallbackName: true);
    }

    private static IReadOnlyList<string> ReadCompiledModelPaths(string vpkPath)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var entries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

        return entries
            .SelectMany(group => group.Value)
            .Select(entry => NormalizeResourcePath(entry.GetFullPath()))
            .Where(path => path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int ScoreHeroModel(string modelPath, HeroCatalogEntry hero)
    {
        var path = NormalizeResourcePath(modelPath);
        var retailPath = ToCompiledModelPath(hero.ModelResourcePath);
        if (string.Equals(path, retailPath, StringComparison.OrdinalIgnoreCase))
        {
            return ExactRetailModelScore;
        }

        if (!IsHeroModelPath(path))
        {
            return 0;
        }

        var heroToken = NormalizeToken(hero.LookupName);
        if (heroToken.Length == 0)
        {
            return 0;
        }

        var fileToken = NormalizeToken(Path.GetFileNameWithoutExtension(path));
        var pathSegments = path
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeToken)
            .ToArray();

        var score = 0;
        if (string.Equals(fileToken, heroToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }
        else if (fileToken.Contains(heroToken, StringComparison.OrdinalIgnoreCase))
        {
            score += 400;
        }

        if (pathSegments.Any(segment =>
                string.Equals(segment, heroToken, StringComparison.OrdinalIgnoreCase)))
        {
            score += 600;
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

        if (path.Contains("/lod", StringComparison.OrdinalIgnoreCase)
            || fileToken.Contains("lod", StringComparison.OrdinalIgnoreCase))
        {
            score -= 300;
        }

        return Math.Max(0, score);
    }

    private static string? TryFindDominantModel(IReadOnlyList<string> models)
    {
        if (models.Count == 0)
        {
            return null;
        }

        var ranked = models
            .Select(path => new ScoredModel(path, ScoreGenericModel(path)))
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (ranked.Length == 1)
        {
            return ranked[0].Path;
        }

        return ranked[0].Score > ranked[1].Score
            ? ranked[0].Path
            : null;
    }

    private static int ScoreGenericModel(string modelPath)
    {
        var path = NormalizeResourcePath(modelPath);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var score = 0;

        if (IsHeroModelPath(path))
        {
            score += 300;
        }
        if (!fileName.Contains("lod", StringComparison.OrdinalIgnoreCase))
        {
            score += 100;
        }
        if (!path.Contains("/attachments/", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/weapons/", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/props/", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        var depth = path.Count(character => character == '/');
        score += Math.Max(0, 20 - depth);
        return score;
    }

    private static bool IsHeroModelPath(string path) =>
        path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase));

    private static string ToCompiledModelPath(string resourcePath)
    {
        var path = NormalizeResourcePath(resourcePath);
        if (path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }
        return path.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase)
            ? path + "_c"
            : path;
    }

    private static string GetModelIdentityName(string modelPath)
    {
        var name = Path.GetFileNameWithoutExtension(NormalizeResourcePath(modelPath));
        return string.IsNullOrWhiteSpace(name) ? "Imported VPK" : name;
    }

    private static string GetVpkIdentityName(string fileName)
    {
        var name = Path.GetFileName(fileName);
        const string suffix = "_dir.vpk";
        if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^suffix.Length];
        }
        else
        {
            name = Path.GetFileNameWithoutExtension(name);
        }
        return string.IsNullOrWhiteSpace(name) ? "Imported VPK" : name;
    }

    private static string SanitizeProjectName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value
            .Trim()
            .Select(character => invalid.Contains(character) || char.IsControl(character)
                ? '_'
                : character)
            .ToArray())
            .TrimEnd(' ', '.');

        if (cleaned.Length == 0)
        {
            cleaned = "Imported VPK";
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };
        if (reserved.Contains(cleaned))
        {
            cleaned += "_project";
        }

        return cleaned;
    }

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private sealed record ScoredHeroModel(HeroCatalogEntry Hero, string ModelPath, int Score);
    private sealed record ScoredModel(string Path, int Score);
}
