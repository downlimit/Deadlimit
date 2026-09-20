using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record RetailTextureTarget(
    string ResourcePath,
    string ReferencingMaterialResourcePath,
    bool HasExtractedSourceFile = false);

public sealed record RetailTextureOverride(
    string ArtistSourcePath,
    string RetailTextureResourcePath,
    string StagedSourceResourcePath,
    string ReferencingMaterialResourcePath = "");

public sealed class AmbiguousTextureTargetException : InvalidOperationException
{
    public AmbiguousTextureTargetException(
        string artistSourcePath,
        string authoringIdentity,
        IReadOnlyList<string> candidateResourcePaths)
        : base(
            $"Authoring texture '{authoringIdentity}' matches more than one Deadlock resource:" +
            Environment.NewLine + string.Join(Environment.NewLine, candidateResourcePaths.Select(path => $"  - {path}")))
    {
        ArtistSourcePath = artistSourcePath;
        AuthoringIdentity = authoringIdentity;
        CandidateResourcePaths = candidateResourcePaths;
    }

    public string ArtistSourcePath { get; }
    public string AuthoringIdentity { get; }
    public IReadOnlyList<string> CandidateResourcePaths { get; }
}

public static class RetailTextureOverrideService
{
    private static readonly HashSet<string> ArtistTextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".psd", ".jpg", ".jpeg", ".tif", ".tiff",
    };

    private static readonly HashSet<string> RetailTextureReferenceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".vtex",
    };

    private static readonly Regex VmatTextureSourceRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_]*\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTexturesBlockRegex = new(
        "(?ms)^[ \\t]*\\\"Compiled Textures\\\"[ \\t]*\\r?\\n[ \\t]*\\{.*?^[ \\t]*\\}[ \\t]*\\r?\\n?",
        RegexOptions.Compiled);

    public static IReadOnlyList<RetailTextureTarget> BuildTargetIndex(string extractedSourceRoot)
    {
        if (!Directory.Exists(extractedSourceRoot))
        {
            return Array.Empty<RetailTextureTarget>();
        }

        var targets = new Dictionary<string, RetailTextureTarget>(StringComparer.OrdinalIgnoreCase);
        var roots = ExtractedSourceLayout.GetOrderedRoots(extractedSourceRoot);
        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var root = roots[rootIndex];
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var vmatPath in Directory.EnumerateFiles(root, "*.vmat", SearchOption.AllDirectories)
                         .Where(path => rootIndex != 0 || !ExtractedSourceLayout.IsInsideNestedPipeline(root, path))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var materialResourcePath = NormalizeResourcePath(Path.GetRelativePath(root, vmatPath));
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
        }

        // The extracted source tree is provenance on its own. Portraits, minimap icons,
        // top-bar images and other UI textures may never be referenced by a VMAT, so they
        // must still be eligible for an exact project-root override at their retail path.
        for (var rootIndex = 0; rootIndex < roots.Count; rootIndex++)
        {
            var root = roots[rootIndex];
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var sourceImage in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                         .Where(path => rootIndex != 0 || !ExtractedSourceLayout.IsInsideNestedPipeline(root, path))
                         .Where(path => ArtistTextureExtensions.Contains(Path.GetExtension(path)))
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var resourcePath = NormalizeResourcePath(Path.GetRelativePath(root, sourceImage));
                if (targets.TryGetValue(resourcePath, out var existing))
                {
                    targets[resourcePath] = existing with { HasExtractedSourceFile = true };
                }
                else
                {
                    targets.Add(resourcePath, new RetailTextureTarget(resourcePath, string.Empty, HasExtractedSourceFile: true));
                }
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
                    .GroupBy(
                        target => Path.ChangeExtension(target.ResourcePath, ".vtex_c"),
                        StringComparer.OrdinalIgnoreCase)
                    .Select(resourceGroup => resourceGroup
                        .OrderBy(target => Path.GetExtension(target.ResourcePath).Equals(
                            ".vtex",
                            StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                        .ThenBy(target => target.ResourcePath, StringComparer.OrdinalIgnoreCase)
                        .First())
                    .OrderBy(target => target.ResourcePath, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var overrides = new List<RetailTextureOverride>();
        var artistSources = ProjectAuthoringLayout.SelectPreferredFiles(
            manifest,
            ProjectAuthoringLayout.EnumerateAuthoringFiles(manifest)
                .Where(path => ArtistTextureExtensions.Contains(Path.GetExtension(path))));
        foreach (var artistSource in artistSources)
        {
            var artistFileName = Path.GetFileName(artistSource);
            var stem = Path.GetFileNameWithoutExtension(artistSource);
            if (!targetsByStem.TryGetValue(stem, out var stemMatches))
            {
                continue;
            }

            var authoringIdentity = ProjectAuthoringLayout.GetAuthoringIdentity(manifest, artistSource);
            var selectedPaths = manifest.TransientTextureTargetBindings.TryGetValue(authoringIdentity, out var transient)
                ? transient
                : manifest.TextureTargetBindings.TryGetValue(authoringIdentity, out var persisted)
                    ? persisted
                    : null;
            var selectedMatches = selectedPaths is null
                ? stemMatches
                : stemMatches.Where(target => selectedPaths.Contains(
                    target.ResourcePath,
                    StringComparer.OrdinalIgnoreCase)).ToArray();

            if (selectedPaths is not null && selectedMatches.Length != selectedPaths.Count)
            {
                manifest.TextureTargetBindings.Remove(authoringIdentity);
                manifest.TransientTextureTargetBindings.Remove(authoringIdentity);
                selectedPaths = null;
                selectedMatches = stemMatches;
            }

            if (selectedPaths is null && stemMatches.Length > 1)
            {
                throw new AmbiguousTextureTargetException(
                    artistSource,
                    authoringIdentity,
                    stemMatches.Select(match => match.ResourcePath).ToArray());
            }

            foreach (var target in selectedMatches)
            {
                var targetDirectory = GetResourceDirectory(target.ResourcePath);
                var stagedResourcePath = targetDirectory.Length == 0
                    ? artistFileName
                    : targetDirectory + "/" + artistFileName;

                overrides.Add(new RetailTextureOverride(
                    artistSource,
                    target.ResourcePath,
                    stagedResourcePath,
                    target.ReferencingMaterialResourcePath));
            }
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
            StagePanoramaTextureDescriptor(destination, replacement.StagedSourceResourcePath);
            staged++;
        }

        // Decompiled retail VMATs carry a cached "Compiled Textures" block whose
        // g_t* entries still name the stock VTEX resources. Leaving that block in
        // place can make ResourceCompiler keep using the retail texture even when
        // the matching Texture* authoring PNG was replaced at its original path.
        // Remove it only from materials that reference an explicit root override;
        // ResourceCompiler then regenerates the block from the staged Texture* inputs.
        foreach (var materialResourcePath in overrides
                     .Select(replacement => replacement.ReferencingMaterialResourcePath)
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var materialPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                materialResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Retail texture override material destination");
            if (!File.Exists(materialPath))
            {
                continue;
            }

            var text = File.ReadAllText(materialPath);
            var sanitized = CompiledTexturesBlockRegex.Replace(text, string.Empty);
            foreach (var replacement in overrides.Where(item => string.Equals(
                         item.ReferencingMaterialResourcePath,
                         materialResourcePath,
                         StringComparison.OrdinalIgnoreCase)))
            {
                sanitized = sanitized.Replace(
                    replacement.RetailTextureResourcePath,
                    replacement.StagedSourceResourcePath,
                    StringComparison.OrdinalIgnoreCase);
            }
            if (!string.Equals(text, sanitized, StringComparison.Ordinal))
            {
                File.WriteAllText(materialPath, sanitized);
            }
        }

        return staged;
    }

    private static void StagePanoramaTextureDescriptor(
        string stagedImagePath,
        string stagedResourcePath)
    {
        var normalizedResourcePath = NormalizeResourcePath(stagedResourcePath);
        if (!normalizedResourcePath.StartsWith("panorama/images/", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var descriptorPath = Path.ChangeExtension(stagedImagePath, ".vtex");
        var descriptor = $$"""
            <!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
            "CDmeVtex"
            {
                "m_inputTextureArray" "element_array"
                [
                    "CDmeInputTexture"
                    {
                        "m_name" "string" "InputTexture0"
                        "m_fileName" "string" "{{normalizedResourcePath}}"
                        "m_colorSpace" "string" "srgb"
                        "m_typeString" "string" "2D"
                        "m_imageProcessorArray" "element_array"
                        [
                            "CDmeImageProcessor"
                            {
                                "m_algorithm" "string" "None"
                                "m_stringArg" "string" ""
                                "m_vFloat4Arg" "vector4" "0 0 0 0"
                            }
                        ]
                    }
                ]
                "m_outputTypeString" "string" "2D"
                "m_outputFormat" "string" "RGBA8888"
                "m_outputClearColor" "vector4" "0 0 0 0"
                "m_nOutputMinDimension" "int" "0"
                "m_nOutputMaxDimension" "int" "0"
                "m_textureOutputChannelArray" "element_array"
                [
                    "CDmeTextureOutputChannel"
                    {
                        "m_inputTextureArray" "string_array" [ "InputTexture0" ]
                        "m_srcChannels" "string" "rgba"
                        "m_dstChannels" "string" "rgba"
                        "m_mipAlgorithm" "CDmeImageProcessor"
                        {
                            "m_algorithm" "string" "None"
                            "m_stringArg" "string" ""
                            "m_vFloat4Arg" "vector4" "0 0 0 0"
                        }
                        "m_outputColorSpace" "string" "srgb"
                    }
                ]
                "m_vClamp" "vector3" "0 0 0"
                "m_bNoLod" "bool" "1"
            }
            """;
        AtomicFile.WriteAllText(descriptorPath, descriptor);
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
