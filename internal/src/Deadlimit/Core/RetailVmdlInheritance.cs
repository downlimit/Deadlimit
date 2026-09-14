using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record VmdlMaterialRemap(string From, string To);

public sealed record RetailModelSourceCopyResult(
    string SourceVmdlPath,
    string DestinationVmdlPath,
    string DestinationModelFolder,
    int FilesCopied);

public sealed record RetailRenderMeshEntry(string Name, string Filename);

public sealed record ArtistDmxOverlayResult(
    string ArtistDmxPath,
    string ResourcePath,
    string PreparedDmxPath,
    VertexColorSidecarResult VertexColor);

public sealed record ArtistFbxOverlayResult(
    string ArtistFbxPath,
    string ResourcePath,
    string PreparedFbxPath);

public sealed record RetailVmdlPatchResult(
    IReadOnlyList<string> RemovedClasses,
    int ExistingMaterialRemapCount,
    int AddedMaterialRemapCount,
    int RenderMeshCount);

public static class RetailVmdlInheritance
{
    // Current Reduced CSDK12 cannot instantiate these source ModelDoc classes.
    // Runtime AG2/NmSkeleton references are handled later by the release pipeline.
    private static readonly HashSet<string> UnsupportedSourceClasses = new(StringComparer.Ordinal)
    {
        "NmSkeletonList",
        "AnimGraph2List",
    };

    private static readonly Regex ClassRegex = new(
        "\\b_class\\s*=\\s*\"(?<class>[^\"]+)\"",
        RegexOptions.Compiled);

    private static readonly Regex MaterialRemapRegex = new(
        "\\bfrom\\s*=\\s*\"(?<from>[^\"]+)\"\\s+to\\s*=\\s*\"(?<to>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex RenderMeshRegex = new(
        "_class\\s*=\\s*\"RenderMeshFile\"(?<body>.*?)name\\s*=\\s*\"(?<name>[^\"]+)\"(?<afterName>.*?)filename\\s*=\\s*\"(?<filename>[^\"]+)\"",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex VmatTextureSourceRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_]*\\\"?[ \\t]*(?:=[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly HashSet<string> RetailTextureSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
        ".exr",
        ".vtex",
    };

    public static string? FindRetailVmdl(ProjectManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            return null;
        }

        var retailSourceResource = ToSourceVmdlResourcePath(manifest.RetailMainModel);
        var exactPath = ExtractedSourceLayout.ResolveResource(manifest, retailSourceResource);
        if (exactPath is not null)
        {
            return exactPath;
        }

        var desiredName = Path.GetFileName(retailSourceResource);
        return ExtractedSourceLayout.EnumerateFilesByPriority(manifest, "*.vmdl")
            .Where(path => string.Equals(Path.GetFileName(path), desiredName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path.Length)
            .FirstOrDefault();
    }

    public static RetailModelSourceCopyResult CopyRetailModelSourceTree(
        ProjectManifest manifest,
        string addonContentRoot)
    {
        var sourceVmdl = FindRetailVmdl(manifest)
            ?? throw new InvalidOperationException(
                "The extracted retail VMDL was not found. Run EXTRACT HERO SOURCE again before preparing the addon.");

        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("Retail main model is unknown.");
        }

        var sourceFolder = Path.GetDirectoryName(sourceVmdl)!;
        var resourceFolder = Path.GetDirectoryName(
                ToSourceVmdlResourcePath(manifest.RetailMainModel)
                    .Replace('/', Path.DirectorySeparatorChar))
            ?? string.Empty;
        var destinationFolder = SafePath.ResolveUnderRoot(
            addonContentRoot,
            resourceFolder,
            "Retail VMDL destination folder");
        var sourceRoot = ExtractedSourceLayout.SelectOwningRoot(manifest, sourceVmdl);

        Directory.CreateDirectory(destinationFolder);

        var copied = 0;
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceFolder, sourceFile);
            var destination = SafePath.ResolveUnderRoot(
                destinationFolder,
                relative,
                "Retail source-tree destination");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
            copied++;
        }

        copied += CopyMissingRenderMeshSources(
            sourceVmdl,
            sourceRoot,
            addonContentRoot);

        copied += CopyExternalRetailTextureDependencies(
            sourceFolder,
            sourceRoot,
            addonContentRoot);

        var textureTargets = RetailTextureOverrideService.BuildTargetIndex(sourceRoot);
        var textureOverrides = RetailTextureOverrideService.ResolveProjectRootOverrides(
            manifest,
            textureTargets);
        copied += RetailTextureOverrideService.StageProjectRootOverrides(
            addonContentRoot,
            textureOverrides);

        var destinationVmdl = Path.Combine(destinationFolder, Path.GetFileName(sourceVmdl));
        if (!File.Exists(destinationVmdl))
        {
            throw new InvalidOperationException(
                $"Retail source copy completed, but the destination VMDL was not found: {destinationVmdl}");
        }

        return new RetailModelSourceCopyResult(
            sourceVmdl,
            destinationVmdl,
            destinationFolder,
            copied);
    }

    private static int CopyMissingRenderMeshSources(
        string sourceVmdl,
        string sourceRoot,
        string addonContentRoot)
    {
        var copied = 0;
        foreach (var renderMesh in ReadRenderMeshes(sourceVmdl))
        {
            var destination = SafePath.ResolveUnderRoot(
                addonContentRoot,
                renderMesh.Filename.Replace('/', Path.DirectorySeparatorChar),
                "Retail render-mesh destination");
            if (File.Exists(destination))
            {
                continue;
            }

            var fallback = ExtractedSourceLayout.ResolveResource(sourceRoot, renderMesh.Filename);
            if (fallback is null)
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(fallback, destination, overwrite: false);
            copied++;
        }
        return copied;
    }

    private static int CopyExternalRetailTextureDependencies(
        string sourceFolder,
        string sourceRoot,
        string addonContentRoot)
    {
        var copied = 0;
        var copiedResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var vmatPath in Directory.EnumerateFiles(sourceFolder, "*.vmat", SearchOption.AllDirectories))
        {
            var vmatText = File.ReadAllText(vmatPath);
            foreach (Match match in VmatTextureSourceRegex.Matches(vmatText))
            {
                var resourcePath = NormalizeResourcePath(match.Groups["path"].Value);
                if (resourcePath.Length == 0
                    || resourcePath.Contains(':', StringComparison.Ordinal)
                    || !RetailTextureSourceExtensions.Contains(Path.GetExtension(resourcePath)))
                {
                    continue;
                }

                string sourcePath;
                try
                {
                    sourcePath = ExtractedSourceLayout.ResolveResource(sourceRoot, resourcePath) ?? string.Empty;
                }
                catch (InvalidOperationException)
                {
                    continue;
                }

                if (sourcePath.Length == 0
                    || !File.Exists(sourcePath)
                    || IsPathUnderRoot(sourceFolder, sourcePath))
                {
                    continue;
                }

                if (!copiedResources.Add(resourcePath))
                {
                    continue;
                }

                var destination = SafePath.ResolveUnderRoot(
                    addonContentRoot,
                    resourcePath.Replace('/', Path.DirectorySeparatorChar),
                    "Retail VMAT external texture dependency destination");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(sourcePath, destination, overwrite: true);
                copied++;
            }
        }

        return copied;
    }

    private static bool IsPathUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    public static IReadOnlyList<RetailRenderMeshEntry> ReadRenderMeshes(string vmdlPath)
    {
        var text = File.ReadAllText(vmdlPath);
        var root = LocateRootChildren(text);
        var renderMeshNode = root.Nodes.FirstOrDefault(node =>
            string.Equals(node.ClassName, "RenderMeshList", StringComparison.Ordinal));

        if (renderMeshNode is null)
        {
            return Array.Empty<RetailRenderMeshEntry>();
        }

        return RenderMeshRegex.Matches(renderMeshNode.Text)
            .Select(match => new RetailRenderMeshEntry(
                match.Groups["name"].Value,
                NormalizeResourcePath(match.Groups["filename"].Value)))
            .ToArray();
    }

    public static IReadOnlyList<ArtistDmxOverlayResult> OverlayArtistDmx(
        RetailModelSourceCopyResult sourceCopy,
        string addonContentRoot,
        string hero,
        IReadOnlyList<string> artistDmxFiles)
    {
        var renderMeshes = ReadRenderMeshes(sourceCopy.DestinationVmdlPath);
        if (renderMeshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The retail VMDL has no RenderMeshFile entries, so Deadlimit cannot map the artist DMX safely.");
        }

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var replaced = new List<ArtistDmxOverlayResult>();

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
                var sourceRoot = ExtractedSourceAssetResolver.TryResolveSourceRootForArtistPath(artistDmx);
                var sourceTarget = sourceRoot is null
                    ? null
                    : ExtractedSourceAssetResolver.ResolveDmxTarget(artistDmx, sourceRoot);

                if (sourceTarget is not null && sourceRoot is not null)
                {
                    targetResourcePath = NormalizeResourcePath(sourceTarget.ResourcePath);
                    ExtractedSourceAssetResolver.StageOwningVmdlSourceTrees(
                        sourceTarget.SourceRoot,
                        addonContentRoot,
                        sourceTarget.OwnerVmdlSourcePaths,
                        sourceCopy.DestinationVmdlPath);

                    foreach (var ownerFolder in sourceTarget.OwnerVmdlSourcePaths
                                 .Select(Path.GetDirectoryName)
                                 .Where(folder => !string.IsNullOrWhiteSpace(folder))
                                 .Select(folder => folder!)
                                 .Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        CopyExternalRetailTextureDependencies(ownerFolder, sourceTarget.SourceRoot, addonContentRoot);
                    }
                }
                else if (artistDmxFiles.Count == 1)
                {
                    var primary = ChoosePrimaryRenderMesh(renderMeshes, hero, artistFileName)
                        ?? throw new InvalidOperationException(
                            $"Could not identify a unique primary retail render mesh for '{artistFileName}'. " +
                            "Use an original extracted source filename or rename the artist DMX to match the retail render-mesh source filename.");
                    targetResourcePath = NormalizeResourcePath(primary.Filename);
                }
                else
                {
                    throw new InvalidOperationException(
                        $"Artist DMX '{artistFileName}' does not uniquely match the main retail VMDL and has no unique extracted-source target. " +
                        "Deadlimit will not guess which retail resource to replace.");
                }
            }

            if (!usedTargets.Add(targetResourcePath))
            {
                throw new InvalidOperationException(
                    $"More than one artist DMX resolved to the same retail render mesh: {targetResourcePath}");
            }

            var targetPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                targetResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "VMDL RenderMeshFile target");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.Copy(artistDmx, targetPath, overwrite: true);
            var stagedVertexColor = VertexColorSourceGuard.PrepareStagedDmx(artistDmx, targetPath);
            var vertexColor = stagedVertexColor.VertexColor;
            replaced.Add(new ArtistDmxOverlayResult(
                artistDmx,
                targetResourcePath,
                targetPath,
                vertexColor));
        }

        return replaced;
    }

    public static IReadOnlyList<ArtistFbxOverlayResult> OverlayArtistFbx(
        RetailModelSourceCopyResult sourceCopy,
        string addonContentRoot,
        string hero,
        IReadOnlyList<string> artistFbxFiles)
    {
        var renderMeshes = ReadRenderMeshes(sourceCopy.DestinationVmdlPath);
        if (artistFbxFiles.Count == 0)
        {
            return [];
        }
        if (renderMeshes.Count == 0)
        {
            throw new InvalidOperationException(
                "The retail VMDL has no RenderMeshFile entries, so Deadlimit cannot map artist FBX files safely.");
        }

        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<ArtistFbxOverlayResult>();
        foreach (var artistFbx in artistFbxFiles)
        {
            var artistStem = Path.GetFileNameWithoutExtension(artistFbx);
            var exact = renderMeshes
                .Where(entry => string.Equals(
                    Path.GetFileNameWithoutExtension(entry.Filename),
                    artistStem,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            RetailRenderMeshEntry target;
            if (exact.Length == 1)
            {
                target = exact[0];
            }
            else if (artistFbxFiles.Count == 1)
            {
                target = ChoosePrimaryRenderMesh(renderMeshes, hero, Path.GetFileName(artistFbx))
                    ?? throw new InvalidOperationException(
                        $"Could not identify a unique primary retail render mesh for '{Path.GetFileName(artistFbx)}'. " +
                        "Use the original extracted render-mesh basename for the FBX file.");
            }
            else
            {
                throw new InvalidOperationException(
                    $"Artist FBX '{Path.GetFileName(artistFbx)}' does not uniquely match a retail render mesh. " +
                    "Use the original extracted render-mesh basename for each FBX file.");
            }

            if (replacements.ContainsKey(target.Filename))
            {
                throw new InvalidOperationException(
                    $"More than one artist FBX resolved to the same retail render mesh: {target.Filename}");
            }

            var fbxResource = NormalizeResourcePath(Path.ChangeExtension(target.Filename, ".fbx"));
            var destination = SafePath.ResolveUnderRoot(
                addonContentRoot,
                fbxResource.Replace('/', Path.DirectorySeparatorChar),
                "VMDL artist FBX destination");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(artistFbx, destination, overwrite: true);
            replacements.Add(target.Filename, fbxResource);
            results.Add(new ArtistFbxOverlayResult(artistFbx, fbxResource, destination));
        }

        ReplaceRenderMeshFilenames(sourceCopy.DestinationVmdlPath, replacements);
        return results;
    }

    private static void ReplaceRenderMeshFilenames(
        string vmdlPath,
        IReadOnlyDictionary<string, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return;
        }

        var text = File.ReadAllText(vmdlPath);
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var updated = RenderMeshRegex.Replace(text, match =>
        {
            var current = NormalizeResourcePath(match.Groups["filename"].Value);
            if (!replacements.TryGetValue(current, out var replacement))
            {
                return match.Value;
            }
            replaced.Add(current);
            var relativeStart = match.Groups["filename"].Index - match.Index;
            return match.Value[..relativeStart]
                + EscapeKv3(replacement)
                + match.Value[(relativeStart + match.Groups["filename"].Length)..];
        });

        var missing = replacements.Keys.Where(key => !replaced.Contains(key)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"Could not update VMDL RenderMeshFile target(s): {string.Join(", ", missing)}");
        }
        File.WriteAllText(vmdlPath, updated, new UTF8Encoding(false));
    }

    public static RetailVmdlPatchResult PatchAuthoringVmdl(
        string destinationVmdlPath,
        IReadOnlyList<VmdlMaterialRemap> additionalMaterialRemaps)
    {
        var mainResult = PatchSingleAuthoringVmdl(
            destinationVmdlPath,
            additionalMaterialRemaps,
            stripSupportingMarker: false);

        var addonContentRoot = FindAddonContentRoot(destinationVmdlPath);
        if (addonContentRoot is null)
        {
            return mainResult;
        }

        var mainFullPath = Path.GetFullPath(destinationVmdlPath);
        foreach (var supportingVmdl in Directory.EnumerateFiles(addonContentRoot, "*.vmdl", SearchOption.AllDirectories)
                     .Where(path => !string.Equals(Path.GetFullPath(path), mainFullPath, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(supportingVmdl);
            if (!text.StartsWith(ExtractedSourceAssetResolver.SupportingVmdlMarker, StringComparison.Ordinal))
            {
                continue;
            }

            PatchSingleAuthoringVmdl(
                supportingVmdl,
                additionalMaterialRemaps,
                stripSupportingMarker: true);
        }

        return mainResult;
    }

    private static RetailVmdlPatchResult PatchSingleAuthoringVmdl(
        string destinationVmdlPath,
        IReadOnlyList<VmdlMaterialRemap> additionalMaterialRemaps,
        bool stripSupportingMarker)
    {
        var text = File.ReadAllText(destinationVmdlPath);
        if (stripSupportingMarker
            && text.StartsWith(ExtractedSourceAssetResolver.SupportingVmdlMarker, StringComparison.Ordinal))
        {
            text = text[ExtractedSourceAssetResolver.SupportingVmdlMarker.Length..]
                .TrimStart('\r', '\n');
        }

        var root = LocateRootChildren(text);

        var removedClasses = root.Nodes
            .Where(node => UnsupportedSourceClasses.Contains(node.ClassName))
            .Select(node => node.ClassName)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        var keptNodes = root.Nodes
            .Where(node => !UnsupportedSourceClasses.Contains(node.ClassName))
            .ToList();

        var materialIndex = keptNodes.FindIndex(node =>
            string.Equals(node.ClassName, "MaterialGroupList", StringComparison.Ordinal));

        var existingRemapCount = 0;
        var addedRemapCount = 0;

        if (materialIndex >= 0)
        {
            var materialNode = keptNodes[materialIndex];
            var merge = MergeMaterialRemaps(materialNode.Text, additionalMaterialRemaps);
            existingRemapCount = merge.ExistingCount;
            addedRemapCount = merge.AddedCount;
            keptNodes[materialIndex] = materialNode with { Text = merge.Text };
        }
        else if (additionalMaterialRemaps.Count > 0)
        {
            var insertIndex = keptNodes.FindIndex(node =>
                string.Equals(node.ClassName, "RenderMeshList", StringComparison.Ordinal));
            insertIndex = insertIndex >= 0 ? insertIndex + 1 : 0;

            keptNodes.Insert(
                insertIndex,
                new RetailVmdlNode(
                    "MaterialGroupList",
                    CreateMaterialGroupList(additionalMaterialRemaps)));
            addedRemapCount = additionalMaterialRemaps
                .Select(remap => remap.From)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        var patchedText = ReplaceRootChildren(text, root, keptNodes);
        File.WriteAllText(
            destinationVmdlPath,
            patchedText,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var renderMeshCount = keptNodes
            .Where(node => string.Equals(node.ClassName, "RenderMeshList", StringComparison.Ordinal))
            .SelectMany(node => RenderMeshRegex.Matches(node.Text))
            .Count();

        return new RetailVmdlPatchResult(
            removedClasses,
            existingRemapCount,
            addedRemapCount,
            renderMeshCount);
    }

    private static string? FindAddonContentRoot(string vmdlPath)
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(vmdlPath))!);
        while (current.Parent is not null)
        {
            if (string.Equals(current.Parent.Name, "citadel_addons", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
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

    private static MaterialMergeResult MergeMaterialRemaps(
        string materialGroupNode,
        IReadOnlyList<VmdlMaterialRemap> additionalRemaps)
    {
        var remapsIndex = materialGroupNode.IndexOf("remaps", StringComparison.Ordinal);
        if (remapsIndex < 0)
        {
            throw new InvalidDataException(
                "Retail MaterialGroupList exists but contains no remaps array. Refusing to replace it destructively.");
        }

        var arrayStart = materialGroupNode.IndexOf('[', remapsIndex);
        if (arrayStart < 0)
        {
            throw new InvalidDataException("Retail MaterialGroupList remaps array is malformed.");
        }

        var arrayEnd = FindMatching(materialGroupNode, arrayStart, '[', ']');
        if (arrayEnd < 0)
        {
            throw new InvalidDataException("Retail MaterialGroupList remaps array is unbalanced.");
        }

        var remapSlice = materialGroupNode[arrayStart..(arrayEnd + 1)];
        var existing = MaterialRemapRegex.Matches(remapSlice)
            .Select(match => new VmdlMaterialRemap(
                match.Groups["from"].Value,
                match.Groups["to"].Value))
            .ToArray();

        var existingFrom = existing
            .Select(remap => remap.From)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var additions = additionalRemaps
            .Where(remap => !existingFrom.Contains(remap.From))
            .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (additions.Length == 0)
        {
            return new MaterialMergeResult(materialGroupNode, existing.Length, 0);
        }

        var insertion = new StringBuilder();
        foreach (var remap in additions)
        {
            insertion.AppendLine();
            insertion.AppendLine("\t\t\t\t\t\t{");
            insertion.AppendLine($"\t\t\t\t\t\t\tfrom = \"{EscapeKv3(remap.From)}\"");
            insertion.AppendLine($"\t\t\t\t\t\t\tto = \"{EscapeKv3(remap.To)}\"");
            insertion.Append("\t\t\t\t\t\t},");
        }

        var merged = materialGroupNode[..arrayEnd]
            + insertion
            + materialGroupNode[arrayEnd..];

        return new MaterialMergeResult(merged, existing.Length, additions.Length);
    }

    private static string CreateMaterialGroupList(IReadOnlyList<VmdlMaterialRemap> remaps)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("\t_class = \"MaterialGroupList\"");
        sb.AppendLine("\tchildren =");
        sb.AppendLine("\t[");
        sb.AppendLine("\t\t{");
        sb.AppendLine("\t\t\t_class = \"DefaultMaterialGroup\"");
        sb.AppendLine("\t\t\tremaps =");
        sb.AppendLine("\t\t\t[");
        foreach (var remap in remaps
                     .GroupBy(value => value.From, StringComparer.OrdinalIgnoreCase)
                     .Select(group => group.First()))
        {
            sb.AppendLine("\t\t\t\t{");
            sb.AppendLine($"\t\t\t\t\tfrom = \"{EscapeKv3(remap.From)}\"");
            sb.AppendLine($"\t\t\t\t\tto = \"{EscapeKv3(remap.To)}\"");
            sb.AppendLine("\t\t\t\t},");
        }
        sb.AppendLine("\t\t\t]");
        sb.AppendLine("\t\t\tuse_global_default = false");
        sb.AppendLine("\t\t\tglobal_default_material = \"\"");
        sb.AppendLine("\t\t},");
        sb.AppendLine("\t]");
        sb.Append('}');
        return sb.ToString();
    }

    private static RootChildrenLocation LocateRootChildren(string text)
    {
        var rootIndex = text.IndexOf("rootNode", StringComparison.Ordinal);
        if (rootIndex < 0)
        {
            throw new InvalidDataException("Retail VMDL has no rootNode.");
        }

        var childrenIndex = text.IndexOf("children", rootIndex, StringComparison.Ordinal);
        if (childrenIndex < 0)
        {
            throw new InvalidDataException("Retail VMDL rootNode has no children array.");
        }

        var arrayStart = text.IndexOf('[', childrenIndex);
        if (arrayStart < 0)
        {
            throw new InvalidDataException("Retail VMDL root children array is malformed.");
        }

        var arrayEnd = FindMatching(text, arrayStart, '[', ']');
        if (arrayEnd < 0)
        {
            throw new InvalidDataException("Retail VMDL root children array is unbalanced.");
        }

        var nodes = ExtractRootChildNodes(text, arrayStart, arrayEnd);
        return new RootChildrenLocation(arrayStart, arrayEnd, nodes);
    }

    private static IReadOnlyList<RetailVmdlNode> ExtractRootChildNodes(
        string text,
        int arrayStart,
        int arrayEnd)
    {
        var nodes = new List<RetailVmdlNode>();
        var cursor = arrayStart + 1;

        while (cursor < arrayEnd)
        {
            cursor = SkipWhitespaceAndCommas(text, cursor, arrayEnd);
            if (cursor >= arrayEnd)
            {
                break;
            }

            if (text[cursor] != '{')
            {
                cursor++;
                continue;
            }

            var nodeEnd = FindMatching(text, cursor, '{', '}');
            if (nodeEnd < 0 || nodeEnd > arrayEnd)
            {
                throw new InvalidDataException("Retail VMDL contains an unbalanced root child node.");
            }

            var nodeText = text[cursor..(nodeEnd + 1)];
            var classMatch = ClassRegex.Match(nodeText);
            if (classMatch.Success)
            {
                nodes.Add(new RetailVmdlNode(classMatch.Groups["class"].Value, nodeText));
            }

            cursor = nodeEnd + 1;
        }

        return nodes;
    }

    private static string ReplaceRootChildren(
        string originalText,
        RootChildrenLocation root,
        IReadOnlyList<RetailVmdlNode> nodes)
    {
        var sb = new StringBuilder(originalText.Length + 1024);
        sb.Append(originalText.AsSpan(0, root.ArrayStart + 1));
        sb.AppendLine();

        foreach (var node in nodes)
        {
            sb.Append(IndentBlock(node.Text.Trim(), "\t\t\t"));
            sb.AppendLine(",");
        }

        sb.Append("\t\t");
        sb.Append(originalText.AsSpan(root.ArrayEnd));
        return sb.ToString();
    }

    private static string IndentBlock(string text, string indent)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        return indent + normalized.Replace("\n", "\n" + indent, StringComparison.Ordinal);
    }

    private static int SkipWhitespaceAndCommas(string text, int index, int limit)
    {
        while (index < limit && (char.IsWhiteSpace(text[index]) || text[index] == ','))
        {
            index++;
        }
        return index;
    }

    private static int FindMatching(string text, int start, char open, char close)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = start; index < text.Length; index++)
        {
            var ch = text[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                }
                else if (ch == '\\')
                {
                    escaped = true;
                }
                else if (ch == '"')
                {
                    inString = false;
                }
                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == open)
            {
                depth++;
            }
            else if (ch == close)
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

    private static string ToSourceVmdlResourcePath(string compiledResourcePath)
    {
        var normalized = NormalizeResourcePath(compiledResourcePath);
        if (!normalized.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Retail main model is not a .vmdl_c resource: {compiledResourcePath}");
        }
        return normalized[..^2];
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string EscapeKv3(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record RetailVmdlNode(string ClassName, string Text);
    private sealed record RootChildrenLocation(
        int ArrayStart,
        int ArrayEnd,
        IReadOnlyList<RetailVmdlNode> Nodes);
    private sealed record MaterialMergeResult(string Text, int ExistingCount, int AddedCount);
}
