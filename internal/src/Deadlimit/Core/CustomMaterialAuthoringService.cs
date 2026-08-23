using System.Text;
using System.Text.RegularExpressions;

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
    private const string GeneratedMarker = "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V2";

    private static readonly TextureSlotDefinition[] TextureSlots =
    [
        new("TextureColor", "materials/default/default_color.tga", ["basecolor", "base_color", "diffuse", "albedo", "color"]),
        new("TextureNormal", "materials/default/default_normal.tga", ["normal", "norm"]),
        new("TextureRoughness", "materials/default/default_rough.tga", ["roughness", "rough"]),
        new("TextureAmbientOcclusion", "materials/default/default_ao.tga", ["ambientocclusion", "ambient_occlusion", "occlusion", "ao"]),
        new("TextureMetalness", string.Empty, ["metalness", "metallic", "metal"]),
    ];

    private static readonly Regex ShaderRegex = new(
        "\\bshader\\s+\"(?<shader>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        _ = templateMaterialResource;

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

        var textureCandidates = rootPngFiles
            .Select(path => ParseTextureCandidate(path, materialResourceFolder))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();

        log.AppendLine($"Custom materials detected: {customReferences.Length}");
        log.AppendLine($"Custom texture sources refreshed from project root: {rootPngFiles.Length}");
        log.AppendLine($"Custom texture source folder: {textureFolder}");
        log.AppendLine("Custom texture naming: <material>_color|diffuse|basecolor|albedo, _normal, _rough|roughness, _ao|occlusion, _metal|metalness|metallic.");

        var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var remaps = new List<VmdlMaterialRemap>();
        var vmatResources = new List<string>();
        var created = 0;
        var preserved = 0;
        var autoBoundTextures = 0;
        var shader = ResolveCleanPbrShader(log);

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
                var bindings = ResolveTextureBindings(
                    customReference,
                    customReferences.Length,
                    textureCandidates,
                    log);

                File.WriteAllText(targetPath, BuildCleanPbrVmat(shader, bindings));
                created++;
                autoBoundTextures += bindings.Values.Count(value => value is not null);

                log.AppendLine($"Custom VMAT created as clean PBR scaffold: {customReference} -> {targetResource}");
                foreach (var binding in bindings.Where(pair => pair.Value is not null))
                {
                    log.AppendLine($"  auto-bind {binding.Key} -> {binding.Value}");
                }
            }

            remaps.Add(new VmdlMaterialRemap(customReference, targetResource));
            vmatResources.Add(targetResource);
        }

        log.AppendLine($"Custom textures auto-bound during VMAT creation: {autoBoundTextures}");
        log.AppendLine("Custom VMAT policy: create only when missing; never overwrite an existing addon-owned VMAT during PREPARE FOR CSDK.");
        log.AppendLine("Custom VMAT scaffold policy: use a clean PBR material with Source 2 default color/normal/roughness/AO fallbacks instead of inheriting hero-specific NPR/rim/self-illum texture references.");
        log.AppendLine("Custom texture policy: project-root PNG files are artist-owned source inputs and are refreshed into the addon texture-source folder; authored VMAT files remain authoritative after creation.");

        return new CustomMaterialAuthoringResult(
            remaps,
            customReferences.Length,
            created,
            preserved,
            rootPngFiles.Length,
            materialContentFolder,
            vmatResources);
    }

    private string ResolveCleanPbrShader(StringBuilder log)
    {
        var defaultVmat = Path.Combine(
            _paths.CsdkContentRoot,
            "core",
            "materials",
            "default",
            "default.vmat");

        if (File.Exists(defaultVmat))
        {
            var text = File.ReadAllText(defaultVmat);
            var match = ShaderRegex.Match(text);
            if (match.Success)
            {
                var shader = match.Groups["shader"].Value.Trim();
                if (shader.Length > 0)
                {
                    log.AppendLine($"Custom VMAT shader inherited from CSDK core default material: {shader}");
                    return shader;
                }
            }
        }

        const string fallback = "shaders/complex.shader";
        log.AppendLine($"Custom VMAT shader: CSDK core default.vmat was unavailable/unreadable; using current Source 2 PBR fallback {fallback}.");
        return fallback;
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
                log.AppendLine($"Custom texture binding unresolved for {customReference} {slot.Key}: multiple filename matches; using default.");
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

    private static string BuildCleanPbrVmat(
        string shader,
        IReadOnlyDictionary<string, string?> bindings)
    {
        string Resolve(string key)
        {
            var slot = TextureSlots.First(definition => string.Equals(definition.Key, key, StringComparison.OrdinalIgnoreCase));
            return bindings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : slot.DefaultResource;
        }

        var metalness = bindings.TryGetValue("TextureMetalness", out var metalnessTexture)
            && !string.IsNullOrWhiteSpace(metalnessTexture)
            ? $"F_METALNESS_TEXTURE \"1\"{Environment.NewLine}TextureMetalness \"{metalnessTexture}\""
            : "g_flMetalness \"0.000\"";

        return $"""
{GeneratedMarker}
// Initial scaffold only. After creation, Material Editor owns this file and Deadlimit will not overwrite it.
Layer0
{{
    shader "{shader}"

    //---- Ambient Occlusion ----
    g_flAmbientOcclusionDirectDiffuse "0.000"
    g_flAmbientOcclusionDirectSpecular "0.000"
    TextureAmbientOcclusion "{Resolve("TextureAmbientOcclusion")}"

    //---- Color ----
    g_flModelTintAmount "1.000"
    g_vColorTint "[1.000000 1.000000 1.000000 0.000000]"
    TextureColor "{Resolve("TextureColor")}"

    //---- Metalness ----
    {metalness}

    //---- Normal ----
    TextureNormal "{Resolve("TextureNormal")}"

    //---- Roughness ----
    g_flRoughnessScaleFactor "1.000"
    TextureRoughness "{Resolve("TextureRoughness")}"

    //---- Fade ----
    g_flFadeExponent "1.000"

    //---- Fog ----
    g_bFogEnabled "1"

    //---- Texture Coordinates ----
    g_nScaleTexCoordUByModelScaleAxis "0"
    g_nScaleTexCoordVByModelScaleAxis "0"
    g_vTexCoordOffset "[0.000 0.000]"
    g_vTexCoordScale "[1.000 1.000]"
    g_vTexCoordScrollSpeed "[0.000 0.000]"
}}
""";
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
}
