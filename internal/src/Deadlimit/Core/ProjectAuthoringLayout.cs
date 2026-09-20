namespace Deadlimit.Core;

public static class ProjectAuthoringLayout
{
    public const string AuthoringFolderName = "1authoring";

    public static readonly string[] ArtistFolderNames =
    [
        "0source",
        AuthoringFolderName,
        "2concept",
        "3scene",
        "4texture",
        "5promo",
        "6temp",
    ];

    public static string GetAuthoringRoot(ProjectManifest manifest) =>
        Path.Combine(manifest.ProjectFolder, AuthoringFolderName);

    public static void EnsureStructure(string projectFolder)
    {
        foreach (var folderName in ArtistFolderNames)
        {
            Directory.CreateDirectory(Path.Combine(projectFolder, folderName));
        }
    }

    public static IEnumerable<string> EnumerateAuthoringFiles(ProjectManifest manifest)
    {
        var authoringRoot = GetAuthoringRoot(manifest);
        if (!Directory.Exists(authoringRoot))
        {
            return [];
        }

        return Directory.EnumerateFiles(authoringRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => GetAuthoringIdentity(manifest, path), StringComparer.OrdinalIgnoreCase);
    }

    public static string GetAuthoringIdentity(ProjectManifest manifest, string path)
    {
        var authoringRoot = GetAuthoringRoot(manifest);
        var fullRoot = Path.GetFullPath(authoringRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return Normalize(Path.Combine(
                AuthoringFolderName,
                Path.GetRelativePath(authoringRoot, path)));
        }

        return Normalize(Path.GetRelativePath(manifest.ProjectFolder, path));
    }

    public static IReadOnlyList<string> SelectPreferredFiles(
        ProjectManifest manifest,
        IEnumerable<string> files)
    {
        return files
            .GroupBy(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(path => GetExtensionPriority(Path.GetExtension(path)))
                .ThenBy(path => GetAuthoringIdentity(manifest, path), StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(path => GetAuthoringIdentity(manifest, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> SelectFirstByFileName(
        ProjectManifest manifest,
        IEnumerable<string> files)
    {
        var selected = new List<string>();
        foreach (var group in files
                     .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                     .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
        {
            var candidates = group
                .OrderBy(path => GetAuthoringIdentity(manifest, path), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (candidates.Length > 1)
            {
                var identities = string.Join(
                    Environment.NewLine,
                    candidates.Select(path => $"  - {GetAuthoringIdentity(manifest, path)}"));
                throw new InvalidOperationException(
                    $"More than one authoring model uses the filename '{group.Key}'. " +
                    "Subfolders do not disambiguate model routing. Keep one file with this name before PREPARE." +
                    Environment.NewLine + identities);
            }

            selected.Add(candidates[0]);
        }

        return selected
            .OrderBy(path => GetAuthoringIdentity(manifest, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static int GetExtensionPriority(string extension) => extension.ToLowerInvariant() switch
    {
        ".tga" => 0,
        ".png" => 1,
        ".psd" => 2,
        _ => 10,
    };

    private static string Normalize(string path) => path.Replace('\\', '/');
}
