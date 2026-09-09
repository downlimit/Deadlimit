namespace Deadlimit.Core;

internal sealed record ExtractedSourceAssetMatch(
    string SourcePath,
    string ResourcePath);

internal sealed record ExtractedDmxTarget(
    string ResourcePath,
    IReadOnlyList<string> OwnerVmdlSourcePaths);

internal static class ExtractedSourceAssetResolver
{
    internal const string SupportingVmdlMarker = "// DEADLIMIT_SUPPORTING_SOURCE_OVERLAY";

    internal static string? TryResolveSourceRootForArtistPath(string artistPath)
    {
        var projectFolder = Path.GetDirectoryName(Path.GetFullPath(artistPath));
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            return null;
        }

        var manifest = ProjectStore.TryLoad(projectFolder);
        if (manifest is null)
        {
            return null;
        }

        var sourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");
        return Directory.Exists(sourceRoot) ? sourceRoot : null;
    }

    internal static ExtractedSourceAssetMatch? ResolveUniqueByFileName(
        string artistPath,
        string? extractedSourceRoot = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artistPath);
        var sourceRoot = extractedSourceRoot ?? TryResolveSourceRootForArtistPath(artistPath);
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        var fileName = Path.GetFileName(artistPath);
        var matches = Directory.EnumerateFiles(sourceRoot, fileName, SearchOption.AllDirectories)
            .Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
            .Select(path => new ExtractedSourceAssetMatch(
                Path.GetFullPath(path),
                NormalizeResourcePath(Path.GetRelativePath(sourceRoot, path))))
            .OrderBy(match => match.ResourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (matches.Length == 0)
        {
            return null;
        }

        if (matches.Length > 1)
        {
            var candidates = string.Join(
                Environment.NewLine,
                matches.Select(match => $"  - {match.ResourcePath}"));
            throw new InvalidOperationException(
                $"Project-root asset '{fileName}' matches more than one extracted retail source. " +
                "Deadlimit will not guess which resource to replace." + Environment.NewLine + candidates);
        }

        return matches[0];
    }

    internal static ExtractedDmxTarget? ResolveDmxTarget(
        string artistDmxPath,
        string? extractedSourceRoot = null)
    {
        var sourceRoot = extractedSourceRoot ?? TryResolveSourceRootForArtistPath(artistDmxPath);
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return null;
        }

        sourceRoot = Path.GetFullPath(sourceRoot);
        var sourceMatch = ResolveUniqueByFileName(artistDmxPath, sourceRoot);
        if (sourceMatch is null)
        {
            return null;
        }

        var owners = FindOwningVmdls(sourceRoot, sourceMatch.ResourcePath);
        if (owners.Count == 0)
        {
            throw new InvalidOperationException(
                $"Extracted source '{sourceMatch.ResourcePath}' exists, but no extracted VMDL references it as a RenderMeshFile. " +
                "Deadlimit will not stage an orphan DMX that cannot be compiled into a model.");
        }

        return new ExtractedDmxTarget(sourceMatch.ResourcePath, owners);
    }

    internal static IReadOnlyList<string> FindOwningVmdls(
        string extractedSourceRoot,
        string dmxResourcePath)
    {
        var sourceRoot = Path.GetFullPath(extractedSourceRoot);
        var normalizedTarget = NormalizeResourcePath(dmxResourcePath);
        var owners = new List<string>();

        foreach (var vmdlPath in Directory.EnumerateFiles(sourceRoot, "*.vmdl", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            IReadOnlyList<RetailRenderMeshEntry> renderMeshes;
            try
            {
                renderMeshes = RetailVmdlInheritance.ReadRenderMeshes(vmdlPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                continue;
            }

            var ownerResourcePath = NormalizeResourcePath(Path.GetRelativePath(sourceRoot, vmdlPath));
            var ownerDirectory = GetResourceDirectory(ownerResourcePath);
            if (renderMeshes.Any(entry => RenderMeshPathMatches(
                    ownerDirectory,
                    entry.Filename,
                    normalizedTarget)))
            {
                owners.Add(Path.GetFullPath(vmdlPath));
            }
        }

        return owners;
    }

    internal static IReadOnlyList<string> StageOwningVmdlSourceTrees(
        string extractedSourceRoot,
        string addonContentRoot,
        IEnumerable<string> ownerVmdlSourcePaths,
        string mainDestinationVmdlPath)
    {
        var sourceRoot = Path.GetFullPath(extractedSourceRoot);
        var mainDestination = Path.GetFullPath(mainDestinationVmdlPath);
        var stagedOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var copiedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ownerSourcePath in ownerVmdlSourcePaths)
        {
            var fullOwnerSourcePath = SafePath.EnsureUnderRoot(
                sourceRoot,
                ownerSourcePath,
                "Supporting VMDL source");
            var sourceFolder = Path.GetDirectoryName(fullOwnerSourcePath)
                ?? throw new InvalidOperationException($"Supporting VMDL has no parent folder: {fullOwnerSourcePath}");

            if (copiedFolders.Add(sourceFolder))
            {
                foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
                {
                    var resourcePath = NormalizeResourcePath(Path.GetRelativePath(sourceRoot, sourceFile));
                    var destination = SafePath.ResolveUnderRoot(
                        addonContentRoot,
                        resourcePath.Replace('/', Path.DirectorySeparatorChar),
                        "Supporting retail source destination");
                    if (File.Exists(destination))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourceFile, destination, overwrite: false);
                }
            }

            var ownerResourcePath = NormalizeResourcePath(Path.GetRelativePath(sourceRoot, fullOwnerSourcePath));
            var destinationOwner = SafePath.ResolveUnderRoot(
                addonContentRoot,
                ownerResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Supporting VMDL destination");
            if (!File.Exists(destinationOwner))
            {
                throw new InvalidOperationException(
                    $"Supporting VMDL source staging did not produce '{ownerResourcePath}'.");
            }

            var fullDestinationOwner = Path.GetFullPath(destinationOwner);
            if (!string.Equals(fullDestinationOwner, mainDestination, StringComparison.OrdinalIgnoreCase))
            {
                MarkSupportingVmdl(fullDestinationOwner);
            }

            stagedOwners.Add(fullDestinationOwner);
        }

        return stagedOwners
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void MarkSupportingVmdl(string vmdlPath)
    {
        var text = File.ReadAllText(vmdlPath);
        if (text.StartsWith(SupportingVmdlMarker, StringComparison.Ordinal))
        {
            return;
        }

        File.WriteAllText(vmdlPath, SupportingVmdlMarker + Environment.NewLine + text);
    }

    private static bool RenderMeshPathMatches(
        string ownerDirectory,
        string renderMeshPath,
        string targetResourcePath)
    {
        var normalizedRenderMesh = NormalizeResourcePath(renderMeshPath);
        if (string.Equals(normalizedRenderMesh, targetResourcePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var ownerRelative = ownerDirectory.Length == 0
            ? normalizedRenderMesh
            : NormalizeResourcePath(ownerDirectory + "/" + normalizedRenderMesh);
        return string.Equals(ownerRelative, targetResourcePath, StringComparison.OrdinalIgnoreCase);
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
