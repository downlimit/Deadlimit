using System.Text;
using System.Text.RegularExpressions;
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
    private const string GeneratedMarker = "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V3";
    private const string DefaultColor = "materials/default/default_color.tga";
    private const string DefaultNormal = "materials/default/default_normal.tga";
    private const string DefaultRoughness = "materials/default/default_rough.tga";
    private const string DefaultAo = "materials/default/default_ao.tga";
    private const string DefaultBlackMask = "materials/default/default_black_mask.tga";

    private static readonly TextureSlotDefinition[] TextureSlots =
    [
        new("TextureColor", DefaultColor, ["basecolor", "base_color", "diffuse", "albedo", "color"]),
        new("TextureNormal", DefaultNormal, ["normal", "norm"]),
        new("TextureRoughness", DefaultRoughness, ["roughness", "rough"]),
        new("TextureAmbientOcclusion", DefaultAo, ["ambientocclusion", "ambient_occlusion", "occlusion", "ao"]),
        new("TextureMetalness", DefaultBlackMask, ["metalness", "metallic", "metal"]),
    ];

    private static readonly Regex TextureAssignmentRegex = new(
        "^(?<indent>\\s*)(?<key>Texture[A-Za-z0-9_]+)\\s+\\\"(?<value>[^\\\"]+)\\\"(?<tail>\\s*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

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

        if (string.IsNullOrWhiteSpace(templateMaterialResource))
        {
            throw new InvalidOperationException(
                "Custom material creation needs one unique retail body/skin/head/face material to inherit shader and non-texture character settings from, but no safe template could be inferred.");
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

        var textureCandidates = rootPngFiles
            .Select(path => ParseTextureCandidate(path, materialResourceFolder))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();

        log.AppendLine($"Custom materials detected: {customReferences.Length}");
        log.AppendLine($"Custom texture sources refreshed from project root: {rootPngFiles.Length}");
        log.AppendLine($"Custom texture source folder: {textureFolder}");
        log.AppendLine("Custom texture naming: <material>_color|diffuse|basecolor|albedo, _normal, _rough|roughness, _ao|occlusion, _metal|metalness|metallic; specialty Texture* fields may also bind by matching the material prefix plus the Texture parameter semantic name.");

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaps = new List<VmdlMaterialRemap>();
        var vmatResources = new List<string>();
        var created = 0;
        var preserved = 0;
        var autoBoundTextures = 0;

        string? retailTemplateText = null;
        string? retailTemplateVpk = null;

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
                if (retailTemplateText is null)
                {
                    (retailTemplateText, retailTemplateVpk) = DecompileRetailMaterialTemplate(
                        manifest,
                        templateMaterialResource,
                        cancellationToken);
                }

                var standardBindings = ResolveTextureBindings(
                    customReference,
                    customReferences.Length,
                    textureCandidates,
                    log);

                var generated = BuildInheritedVmat(
                    retailTemplateText,
                    customReference,
                    customReferences.Length,
                    rootPngFiles,
                    materialResourceFolder,
                    standardBindings,
                    log,
                    out var boundCount,
                    out var sanitizedCount);

                File.WriteAllText(targetPath, generated);
                created++;
                autoBoundTextures += boundCount;

                log.AppendLine(
                    $"Custom VMAT created from retail character template with sanitized texture inputs: {customReference} -> {targetResource} | template {templateMaterialResource} | VPK {retailTemplateVpk}");
                log.AppendLine($"  inherited Texture* references sanitized/defaulted: {sanitizedCount}");
            }

            remaps.Add(new VmdlMaterialRemap(customReference, targetResource));
            vmatResources.Add(targetResource);
        }

        log.AppendLine($"Custom textures auto-bound during VMAT creation: {autoBoundTextures}");
        log.AppendLine("Custom VMAT policy: create only when missing; never overwrite an existing addon-owned VMAT during PREPARE FOR CSDK.");
        log.AppendLine("Custom VMAT scaffold policy: inherit the current hero character material so shader, outline/NPR colors, strengths, thicknesses and other non-texture tuning survive, but never inherit unresolved hero texture-source paths.");
        log.AppendLine("Custom texture policy: matching project PNGs replace inherited texture inputs automatically; standard missing PBR inputs use Source 2 defaults and all other missing Texture* effect/mask inputs fall back to a black mask so inherited effects cannot light up the entire custom surface by accident.");

        return new CustomMaterialAuthoringResult(
            remaps,
            customReferences.Length,
            created,
            preserved,
            rootPngFiles.Length,
            materialContentFolder,
            vmatResources);
    }

    private (string Text, string VpkPath) DecompileRetailMaterialTemplate(
        ProjectManifest manifest,
        string templateMaterialResource,
        CancellationToken cancellationToken)
    {
        var compiledResourcePaths = ToCompiledMaterialResourcePaths(templateMaterialResource)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var vpkPath in EnumerateRetailVpks(manifest))
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

                return (Encoding.UTF8.GetString(contentFile.Data.ToArray()), vpkPath);
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

    private static string BuildInheritedVmat(
        string retailTemplateText,
        string customReference,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        StringBuilder log,
        out int boundCount,
        out int sanitizedCount)
    {
        boundCount = 0;
        sanitizedCount = 0;

        var materialToken = NormalizeMatchToken(GetResourceLeaf(customReference));

        var patched = TextureAssignmentRegex.Replace(retailTemplateText, match =>
        {
            var key = match.Groups["key"].Value;
            var replacement = ResolveTextureReplacement(
                key,
                materialToken,
                customMaterialCount,
                rootPngFiles,
                materialResourceFolder,
                standardBindings,
                log);

            if (replacement.AutoBound)
            {
                boundCount++;
            }
            else
            {
                sanitizedCount++;
            }

            return $"{match.Groups["indent"].Value}{key} \"{replacement.ResourcePath}\"{match.Groups["tail"].Value}";
        });

        return $"{GeneratedMarker}{Environment.NewLine}" +
               "// Initial scaffold: non-texture values are inherited from the current retail character material; texture-source paths are rebound or neutralized by Deadlimit. After creation Material Editor owns this file and Deadlimit will not overwrite it." +
               Environment.NewLine + patched;
    }

    private static TextureReplacement ResolveTextureReplacement(
        string key,
        string materialToken,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        StringBuilder log)
    {
        if (standardBindings.TryGetValue(key, out var standard) && !string.IsNullOrWhiteSpace(standard))
        {
            return new TextureReplacement(standard, true);
        }

        var specialty = ResolveSpecialtyTextureBinding(
            key,
            materialToken,
            customMaterialCount,
            rootPngFiles,
            materialResourceFolder);

        if (specialty is not null)
        {
            log.AppendLine($"Custom specialty texture auto-bind {key} -> {specialty}");
            return new TextureReplacement(specialty, true);
        }

        return new TextureReplacement(GetTextureFallback(key), false);
    }

    private static string? ResolveSpecialtyTextureBinding(
        string key,
        string materialToken,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder)
    {
        var semantic = NormalizeMatchToken(key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
            ? key["Texture".Length..]
            : key);

        if (semantic.Length == 0)
        {
            return null;
        }

        var semanticVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { semantic };
        if (semantic.EndsWith("mask", StringComparison.OrdinalIgnoreCase) && semantic.Length > 4)
        {
            semanticVariants.Add(semantic[..^4]);
        }

        var exact = rootPngFiles
            .Where(path =>
            {
                var stemToken = NormalizeMatchToken(Path.GetFileNameWithoutExtension(path));
                return semanticVariants.Any(variant => string.Equals(
                    stemToken,
                    materialToken + variant,
                    StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        if (exact.Length == 1)
        {
            return NormalizeResourcePath($"{materialResourceFolder}/textures/{Path.GetFileName(exact[0])}");
        }

        if (exact.Length > 1 || customMaterialCount != 1)
        {
            return null;
        }

        var uniqueSemantic = rootPngFiles
            .Where(path =>
            {
                var stemToken = NormalizeMatchToken(Path.GetFileNameWithoutExtension(path));
                return semanticVariants.Any(variant => stemToken.EndsWith(variant, StringComparison.OrdinalIgnoreCase));
            })
            .ToArray();

        return uniqueSemantic.Length == 1
            ? NormalizeResourcePath($"{materialResourceFolder}/textures/{Path.GetFileName(uniqueSemantic[0])}")
            : null;
    }

    private static string GetTextureFallback(string key)
    {
        var known = TextureSlots.FirstOrDefault(slot => string.Equals(slot.Key, key, StringComparison.OrdinalIgnoreCase));
        return known?.DefaultResource ?? DefaultBlackMask;
    }

    private static IReadOnlyDictionary<string, string?> ResolveTextureBindings(
        string customReference,
        int customMaterialCount,
        IReadOnlyList<TextureCandidate> textureCandidates,
        StringBuilder log)
    {
        var materialToken = NormalizeMatchToken(GetResourceLeaf(customReference));
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var slot in TextureSlots)
        {
            var slotCandidates = textureCandidates
                .Where(candidate => string.Equals(candidate.SlotKey, slot.Key, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var exact = slotCandidates
                .Where(candidate => string.Equals(candidate.BaseToken, materialToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exact.Length == 1)
            {
                result[slot.Key] = exact[0].ResourcePath;
                continue;
            }

            if (exact.Length > 1)
            {
                log.AppendLine($"Custom texture binding unresolved for {customReference} {slot.Key}: multiple filename matches; using safe fallback.");
                result[slot.Key] = null;
                continue;
            }

            if (customMaterialCount == 1 && slotCandidates.Length == 1)
            {
                result[slot.Key] = slotCandidates[0].ResourcePath;
                log.AppendLine(
                    $"Custom texture binding used unique-project fallback for {customReference} {slot.Key}: {slotCandidates[0].FileName}");
                continue;
            }

            result[slot.Key] = null;
        }

        return result;
    }

    private static TextureCandidate? ParseTextureCandidate(string path, string materialResourceFolder)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        foreach (var slot in TextureSlots)
        {
            foreach (var suffix in slot.Suffixes.OrderByDescending(value => value.Length))
            {
                foreach (var separator in new[] { "_", "-", " " })
                {
                    var tail = separator + suffix;
                    if (!stem.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var baseName = stem[..^tail.Length].Trim();
                    if (baseName.Length == 0)
                    {
                        return null;
                    }

                    var resourcePath = $"{materialResourceFolder}/textures/{Path.GetFileName(path)}";
                    return new TextureCandidate(
                        slot.Key,
                        NormalizeMatchToken(baseName),
                        NormalizeResourcePath(resourcePath),
                        Path.GetFileName(path));
                }
            }
        }

        return null;
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

    private static string NormalizeMatchToken(string value) =>
        new(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private sealed record TextureSlotDefinition(
        string Key,
        string DefaultResource,
        IReadOnlyList<string> Suffixes);

    private sealed record TextureCandidate(
        string SlotKey,
        string BaseToken,
        string ResourcePath,
        string FileName);

    private sealed record TextureReplacement(string ResourcePath, bool AutoBound);
}
