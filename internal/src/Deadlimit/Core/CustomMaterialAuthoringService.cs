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
    private const string GeneratedMarker = "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V4";
    private const string ManagedComment = "// Deadlimit manages Texture* source assignments in this generated VMAT from project-root textures on every PREPARE. Non-texture Material Editor edits remain authoritative.";
    private const string VertexColorGeneratedMarker = "// DEADLIMIT_VERTEXCOLOR_VMAT_V1";
    private const string VertexColorManagedComment = "// Deadlimit vertex-color material: mesh vertex color drives base color; project color textures are intentionally ignored.";
    private const string VertexColorTemplateMaterial = "materials/dev/vertcolor_pbr_basic.vmat";
    private const string MetalPresetValue = "0.800";
    private const string MetalPresetRoughness = "[0.501961 0.501961 0.501961 0.000000]";
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

    private static readonly TextureSlotDefinition[] TextureSlots =
    [
        new("TextureColor", NeutralColor, ["basecolor", "base_color", "diffuse", "albedo", "color"]),
        new("TextureNormal", NeutralNormal, ["normal", "norm"]),
        new("TextureRoughness", NeutralRoughness, ["roughness", "rough"]),
        new("TextureAmbientOcclusion", NeutralWhite, ["ambientocclusion", "ambient_occlusion", "occlusion", "ao"]),
        new("TextureMetalness", NeutralBlack, ["metalness", "metallic", "metal"]),
    ];

    private static readonly Regex TextureAssignmentRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>Texture[A-Za-z0-9_]+)\\\"|(?<bareKey>Texture[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]+)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex GeneratedMarkerRegex = new(
        @"\A// DEADLIMIT_GENERATED_CUSTOM_VMAT_V\d+\r?\n",
        RegexOptions.Compiled);

    private static readonly Regex VertexColorGeneratedMarkerRegex = new(
        @"\A// DEADLIMIT_VERTEXCOLOR_VMAT_V\d+\r?\n",
        RegexOptions.Compiled);

    private static readonly Regex InitialScaffoldCommentRegex = new(
        @"^// Initial scaffold:.*\r?\n?",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex StringParameterRegex = new(
        "^(?<prefix>[ \\t]*(?<key>\\\"?[A-Za-z0-9_]+\\\"?)(?:(?:[ \\t]*=[ \\t]*)|[ \\t]+))(?<valueToken>\\\"[^\\\"\\r\\n]*\\\"|[+-]?(?:\\d+(?:\\.\\d*)?|\\.\\d+))(?<suffix>[^\\r\\n]*)(?<carriageReturn>\\r?)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTexturesBlockRegex = new(
        "(?ms)^[ \\t]*\\\"Compiled Textures\\\"[ \\t]*\\r?\\n[ \\t]*\\{.*?^[ \\t]*\\}[ \\t]*\\r?\\n?",
        RegexOptions.Compiled);

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
        IReadOnlyDictionary<string, string> knownTargetResources,
        StringBuilder log,
        CancellationToken cancellationToken,
        bool regenerateExistingMaterials = false)
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

        var rootPngFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(path => TextureSourceExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        SyncTextureSourceFolder(rootPngFiles, textureFolder, cancellationToken, log);

        var textureCandidates = rootPngFiles
            .Select(path => ParseTextureCandidate(path, materialResourceFolder))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .ToArray();

        log.AppendLine($"Custom materials detected: {customReferences.Length}");
        log.AppendLine($"Custom texture sources synchronized from project root: {rootPngFiles.Length}");
        log.AppendLine($"Custom texture source folder: {textureFolder}");
        log.AppendLine("Custom texture naming: <material>_color|diffuse|basecolor|albedo, _normal, _rough|roughness, _ao|occlusion, _metal|metalness|metallic; specialty Texture* fields may also bind by matching the material prefix plus the Texture parameter semantic name.");
        log.AppendLine("Vertex-color naming: any custom material whose name contains 'vertexcolor' (prefix, suffix, or middle; case-insensitive) is prepared from the retail vertcolor_pbr_basic material and does not consume project color textures.");
        log.AppendLine("Metal naming: any custom material whose name contains 'metal' (prefix, suffix, or middle; case-insensitive) receives the metal preset: Metalness 0.8 and Roughness 128/255. The modifier is independent from vertexcolor and may be combined with it.");

        var targetResources = AllocateStableTargetResources(
            customReferences,
            materialResourceFolder,
            knownTargetResources,
            log);
        var remaps = new List<VmdlMaterialRemap>();
        var vmatResources = new List<string>();
        var created = 0;
        var preserved = 0;
        var autoBoundTextures = 0;
        var managedUpdates = 0;

        string? retailTemplateText = null;
        string? retailTemplateVpk = null;
        string? vertexColorTemplateText = null;
        string? vertexColorTemplateVpk = null;

        foreach (var customReference in customReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetResource = targetResources[customReference];
            var targetPath = Path.Combine(
                addonContentRoot,
                targetResource.Replace('/', Path.DirectorySeparatorChar));

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            var vertexColorMode = IsVertexColorMaterialReference(customReference);
            var standardBindings = new Dictionary<string, string?>(ResolveTextureBindings(
                customReference,
                customReferences.Length,
                textureCandidates,
                log), StringComparer.OrdinalIgnoreCase);
            if (vertexColorMode)
            {
                // Base color remains vertex-driven. Other matching project textures stay usable.
                standardBindings["TextureColor"] = null;
            }

            if (regenerateExistingMaterials && File.Exists(targetPath))
            {
                File.Delete(targetPath);
                log.AppendLine($"Clean material prepare removed existing VMAT before template regeneration: {targetResource}");
            }

            if (File.Exists(targetPath))
            {
                var existing = File.ReadAllText(targetPath);
                if (vertexColorMode && (IsVertexColorManagedVmat(existing) || IsDeadlimitManagedVmat(existing)))
                {
                    var reconciled = ReconcileVertexColorVmat(
                        existing,
                        customReference,
                        customReferences.Length,
                        rootPngFiles,
                        materialResourceFolder,
                        standardBindings,
                        log,
                        out var boundCount,
                        out var sanitizedCount);

                    if (!string.Equals(existing, reconciled, StringComparison.Ordinal))
                    {
                        File.WriteAllText(targetPath, reconciled);
                        managedUpdates++;
                    }

                    autoBoundTextures += boundCount;
                    preserved++;
                    log.AppendLine($"Vertex-color VMAT reconciled: {customReference} -> {targetResource}");
                    log.AppendLine($"  texture fallbacks/defaults applied: {sanitizedCount}");
                }
                else if (IsDeadlimitManagedVmat(existing))
                {
                    var reconciled = ReconcileManagedVmat(
                        existing,
                        customReference,
                        customReferences.Length,
                        rootPngFiles,
                        materialResourceFolder,
                        standardBindings,
                        log,
                        out var boundCount,
                        out var sanitizedCount);

                    if (!string.Equals(existing, reconciled, StringComparison.Ordinal))
                    {
                        File.WriteAllText(targetPath, reconciled);
                        managedUpdates++;
                    }

                    autoBoundTextures += boundCount;
                    preserved++;
                    log.AppendLine($"Custom VMAT managed texture inputs reconciled: {customReference} -> {targetResource}");
                    log.AppendLine($"  managed texture fallbacks/defaults applied: {sanitizedCount}");
                }
                else
                {
                    preserved++;
                    log.AppendLine($"Existing custom VMAT preserved for registry-backed texture synchronization: {customReference} -> {targetResource}");
                }
            }
            else if (vertexColorMode)
            {
                if (vertexColorTemplateText is null)
                {
                    (vertexColorTemplateText, vertexColorTemplateVpk) = DecompileRetailMaterialTemplate(
                        manifest,
                        VertexColorTemplateMaterial,
                        cancellationToken);
                }

                var generated = BuildVertexColorVmat(
                    vertexColorTemplateText,
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
                    $"Vertex-color VMAT created from retail template: {customReference} -> {targetResource} | template {VertexColorTemplateMaterial} | VPK {vertexColorTemplateVpk}");
                log.AppendLine($"  inherited texture-source paths neutralized/defaulted: {sanitizedCount}");
            }
            else
            {
                if (string.IsNullOrWhiteSpace(templateMaterialResource))
                {
                    throw new InvalidOperationException(
                        $"Custom material '{customReference}' needs a new VMAT, but Deadlimit could not infer one unique retail body/skin/head/face material to inherit shader and non-texture character settings from.");
                }

                if (retailTemplateText is null)
                {
                    (retailTemplateText, retailTemplateVpk) = DecompileRetailMaterialTemplate(
                        manifest,
                        templateMaterialResource,
                        cancellationToken);
                }

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
                    $"Custom VMAT created from retail character template with managed texture inputs: {customReference} -> {targetResource} | template {templateMaterialResource} | VPK {retailTemplateVpk}");
                log.AppendLine($"  inherited texture-source paths neutralized/defaulted: {sanitizedCount}");
            }

            remaps.Add(new VmdlMaterialRemap(customReference, targetResource));
            vmatResources.Add(targetResource);
        }

        log.AppendLine($"Custom textures auto-bound in current PREPARE: {autoBoundTextures}");
        log.AppendLine($"Existing Deadlimit-managed VMAT files updated in current PREPARE: {managedUpdates}");
        log.AppendLine("Custom VMAT ownership policy: files carrying a DEADLIMIT_GENERATED_CUSTOM_VMAT marker remain managed by Deadlimit; PREPARE may update their Texture* source assignments, required texture-enable combo state, and explicit material-name modifiers such as metal. Other non-texture Material Editor edits are preserved.");
        log.AppendLine("Custom VMAT ownership policy: files carrying a DEADLIMIT_VERTEXCOLOR_VMAT marker are managed for vertex-color behavior plus explicit material-name modifiers such as metal; project color-texture auto-binding intentionally skips them.");
        log.AppendLine("Custom VMAT ownership policy: generated markers and the project .deadlimit ownership registry identify texture-managed VMAT files even after Material Editor replaces the first-line marker.");
        log.AppendLine("Custom VMAT scaffold policy: inherit the current hero character material so shader, outline/NPR colors, strengths, thicknesses and other non-texture tuning survive, but never inherit unresolved hero texture-source paths.");
        log.AppendLine("Custom texture policy: the project-root PNG/TGA/JPG/TIFF set is authoritative for Deadlimit-managed texture slots on every PREPARE. Adding a matching texture binds it; removing it reverts the managed slot to its safe default/fallback. Derived texture copies absent from the project root are removed from the addon texture-source folder.");

        return new CustomMaterialAuthoringResult(
            remaps,
            customReferences.Length,
            created,
            preserved,
            rootPngFiles.Length,
            materialContentFolder,
            vmatResources);
    }

    private static void SyncTextureSourceFolder(
        IReadOnlyList<string> rootPngFiles,
        string textureFolder,
        CancellationToken cancellationToken,
        StringBuilder log)
    {
        var sourceNames = rootPngFiles
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var derivedPng in Directory.EnumerateFiles(textureFolder, "*", SearchOption.TopDirectoryOnly)
                     .Where(path => TextureSourceExtensions.Contains(Path.GetExtension(path))))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (sourceNames.Contains(Path.GetFileName(derivedPng)))
            {
                continue;
            }

            File.Delete(derivedPng);
            removed++;
        }

        foreach (var sourcePng in rootPngFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePng, Path.Combine(textureFolder, Path.GetFileName(sourcePng)), overwrite: true);
        }

        log.AppendLine($"Derived custom texture files removed because their project-root source disappeared: {removed}");
    }

    private (string Text, string VpkPath) DecompileRetailMaterialTemplate(
        ProjectManifest manifest,
        string templateMaterialResource,
        CancellationToken cancellationToken)
    {
        var compiledResourcePaths = ToCompiledMaterialResourcePaths(templateMaterialResource)
            .Select(NormalizeResourcePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var compiledLeafNames = compiledResourcePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var leafMatches = new List<(string VpkPath, string EntryPath)>();

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
                if (compiledResourcePaths.Contains(entryPath))
                {
                    return DecompileRetailMaterialEntry(vpkPath, entryPath, cancellationToken);
                }

                if (compiledLeafNames.Contains(Path.GetFileName(entryPath)))
                {
                    leafMatches.Add((vpkPath, entryPath));
                }
            }
        }

        var uniqueLeafPaths = leafMatches
            .Select(match => match.EntryPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueLeafPaths.Length == 1)
        {
            var resolvedEntryPath = uniqueLeafPaths[0];
            var resolved = leafMatches.First(match => string.Equals(
                match.EntryPath,
                resolvedEntryPath,
                StringComparison.OrdinalIgnoreCase));

            return DecompileRetailMaterialEntry(
                resolved.VpkPath,
                resolved.EntryPath,
                cancellationToken);
        }

        if (uniqueLeafPaths.Length > 1)
        {
            throw new InvalidOperationException(
                $"Retail material template '{templateMaterialResource}' was not found by exact resource path, " +
                "and filename-only resolution was ambiguous. " +
                $"Matches: {string.Join(", ", uniqueLeafPaths)}");
        }

        throw new InvalidOperationException(
            $"Could not find retail material template '{templateMaterialResource}' in the configured Deadlock VPKs. " +
            $"Tried: {string.Join(", ", compiledResourcePaths)}. " +
            "Run EXTRACT HERO SOURCE against the current retail build and verify the Project8Staging path in SETTINGS.");
    }

    private static (string Text, string VpkPath) DecompileRetailMaterialEntry(
        string vpkPath,
        string entryPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var package = new Package();
        package.Read(vpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidOperationException($"Retail VPK contains no entries: {vpkPath}");

        foreach (var entry in packageEntries.SelectMany(group => group.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var candidatePath = NormalizeResourcePath(entry.GetFullPath());
            if (!string.Equals(candidatePath, entryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            package.ReadEntry(entry, out byte[] rawData);
            using var fileLoader = new GameFileLoader(package, package.FileName);
            using var stream = new MemoryStream(rawData, writable: false);
            using var resource = new Resource { FileName = candidatePath };
            resource.Read(stream);
            using var contentFile = FileExtract.Extract(resource, fileLoader, null);

            if (contentFile.Data is null || contentFile.Data.Length == 0)
            {
                throw new InvalidOperationException(
                    $"ValveResourceFormat found retail material '{candidatePath}', but decompilation produced no VMAT source data.");
            }

            return (Encoding.UTF8.GetString(contentFile.Data.ToArray()), vpkPath);
        }

        throw new InvalidOperationException(
            $"Retail material entry '{entryPath}' disappeared from VPK '{vpkPath}' while preparing the material template.");
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
        var body = RemoveCompiledTexturesBlock(retailTemplateText);
        body = ReconcileTextureInputs(
            body,
            customReference,
            customMaterialCount,
            rootPngFiles,
            materialResourceFolder,
            standardBindings,
            useVertexColorFallbacks: false,
            log,
            out boundCount,
            out sanitizedCount);

        body = EnsureStandardTextureAssignments(body, standardBindings);
        body = ReconcileMetalnessTextureCombo(body, HasBinding(standardBindings, "TextureMetalness"));
        body = ApplyMaterialNameModifiers(body, customReference, vertexColorMode: false);

        return GeneratedMarker + Environment.NewLine +
               ManagedComment + Environment.NewLine +
               body.TrimStart('\r', '\n');
    }

    private static string BuildVertexColorVmat(
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
        var body = RemoveCompiledTexturesBlock(retailTemplateText);
        body = RemoveLegacyUnnumberedVertexColorAssignments(body);
        body = ReconcileTextureInputs(
            body,
            customReference,
            customMaterialCount,
            rootPngFiles,
            materialResourceFolder,
            standardBindings,
            useVertexColorFallbacks: true,
            log,
            out boundCount,
            out sanitizedCount);

        body = EnsureVertexColorTextureAssignments(body, standardBindings);
        body = ReconcileMetalnessTextureCombo(body, HasBinding(standardBindings, "TextureMetalness"));
        body = UpsertStringParameter(body, "F_VERTEX_COLOR", "1");
        body = ApplyMaterialNameModifiers(body, customReference, vertexColorMode: true);

        return VertexColorGeneratedMarker + Environment.NewLine +
               VertexColorManagedComment + Environment.NewLine +
               body.TrimStart('\r', '\n');
    }

    private static string ReconcileManagedVmat(
        string existingText,
        string customReference,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        StringBuilder log,
        out int boundCount,
        out int sanitizedCount)
    {
        var body = GeneratedMarkerRegex.Replace(existingText, string.Empty, 1);
        body = InitialScaffoldCommentRegex.Replace(body, string.Empty);
        body = body.Replace(ManagedComment + "\r\n", string.Empty, StringComparison.Ordinal)
            .Replace(ManagedComment + "\n", string.Empty, StringComparison.Ordinal);

        body = RemoveCompiledTexturesBlock(body);
        body = ReconcileTextureInputs(
            body,
            customReference,
            customMaterialCount,
            rootPngFiles,
            materialResourceFolder,
            standardBindings,
            useVertexColorFallbacks: false,
            log,
            out boundCount,
            out sanitizedCount);

        body = EnsureStandardTextureAssignments(body, standardBindings);
        body = ReconcileMetalnessTextureCombo(body, HasBinding(standardBindings, "TextureMetalness"));
        body = ApplyMaterialNameModifiers(body, customReference, vertexColorMode: false);

        return GeneratedMarker + Environment.NewLine +
               ManagedComment + Environment.NewLine +
               body.TrimStart('\r', '\n');
    }

    private static string ReconcileVertexColorVmat(
        string existingText,
        string customReference,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        StringBuilder log,
        out int boundCount,
        out int sanitizedCount)
    {
        var body = VertexColorGeneratedMarkerRegex.Replace(existingText, string.Empty, 1);
        body = GeneratedMarkerRegex.Replace(body, string.Empty, 1);
        body = InitialScaffoldCommentRegex.Replace(body, string.Empty);
        body = body.Replace(VertexColorManagedComment + "\r\n", string.Empty, StringComparison.Ordinal)
            .Replace(VertexColorManagedComment + "\n", string.Empty, StringComparison.Ordinal)
            .Replace(ManagedComment + "\r\n", string.Empty, StringComparison.Ordinal)
            .Replace(ManagedComment + "\n", string.Empty, StringComparison.Ordinal);

        body = RemoveCompiledTexturesBlock(body);
        body = RemoveLegacyUnnumberedVertexColorAssignments(body);
        body = ReconcileTextureInputs(
            body,
            customReference,
            customMaterialCount,
            rootPngFiles,
            materialResourceFolder,
            standardBindings,
            useVertexColorFallbacks: true,
            log,
            out boundCount,
            out sanitizedCount);

        body = EnsureVertexColorTextureAssignments(body, standardBindings);
        body = ReconcileMetalnessTextureCombo(body, HasBinding(standardBindings, "TextureMetalness"));
        body = UpsertStringParameter(body, "F_VERTEX_COLOR", "1");
        body = ApplyMaterialNameModifiers(body, customReference, vertexColorMode: true);

        return VertexColorGeneratedMarker + Environment.NewLine +
               VertexColorManagedComment + Environment.NewLine +
               body.TrimStart('\r', '\n');
    }

    private static string ReconcileTextureInputs(
        string sourceText,
        string customReference,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        bool useVertexColorFallbacks,
        StringBuilder log,
        out int boundCount,
        out int sanitizedCount)
    {
        var localBoundCount = 0;
        var localSanitizedCount = 0;
        var materialToken = NormalizeMatchToken(GetResourceLeaf(customReference));

        var patched = TextureAssignmentRegex.Replace(sourceText, match =>
        {
            var key = GetTextureKey(match);
            var originalValue = match.Groups["value"].Value;
            var replacement = ResolveTextureReplacement(
                key,
                originalValue,
                materialToken,
                customMaterialCount,
                rootPngFiles,
                materialResourceFolder,
                standardBindings,
                useVertexColorFallbacks,
                log);

            if (replacement.AutoBound)
            {
                localBoundCount++;
            }
            else if (replacement.Sanitized)
            {
                localSanitizedCount++;
            }

            return match.Groups["prefix"].Value + replacement.Value + match.Groups["suffix"].Value;
        });

        boundCount = localBoundCount;
        sanitizedCount = localSanitizedCount;
        return patched;
    }

    private static string EnsureStandardTextureAssignments(
        string text,
        IReadOnlyDictionary<string, string?> standardBindings)
    {
        var missing = new List<string>();
        foreach (var slot in TextureSlots)
        {
            var exists = TextureAssignmentRegex.Matches(text)
                .Any(match => string.Equals(GetTextureKey(match), slot.Key, StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                continue;
            }

            var value = standardBindings.TryGetValue(slot.Key, out var bound) && !string.IsNullOrWhiteSpace(bound)
                ? bound
                : slot.DefaultValue;
            missing.Add($"    {slot.Key} \"{value}\"");
        }

        if (missing.Count == 0)
        {
            return text;
        }

        var closingBrace = text.LastIndexOf('}');
        if (closingBrace < 0)
        {
            throw new InvalidDataException("Generated/inherited VMAT did not contain a closing Layer0 brace.");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = string.Join(newline, missing) + newline;
        return text.Insert(closingBrace, insertion);
    }

    private static string RemoveCompiledTexturesBlock(string text) =>
        CompiledTexturesBlockRegex.Replace(text, string.Empty, 1);

    private static string RemoveLegacyUnnumberedVertexColorAssignments(string text)
    {
        return TextureAssignmentRegex.Replace(text, match =>
            TextureSlots.Any(slot => string.Equals(
                slot.Key,
                GetTextureKey(match),
                StringComparison.OrdinalIgnoreCase))
                ? string.Empty
                : match.Value);
    }

    private static string EnsureVertexColorTextureAssignments(
        string text,
        IReadOnlyDictionary<string, string?> standardBindings)
    {
        var required = new[]
        {
            (Key: "TextureColor1", BindingKey: "TextureColor"),
            (Key: "TextureNormal1", BindingKey: "TextureNormal"),
            (Key: "TextureRoughness1", BindingKey: "TextureRoughness"),
            (Key: "TextureAmbientOcclusion1", BindingKey: "TextureAmbientOcclusion"),
            (Key: "TextureMetalness1", BindingKey: "TextureMetalness"),
        };

        var missing = new List<string>();
        foreach (var slot in required)
        {
            var exists = TextureAssignmentRegex.Matches(text)
                .Any(match => string.Equals(
                    GetTextureKey(match),
                    slot.Key,
                    StringComparison.OrdinalIgnoreCase));
            if (exists)
            {
                continue;
            }

            var value = !string.Equals(slot.BindingKey, "TextureColor", StringComparison.OrdinalIgnoreCase)
                        && standardBindings.TryGetValue(slot.BindingKey, out var bound)
                        && !string.IsNullOrWhiteSpace(bound)
                ? bound
                : GetVertexColorFallback(slot.Key);
            missing.Add($"    \"{slot.Key}\"\t\"{value}\"");
        }

        if (missing.Count == 0)
        {
            return text;
        }

        var closingBrace = text.LastIndexOf('}');
        if (closingBrace < 0)
        {
            throw new InvalidDataException("Vertex-color VMAT did not contain a closing Layer0 brace.");
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return text.Insert(closingBrace, string.Join(newline, missing) + newline);
    }

    private static string ReconcileMetalnessTextureCombo(string text, bool hasMetalnessTexture)
    {
        var desired = hasMetalnessTexture ? "1" : "0";
        var patched = UpsertStringParameter(text, "F_METALNESS_TEXTURE", desired);

        if (hasMetalnessTexture && !HasStringParameter(patched, "F_SPECULAR"))
        {
            patched = UpsertStringParameter(patched, "F_SPECULAR", "1");
        }

        return patched;
    }

    private static string UpsertTextureAssignment(string text, string key, string value)
{
    var useCrLf = text.Contains("\r\n", StringComparison.Ordinal);
    var normalized = useCrLf ? text.Replace("\r\n", "\n", StringComparison.Ordinal) : text;
    var found = false;
    var patched = TextureAssignmentRegex.Replace(normalized, match =>
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

    if (!found)
    {
        var closingBrace = patched.LastIndexOf('}');
        if (closingBrace < 0)
        {
            throw new InvalidDataException("Generated/inherited VMAT did not contain a closing Layer0 brace.");
        }

        patched = patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"\n");
    }

    return useCrLf ? patched.Replace("\n", "\r\n", StringComparison.Ordinal) : patched;
}

    private static bool HasStringParameter(string text, string key) =>
        StringParameterRegex.Matches(text)
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
            throw new InvalidDataException("Generated/inherited VMAT did not contain a closing Layer0 brace.");
        }

        var newline = patched.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"{newline}");
    }

    private static bool HasBinding(IReadOnlyDictionary<string, string?> bindings, string key) =>
        bindings.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value);

    private static bool IsDeadlimitManagedVmat(string text) => GeneratedMarkerRegex.IsMatch(text);

    private static bool IsVertexColorManagedVmat(string text) => VertexColorGeneratedMarkerRegex.IsMatch(text);

    private static bool IsVertexColorMaterialReference(string reference)
    {
        var leaf = Path.GetFileNameWithoutExtension(GetResourceLeaf(reference));
        return NormalizeMatchToken(leaf).Contains("vertexcolor", StringComparison.Ordinal);
    }

    private static bool IsMetalMaterialReference(string reference)
    {
        var leaf = Path.GetFileNameWithoutExtension(GetResourceLeaf(reference));
        return NormalizeMatchToken(leaf).Contains("metal", StringComparison.Ordinal);
    }

    private static string ApplyMaterialNameModifiers(
    string text,
    string customReference,
    bool vertexColorMode)
{
    if (!IsMetalMaterialReference(customReference))
    {
        return text;
    }

    var patched = UpsertStringParameter(text, "g_flMetalness", MetalPresetValue);
    var roughnessKey = vertexColorMode ? "TextureRoughness1" : "TextureRoughness";
    return UpsertTextureAssignment(patched, roughnessKey, MetalPresetRoughness);
}

    private static TextureReplacement ResolveTextureReplacement(
        string key,
        string originalValue,
        string materialToken,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string?> standardBindings,
        bool useVertexColorFallbacks,
        StringBuilder log)
    {
        var semantic = GetTextureSemantic(key);
        var standardBinding = FindStandardBinding(standardBindings, key);
        if (!(useVertexColorFallbacks && string.Equals(semantic, "color", StringComparison.Ordinal))
            && !string.IsNullOrWhiteSpace(standardBinding))
        {
            return new TextureReplacement(standardBinding, AutoBound: true, Sanitized: false);
        }

        var canonicalKey = TrimTrailingDigits(key);
        var knownSlot = TextureSlots.FirstOrDefault(slot => string.Equals(
            slot.Key,
            canonicalKey,
            StringComparison.OrdinalIgnoreCase));
        if (knownSlot is not null)
        {
            var slotFallback = useVertexColorFallbacks
                ? GetVertexColorFallback(key)
                : knownSlot.DefaultValue;
            return new TextureReplacement(slotFallback, AutoBound: false, Sanitized: true);
        }

        var specialty = useVertexColorFallbacks && string.Equals(semantic, "color", StringComparison.Ordinal)
            ? null
            : ResolveSpecialtyTextureBinding(
                key,
                materialToken,
                customMaterialCount,
                rootPngFiles,
                materialResourceFolder);

        if (specialty is not null)
        {
            log.AppendLine($"Custom specialty texture auto-bind {key} -> {specialty}");
            return new TextureReplacement(specialty, AutoBound: true, Sanitized: false);
        }

        if (!LooksLikeTextureSourcePath(originalValue))
        {
            return new TextureReplacement(originalValue, AutoBound: false, Sanitized: false);
        }

        var fallback = useVertexColorFallbacks
            ? GetVertexColorFallback(key)
            : GetTextureFallback(key);
        log.AppendLine($"Custom inherited texture source neutralized {key}: {originalValue} -> {fallback}");
        return new TextureReplacement(fallback, AutoBound: false, Sanitized: true);
    }

    private static bool LooksLikeTextureSourcePath(string value)
    {
        var extension = Path.GetExtension(value.Replace('\\', '/'));
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vtex", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveSpecialtyTextureBinding(
        string key,
        string materialToken,
        int customMaterialCount,
        IReadOnlyList<string> rootPngFiles,
        string materialResourceFolder)
    {
        var semantic = GetTextureSlotToken(key);

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
        if (known is not null)
        {
            return known.DefaultValue;
        }

        return GetTextureSemantic(key) switch
        {
            "color" => NeutralColor,
            "normal" => NeutralNormal,
            "roughness" => NeutralRoughness,
            "ao" => NeutralWhite,
            "metalness" => NeutralBlack,
            _ => NeutralBlack,
        };
    }

    private static string GetVertexColorFallback(string key)
    {
        return GetTextureSemantic(key) switch
        {
            "color" => NeutralWhite,
            "normal" => NeutralNormal,
            "roughness" => NeutralRoughness,
            "ao" => NeutralWhite,
            "metalness" => NeutralBlack,
            _ => NeutralBlack,
        };
    }

    private static string? FindStandardBinding(
        IReadOnlyDictionary<string, string?> standardBindings,
        string textureKey)
    {
        var canonicalKey = TrimTrailingDigits(textureKey);
        foreach (var binding in standardBindings)
        {
            if (string.Equals(binding.Key, canonicalKey, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(binding.Value))
            {
                return binding.Value;
            }
        }

        return null;
    }

    private static string GetTextureSemantic(string key)
    {
        var semantic = GetTextureSlotToken(key);

        if (semantic.Contains("normal", StringComparison.Ordinal))
        {
            return "normal";
        }
        if (semantic.Contains("rough", StringComparison.Ordinal))
        {
            return "roughness";
        }
        if (semantic.Contains("ambientocclusion", StringComparison.Ordinal)
            || semantic.Contains("occlusion", StringComparison.Ordinal)
            || string.Equals(semantic, "ao", StringComparison.Ordinal)
            || semantic.StartsWith("ao", StringComparison.Ordinal))
        {
            return "ao";
        }
        if (semantic.Contains("color", StringComparison.Ordinal)
            || semantic.Contains("albedo", StringComparison.Ordinal)
            || semantic.Contains("diffuse", StringComparison.Ordinal))
        {
            return "color";
        }
        if (semantic.Contains("metal", StringComparison.Ordinal))
        {
            return "metalness";
        }

        return semantic;
    }

    private static string GetTextureSlotToken(string key)
    {
        var raw = key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
            ? key["Texture".Length..]
            : key;
        return NormalizeMatchToken(TrimTrailingDigits(raw));
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

    private static string GetTextureKey(Match match)
    {
        var quoted = match.Groups["quotedKey"];
        return quoted.Success ? quoted.Value : match.Groups["bareKey"].Value;
    }

    private static string GetStringParameterKey(Match match)
        => match.Groups["key"].Value.Trim('"');

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

    private static IReadOnlyDictionary<string, string> AllocateStableTargetResources(
        IReadOnlyList<string> customReferences,
        string materialResourceFolder,
        IReadOnlyDictionary<string, string> knownTargetResources,
        StringBuilder log)
    {
        var usedTargets = knownTargetResources.Values
            .Select(target => TryNormalizeOwnedTarget(target, materialResourceFolder, out var normalized)
                ? normalized
                : null)
            .Where(target => target is not null)
            .Select(target => target!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var claimedKnownTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var customReference in customReferences)
        {
            if (!knownTargetResources.TryGetValue(customReference, out var knownTarget))
            {
                continue;
            }

            if (!TryNormalizeOwnedTarget(knownTarget, materialResourceFolder, out var normalizedTarget))
            {
                log.AppendLine(
                    $"Stored custom material target ignored because it is outside the current addon material folder: {customReference} -> {knownTarget}");
                continue;
            }

            if (!claimedKnownTargets.Add(normalizedTarget))
            {
                log.AppendLine(
                    $"Stored custom material target ignored because another DMX material already claims it: {customReference} -> {normalizedTarget}");
                continue;
            }

            assignments.Add(customReference, normalizedTarget);
            log.AppendLine($"Stable custom material target reused: {customReference} -> {normalizedTarget}");
        }

        foreach (var customReference in customReferences)
        {
            if (assignments.ContainsKey(customReference))
            {
                continue;
            }

            var materialName = MakeResourceToken(GetResourceLeaf(customReference));
            if (materialName.Length == 0)
            {
                materialName = "custom_material";
            }

            var targetResource = AllocateTargetResource(materialResourceFolder, materialName, usedTargets);
            assignments.Add(customReference, targetResource);
            log.AppendLine($"Permanent custom material target allocated: {customReference} -> {targetResource}");
        }

        return assignments;
    }

    private static bool TryNormalizeOwnedTarget(
        string targetResource,
        string materialResourceFolder,
        out string normalizedTarget)
    {
        normalizedTarget = NormalizeResourcePath(targetResource);
        var prefix = NormalizeResourcePath(materialResourceFolder).TrimEnd('/') + "/";
        if (!normalizedTarget.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var leaf = normalizedTarget[prefix.Length..];
        return leaf.Length > ".vmat".Length
               && !leaf.Contains('/')
               && leaf.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase)
               && leaf.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
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
        string DefaultValue,
        IReadOnlyList<string> Suffixes);

    private sealed record TextureCandidate(
        string SlotKey,
        string BaseToken,
        string ResourcePath,
        string FileName);

    private sealed record TextureReplacement(string Value, bool AutoBound, bool Sanitized);
}
