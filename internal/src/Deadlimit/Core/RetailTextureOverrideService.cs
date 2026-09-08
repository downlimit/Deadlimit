using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailTextureTarget(
    string ResourcePath,
    string ReferencingMaterialResourcePath);

public sealed record RetailTextureOverride(
    string ArtistSourcePath,
    string RetailTextureResourcePath,
    string StagedSourceResourcePath);

public static class RetailTextureOverrideService
{
    private static readonly HashSet<string> ArtistTextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff",
    };

    private static readonly HashSet<string> RetailTextureReferenceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".vtex",
    };

    private static readonly Regex VmatTextureSourceRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_]*\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<RetailTextureTarget> BuildTargetIndex(string extractedSourceRoot)
    {
        if (!Directory.Exists(extractedSourceRoot))
        {
            return Array.Empty<RetailTextureTarget>();
        }

        var targets = new Dictionary<string, RetailTextureTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var vmatPath in Directory.EnumerateFiles(extractedSourceRoot, "*.vmat", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var materialResourcePath = NormalizeResourcePath(Path.GetRelativePath(extractedSourceRoot, vmatPath));
            var text = File.ReadAllText(vmatPath);
            foreach (Match match in VmatTextureSourceRegex.Matches(text))
            {
                var resourcePath = NormalizeResourcePath(match.Groups["path"].Value);
                if (!IsRetailTextureSourceReference(resourcePath))
                {
                    continue;
                }

                targets.TryAdd(resourcePath, new RetailTextureTarget(resourcePath, materialResourcePath));
            }
        }

        return targets.Values
            .OrderBy(target => target.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<RetailTextureOverride> ResolveProjectRootOverrides(
        ProjectManifest manifest,
        IReadOnlyList<RetailTextureTarget> targets)
    {
        if (!Directory.Exists(manifest.ProjectFolder) || targets.Count == 0)
        {
            return Array.Empty<RetailTextureOverride>();
        }

        var targetsByStem = targets
            .GroupBy(target => GetResourceStem(target.ResourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .GroupBy(target => target.ResourcePath, StringComparer.OrdinalIgnoreCase)
                    .Select(resourceGroup => resourceGroup.First())
                    .OrderBy(target => target.ResourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var overrides = new List<RetailTextureOverride>();
        foreach (var artistSource in Directory.EnumerateFiles(manifest.ProjectFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => ArtistTextureExtensions.Contains(Path.GetExtension(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var artistFileName = Path.GetFileName(artistSource);
            var stem = Path.GetFileNameWithoutExtension(artistSource);
            if (!targetsByStem.TryGetValue(stem, out var stemMatches))
            {
                continue;
            }

            var exactMatches = stemMatches
                .Where(target => string.Equals(
                    Path.GetFileName(target.ResourcePath.Replace('/', Path.DirectorySeparatorChar)),
                    artistFileName,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exactMatches.Length == 0)
            {
                var expectedNames = string.Join(
                    ", ",
                    stemMatches
                        .Select(match => Path.GetFileName(match.ResourcePath.Replace('/', Path.DirectorySeparatorChar)))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase));
                throw new InvalidOperationException(
                    $"Project-root texture '{artistFileName}' has the same basename as a retail texture but a different source extension. " +
                    $"Use the original retail filename: {expectedNames}");
            }

            if (exactMatches.Length != 1)
            {
                var candidates = string.Join(
                    Environment.NewLine,
                    exactMatches.Select(match => $"  - {match.ResourcePath}"));
                throw new InvalidOperationException(
                    $"Project-root texture '{artistFileName}' matches more than one retail texture resource. " +
                    "Deadlimit will not guess which resource to replace." + Environment.NewLine + candidates);
            }

            var target = exactMatches[0];
            var targetDirectory = GetResourceDirectory(target.ResourcePath);
            var stagedResourcePath = targetDirectory.Length == 0
                ? artistFileName
                : targetDirectory + "/" + artistFileName;

            overrides.Add(new RetailTextureOverride(
                artistSource,
                target.ResourcePath,
                stagedResourcePath));
        }

        return overrides;
    }

    public static int StageProjectRootOverrides(
        string addonContentRoot,
        IReadOnlyList<RetailTextureOverride> overrides)
    {
        var staged = 0;
        foreach (var replacement in overrides)
        {
            var destination = SafePath.ResolveUnderRoot(
                addonContentRoot,
                replacement.StagedSourceResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Retail texture override destination");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(replacement.ArtistSourcePath, destination, overwrite: true);
            staged++;
        }

        return staged;
    }

    private static bool IsRetailTextureSourceReference(string resourcePath)
    {
        if (resourcePath.Length == 0 || resourcePath.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return RetailTextureReferenceExtensions.Contains(Path.GetExtension(resourcePath));
    }

    private static string GetResourceStem(string resourcePath) =>
        Path.GetFileNameWithoutExtension(resourcePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetResourceDirectory(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').Trim().TrimStart('/');
}
