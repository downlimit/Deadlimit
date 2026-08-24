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
    private const string ManagedComment = "// Deadlimit inherited this material once. Later PREPARE runs only synchronize matching project-root textures; manual material parameters remain authoritative.";

    private const string DefaultColor = "materials/default/default_color.tga";
    private const string DefaultNormal = "materials/default/default_normal.tga";
    private const string DefaultRoughness = "materials/default/default_rough.tga";
    private const string DefaultAo = "materials/default/default_ao.tga";
    private const string DefaultBlackMask = "materials/default/default_black_mask.tga";

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
        new("color", ["base_color", "basecolor", "diffuse", "albedo", "color"]),
        new("normal", ["normal", "norm"]),
        new("roughness", ["roughness", "rough"]),
        new("ao", ["ambient_occlusion", "ambientocclusion", "occlusion", "ao"]),
        new("metalness", ["metalness", "metallic", "metal"]),
    ];

    private static readonly Regex TextureAssignmentRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>Texture[A-Za-z0-9_]+)\\\"|(?<bareKey>Texture[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]+)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex StringParameterRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>[A-Za-z0-9_]+)\\\"|(?<bareKey>[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]*)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

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
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var materialResourceFolder = $"materials/{addonName}";
        var textureFolder = Path.Combine(addonContentRoot, "materials", addonName, "textures");
        Directory.CreateDirectory(textureFolder);

        var projectTextures = Directory.EnumerateFiles(manifest.ProjectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => TextureSourceExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

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
            var targetPath = Path.Combine(
                addonContentRoot,
                targetResource.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(targetPath))
            {
                continue;
            }

            var original = File.ReadAllText(targetPath);
            var isLegacyGenerated = original.StartsWith(LegacyGeneratedPrefix, StringComparison.Ordinal);
            var isPendingMigration = original.StartsWith(PendingManagedMarker, StringComparison.Ordinal);
            var isManaged = original.StartsWith(ManagedMarker, StringComparison.Ordinal);

            if (!isLegacyGenerated && !isPendingMigration && !isManaged)
            {
                log.AppendLine($"Project texture sync skipped artist-owned custom VMAT: {targetResource}");
                continue;
            }

            managedMaterials++;
            var firstManagedPass = isLegacyGenerated || isPendingMigration;
            var text = RewriteManagedHeader(original, ManagedMarker);

            var bindings = ResolveMaterialBindings(
                remap.From,
                customMaterials.CustomMaterialCount,
                candidates,
                log);

            var assignments = ReadAssignments(text);
            var replacements = new Dictionary<int, string>();
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
                    unresolvedTextures++;
                    log.AppendLine($"Project texture has no matching Texture* slot in {targetResource}: {binding.Key} -> {binding.Value}");
                    continue;
                }

                var selected = compatible[0];
                replacements[selected.Index] = binding.Value;
                boundSemantics.Add(binding.Key);
                boundTextures++;
                log.AppendLine($"Project texture auto-bind {targetResource}: {Path.GetFileName(binding.Value)} -> {selected.Key}");
            }

            text = ReplaceAssignments(text, replacements);

            if (boundSemantics.Contains("metalness"))
            {
                text = EnableMetalnessTexture(text);
            }

            var sanitizeDeadlimitDerivedPaths = true;
            text = SanitizeMissingTextureSources(
                text,
                addonContentRoot,
                materialResourceFolder,
                firstManagedPass,
                sanitizeDeadlimitDerivedPaths,
                log,
                out var currentSanitized);
            sanitizedTextures += currentSanitized;

            if (!string.Equals(original, text, StringComparison.Ordinal))
            {
                File.WriteAllText(targetPath, text);
            }
        }

        log.AppendLine($"Managed custom VMAT project-texture sync: {managedMaterials} material(s), {boundTextures} texture binding(s), {sanitizedTextures} stale inherited/derived source repair(s), {unresolvedTextures} unmatched project texture(s).");
        log.AppendLine("Custom VMAT parameter policy: retail/template shader and non-texture parameters are inherited only when a VMAT is first created. Later PREPARE runs do not re-apply hero parameters; matching project-root texture files are the only automatic overrides.");

        return new ProjectTextureBindingResult(
            managedMaterials,
            boundTextures,
            sanitizedTextures,
            unresolvedTextures);
    }

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

    private static string SanitizeMissingTextureSources(
        string text,
        string addonContentRoot,
        string materialResourceFolder,
        bool sanitizeInheritedMissingSources,
        bool sanitizeDeadlimitDerivedPaths,
        StringBuilder log,
        out int sanitizedCount)
    {
        var localSanitized = 0;
        var result = TextureAssignmentRegex.Replace(text, match =>
        {
            var value = match.Groups["value"].Value;
            if (!LooksLikeLocalTextureSource(value)
                || IsKnownSafeDefault(value)
                || TextureSourceExists(addonContentRoot, value))
            {
                return match.Value;
            }

            var normalized = NormalizeResourcePath(value);
            var isDeadlimitDerived = normalized.StartsWith(
                NormalizeResourcePath(materialResourceFolder + "/textures/"),
                StringComparison.OrdinalIgnoreCase);

            if (!sanitizeInheritedMissingSources && !(sanitizeDeadlimitDerivedPaths && isDeadlimitDerived))
            {
                return match.Value;
            }

            var key = GetTextureKey(match);
            var fallback = GetTextureFallback(key);
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
        int customMaterialCount,
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

            ProjectTextureCandidate? selected = null;
            if (exact.Length == 1)
            {
                selected = exact[0];
            }
            else if (exact.Length > 1)
            {
                log.AppendLine($"Project texture binding ambiguous for {customReference} semantic '{semanticGroup.Key}': multiple exact filename matches.");
                continue;
            }
            else if (customMaterialCount == 1)
            {
                var unique = semanticGroup.ToArray();
                if (unique.Length == 1)
                {
                    selected = unique[0];
                    log.AppendLine($"Project texture binding used single-material fallback for {customReference} semantic '{semanticGroup.Key}': {selected.FileName}");
                }
            }

            if (selected is not null)
            {
                result[semanticGroup.Key] = selected.ResourcePath;
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
                foreach (var separator in new[] { "_", "-", " " })
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
            stem.LastIndexOf('_'),
            Math.Max(stem.LastIndexOf('-'), stem.LastIndexOf(' ')));
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
        raw = raw.TrimEnd(char.IsDigit);
        return CanonicalizeSemantic(NormalizeMatchToken(raw));
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
        if (semantic is "ao" or "ambientocclusion" || semantic.Contains("occlusion", StringComparison.Ordinal))
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
        var match = StringParameterRegex.Matches(text)
            .Cast<Match>()
            .FirstOrDefault(candidate => string.Equals(GetStringParameterKey(candidate), key, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            return text[..match.Index] +
                   match.Groups["prefix"].Value + value + match.Groups["suffix"].Value +
                   text[(match.Index + match.Length)..];
        }

        var closingBrace = text.LastIndexOf('}');
        if (closingBrace < 0)
        {
            return text;
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.Insert(closingBrace, $"    {key} \"{value}\"{newline}");
    }

    private static string GetStringParameterKey(Match match)
    {
        var quoted = match.Groups["quotedKey"];
        return quoted.Success ? quoted.Value : match.Groups["bareKey"].Value;
    }

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
        return File.Exists(Path.Combine(addonContentRoot, relative));
    }

    private static bool LooksLikeLocalTextureSource(string value) =>
        TextureSourceExtensions.Contains(Path.GetExtension(value.Replace('\\', '/')));

    private static bool IsKnownSafeDefault(string value) =>
        NormalizeResourcePath(value).StartsWith("materials/default/", StringComparison.OrdinalIgnoreCase);

    private static string GetTextureFallback(string key)
    {
        return GetSemanticFromTextureKey(key) switch
        {
            "color" => DefaultColor,
            "normal" => DefaultNormal,
            "roughness" => DefaultRoughness,
            "ao" => DefaultAo,
            "metalness" => DefaultBlackMask,
            _ => DefaultBlackMask,
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
