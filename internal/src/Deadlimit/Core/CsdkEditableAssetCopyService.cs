using System.Security.Cryptography;

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
        var selected = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Select(path => new EditableSource(
                path,
                NormalizeRelativePath(Path.GetRelativePath(sourceRoot, path)),
                IsMaterial(path),
                IsAbilityFx(path)))
            .Where(item => (copyMaterials && item.IsMaterial)
                           || (copyAbilityFx && item.IsAbilityFx))
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

                if (File.Exists(target) && FilesEqual(source.Path, target))
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
                    File.Copy(source.Path, tempTarget, overwrite: true);
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

    private static bool IsMaterial(string path) =>
        string.Equals(Path.GetExtension(path), ".vmat", StringComparison.OrdinalIgnoreCase);

    private static bool IsAbilityFx(string path) =>
        AbilityFxExtensions.Contains(Path.GetExtension(path));

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
        value.Replace('\\', '/').TrimStart('/');

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
