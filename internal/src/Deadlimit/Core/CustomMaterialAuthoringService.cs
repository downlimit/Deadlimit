using System.Text;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Deadlimit.Core;

public sealed record CustomMaterialAuthoringResult(
    IReadOnlyList<VmdlMaterialRemap> Remaps,
    int CustomMaterialCount,
    int CreatedVmatCount,
    int PreservedVmatCount,
    int TextureSourceCount,
    string MaterialContentFolder,
    IReadOnlyList<string> VmatResourcePaths);

public sealed class CustomMaterialAuthoringService
{
    private readonly DeadlimitPaths _paths;

    public CustomMaterialAuthoringService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public CustomMaterialAuthoringResult Prepare(
        ProjectManifest manifest,
        string addonName,
        string addonContentRoot,
        IReadOnlyList<string> dmxMaterialReferences,
        IReadOnlyCollection<string> resolvedMaterialSources,
        string? templateMaterialResource,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var resolved = resolvedMaterialSources
            .Select(NormalizeResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var customReferences = dmxMaterialReferences
            .Select(NormalizeResourcePath)
            .Where(IsWallWormCustomMaterialReference)
            .Where(reference => !resolved.Contains(reference))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var materialResourceFolder = $"materials/{addonName}";
        var materialContentFolder = Path.Combine(
            addonContentRoot,
            materialResourceFolder.Replace('/', Path.DirectorySeparatorChar));

        if (customReferences.Length == 0)
        {
            log.AppendLine("Custom materials: no unresolved Wall Worm custom material references were detected.");
            return new CustomMaterialAuthoringResult(
                Array.Empty<VmdlMaterialRemap>(),
                0,
                0,
                0,
                0,
                materialContentFolder,
                Array.Empty<string>());
        }

        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(materialContentFolder);

        var textureFolder = Path.Combine(materialContentFolder, "textures");
        Directory.CreateDirectory(textureFolder);

        var rootPngFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var sourcePng in rootPngFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePng, Path.Combine(textureFolder, Path.GetFileName(sourcePng)), overwrite: true);
        }

        log.AppendLine($"Custom materials detected: {customReferences.Length}");
        log.AppendLine($"Custom texture sources refreshed from project root: {rootPngFiles.Length}");
        log.AppendLine($"Custom texture source folder: {textureFolder}");

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaps = new List<VmdlMaterialRemap>();
        var vmatResources = new List<string>();
        var created = 0;
        var preserved = 0;

        foreach (var customReference in customReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var materialName = MakeResourceToken(GetResourceLeaf(customReference));
            if (materialName.Length == 0)
            {
                materialName = "custom_material";
            }

            var targetResource = AllocateTargetResource(materialResourceFolder, materialName, usedTargets);
            var targetPath = Path.Combine(
                addonContentRoot,
                targetResource.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            if (File.Exists(targetPath))
            {
                preserved++;
                log.AppendLine($"Custom VMAT preserved: {customReference} -> {targetResource}");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(templateMaterialResource))
                {
                    throw new InvalidOperationException(
                        $"Custom material '{customReference}' needs a new VMAT, but Deadlimit could not infer one unique retail body/skin/head/face material to use as a safe character-material template.");
                }

                var sourceVpk = DecompileRetailMaterialTemplate(
                    manifest,
                    templateMaterialResource,
                    targetPath,
                    cancellationToken);

                created++;
                log.AppendLine(
                    $"Custom VMAT created from retail character-material template: {customReference} -> {targetResource} | template {templateMaterialResource} | VPK {sourceVpk}");
            }

            remaps.Add(new VmdlMaterialRemap(customReference, targetResource));
            vmatResources.Add(targetResource);
        }

        log.AppendLine("Custom VMAT policy: create only when missing; never overwrite an existing addon-owned VMAT during PREPARE FOR CSDK.");
        log.AppendLine("Custom texture policy: project-root PNG files are artist-owned source inputs and are refreshed into the addon texture-source folder; authored VMAT files remain authoritative for shader/slot assignment.");

        return new CustomMaterialAuthoringResult(
            remaps,
            customReferences.Length,
            created,
            preserved,
            rootPngFiles.Length,
            materialContentFolder,
            vmatResources);
    }

    private string DecompileRetailMaterialTemplate(
        ProjectManifest manifest,
        string templateMaterialResource,
        string destinationVmatPath,
        CancellationToken cancellationToken)
    {
        var compiledResourcePaths = ToCompiledMaterialResourcePaths(templateMaterialResource)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var vpkCandidates = EnumerateRetailVpks(manifest).ToArray();

        foreach (var vpkPath in vpkCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var package = new Package();
            package.Read(vpkPath);
            var packageEntries = package.Entries;
            if (packageEntries is null)
            {
                continue;
            }

            foreach (var entry in packageEntries.SelectMany(group => group.Value))
            {
                var entryPath = NormalizeResourcePath(entry.GetFullPath());
                if (!compiledResourcePaths.Contains(entryPath))
                {
                    continue;
                }

                package.ReadEntry(entry, out byte[] rawData);
                using var fileLoader = new GameFileLoader(package, package.FileName);
                using var stream = new MemoryStream(rawData, writable: false);
                using var resource = new Resource { FileName = entryPath };
                resource.Read(stream);
                using var contentFile = FileExtract.Extract(resource, fileLoader, null);

                if (contentFile.Data is null || contentFile.Data.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"ValveResourceFormat found retail material '{entryPath}', but decompilation produced no VMAT source data.");
                }

                File.WriteAllBytes(destinationVmatPath, contentFile.Data.ToArray());
                return vpkPath;
            }
        }

        throw new InvalidOperationException(
            $"Could not find retail material template '{templateMaterialResource}' in the configured Deadlock VPKs. " +
            $"Tried: {string.Join(", ", compiledResourcePaths)}. " +
            "Run EXTRACT HERO SOURCE against the current retail build and verify the Project8Staging path in SETTINGS.");
    }

    private IEnumerable<string> EnumerateRetailVpks(ProjectManifest manifest)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(manifest.RetailSourceVpk)
            && File.Exists(manifest.RetailSourceVpk)
            && seen.Add(manifest.RetailSourceVpk))
        {
            yield return manifest.RetailSourceVpk;
        }

        var retailGameRoot = Path.Combine(_paths.RetailDeadlockRoot, "game");
        if (!Directory.Exists(retailGameRoot))
        {
            yield break;
        }

        foreach (var vpkPath in Directory.EnumerateFiles(retailGameRoot, "*_dir.vpk", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(vpkPath))
            {
                yield return vpkPath;
            }
        }
    }

    private static bool IsWallWormCustomMaterialReference(string reference)
    {
        var normalized = NormalizeResourcePath(reference);
        if (!normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalized.StartsWith("materials/dev/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.IsNullOrEmpty(Path.GetExtension(normalized));
    }

    private static string AllocateTargetResource(
        string materialResourceFolder,
        string baseName,
        HashSet<string> usedTargets)
    {
        for (var suffix = 1; ; suffix++)
        {
            var name = suffix == 1 ? baseName : $"{baseName}_{suffix}";
            var candidate = $"{materialResourceFolder}/{name}.vmat";
            if (usedTargets.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static IReadOnlyList<string> ToCompiledMaterialResourcePaths(string vmatResourcePath)
    {
        var normalized = NormalizeResourcePath(vmatResourcePath);
        if (!normalized.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".vmat";
        }

        var candidates = new List<string>();
        if (normalized.StartsWith("materials/", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(normalized + "_c");
            candidates.Add(normalized["materials/".Length..] + "_c");
        }
        else
        {
            candidates.Add($"materials/{normalized}_c");
            candidates.Add(normalized + "_c");
        }

        return candidates
            .Select(NormalizeResourcePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetResourceLeaf(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string MakeResourceToken(string value)
    {
        var sb = new StringBuilder();
        var previousUnderscore = false;

        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                previousUnderscore = true;
            }
        }

        return sb.ToString().Trim('_');
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
