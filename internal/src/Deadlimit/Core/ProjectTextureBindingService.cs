using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record ProjectTextureBindingResult(
    int ManagedMaterialCount,
    int BoundTextureCount,
    int SanitizedTextureCount,
    int UnresolvedTextureCount);

internal static class ProjectTextureBindingService
{
    private const string LegacyGeneratedPrefix = "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V";
    private const string PendingManagedMarker = "// DEADLIMIT_MANAGED_CUSTOM_VMAT_V5_PENDING";
    private const string ManagedMarker = "// DEADLIMIT_MANAGED_CUSTOM_VMAT_V5";
    private const string VertexColorGeneratedPrefix = "// DEADLIMIT_VERTEXCOLOR_VMAT_V";
    private const string ManagedComment = "// Deadlimit inherited this material once. Later PREPARE runs only synchronize matching project-root textures; manual material parameters remain authoritative.";

    private const string NeutralColor = "[0.500000 0.500000 0.500000 0.000000]";
    private const string NeutralWhite = "[1.000000 1.000000 1.000000 0.000000]";
    private const string NeutralNormal = "[0.501961 0.501961 1.000000 0.000000]";
    private const string NeutralRoughness = "[0.800000 0.800000 0.800000 0.000000]";
    private const string NeutralBlack = "[0.000000 0.000000 0.000000 0.000000]";

    private static readonly HashSet<string> TextureSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
    };

    private static readonly TextureSemanticDefinition[] KnownSemantics =
    [
        new("color",
        [
            "basecolor", "base_color", "basecolour", "base_colour", "basecol", "base_col",
            "diffuse", "diffusemap", "diffuse_map", "diffusemask", "diffuse_mask", "diff",
            "albedo", "albedomap", "albedo_map", "albedomask", "albedo_mask",
            "color", "colormap", "color_map", "colormask", "color_mask", "colour",
            "colourmap", "colour_map", "colourmask", "colour_mask", "col"
        ]),
        new("normal",
        [
            "normal", "normalmap", "normal_map", "normalmask", "normal_mask", "normals", "norm", "nrm"
        ]),
        new("roughness",
        [
            "roughness", "roughnessmap", "roughness_map", "roughnessmask", "roughness_mask",
            "rough", "roughmap", "rough_map", "roughmask", "rough_mask", "rgh"
        ]),
        new("ao",
        [
            "ambientocclusion", "ambient_occlusion", "ambientocclusionmap", "ambient_occlusion_map",
            "ambientocclusionmask", "ambient_occlusion_mask", "occlusion", "occlusionmap",
            "occlusion_map", "occlusionmask", "occlusion_mask", "ao", "aomap", "ao_map",
            "aomask", "ao_mask"
        ]),
        new("metalness",
        [
            "metalness", "metalnessmap", "metalness_map", "metalnessmask", "metalness_mask",
            "metallic", "metallicmap", "metallic_map", "metallicmask", "metallic_mask",
            "metal", "metalmap", "metal_map", "metalmask", "metal_mask", "mtl"
        ]),
    ];

    private static readonly Regex TextureAssignmentRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>Texture[A-Za-z0-9_]+)\\\"|(?<bareKey>Texture[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]+)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex StringParameterRegex = new(
        "^(?<prefix>[ \\t]*(?<key>\\\"?[A-Za-z0-9_]+\\\"?)(?:(?:[ \\t]*=[ \\t]*)|[ \\t]+))(?<valueToken>\\\"[^\\\"\\r\\n]*\\\"|[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))(?<suffix>[^\\r\\n]*)(?<carriageReturn>\\r?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTexturesBlockRegex = new(
        "(?ms)^[ \\t]*\\\"Compiled Textures\\\"[ \\t]*\\r?\\n[ \\t]*\\{.*?^[ \\t]*\\}[ \\t]*\\r?\\n?",
        RegexOptions.Compiled);

    public static int MarkLegacyManagedMaterialsForMigration(
        string addonContentRoot,
        string addonName,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var materialFolder = Path.Combine(addonContentRoot, "materials", addonName);
        if (!Directory.Exists(materialFolder))
        {
            return 0;
        }

        var migrated = 0;
        foreach (var path in Directory.EnumerateFiles(materialFolder, "*.vmat", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = File.ReadAllText(path);
            if (!text.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            File.WriteAllText(path, RewriteManagedHeader(text, PendingManagedMarker));
            migrated++;
            log.AppendLine($"Custom VMAT ownership migration queued: {Path.GetFileName(path)}");
        }

        if (migrated > 0)
        {
            log.AppendLine($"Legacy Deadlimit-managed custom VMAT files protected from template re-application: {migrated}");
        }

        return migrated;
    }

    public static ProjectTextureBindingResult Synchronize(
        ProjectManifest manifest,
        string addonName,
        string addonContentRoot,
        CustomMaterialAuthoringResult customMaterials,
        IReadOnlyList<ManagedCustomMaterialOwnership> knownOwnership,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var materialResourceFolder = $"materials/{addonName}";
        var materialFolder = Path.Combine(addonContentRoot, "materials", addonName);
        var textureFolder = Path.Combine(materialFolder, "textures");
        Directory.CreateDirectory(materialFolder);
        Directory.CreateDirectory(textureFolder);

        var desiredMaterialPaths = customMaterials.Remaps
            .Select(remap => NormalizeResourcePath(remap.To))
            .Select(resource => SafePath.ResolveUnderRoot(
                addonContentRoot,
                resource.Replace('/', Path.DirectorySeparatorChar),
                "Managed custom VMAT target"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedStaleMaterials = RemoveStaleManagedMaterials(
            materialFolder,
            desiredMaterialPaths,
            knownOwnership,
            log,
            cancellationToken);

        var projectTextures = Directory.EnumerateFiles(manifest.ProjectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => TextureSourceExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var projectTextureNames = projectTextures
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removedDerivedTextures = 0;
        foreach (var derivedTexture in Directory.EnumerateFiles(textureFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => TextureSourceExtensions.Contains(Path.GetExtension(path))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (projectTextureNames.Contains(Path.GetFileName(derivedTexture)))
            {
                continue;
            }

            File.Delete(derivedTexture);
            removedDerivedTextures++;
            log.AppendLine($"Removed derived project texture because its source disappeared: {Path.GetFileName(derivedTexture)}");
        }

        foreach (var source in projectTextures)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(source, Path.Combine(textureFolder, Path.GetFileName(source)), overwrite: true);
        }

        var candidates = projectTextures
            .Select(path => ParseTextureCandidate(path, materialResourceFolder))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();

        var managedMaterials = 0;
        var boundTextures = 0;
        var sanitizedTextures = 0;
        var unresolvedTextures = 0;

        foreach (var remap in customMaterials.Remaps)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetResource = NormalizeResourcePath(remap.To);
            var targetPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                targetResource.Replace('/', Path.DirectorySeparatorChar),
                "Managed custom VMAT target");

            if (!File.Exists(targetPath))
            {
                continue;
            }

            var original = File.ReadAllText(targetPath);
            var ownership = knownOwnership.FirstOrDefault(item =>
                string.Equals(
                    NormalizeResourcePath(item.TargetResource),
                    targetResource,
                    StringComparison.OrdinalIgnoreCase));
            var vertexColorMode = ownership?.VertexColor == true
                                  || original.StartsWith(VertexColorGeneratedPrefix, StringComparison.Ordinal);

            var isLegacyGenerated = original.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal);
            var isPendingMigration = original.StartsWith(PendingManagedMarker, StringComparison.Ordinal);
            var isManaged = original.StartsWith(ManagedMarker, StringComparison.Ordinal);

            if (!isLegacyGenerated && !isPendingMigration && !isManaged && ownership is null)
            {
                log.AppendLine($"Project texture sync skipped artist-owned custom VMAT: {targetResource}");
                continue;
            }

            managedMaterials++;
            var firstManagedPass = isLegacyGenerated || isPendingMigration;
            var text = isLegacyGenerated || isPendingMigration || isManaged
                ? RemoveCompiledTexturesBlock(RewriteManagedHeader(original, ManagedMarker))
                : RemoveCompiledTexturesBlock(original);

            var bindings = new Dictionary<string, string>(ResolveMaterialBindings(
                remap.From,
                candidates,
                log), StringComparer.OrdinalIgnoreCase);
            if (vertexColorMode)
            {
                bindings.Remove("color");
            }

            var assignments = ReadAssignments(text);
            var replacements = new Dictionary<int, string>();
            var insertions = new List<(string Key, string Value, string Semantic)>();
            var boundSemantics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var binding in bindings)
            {
                var compatible = assignments
                    .Where(assignment => SemanticsCompatible(assignment.Semantic, binding.Key))
                    .OrderBy(assignment => SlotRank(assignment.Key))
                    .ThenBy(assignment => assignment.Index)
                    .ToArray();

                if (compatible.Length == 0)
                {
                    var preferredKey = GetPreferredStandardTextureKey(binding.Key, vertexColorMode);
                    if (preferredKey is null)
                    {
                        unresolvedTextures++;
                        log.AppendLine($"Project texture has no safe standard Texture* slot in {targetResource}: {binding.Key} -> {binding.Value}");
                        continue;
                    }

                    insertions.Add((preferredKey, binding.Value, binding.Key));
                    boundSemantics.Add(binding.Key);
                    boundTextures++;
                    log.AppendLine($"Project texture auto-bind inserted missing slot {targetResource}: {Path.GetFileName(binding.Value)} -> {preferredKey}");
                    continue;
                }

                var selected = compatible[0];
                replacements[selected.Index] = binding.Value;
                boundSemantics.Add(binding.Key);
                boundTextures++;
                log.AppendLine($"Project texture auto-bind {targetResource}: {Path.GetFileName(binding.Value)} -> {selected.Key}");
            }

            text = ReplaceAssignments(text, replacements);
            foreach (var insertion in insertions)
            {
                text = UpsertTextureAssignment(text, insertion.Key, insertion.Value);
            }

            text = ReconcileUnboundStandardTextureValues(
                text,
                boundSemantics,
                vertexColorMode,
                log,
                out var neutralizedStandardSlots);
            sanitizedTextures += neutralizedStandardSlots;

            text = SanitizeUnmatchedManagedTextureSources(
                text,
                materialResourceFolder,
                bindings,
                vertexColorMode,
                log,
                out var unmatchedSanitized);
            sanitizedTextures += unmatchedSanitized;

            if (boundSemantics.Contains("metalness"))
            {
                text = EnableMetalnessTexture(text);
            }
            else
            {
                text = UpsertStringParameter(text, "F_METALNESS_TEXTURE", "0");
            }

            text = SanitizeMissingTextureSources(
                text,
                addonContentRoot,
                materialResourceFolder,
                firstManagedPass,
                sanitizeDeadlimitDerivedPaths: true,
                vertexColorMode,
                log,
                out var currentSanitized);
            sanitizedTextures += currentSanitized;

            if (vertexColorMode)
            {
                text = UpsertStringParameter(text, "F_VERTEX_COLOR", "1");
            }

            if (!string.Equals(original, text, StringComparison.Ordinal))
            {
                File.WriteAllText(targetPath, text);
            }
        }

        log.AppendLine($"Stale Deadlimit-managed custom VMAT files removed: {removedStaleMaterials}");
        log.AppendLine($"Derived project texture files removed after source deletion: {removedDerivedTextures}");
        log.AppendLine($"Managed custom VMAT project-texture sync: {managedMaterials} material(s), {boundTextures} texture binding(s), {sanitizedTextures} stale/mismatched inherited or derived source repair(s), {unresolvedTextures} unmatched project texture(s).");
        log.AppendLine("Custom texture naming policy: project textures bind only when the filename material prefix exactly matches the custom material name; a matching standard PBR texture replaces the existing compatible Texture* assignment or inserts the canonical slot when that assignment is absent. Deadlimit does not guess based on there being only one material or one texture.");
        log.AppendLine("Custom VMAT lifecycle policy: Deadlimit-owned generated VMAT files are removed when their material is no longer referenced by the current artist DMX. Artist-owned/unmanaged VMAT files are preserved.");
        log.AppendLine("Custom VMAT parameter policy: retail/template shader and non-texture parameters are inherited only when a VMAT is first created. Later PREPARE runs do not re-apply hero parameters; matching project-root texture files are the only automatic overrides.");

        return new ProjectTextureBindingResult(
            managedMaterials,
            boundTextures,
            sanitizedTextures,
            unresolvedTextures);
    }

    private static int RemoveStaleManagedMaterials(
        string materialFolder,
        IReadOnlySet<string> desiredMaterialPaths,
        IReadOnlyList<ManagedCustomMaterialOwnership> knownOwnership,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(materialFolder))
        {
            return 0;
        }

        var removed = 0;
        foreach (var path in Directory.EnumerateFiles(materialFolder, "*.vmat", SearchOption.TopDirectoryOnly))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullPath = Path.GetFullPath(path);
            if (desiredMaterialPaths.Contains(fullPath))
            {
                continue;
            }

            var text = File.ReadAllText(path);
            var relativeResource = NormalizeResourcePath(Path.GetRelativePath(
                Directory.GetParent(Directory.GetParent(materialFolder)!.FullName)!.FullName,
                fullPath));
            var registryOwned = knownOwnership.Any(item => string.Equals(
                NormalizeResourcePath(item.TargetResource),
                relativeResource,
                StringComparison.OrdinalIgnoreCase));
            if (!IsDeadlimitOwnedMaterial(text) && !registryOwned)
            {
                continue;
            }

            File.Delete(path);
            removed++;
            log.AppendLine($"Removed stale Deadlimit-managed custom VMAT no longer referenced by artist DMX: {Path.GetFileName(path)}");
        }

        return removed;
    }

    private static bool IsDeadlimitOwnedMaterial(string text) =>
        text.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal)
        || text.StartsWith(PendingManagedMarker, StringComparison.Ordinal)
        || text.StartsWith(ManagedMarker, StringComparison.Ordinal)
        || text.StartsWith(VertexColorGeneratedPrefix, StringComparison.Ordinal);

    private static string ReplaceAssignments(string text, IReadOnlyDictionary<int, string> replacements)
    {
        if (replacements.Count == 0)
        {
            return text;
        }

        return TextureAssignmentRegex.Replace(text, match =>
        {
            if (!replacements.TryGetValue(match.Index, out var replacement))
            {
                return match.Value;
            }

            return match.Groups["prefix"].Value + replacement + match.Groups["suffix"].Value;
        });
    }

    private static string ReconcileUnboundStandardTextureValues(
        string text,
        IReadOnlySet<string> boundSemantics,
        bool vertexColorMode,
        StringBuilder log,
        out int neutralizedCount)
    {
        var localNeutralizedCount = 0;
        var result = TextureAssignmentRegex.Replace(text, match =>
        {
            var key = GetTextureKey(match);
            if (!TryGetStandardSemantic(key, out var semantic)
                || boundSemantics.Contains(semantic))
            {
                return match.Value;
            }

            var fallback = GetTextureFallback(key, vertexColorMode);
            if (string.Equals(match.Groups["value"].Value, fallback, StringComparison.Ordinal))
            {
                return match.Value;
            }

            localNeutralizedCount++;
            log.AppendLine($"Custom VMAT untextured standard slot neutralized {key}: {match.Groups["value"].Value} -> {fallback}");
            return match.Groups["prefix"].Value + fallback + match.Groups["suffix"].Value;
        });

        neutralizedCount = localNeutralizedCount;
        return result;
    }

    private static bool TryGetStandardSemantic(string key, out string semantic)
    {
        var canonicalKey = TrimTrailingDigits(key);
        if (string.Equals(canonicalKey, "TextureColor", StringComparison.OrdinalIgnoreCase))
        {
            semantic = "color";
            return true;
        }
        if (string.Equals(canonicalKey, "TextureNormal", StringComparison.OrdinalIgnoreCase))
        {
            semantic = "normal";
            return true;
        }
        if (string.Equals(canonicalKey, "TextureRoughness", StringComparison.OrdinalIgnoreCase))
        {
            semantic = "roughness";
            return true;
        }
        if (string.Equals(canonicalKey, "TextureAmbientOcclusion", StringComparison.OrdinalIgnoreCase))
        {
            semantic = "ao";
            return true;
        }
        if (string.Equals(canonicalKey, "TextureMetalness", StringComparison.OrdinalIgnoreCase))
        {
            semantic = "metalness";
            return true;
        }

        semantic = string.Empty;
        return false;
    }

    private static string SanitizeUnmatchedManagedTextureSources(
        string text,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string> bindings,
        bool vertexColorMode,
        StringBuilder log,
        out int sanitizedCount)
    {
        var managedTexturePrefix = NormalizeResourcePath(materialResourceFolder + "/textures/");
        var localSanitized = 0;

        var result = TextureAssignmentRegex.Replace(text, match =>
        {
            var value = NormalizeResourcePath(match.Groups["value"].Value);
            if (!value.StartsWith(managedTexturePrefix, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            var key = GetTextureKey(match);
            var slotSemantic = GetSemanticFromTextureKey(key);
            var hasExactBinding = bindings.Any(binding =>
                SemanticsCompatible(slotSemantic, binding.Key)
                && string.Equals(
                    NormalizeResourcePath(binding.Value),
                    value,
                    StringComparison.OrdinalIgnoreCase));

            if (hasExactBinding)
            {
                return match.Value;
            }

            var fallback = GetTextureFallback(key, vertexColorMode);
            localSanitized++;
            log.AppendLine($"Custom VMAT mismatched project texture removed {key}: {match.Groups["value"].Value} -> {fallback}");
            return match.Groups["prefix"].Value + fallback + match.Groups["suffix"].Value;
        });

        sanitizedCount = localSanitized;
        return result;
    }

    private static string SanitizeMissingTextureSources(
        string text,
        string addonContentRoot,
        string materialResourceFolder,
        bool sanitizeInheritedMissingSources,
        bool sanitizeDeadlimitDerivedPaths,
        bool vertexColorMode,
        StringBuilder log,
        out int sanitizedCount)
    {
        var localSanitized = 0;
        var result = TextureAssignmentRegex.Replace(text, match =>
        {
            var value = match.Groups["value"].Value;
            if (!LooksLikeLocalTextureSource(value)
                || TextureSourceExists(addonContentRoot, value))
            {
                return match.Value;
            }

            var normalized = NormalizeResourcePath(value);
            var isDeadlimitDerived = normalized.StartsWith(
                NormalizeResourcePath(materialResourceFolder + "/textures/"),
                StringComparison.OrdinalIgnoreCase);

            var isKnownSafeDefault = IsKnownSafeDefault(value);
            if (!sanitizeInheritedMissingSources
                && !(sanitizeDeadlimitDerivedPaths && isDeadlimitDerived)
                && !isKnownSafeDefault)
            {
                return match.Value;
            }

            var key = GetTextureKey(match);
            var fallback = GetTextureFallback(key, vertexColorMode);
            localSanitized++;
            log.AppendLine($"Custom VMAT missing source repaired {key}: {value} -> {fallback}");
            return match.Groups["prefix"].Value + fallback + match.Groups["suffix"].Value;
        });

        sanitizedCount = localSanitized;
        return result;
    }

    private static IReadOnlyList<TextureAssignment> ReadAssignments(string text)
    {
        return TextureAssignmentRegex.Matches(text)
            .Cast<Match>()
            .Select(match =>
            {
                var key = GetTextureKey(match);
                return new TextureAssignment(
                    match.Index,
                    key,
                    match.Groups["value"].Value,
                    GetSemanticFromTextureKey(key));
            })
            .ToArray();
    }

    private static IReadOnlyDictionary<string, string> ResolveMaterialBindings(
        string customReference,
        IReadOnlyList<ProjectTextureCandidate> candidates,
        StringBuilder log)
    {
        var materialToken = NormalizeMatchToken(GetResourceLeaf(customReference));
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var semanticGroup in candidates.GroupBy(candidate => candidate.Semantic, StringComparer.OrdinalIgnoreCase))
        {
            var exact = semanticGroup
                .Where(candidate => string.Equals(candidate.BaseToken, materialToken, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (exact.Length == 1)
            {
                result[semanticGroup.Key] = exact[0].ResourcePath;
                continue;
            }

            if (exact.Length > 1)
            {
                log.AppendLine($"Project texture binding ambiguous for {customReference} semantic '{semanticGroup.Key}': multiple exact filename matches.");
            }
        }

        return result;
    }

    private static ProjectTextureCandidate? ParseTextureCandidate(string path, string materialResourceFolder)
    {
        var stem = Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrWhiteSpace(stem))
        {
            return null;
        }

        foreach (var definition in KnownSemantics)
        {
            foreach (var alias in definition.Aliases.OrderByDescending(value => value.Length))
            {
                foreach (var separator in new[] { "_", "-", " ", "." })
                {
                    var tail = separator + alias;
                    if (!stem.EndsWith(tail, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var baseName = stem[..^tail.Length].Trim();
                    if (baseName.Length == 0)
                    {
                        continue;
                    }

                    return NewCandidate(path, materialResourceFolder, baseName, definition.Semantic);
                }
            }

            if (definition.Aliases.Any(alias => string.Equals(stem, alias, StringComparison.OrdinalIgnoreCase)))
            {
                return NewCandidate(path, materialResourceFolder, string.Empty, definition.Semantic);
            }
        }

        var separatorIndex = Math.Max(
            Math.Max(stem.LastIndexOf('_'), stem.LastIndexOf('-')),
            Math.Max(stem.LastIndexOf(' '), stem.LastIndexOf('.')));
        if (separatorIndex <= 0 || separatorIndex >= stem.Length - 1)
        {
            return null;
        }

        var basePart = stem[..separatorIndex].Trim();
        var semanticPart = stem[(separatorIndex + 1)..].Trim();
        if (basePart.Length == 0 || semanticPart.Length == 0)
        {
            return null;
        }

        return NewCandidate(
            path,
            materialResourceFolder,
            basePart,
            CanonicalizeSemantic(NormalizeMatchToken(semanticPart)));
    }

    private static ProjectTextureCandidate NewCandidate(
        string path,
        string materialResourceFolder,
        string baseName,
        string semantic)
    {
        return new ProjectTextureCandidate(
            NormalizeMatchToken(baseName),
            semantic,
            NormalizeResourcePath($"{materialResourceFolder}/textures/{Path.GetFileName(path)}"),
            Path.GetFileName(path));
    }

    private static string GetSemanticFromTextureKey(string key)
    {
        var raw = key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
            ? key["Texture".Length..]
            : key;
        raw = TrimTrailingDigits(raw);
        return CanonicalizeSemantic(NormalizeMatchToken(raw));
    }

    private static string TrimTrailingDigits(string value)
    {
        var end = value.Length;
        while (end > 0 && char.IsDigit(value[end - 1]))
        {
            end--;
        }
        return value[..end];
    }

    private static string CanonicalizeSemantic(string semantic)
    {
        if (semantic.Length == 0)
        {
            return semantic;
        }

        if (semantic is "color" or "basecolor" or "diffuse" or "albedo")
        {
            return "color";
        }
        if (semantic.Contains("normal", StringComparison.Ordinal))
        {
            return "normal";
        }
        if (semantic.Contains("rough", StringComparison.Ordinal))
        {
            return "roughness";
        }
        if (semantic.Contains("metal", StringComparison.Ordinal))
        {
            return "metalness";
        }
        if (string.Equals(semantic, "ao", StringComparison.Ordinal)
            || string.Equals(semantic, "ambientocclusion", StringComparison.Ordinal)
            || semantic.Contains("occlusion", StringComparison.Ordinal))
        {
            return "ao";
        }

        return semantic;
    }

    private static bool SemanticsCompatible(string slotSemantic, string projectSemantic)
    {
        if (string.Equals(slotSemantic, projectSemantic, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (slotSemantic.EndsWith("mask", StringComparison.OrdinalIgnoreCase)
            && string.Equals(slotSemantic[..^4], projectSemantic, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return projectSemantic.EndsWith("mask", StringComparison.OrdinalIgnoreCase)
            && string.Equals(projectSemantic[..^4], slotSemantic, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetPreferredStandardTextureKey(string semantic, bool vertexColorMode)
    {
        return semantic.ToLowerInvariant() switch
        {
            "color" => vertexColorMode ? null : "TextureColor",
            "normal" => vertexColorMode ? "TextureNormal1" : "TextureNormal",
            "roughness" => vertexColorMode ? "TextureRoughness1" : "TextureRoughness",
            "ao" => vertexColorMode ? "TextureAmbientOcclusion1" : "TextureAmbientOcclusion",
            "metalness" => vertexColorMode ? "TextureMetalness1" : "TextureMetalness",
            _ => null,
        };
    }

    private static string UpsertTextureAssignment(string text, string key, string value)
    {
        var found = false;
        var patched = TextureAssignmentRegex.Replace(text, match =>
        {
            if (!string.Equals(GetTextureKey(match), key, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            if (found)
            {
                return string.Empty;
            }

            found = true;
            return match.Groups["prefix"].Value + value + match.Groups["suffix"].Value;
        });

        if (found)
        {
            return patched;
        }

        var closingBrace = patched.LastIndexOf('}');
        if (closingBrace < 0)
        {
            return patched;
        }

        var newline = patched.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"{newline}");
    }
    private static int SlotRank(string key)
    {
        var raw = key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
            ? key["Texture".Length..]
            : key;

        var digits = new string(raw.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (int.TryParse(digits, out var index))
        {
            return index == 1 ? 0 : 100 + index;
        }

        return 50;
    }

    private static string EnableMetalnessTexture(string text)
    {
        var updated = UpsertStringParameter(text, "F_METALNESS_TEXTURE", "1");
        if (!HasStringParameter(updated, "F_SPECULAR"))
        {
            updated = UpsertStringParameter(updated, "F_SPECULAR", "1");
        }
        return updated;
    }

    private static bool HasStringParameter(string text, string key) =>
        StringParameterRegex.Matches(text)
            .Cast<Match>()
            .Any(match => string.Equals(GetStringParameterKey(match), key, StringComparison.OrdinalIgnoreCase));

    private static string UpsertStringParameter(string text, string key, string value)
    {
        var found = false;
        var patched = StringParameterRegex.Replace(text, match =>
        {
            if (!string.Equals(GetStringParameterKey(match), key, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            if (found)
            {
                return string.Empty;
            }

            found = true;
            var existingToken = match.Groups["valueToken"].Value;
            var replacement = existingToken.StartsWith('"') ? $"\"{value}\"" : value;
            return match.Groups["prefix"].Value + replacement + match.Groups["suffix"].Value +
                   match.Groups["carriageReturn"].Value;
        });

        if (found)
        {
            return patched;
        }

        var closingBrace = patched.LastIndexOf('}');
        if (closingBrace < 0)
        {
            return patched;
        }

        var newline = patched.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"{newline}");
    }

    private static string GetStringParameterKey(Match match)
        => match.Groups["key"].Value.Trim('"');

    private static string GetTextureKey(Match match)
    {
        var quoted = match.Groups["quotedKey"];
        return quoted.Success ? quoted.Value : match.Groups["bareKey"].Value;
    }

    private static string RewriteManagedHeader(string text, string marker)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var body = text;

        if (body.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal)
            || body.StartsWith(PendingManagedMarker, StringComparison.Ordinal)
            || body.StartsWith(ManagedMarker, StringComparison.Ordinal))
        {
            body = RemoveFirstLine(body);
        }

        while (body.StartsWith("// Deadlimit manages Texture*", StringComparison.Ordinal)
               || body.StartsWith("// Deadlimit inherited this material once.", StringComparison.Ordinal)
               || body.StartsWith("// Initial scaffold:", StringComparison.Ordinal))
        {
            body = RemoveFirstLine(body);
        }

        return marker + newline + ManagedComment + newline + body;
    }

    private static string RemoveCompiledTexturesBlock(string text) =>
        CompiledTexturesBlockRegex.Replace(text, string.Empty, 1);

    private static string RemoveFirstLine(string text)
    {
        var index = text.IndexOf('\n');
        return index >= 0 ? text[(index + 1)..] : string.Empty;
    }

    private static bool TextureSourceExists(string addonContentRoot, string resourcePath)
    {
        if (Path.IsPathRooted(resourcePath))
        {
            return File.Exists(resourcePath);
        }

        var relative = NormalizeResourcePath(resourcePath)
            .Replace('/', Path.DirectorySeparatorChar);
        try
        {
            return File.Exists(SafePath.ResolveUnderRoot(
                addonContentRoot,
                relative,
                "VMAT texture source"));
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static bool LooksLikeLocalTextureSource(string value) =>
        TextureSourceExtensions.Contains(Path.GetExtension(value.Replace('\\', '/')));

    private static bool IsKnownSafeDefault(string value) =>
        NormalizeResourcePath(value).StartsWith("materials/default/", StringComparison.OrdinalIgnoreCase);

    private static string GetTextureFallback(string key, bool vertexColorMode = false)
    {
        return GetSemanticFromTextureKey(key) switch
        {
            "color" when vertexColorMode => NeutralWhite,
            "color" => NeutralColor,
            "normal" => NeutralNormal,
            "roughness" => NeutralRoughness,
            "ao" => NeutralWhite,
            "metalness" => NeutralBlack,
            _ => NeutralBlack,
        };
    }

    private static string GetResourceLeaf(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[(slash + 1)..] : normalized;
    }

    private static string NormalizeMatchToken(string value) =>
        new(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private sealed record TextureSemanticDefinition(string Semantic, IReadOnlyList<string> Aliases);

    private sealed record ProjectTextureCandidate(
        string BaseToken,
        string Semantic,
        string ResourcePath,
        string FileName);

    private sealed record TextureAssignment(
        int Index,
        string Key,
        string Value,
        string Semantic);
}
