using System.Security.Cryptography;
using System.Text;
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

    private static readonly Regex CompiledTexturesHeaderRegex = new(
        "^[ \\t]*\\\"?Compiled Textures\\\"?[ \\t]*(?:=[ \\t]*)?(?=\\{|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTextureEntryRegex = new(
        "^[ \\t]*\\\"?(?<key>[A-Za-z_$][A-Za-z0-9_$]*)\\\"?[ \\t]*(?:=[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ActiveTextureEntryRegex = new(
        "^(?<prefix>[ \\t]*\\\"?(?<key>(?:Texture|g_t)[A-Za-z0-9_$]*)\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<path>[^\\\"\\r\\n]+)(?<suffix>\\\")",
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
                var preparedBytes = PrepareEditableSourceBytes(source);

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

    private static byte[] PrepareEditableSourceBytes(EditableSource source)
    {
        var bytes = File.ReadAllBytes(source.Path);
        if (!source.IsMaterial)
        {
            return bytes;
        }

        var sourceText = Encoding.UTF8.GetString(bytes);
        var preparedText = RestoreRetailCompiledTextureReferences(sourceText);
        return string.Equals(sourceText, preparedText, StringComparison.Ordinal)
            ? bytes
            : Encoding.UTF8.GetBytes(preparedText);
    }

    internal static string RestoreRetailCompiledTextureReferences(string materialText)
    {
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

        var editablePart = materialText[..header.Index];
        var rewrittenEditablePart = ActiveTextureEntryRegex.Replace(editablePart, match =>
        {
            var key = match.Groups["key"].Value;
            var activePath = match.Groups["path"].Value;

            string? retailPath = null;
            if (!compiledByKey.TryGetValue(key, out retailPath))
            {
                compiledByStem.TryGetValue(GetResourceStemPath(activePath), out retailPath);
            }

            if (string.IsNullOrWhiteSpace(retailPath))
            {
                return match.Value;
            }

            return match.Groups["prefix"].Value
                   + retailPath
                   + match.Groups["suffix"].Value;
        });

        return rewrittenEditablePart + materialText[header.Index..];
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

    private static string GetResourceStemPath(string path)
    {
        var normalized = path.Replace('\\', '/').Trim();
        var slash = normalized.LastIndexOf('/');
        var dot = normalized.LastIndexOf('.');
        return dot > slash ? normalized[..dot] : normalized;
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
