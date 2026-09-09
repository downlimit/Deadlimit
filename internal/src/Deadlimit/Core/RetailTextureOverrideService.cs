using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailTextureTarget(
    string ResourcePath,
    string ReferencingMaterialResourcePath,
    bool HasExtractedSourceFile = false);

public sealed record RetailTextureOverride(
    string ArtistSourcePath,
    string RetailTextureResourcePath,
    string StagedSourceResourcePath);

public static class RetailTextureOverrideService
{
    private static readonly AsyncLocal<string?> LastExtractedSourceRoot = new();

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

    private static readonly Regex CompiledTexturesHeaderRegex = new(
        "^[ \\t]*\\\"?Compiled Textures\\\"?[ \\t]*(?:=[ \\t]*)?(?=\\{|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTextureEntryRegex = new(
        "^[ \\t]*\\\"?(?<key>[A-Za-z_$][A-Za-z0-9_$]*)\\\"?[ \\t]*(?:=[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ActiveTextureEntryRegex = new(
        "^(?<prefix>[ \\t]*\\\"?(?<key>(?:Texture|g_t)[A-Za-z0-9_$]*)\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<path>[^\\\"\\r\\n]+)(?<suffix>\\\")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyList<RetailTextureTarget> BuildTargetIndex(string extractedSourceRoot)
    {
        if (!Directory.Exists(extractedSourceRoot))
        {
            LastExtractedSourceRoot.Value = null;
            return Array.Empty<RetailTextureTarget>();
        }

        LastExtractedSourceRoot.Value = Path.GetFullPath(extractedSourceRoot);

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

        // The extracted source tree is provenance on its own. Portraits, minimap icons,
        // top-bar images and other UI textures may never be referenced by a VMAT, so they
        // must still be eligible for an exact project-root override at their retail path.
        foreach (var sourceImage in Directory.EnumerateFiles(extractedSourceRoot, "*", SearchOption.AllDirectories)
                     .Where(path => ArtistTextureExtensions.Contains(Path.GetExtension(path)))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var resourcePath = NormalizeResourcePath(Path.GetRelativePath(extractedSourceRoot, sourceImage));
            if (targets.TryGetValue(resourcePath, out var existing))
            {
                targets[resourcePath] = existing with { HasExtractedSourceFile = true };
            }
            else
            {
                targets.Add(resourcePath, new RetailTextureTarget(resourcePath, string.Empty, HasExtractedSourceFile: true));
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

        // A source-backed VMAT is enough provenance to route an artist image to the same
        // retail texture path. Extracting the stock image is only needed when the artist
        // wants a local reference copy; it is not a prerequisite for replacing that texture.
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
        PrepareRetailTextureReferences(
            addonContentRoot,
            LastExtractedSourceRoot.Value);

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

    internal static int RepairMissingRetailTextureReferences(string addonContentRoot)
    {
        return RewriteRetailTextureReferences(
            addonContentRoot,
            extractedSourceRoot: null,
            removeStockCopies: false);
    }

    private static int PrepareRetailTextureReferences(
        string addonContentRoot,
        string? extractedSourceRoot)
    {
        return RewriteRetailTextureReferences(
            addonContentRoot,
            extractedSourceRoot,
            removeStockCopies: true);
    }

    private static int RewriteRetailTextureReferences(
        string addonContentRoot,
        string? extractedSourceRoot,
        bool removeStockCopies)
    {
        if (!Directory.Exists(addonContentRoot))
        {
            return 0;
        }

        var stockCopiesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var repairedCount = 0;
        foreach (var vmatPath in Directory.EnumerateFiles(addonContentRoot, "*.vmat", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(vmatPath);
            var repaired = RestoreRetailTextureReferences(
                text,
                addonContentRoot,
                extractedSourceRoot,
                removeStockCopies,
                stockCopiesToDelete,
                out var fileRepairCount);
            if (fileRepairCount == 0)
            {
                continue;
            }

            File.WriteAllText(vmatPath, repaired);
            repairedCount += fileRepairCount;
        }

        foreach (var stockCopy in stockCopiesToDelete)
        {
            if (File.Exists(stockCopy))
            {
                File.Delete(stockCopy);
            }
        }

        return repairedCount;
    }

    private static string RestoreRetailTextureReferences(
        string materialText,
        string addonContentRoot,
        string? extractedSourceRoot,
        bool removeStockCopies,
        ISet<string> stockCopiesToDelete,
        out int repairedCount)
    {
        repairedCount = 0;
        var header = CompiledTexturesHeaderRegex.Match(materialText);
        if (!header.Success)
        {
            return materialText;
        }

        var openBrace = materialText.IndexOf('{', header.Index + header.Length);
        if (openBrace < 0)
        {
            return materialText;
        }

        var closeBrace = FindMatchingBrace(materialText, openBrace);
        if (closeBrace < 0)
        {
            return materialText;
        }

        var compiledBlock = materialText[(openBrace + 1)..closeBrace];
        var compiledEntries = CompiledTextureEntryRegex.Matches(compiledBlock)
            .Cast<Match>()
            .Select(match => new
            {
                Key = match.Groups["key"].Value,
                Path = match.Groups["path"].Value,
            })
            .Where(item => item.Path.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (compiledEntries.Length == 0)
        {
            return materialText;
        }

        var compiledByKey = compiledEntries.ToDictionary(
            item => item.Key,
            item => item.Path,
            StringComparer.OrdinalIgnoreCase);
        var compiledByStem = compiledEntries
            .GroupBy(item => GetResourceStemPath(item.Path), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(
                group => group.Key,
                group => group.Single().Path,
                StringComparer.OrdinalIgnoreCase);

        var localRepairedCount = 0;
        var editablePart = materialText[..header.Index];
        var rewrittenEditablePart = ActiveTextureEntryRegex.Replace(editablePart, match =>
        {
            var activePath = match.Groups["path"].Value;
            if (!IsEditableTextureSourcePath(activePath))
            {
                return match.Value;
            }

            var key = match.Groups["key"].Value;
            string? retailPath = null;
            if (!compiledByKey.TryGetValue(key, out retailPath))
            {
                compiledByStem.TryGetValue(GetResourceStemPath(activePath), out retailPath);
            }

            if (string.IsNullOrWhiteSpace(retailPath))
            {
                return match.Value;
            }

            var addonSourcePath = TryResolveTextureSourcePath(addonContentRoot, activePath);
            var sourceExists = addonSourcePath is not null && File.Exists(addonSourcePath);
            var isUnchangedRetailCopy = false;

            if (sourceExists
                && removeStockCopies
                && !string.IsNullOrWhiteSpace(extractedSourceRoot))
            {
                var extractedSourcePath = TryResolveTextureSourcePath(extractedSourceRoot, activePath);
                isUnchangedRetailCopy = extractedSourcePath is not null
                    && File.Exists(extractedSourcePath)
                    && FilesEqual(extractedSourcePath, addonSourcePath!);
            }

            if (sourceExists && !isUnchangedRetailCopy)
            {
                return match.Value;
            }

            if (isUnchangedRetailCopy && addonSourcePath is not null)
            {
                stockCopiesToDelete.Add(addonSourcePath);
            }

            localRepairedCount++;
            return match.Groups["prefix"].Value
                   + retailPath
                   + match.Groups["suffix"].Value;
        });

        repairedCount = localRepairedCount;
        return rewrittenEditablePart + materialText[header.Index..];
    }

    public static string ResolveOnlineTextureTarget(
        string projectFolder,
        string artistSourcePath,
        string defaultTextureTargetFolder)
    {
        var defaultTarget = Path.Combine(defaultTextureTargetFolder, Path.GetFileName(artistSourcePath));
        var manifest = ProjectStore.TryLoad(projectFolder);
        if (manifest is null)
        {
            return defaultTarget;
        }

        var sourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");
        var overrides = ResolveProjectRootOverrides(manifest, BuildTargetIndex(sourceRoot));
        var fullArtistSourcePath = Path.GetFullPath(artistSourcePath);
        var replacement = overrides.FirstOrDefault(candidate => string.Equals(
            Path.GetFullPath(candidate.ArtistSourcePath),
            fullArtistSourcePath,
            StringComparison.OrdinalIgnoreCase));
        if (replacement is null)
        {
            return defaultTarget;
        }

        var addonContentRoot = new DirectoryInfo(defaultTextureTargetFolder)
            .Parent?.Parent?.Parent?.FullName
            ?? throw new InvalidOperationException(
                $"Online texture target folder is not under an addon content root: {defaultTextureTargetFolder}");

        return SafePath.ResolveUnderRoot(
            addonContentRoot,
            replacement.StagedSourceResourcePath.Replace('/', Path.DirectorySeparatorChar),
            "Online retail texture override destination");
    }

    private static string? TryResolveTextureSourcePath(string root, string resourcePath)
    {
        if (Path.IsPathRooted(resourcePath))
        {
            return null;
        }

        try
        {
            var relative = resourcePath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimStart(Path.DirectorySeparatorChar);
            return SafePath.ResolveUnderRoot(
                root,
                relative,
                "Retail VMAT texture source");
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }

    private static bool IsEditableTextureSourcePath(string resourcePath)
    {
        var extension = Path.GetExtension(resourcePath.Replace('\\', '/'));
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".exr", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = openBrace; index < text.Length; index++)
        {
            var value = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (value == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (value == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (value == '"')
            {
                inString = true;
                continue;
            }

            if (value == '{')
            {
                depth++;
            }
            else if (value == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
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

    private static string GetResourceStemPath(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var extension = Path.GetExtension(normalized);
        return extension.Length == 0 ? normalized : normalized[..^extension.Length];
    }

    private static string GetResourceDirectory(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash < 0 ? string.Empty : normalized[..slash];
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').Trim().TrimStart('/');
}
