using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record CsdkEditableAssetCopyResult(
    int MaterialCopiedCount,
    int AbilityFxCopiedCount,
    int OverwrittenCount,
    string? BackupFolder);

internal sealed class CsdkEditableAssetCopyService
{
    private const string BackupFolderName = "fx_and_mat_bckps";

    private static readonly HashSet<string> AbilityFxExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vpcf",
        ".vsnap",
    };

    private static readonly HashSet<string> AuthoringTextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr", ".vtex",
    };

    private static readonly Regex VmatTextureSourceRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_$]*\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private readonly DeadlimitPaths _paths;

    public CsdkEditableAssetCopyService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public CsdkEditableAssetCopyResult Copy(
        ProjectManifest manifest,
        string sourceRoot,
        HeroExtractionOptions options,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!options.CopyMaterialsToCsdkForEditing && !options.CopyAbilityFxToCsdkForEditing)
        {
            return new CsdkEditableAssetCopyResult(0, 0, 0, null);
        }

        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(sourceRoot);
        }

        if (!Directory.Exists(_paths.CsdkContentRoot))
        {
            throw new DirectoryNotFoundException(
                $"CSDK content root was not found: {_paths.CsdkContentRoot}");
        }

        var addon = new AddonIdentityService(_paths).ResolveAndClaim(manifest);
        var backupParent = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            BackupFolderName);

        progress?.Report(new HeroExtractionProgress(
            "Copying selected materials and ability FX into the CSDK addon for editing..."));

        return CopySelectedFiles(
            sourceRoot,
            addon.ContentRoot,
            backupParent,
            options.CopyMaterialsToCsdkForEditing,
            options.CopyAbilityFxToCsdkForEditing,
            options.BackupCsdkOverwrites,
            cancellationToken);
    }

    internal static CsdkEditableAssetCopyResult CopySelectedFiles(
        string sourceRoot,
        string addonContentRoot,
        string backupParent,
        bool copyMaterials,
        bool copyAbilityFx,
        bool backupOverwrites,
        CancellationToken cancellationToken)
    {
        var selectedByRelativePath = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => new EditableSource(
                path,
                NormalizeRelativePath(Path.GetRelativePath(sourceRoot, path)),
                IsMaterial(path),
                IsAbilityFx(path)))
            .Where(item => (copyMaterials && item.IsMaterial)
                           || (copyAbilityFx && item.IsAbilityFx))
            .ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);

        if (copyMaterials)
        {
            foreach (var material in selectedByRelativePath.Values.Where(item => item.IsMaterial).ToArray())
            {
                foreach (var dependency in EnumerateMaterialTextureDependencies(material.Path, sourceRoot))
                {
                    selectedByRelativePath.TryAdd(dependency.RelativePath, dependency);
                }
            }
        }

        var selected = selectedByRelativePath.Values
            .OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (selected.Length == 0)
        {
            return new CsdkEditableAssetCopyResult(0, 0, 0, null);
        }

        Directory.CreateDirectory(addonContentRoot);

        var rollbackRoot = Path.Combine(
            Path.GetDirectoryName(backupParent)!,
            $"csdk-edit-copy-rollback-{Guid.NewGuid():N}");
        var createdTargets = new List<string>();
        var replacedTargets = new List<(string Target, string Rollback)>();
        string? backupRoot = null;
        var materialCount = 0;
        var abilityFxCount = 0;
        var overwrittenCount = 0;

        try
        {
            foreach (var source in selected)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = SafePath.ResolveUnderRoot(
                    addonContentRoot,
                    source.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                    "CSDK editable asset target");
                var preparedBytes = File.ReadAllBytes(source.Path);

                if (File.Exists(target) && FilesEqual(preparedBytes, target))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                if (File.Exists(target))
                {
                    overwrittenCount++;

                    var rollback = SafePath.ResolveUnderRoot(
                        rollbackRoot,
                        source.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                        "CSDK editable asset rollback");
                    Directory.CreateDirectory(Path.GetDirectoryName(rollback)!);
                    File.Copy(target, rollback, overwrite: true);
                    replacedTargets.Add((target, rollback));

                    if (backupOverwrites)
                    {
                        backupRoot ??= CreateUniqueBackupRoot(backupParent);
                        var backup = SafePath.ResolveUnderRoot(
                            backupRoot,
                            source.RelativePath.Replace('/', Path.DirectorySeparatorChar),
                            "CSDK editable asset backup");
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(target, backup, overwrite: true);
                    }
                }
                else
                {
                    createdTargets.Add(target);
                }

                var tempTarget = target + $".deadlimit-copy-{Guid.NewGuid():N}.tmp";
                try
                {
                    File.WriteAllBytes(tempTarget, preparedBytes);
                    File.Move(tempTarget, target, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempTarget))
                    {
                        File.Delete(tempTarget);
                    }
                }

                if (source.IsMaterial)
                {
                    materialCount++;
                }
                if (source.IsAbilityFx)
                {
                    abilityFxCount++;
                }
            }

            DeleteDirectoryIfExists(rollbackRoot);
            return new CsdkEditableAssetCopyResult(
                materialCount,
                abilityFxCount,
                overwrittenCount,
                backupRoot);
        }
        catch
        {
            foreach (var target in createdTargets.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch
                {
                    // Preserve the original exception. A later PREPARE can clean a stray new file.
                }
            }

            foreach (var (target, rollback) in replacedTargets.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(rollback))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                        File.Copy(rollback, target, overwrite: true);
                    }
                }
                catch
                {
                    // Preserve the original exception; the persistent user backup is kept when requested.
                }
            }

            DeleteDirectoryIfExists(rollbackRoot);
            throw;
        }
    }

    private static IEnumerable<EditableSource> EnumerateMaterialTextureDependencies(
        string materialPath,
        string sourceRoot)
    {
        var text = File.ReadAllText(materialPath);
        var compiledTexturesIndex = text.IndexOf("Compiled Textures", StringComparison.OrdinalIgnoreCase);
        var authoringText = compiledTexturesIndex >= 0 ? text[..compiledTexturesIndex] : text;

        foreach (Match match in VmatTextureSourceRegex.Matches(authoringText))
        {
            var resourcePath = NormalizeRelativePath(match.Groups["path"].Value.Trim().Trim('"'));
            if (resourcePath.Length == 0
                || resourcePath.Contains(':', StringComparison.Ordinal)
                || !AuthoringTextureExtensions.Contains(Path.GetExtension(resourcePath)))
            {
                continue;
            }

            string sourcePath;
            try
            {
                sourcePath = SafePath.ResolveUnderRoot(
                    sourceRoot,
                    resourcePath.Replace('/', Path.DirectorySeparatorChar),
                    "CSDK material authoring texture dependency");
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
            {
                continue;
            }

            if (!File.Exists(sourcePath))
            {
                continue;
            }

            yield return new EditableSource(
                sourcePath,
                resourcePath,
                IsMaterial: false,
                IsAbilityFx: false);
        }
    }

    private static bool IsMaterial(string path) =>
        string.Equals(Path.GetExtension(path), ".vmat", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbilityFx(string path) =>
        AbilityFxExtensions.Contains(Path.GetExtension(path));

    private static bool FilesEqual(ReadOnlySpan<byte> expected, string actualPath)
    {
        var actualInfo = new FileInfo(actualPath);
        if (actualInfo.Length != expected.Length)
        {
            return false;
        }

        using var actualStream = File.OpenRead(actualPath);
        return SHA256.HashData(expected).AsSpan().SequenceEqual(SHA256.HashData(actualStream));
    }

    private static string CreateUniqueBackupRoot(string backupParent)
    {
        Directory.CreateDirectory(backupParent);
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");

        for (var suffix = 0; ; suffix++)
        {
            var folderName = suffix == 0 ? timestamp : $"{timestamp}_{suffix + 1}";
            var candidate = Path.Combine(backupParent, folderName);
            if (Directory.Exists(candidate))
            {
                continue;
            }

            Directory.CreateDirectory(candidate);
            return candidate;
        }
    }

    private static string NormalizeRelativePath(string value) =>
        value.Replace('\\', '/').Trim().TrimStart('/');

    private static void DeleteDirectoryIfExists(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
            // Rollback storage is temporary and outside the CSDK content tree. Leaving it behind is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Rollback storage is temporary and outside the CSDK content tree. Leaving it behind is harmless.
        }
    }

    private sealed record EditableSource(
        string Path,
        string RelativePath,
        bool IsMaterial,
        bool IsAbilityFx);
}
