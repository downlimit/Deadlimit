using System.Security.Cryptography;
using SteamDatabase.ValvePak;
using ValveResourceFormat;

namespace Deadlimit.Core;

internal sealed record RetailResourcePackagingPlan(
    IReadOnlySet<string> IncludedRelativePaths,
    IReadOnlySet<string> ExcludedRelativePaths,
    int RetailResourceCount,
    int ProjectRootCount);

internal static class RetailResourcePackagingPolicy
{
    private static readonly HashSet<string> DirectCompileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vmdl", ".vmat", ".vtex", ".vpcf", ".vsndevts", ".wav", ".xml", ".css", ".js", ".vsvg",
    };

    internal static RetailResourcePackagingPlan Resolve(
        ProjectManifest manifest,
        string retailDeadlockRoot,
        string extractedSourceRoot,
        string addonContentRoot,
        string addonGameRoot,
        string compiledMainModel,
        CancellationToken cancellationToken)
    {
        var addonFiles = Directory.EnumerateFiles(addonGameRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => NormalizeRelativePath(Path.GetRelativePath(addonGameRoot, path)),
                path => path,
                StringComparer.OrdinalIgnoreCase);

        var retailResources = BuildRetailResourceIndex(
            retailDeadlockRoot,
            addonFiles.Keys,
            cancellationToken);
        var projectRoots = ResolveProjectRoots(
            manifest,
            extractedSourceRoot,
            addonContentRoot,
            addonGameRoot,
            addonFiles,
            compiledMainModel,
            cancellationToken,
            out var forcedMaterialDependencyOwners);

        var included = ResolveClosure(
            addonFiles,
            retailResources,
            projectRoots,
            forcedMaterialDependencyOwners,
            cancellationToken);
        var excluded = addonFiles.Keys
            .Where(path => !included.Contains(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new RetailResourcePackagingPlan(
            included,
            excluded,
            retailResources.Count,
            projectRoots.Count);
    }

    internal static IReadOnlySet<string> ResolveClosureForSmoke(
        IReadOnlyDictionary<string, string> addonFiles,
        IReadOnlySet<string> retailResources,
        IReadOnlySet<string> projectRoots,
        IReadOnlySet<string> forcedMaterialDependencyOwners,
        IReadOnlyDictionary<string, IReadOnlyList<string>> references)
    {
        return ResolveClosure(
            addonFiles,
            retailResources,
            projectRoots,
            forcedMaterialDependencyOwners,
            _ => references.TryGetValue(_, out var value) ? value : Array.Empty<string>(),
            CancellationToken.None);
    }

    private static HashSet<string> ResolveProjectRoots(
        ProjectManifest manifest,
        string extractedSourceRoot,
        string addonContentRoot,
        string addonGameRoot,
        IReadOnlyDictionary<string, string> addonFiles,
        string compiledMainModel,
        CancellationToken cancellationToken,
        out HashSet<string> forcedMaterialDependencyOwners)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var textureTargets = RetailTextureOverrideService.BuildTargetIndex(extractedSourceRoot);
        var textureOverridePaths = RetailTextureOverrideService.ResolveProjectRootOverrides(manifest, textureTargets)
            .Select(textureOverride => textureOverride.RetailTextureResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var textureOverrideMaterials = textureTargets
            .Where(target => textureOverridePaths.Contains(target.ResourcePath))
            .Select(target => target.ReferencingMaterialResourcePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        forcedMaterialDependencyOwners = textureOverrideMaterials
            .Select(GetCompiledRelativePath)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in Directory.EnumerateFiles(addonContentRoot, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!DirectCompileExtensions.Contains(Path.GetExtension(sourcePath)))
            {
                continue;
            }

            var sourceRelativePath = NormalizeRelativePath(Path.GetRelativePath(addonContentRoot, sourcePath));
            var compiledRelativePath = GetCompiledRelativePath(sourceRelativePath);
            if (compiledRelativePath is null || !addonFiles.ContainsKey(compiledRelativePath))
            {
                continue;
            }

            var extractedPath = TryResolve(extractedSourceRoot, sourceRelativePath);
            var projectOwned = extractedPath is null
                || !File.Exists(extractedPath)
                || !FilesEqual(extractedPath, sourcePath, fileHashes);

            projectOwned = projectOwned || textureOverrideMaterials.Contains(sourceRelativePath);

            if (projectOwned)
            {
                roots.Add(compiledRelativePath);
            }
        }

        if (IsPathUnderRoot(addonGameRoot, compiledMainModel))
        {
            var mainModelRelativePath = NormalizeRelativePath(
                Path.GetRelativePath(addonGameRoot, compiledMainModel));
            if (addonFiles.ContainsKey(mainModelRelativePath))
            {
                roots.Add(mainModelRelativePath);
            }
        }

        foreach (var path in addonFiles.Keys.Where(path => !path.EndsWith("_c", StringComparison.OrdinalIgnoreCase)))
        {
            roots.Add(path);
        }

        return roots;
    }

    private static HashSet<string> BuildRetailResourceIndex(
        string retailDeadlockRoot,
        IEnumerable<string> candidatePaths,
        CancellationToken cancellationToken)
    {
        var retailGameRoot = Path.Combine(retailDeadlockRoot, "game");
        var primaryVpk = Path.Combine(retailGameRoot, "citadel", "pak01_dir.vpk");
        if (!File.Exists(primaryVpk))
        {
            throw new FileNotFoundException(
                "Retail Deadlock pak01_dir.vpk is required to resolve reusable game resources.",
                primaryVpk);
        }

        var vpkPaths = new List<string> { Path.GetFullPath(primaryVpk) };
        var coreVpk = Path.Combine(retailGameRoot, "core", "pak01_dir.vpk");
        if (File.Exists(coreVpk))
        {
            vpkPaths.Add(Path.GetFullPath(coreVpk));
        }

        var candidates = candidatePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var vpkPath in vpkPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var package = new Package();
            package.Read(vpkPath);
            var entries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
            foreach (var entry in entries.SelectMany(group => group.Value))
            {
                var candidatePath = entry.GetFullPath().Replace('\\', '/').TrimStart('/');
                if (candidates.Contains(candidatePath))
                {
                    resources.Add(NormalizeRelativePath(candidatePath));
                }
            }
        }

        return resources;
    }

    private static HashSet<string> ResolveClosure(
        IReadOnlyDictionary<string, string> addonFiles,
        IReadOnlySet<string> retailResources,
        IReadOnlySet<string> projectRoots,
        IReadOnlySet<string> forcedMaterialDependencyOwners,
        CancellationToken cancellationToken)
    {
        return ResolveClosure(
            addonFiles,
            retailResources,
            projectRoots,
            forcedMaterialDependencyOwners,
            relativePath => ReadExternalReferences(addonFiles[relativePath], relativePath),
            cancellationToken);
    }

    private static HashSet<string> ResolveClosure(
        IReadOnlyDictionary<string, string> addonFiles,
        IReadOnlySet<string> retailResources,
        IReadOnlySet<string> projectRoots,
        IReadOnlySet<string> forcedMaterialDependencyOwners,
        Func<string, IReadOnlyList<string>> readReferences,
        CancellationToken cancellationToken)
    {
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Queue<string>(projectRoots
            .Where(addonFiles.ContainsKey)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Dequeue();
            if (!included.Add(current))
            {
                continue;
            }

            var retainMaterialDependencies = forcedMaterialDependencyOwners.Contains(current);
            foreach (var reference in readReferences(current))
            {
                var dependency = ToCompiledResourcePath(reference);
                if (!addonFiles.ContainsKey(dependency) || included.Contains(dependency))
                {
                    continue;
                }

                if (retainMaterialDependencies || !retailResources.Contains(dependency))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        return included;
    }

    private static IReadOnlyList<string> ReadExternalReferences(string filePath, string resourcePath)
    {
        if (resourcePath.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var stream = File.OpenRead(filePath);
            using var resource = new Resource { FileName = resourcePath };
            resource.Read(stream);
            return resource.ExternalReferences?.ResourceRefInfoList
                .Select(reference => NormalizeRelativePath(reference.Name))
                .Where(reference => reference.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
                ?? Array.Empty<string>();
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }

    private static string ToCompiledResourcePath(string resourcePath)
    {
        var normalized = NormalizeRelativePath(resourcePath);
        return normalized.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + "_c";
    }

    private static string? GetCompiledRelativePath(string sourceRelativePath)
    {
        var extension = Path.GetExtension(sourceRelativePath);
        var compiledExtension = extension.ToLowerInvariant() switch
        {
            ".vmdl" => ".vmdl_c",
            ".vmat" => ".vmat_c",
            ".vtex" => ".vtex_c",
            ".vpcf" => ".vpcf_c",
            ".vsndevts" => ".vsndevts_c",
            ".wav" => ".vsnd_c",
            ".xml" => ".vxml_c",
            ".css" => ".vcss_c",
            ".js" => ".vjs_c",
            ".vsvg" => ".vsvg_c",
            _ => null,
        };
        return compiledExtension is null
            ? null
            : NormalizeRelativePath(Path.ChangeExtension(sourceRelativePath, compiledExtension));
    }

    private static string? TryResolve(string root, string relativePath)
    {
        try
        {
            return SafePath.ResolveUnderRoot(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                "Retail resource packaging path");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool FilesEqual(
        string left,
        string right,
        IDictionary<string, string> fileHashes)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        return string.Equals(
            GetFileHash(left, fileHashes),
            GetFileHash(right, fileHashes),
            StringComparison.Ordinal);
    }

    private static string GetFileHash(string path, IDictionary<string, string> fileHashes)
    {
        if (fileHashes.TryGetValue(path, out var hash))
        {
            return hash;
        }

        using var stream = File.OpenRead(path);
        hash = Convert.ToHexString(SHA256.HashData(stream));
        fileHashes[path] = hash;
        return hash;
    }

    private static bool IsPathUnderRoot(string root, string path)
    {
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRelativePath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "Retail resource packaging path");
}
