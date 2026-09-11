namespace Deadlimit.Core;

internal static class ExtractedSourceLayout
{
    public const string GltfPipelineFolderName = "glTFpipeline";
    public const string LegacyGltfPipelineFolderName = "glTFsource";

    public static IReadOnlyList<string> GetOrderedRoots(ProjectManifest manifest)
    {
        var sourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");

        return GetOrderedRoots(sourceRoot);
    }

    public static IReadOnlyList<string> GetOrderedRoots(string sourceOrPipelineRoot)
    {
        var root = Path.GetFullPath(sourceOrPipelineRoot);
        if (string.Equals(Path.GetFileName(root), GltfPipelineFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(root), LegacyGltfPipelineFolderName, StringComparison.OrdinalIgnoreCase))
        {
            root = Path.GetDirectoryName(root)
                ?? throw new InvalidOperationException($"glTF pipeline root has no parent: {sourceOrPipelineRoot}");
        }

        return
        [
            root,
            Path.Combine(root, GltfPipelineFolderName),
            Path.Combine(root, LegacyGltfPipelineFolderName),
        ];
    }

    public static string? ResolveResource(ProjectManifest manifest, string resourcePath) =>
        ResolveResource(GetOrderedRoots(manifest), resourcePath);

    public static string? ResolveResource(string sourceOrPipelineRoot, string resourcePath) =>
        ResolveResource(GetOrderedRoots(sourceOrPipelineRoot), resourcePath);

    private static string? ResolveResource(IReadOnlyList<string> roots, string resourcePath)
    {
        var relative = resourcePath.Replace('/', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var candidate = SafePath.ResolveUnderRoot(root, relative, "Extracted source resource");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static string SelectOwningRoot(ProjectManifest manifest, string path)
    {
        var fullPath = Path.GetFullPath(path);
        foreach (var root in GetOrderedRoots(manifest).Skip(1))
        {
            if (IsPathUnderRoot(root, fullPath))
            {
                return Path.GetFullPath(root);
            }
        }

        return Path.GetFullPath(GetOrderedRoots(manifest)[0]);
    }

    public static IEnumerable<string> EnumerateFilesByPriority(ProjectManifest manifest, string pattern)
    {
        var seenResources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = GetOrderedRoots(manifest);
        for (var index = 0; index < roots.Count; index++)
        {
            var root = roots[index];
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                         .OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            {
                if (index == 0 && IsUnderPipelineFolder(root, path))
                {
                    continue;
                }

                var resource = Path.GetRelativePath(root, path)
                    .Replace(Path.DirectorySeparatorChar, '/');
                if (seenResources.Add(resource))
                {
                    yield return path;
                }
            }
        }
    }

    public static bool IsInsideNestedPipeline(string sourceRoot, string path)
    {
        var relative = Path.GetRelativePath(sourceRoot, path);
        if (relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
        {
            return false;
        }

        var first = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
        return string.Equals(first, GltfPipelineFolderName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(first, LegacyGltfPipelineFolderName, StringComparison.OrdinalIgnoreCase);
    }


    private static bool IsUnderPipelineFolder(string sourceRoot, string path) =>
        IsPathUnderRoot(Path.Combine(sourceRoot, GltfPipelineFolderName), path)
        || IsPathUnderRoot(Path.Combine(sourceRoot, LegacyGltfPipelineFolderName), path);

    private static bool IsPathUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}
