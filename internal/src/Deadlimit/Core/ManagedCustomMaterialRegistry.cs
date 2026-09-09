using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record ManagedCustomMaterialOwnership(
    string SourceReference,
    string TargetResource,
    bool VertexColor,
    int NameModifierRevision = 0);

internal sealed class ManagedCustomMaterialRegistry
{
    public int Version { get; set; } = 3;
    public List<ManagedCustomMaterialOwnership> Materials { get; set; } = [];
}

internal static class ManagedCustomMaterialRegistryStore
{
    private const string FileName = "managed-custom-materials.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ManagedCustomMaterialRegistry Load(ProjectManifest manifest)
    {
        var path = GetPath(manifest);
        if (!File.Exists(path))
        {
            return new ManagedCustomMaterialRegistry();
        }

        try
        {
            return JsonSerializer.Deserialize<ManagedCustomMaterialRegistry>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new ManagedCustomMaterialRegistry();
        }
        catch (JsonException)
        {
            return new ManagedCustomMaterialRegistry();
        }
    }

    public static IReadOnlyList<ManagedCustomMaterialOwnership> BuildCurrent(
        IEnumerable<VmdlMaterialRemap> remaps)
    {
        return remaps
            .Select(remap => new ManagedCustomMaterialOwnership(
                NormalizeResourcePath(remap.From),
                NormalizeResourcePath(remap.To),
                IsVertexColorReference(remap.From)))
            .GroupBy(item => item.SourceReference, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(item => item.SourceReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyDictionary<string, string> BuildTargetMap(
        ManagedCustomMaterialRegistry registry) =>
        registry.Materials
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceReference)
                           && !string.IsNullOrWhiteSpace(item.TargetResource))
            .GroupBy(
                item => NormalizeResourcePath(item.SourceReference),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => NormalizeResourcePath(group.Last().TargetResource),
                StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<ManagedCustomMaterialOwnership> MergeKnownWithCurrent(
        ManagedCustomMaterialRegistry registry,
        IReadOnlyList<ManagedCustomMaterialOwnership> current)
    {
        return registry.Materials
            .Concat(current)
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceReference)
                           && !string.IsNullOrWhiteSpace(item.TargetResource))
            .Select(item => new ManagedCustomMaterialOwnership(
                NormalizeResourcePath(item.SourceReference),
                NormalizeResourcePath(item.TargetResource),
                item.VertexColor,
                item.NameModifierRevision))
            .GroupBy(item => item.SourceReference, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var entries = group.ToArray();
                var latest = entries[^1];
                return latest with
                {
                    NameModifierRevision = entries.Max(item => item.NameModifierRevision),
                };
            })
            .OrderBy(item => item.SourceReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static void Save(
        ProjectManifest manifest,
        IReadOnlyList<ManagedCustomMaterialOwnership> materials)
    {
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        Directory.CreateDirectory(metadataFolder);

        var registry = new ManagedCustomMaterialRegistry
        {
            Materials = ApplyPendingNameModifierMigrations(manifest, materials).ToList(),
        };

        AtomicFile.WriteJson(GetPath(manifest), registry, JsonOptions);
    }

    private static IReadOnlyList<ManagedCustomMaterialOwnership> ApplyPendingNameModifierMigrations(
        ProjectManifest manifest,
        IReadOnlyList<ManagedCustomMaterialOwnership> materials)
    {
        var addonContentRoot = TryFindAddonContentRoot(manifest.SourceVmdl);
        if (addonContentRoot is null)
        {
            return materials;
        }

        var updated = new List<ManagedCustomMaterialOwnership>(materials.Count);
        foreach (var material in materials)
        {
            if (material.NameModifierRevision >= ManagedMaterialNameModifierPolicy.CurrentRevision
                || !ManagedMaterialNameModifierPolicy.HasModifier(material.SourceReference))
            {
                updated.Add(material);
                continue;
            }

            string targetPath;
            try
            {
                targetPath = SafePath.ResolveUnderRoot(
                    addonContentRoot,
                    NormalizeResourcePath(material.TargetResource).Replace('/', Path.DirectorySeparatorChar),
                    "Managed custom material target");
            }
            catch (InvalidOperationException)
            {
                updated.Add(material);
                continue;
            }

            if (!File.Exists(targetPath))
            {
                updated.Add(material);
                continue;
            }

            var existing = File.ReadAllText(targetPath);
            if (!ManagedMaterialNameModifierPolicy.IsSafeDeadlimitOwnedMaterial(existing))
            {
                // An unmarked registry-owned VMAT may have been rewritten in Material Editor.
                // Preserve it as artist-authored rather than guessing and reapplying a preset.
                updated.Add(material);
                continue;
            }

            var patched = ManagedMaterialNameModifierPolicy.Apply(
                existing,
                material.SourceReference,
                material.VertexColor);
            if (!string.Equals(existing, patched, StringComparison.Ordinal))
            {
                File.WriteAllText(targetPath, patched);
            }

            updated.Add(material with
            {
                NameModifierRevision = ManagedMaterialNameModifierPolicy.CurrentRevision,
            });
        }

        return updated;
    }

    private static string? TryFindAddonContentRoot(string? sourceVmdlPath)
    {
        if (string.IsNullOrWhiteSpace(sourceVmdlPath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(sourceVmdlPath));
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        var current = new DirectoryInfo(directory);
        while (current.Parent is not null)
        {
            if (string.Equals(current.Parent.Name, "citadel_addons", StringComparison.OrdinalIgnoreCase))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return null;
    }

    private static string GetPath(ProjectManifest manifest) =>
        Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), FileName);

    private static bool IsVertexColorReference(string reference)
    {
        var normalized = NormalizeResourcePath(reference);
        var slash = normalized.LastIndexOf('/');
        var leaf = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var token = new string(leaf
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return token.Contains("vertexcolor", StringComparison.Ordinal);
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}

internal static class ManagedMaterialNameModifierPolicy
{
    internal const int CurrentRevision = 1;

    private const string MetalPresetValue = "0.800";
    private const string MetalPresetRoughness = "[0.501961 0.501961 0.501961 0.000000]";

    private static readonly string[] DeadlimitOwnedMarkerPrefixes =
    [
        "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V",
        "// DEADLIMIT_MANAGED_CUSTOM_VMAT_V",
        "// DEADLIMIT_VERTEXCOLOR_VMAT_V",
    ];

    private static readonly Regex StringParameterRegex = new(
        "^(?<prefix>[ \\t]*(?<key>\\\"?[A-Za-z0-9_]+\\\"?)(?:(?:[ \\t]*=[ \\t]*)|[ \\t]+))(?<valueToken>\\\"[^\\\"\\r\\n]*\\\"|[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))(?<suffix>[^\\r\\n]*)(?<carriageReturn>\\r?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex TextureAssignmentRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>Texture[A-Za-z0-9_]+)\\\"|(?<bareKey>Texture[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]+)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    internal static bool HasModifier(string sourceReference)
    {
        var normalized = sourceReference.Replace('\\', '/').TrimStart('/');
        var slash = normalized.LastIndexOf('/');
        var leaf = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var stem = Path.GetFileNameWithoutExtension(leaf);
        var token = new string(stem
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return token.Contains("metal", StringComparison.Ordinal);
    }

    internal static bool IsSafeDeadlimitOwnedMaterial(string vmatText) =>
        DeadlimitOwnedMarkerPrefixes.Any(prefix =>
            vmatText.StartsWith(prefix, StringComparison.Ordinal));

    internal static string Apply(string text, string sourceReference, bool vertexColor)
    {
        if (!HasModifier(sourceReference))
        {
            return text;
        }

        var patched = UpsertStringParameter(text, "g_flMetalness", MetalPresetValue);
        return UpsertTextureAssignment(
            patched,
            vertexColor ? "TextureRoughness1" : "TextureRoughness",
            MetalPresetRoughness);
    }

    private static string UpsertStringParameter(string text, string key, string value)
    {
        var found = false;
        var patched = StringParameterRegex.Replace(text, match =>
        {
            if (!string.Equals(match.Groups["key"].Value.Trim('"'), key, StringComparison.OrdinalIgnoreCase))
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
            throw new InvalidDataException("Managed custom VMAT did not contain a closing Layer0 brace.");
        }

        var newline = patched.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"{newline}");
    }

    private static string UpsertTextureAssignment(string text, string key, string value)
    {
        var useCrLf = text.Contains("\r\n", StringComparison.Ordinal);
        var normalized = useCrLf ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;
        var found = false;
        var patched = TextureAssignmentRegex.Replace(normalized, match =>
        {
            var quotedKey = match.Groups["quotedKey"];
            var actualKey = quotedKey.Success ? quotedKey.Value : match.Groups["bareKey"].Value;
            if (!string.Equals(actualKey, key, StringComparison.OrdinalIgnoreCase))
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

        if (!found)
        {
            var closingBrace = patched.LastIndexOf('}');
            if (closingBrace < 0)
            {
                throw new InvalidDataException("Managed custom VMAT did not contain a closing Layer0 brace.");
            }

            patched = patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"\n");
        }

        return useCrLf ? patched.Replace("\n", "\r\n", StringComparison.Ordinal) : patched;
    }
}
