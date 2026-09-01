using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record PrepareAuthoringProgress(string Message);

public sealed record PrepareAuthoringResult(
    string AddonName,
    string AddonContentRoot,
    string SourceVmdlPath,
    int DmxCount,
    int VertexColorAppliedDmxCount,
    int VertexColorMissingDmxCount,
    int VertexColorSkippedDmxCount,
    IReadOnlyList<string> VertexColorWarnings,
    int DmxMaterialReferenceCount,
    int ExistingMaterialRemapCount,
    int AddedMaterialRemapCount,
    int CompatibilityRemapCount,
    int CustomMaterialCount,
    int CustomVmatCreatedCount,
    int CustomVmatPreservedCount,
    int TextureSourceCount,
    string CustomMaterialContentFolder,
    int RetailSourceFilesCopied,
    bool GameOutputCleaned,
    string LogPath);

public sealed class PrepareAuthoringService
{
    private const string GenericEyeFallbackMaterial = "materials/dev/vertcolor_pbr_basic.vmat";
    private static readonly string[] ManagedVmatMarkerPrefixes =
    [
        "// DEADLIMIT_GENERATED_CUSTOM_VMAT_V",
        "// DEADLIMIT_MANAGED_CUSTOM_VMAT_V",
        "// DEADLIMIT_VERTEXCOLOR_VMAT_V",
    ];

    private static readonly Regex InvalidMaterialRegex = new(
        @"materials/models/[A-Za-z0-9_./\\-]+\.vmat",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex DmxMaterialReferenceRegex = new(
        @"materials/(?:[^\0\r\n\t""]+?\.vmat|[A-Za-z0-9_./\\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MaterialRemapRegex = new(
        "\\bfrom\\s*=\\s*\"(?<from>[^\"]+)\"\\s+to\\s*=\\s*\"(?<to>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EyeIdentifierRegex = new(
        @"(^|[^a-z0-9])(eye|eyes|eyeball|pupil|iris)([^a-z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public PrepareAuthoringService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<PrepareAuthoringResult> PrepareAsync(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool regenerateCustomMaterials = false) =>
        Task.Run(
            () => Prepare(manifest, progress, cancellationToken, regenerateCustomMaterials),
            cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken,
        bool regenerateCustomMaterials)
    {
        ValidateEnvironment(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var rootDmxFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.dmx", SearchOption.TopDirectoryOnly)
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rootDmxFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "No .dmx files were found in the project root. Export the current artist model to the project root first.");
        }

        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException(
                "Retail main model is unknown. Run EXTRACT HERO SOURCE once before PREPARE FOR CSDK.");
        }

        var addonIdentity = new AddonIdentityService(_paths).ResolveAndClaim(manifest);
        var addonName = addonIdentity.AddonId;
        var addonContentRoot = addonIdentity.ContentRoot;
        var addonGameRoot = addonIdentity.GameRoot;

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"prepare-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit authoring prepare — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Addon: {addonName}");
        log.AppendLine($"Retail model: {manifest.RetailMainModel}");
        log.AppendLine($"CSDK content root: {addonContentRoot}");
        log.AppendLine($"CSDK game output root: {addonGameRoot}");
        log.AppendLine($"Custom material mode: {(regenerateCustomMaterials ? "clean regeneration" : "preserve artist edits and synchronize project textures")}");
        log.AppendLine();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Validating Vertex Color source pairs before changing CSDK content...", "Проверка пар исходников Vertex Color перед изменением CSDK content...")));

            var vertexColorSourceStates = VertexColorSourceGuard.ValidateForPrepare(
                rootDmxFiles,
                cancellationToken)
                .ToDictionary(
                    state => Path.GetFullPath(VertexColorSidecarService.GetArtistDmxPath(state.SidecarPath)),
                    state => state,
                    StringComparer.OrdinalIgnoreCase);

            foreach (var dmxPath in rootDmxFiles)
            {
                var fullDmxPath = Path.GetFullPath(dmxPath);
                if (!vertexColorSourceStates.TryGetValue(fullDmxPath, out var state))
                {
                    continue;
                }

                log.AppendLine(
                    $"Vertex Color source preflight: {Path.GetFileName(dmxPath)} | " +
                    $"material={state.UsesVertexColorMaterial} | embedded={state.HasEmbeddedVertexColor} | " +
                    $"sidecarExists={state.SidecarExists} | sidecarCurrent={state.SidecarCurrent} | {state.Message}");
            }
            log.AppendLine("Vertex Color FBX policy: *_vertexcolor.fbx is a persistent project source file. PREPARE never deletes it.");
            log.AppendLine();

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Cleaning stale compiled output for this addon...", "Очистка устаревшего compiled output этого аддона...")));

            var gameOutputCleaned = false;
            if (Directory.Exists(addonGameRoot))
            {
                Directory.Delete(addonGameRoot, recursive: true);
                gameOutputCleaned = true;
            }

            log.AppendLine(gameOutputCleaned
                ? $"Removed stale addon runtime output: {addonGameRoot}"
                : $"No stale addon runtime output existed: {addonGameRoot}");
            log.AppendLine("Deadlimit does not compile content during PREPARE FOR CSDK; CSDK12 rebuilds game output from content when launched/compiled.");

            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Refreshing retail authoring template in CSDK content...", "Обновление retail-шаблона модели в CSDK content...")));
            Directory.CreateDirectory(addonContentRoot);

            var sourceCopy = RetailVmdlInheritance.CopyRetailModelSourceTree(manifest, addonContentRoot);
            log.AppendLine($"Retail source template: {sourceCopy.SourceVmdlPath}");
            log.AppendLine($"Retail source files copied: {sourceCopy.FilesCopied}");
            log.AppendLine($"Destination VMDL: {sourceCopy.DestinationVmdlPath}");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Overlaying artist DMX on matching retail render meshes...", "Наложение пользовательских DMX на соответствующие retail render mesh...")));

            var replacedRenderMeshes = RetailVmdlInheritance.OverlayArtistDmx(
                sourceCopy,
                addonContentRoot,
                manifest.Hero,
                rootDmxFiles);

            log.AppendLine($"Artist DMX overlays: {replacedRenderMeshes.Count}");
            foreach (var overlay in replacedRenderMeshes)
            {
                log.AppendLine($"  replace {overlay.ResourcePath}");
                log.AppendLine(
                    $"    vertex color [{overlay.VertexColor.Status}]: {overlay.VertexColor.Message} | " +
                    $"sidecar {overlay.VertexColor.SidecarPath}");
            }

            var vertexColorAppliedCount = replacedRenderMeshes.Count(overlay =>
                overlay.VertexColor.Status == VertexColorSidecarStatus.Applied);
            var vertexColorMissingCount = replacedRenderMeshes.Count(overlay =>
                overlay.VertexColor.Status == VertexColorSidecarStatus.Missing);
            var vertexColorSkippedCount = replacedRenderMeshes.Count(overlay =>
                overlay.VertexColor.Status == VertexColorSidecarStatus.Skipped);
            log.AppendLine($"Vertex Color sidecars applied: {vertexColorAppliedCount}");
            log.AppendLine($"Vertex Color sidecars missing: {vertexColorMissingCount}");
            log.AppendLine($"Vertex Color sidecars skipped: {vertexColorSkippedCount}");

            var dmxMaterialReferences = DiscoverDmxMaterialReferences(rootDmxFiles);
            log.AppendLine($"DMX material references detected: {dmxMaterialReferences.Count}");
            foreach (var materialReference in dmxMaterialReferences)
            {
                log.AppendLine($"  material {materialReference}");
            }

            var vertexColorWarnings = replacedRenderMeshes
                .Where(overlay => overlay.VertexColor.Status != VertexColorSidecarStatus.Applied)
                .Where(overlay =>
                {
                    var fullPath = Path.GetFullPath(overlay.ArtistDmxPath);
                    return vertexColorSourceStates.TryGetValue(fullPath, out var state)
                        && state.NeedsExternalSidecar;
                })
                .Select(overlay => LocalizedText.T(
                    $"{Path.GetFileName(overlay.ArtistDmxPath)}: Vertex Color [{overlay.VertexColor.Status}] — {overlay.VertexColor.Message}",
                    $"{Path.GetFileName(overlay.ArtistDmxPath)}: не удалось безопасно применить Vertex Color sidecar."))
                .ToArray();
            foreach (var warning in vertexColorWarnings)
            {
                log.AppendLine($"WARNING: {warning}");
            }

            if (vertexColorWarnings.Length > 0)
            {
                throw new InvalidOperationException(
                    "Vertex Color source changed during PREPARE after the safety preflight. " +
                    "No successful PREPARE state will be recorded. Export the DMX and Vertex Color FBX again as a matching pair, then rerun PREPARE.\n\n" +
                    string.Join("\n", vertexColorWarnings));
            }

            var authoringMaterialReferences = ExpandWallWormMaterialAliases(dmxMaterialReferences);

            var compatibilityRemaps = DiscoverMaterialRepairs(
                rootDmxFiles,
                dmxMaterialReferences,
                sourceCopy.DestinationVmdlPath,
                manifest.Hero,
                log);

            var existingRemapsBeforePatch = ReadMaterialRemaps(sourceCopy.DestinationVmdlPath);
            var templateCandidates = existingRemapsBeforePatch
                .Concat(compatibilityRemaps)
                .GroupBy(candidate => candidate.From, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();

            var customTemplateMaterial = ChooseLikelyCharacterSurfaceMaterial(templateCandidates, manifest.Hero);
            if (customTemplateMaterial is null)
            {
                var sourceDumpRoot = SafePath.ResolveUnderRoot(
                    manifest.ProjectFolder,
                    manifest.SourceDumpFolderName,
                    "Project source-dump folder");
                var retailTemplateCandidates = DiscoverRetailTemplateMaterialCandidates(
                    sourceCopy.SourceVmdlPath,
                    sourceDumpRoot,
                    log);
                customTemplateMaterial = ChooseLikelyCharacterSurfaceMaterial(retailTemplateCandidates, manifest.Hero);

                if (customTemplateMaterial is not null)
                {
                    log.AppendLine($"Custom material retail template inferred from original extracted retail DMX: {customTemplateMaterial}");
                }
            }

            var resolvedMaterialSources = templateCandidates
                .Select(remap => remap.From)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Preparing addon-owned custom materials...", "Подготовка custom-материалов аддона...")));

            if (regenerateCustomMaterials)
            {
                BackupCustomMaterialsForCleanPrepare(
                    manifest,
                    addonContentRoot,
                    addonName,
                    log,
                    cancellationToken);
            }

            ProjectTextureBindingService.MarkLegacyManagedMaterialsForMigration(
                addonContentRoot,
                addonName,
                log,
                cancellationToken);

            var previousOwnership = ManagedCustomMaterialRegistryStore.Load(manifest);
            var knownMaterialTargets = ManagedCustomMaterialRegistryStore.BuildTargetMap(previousOwnership);

            var customMaterials = new CustomMaterialAuthoringService(_paths).Prepare(
                manifest,
                addonName,
                addonContentRoot,
                authoringMaterialReferences,
                resolvedMaterialSources,
                customTemplateMaterial,
                knownMaterialTargets,
                log,
                cancellationToken,
                regenerateCustomMaterials);

            var currentOwnership = ManagedCustomMaterialRegistryStore.BuildCurrent(customMaterials.Remaps);
            var knownOwnership = ManagedCustomMaterialRegistryStore.MergeKnownWithCurrent(
                previousOwnership,
                currentOwnership);

            ProjectTextureBindingService.Synchronize(
                manifest,
                addonName,
                addonContentRoot,
                customMaterials,
                knownOwnership,
                log,
                cancellationToken);

            var finalTextureRepairs = FinalizeManagedCustomMaterials(
                customMaterials,
                addonContentRoot,
                log,
                cancellationToken);
            log.AppendLine($"Managed custom VMAT final missing-source repairs: {finalTextureRepairs}");

            var exactCustomMaterialRemaps = ResolveExactCustomMaterialRemaps(
                dmxMaterialReferences,
                customMaterials.Remaps,
                log);

            var generatedRemaps = compatibilityRemaps
                .Concat(customMaterials.Remaps)
                .Concat(exactCustomMaterialRemaps)
                .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            log.AppendLine($"Compatibility material remaps generated: {compatibilityRemaps.Count}");
            log.AppendLine($"Custom material remaps generated: {customMaterials.Remaps.Count}");
            log.AppendLine($"Exact custom DMX material remaps generated: {exactCustomMaterialRemaps.Count}");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Applying narrow CSDK compatibility patches to retail VMDL...", "Применение необходимых CSDK-патчей совместимости к retail VMDL...")));

            var patchResult = RetailVmdlInheritance.PatchAuthoringVmdl(
                sourceCopy.DestinationVmdlPath,
                generatedRemaps);

            log.AppendLine($"Retail material remaps preserved: {patchResult.ExistingMaterialRemapCount}");
            log.AppendLine($"VMDL material remaps added: {patchResult.AddedMaterialRemapCount}");
            foreach (var remap in generatedRemaps)
            {
                log.AppendLine($"  candidate {remap.From} -> {remap.To}");
            }

            log.AppendLine($"Retail RenderMeshFile entries preserved: {patchResult.RenderMeshCount}");
            foreach (var removedClass in patchResult.RemovedClasses)
            {
                log.AppendLine($"Removed current-CSDK-incompatible root node: {removedClass}");
            }

            log.AppendLine("VMDL policy: preserve the extracted retail document/header/order and patch only proven incompatible or project-owned data.");
            log.AppendLine("Material policy: DMX material-reference count is diagnostic only; VMDL remaps are a separate concept.");
            log.AppendLine("Material policy: preserve retail reuse, generate narrow compatibility repairs, and route unresolved Wall Worm custom slots to addon-owned VMAT files.");
            log.AppendLine("Material policy: direct materials/<name>.vmat references from Wall Worm are paired with an extensionless authoring alias, so spaces and the explicit .vmat suffix survive into the final VMDL remap.");
            log.AppendLine("Material policy: copy retail/template material parameters only when a custom VMAT is first created; later PREPARE runs preserve manual VMAT edits and synchronize only matching project-root texture sources.");
            log.AppendLine("Render-mesh policy: preserve retail RenderMeshList/bodygroups/LODs; overlay artist DMX at the original render-mesh resource path.");
            log.AppendLine("Vertex Color policy: *_vertexcolor.fbx stays beside the artist DMX as persistent source data; repeated PREPARE, BUILD FOR TEST and ONLINE activation may reuse it safely.");

            manifest.SourceVmdl = sourceCopy.DestinationVmdlPath;
            manifest.CompiledVmdl = null;
            ProjectStore.Save(manifest);
            ManagedCustomMaterialRegistryStore.Save(manifest, knownOwnership);

            log.AppendLine();
            log.AppendLine("RESULT: AUTHORING CONTENT PREPARED; ADDON GAME OUTPUT CLEAN");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Authoring content prepared. Launch CSDK to rebuild clean game output.", "Authoring content подготовлен. Запустите CSDK для чистой пересборки game output.")));

            return new PrepareAuthoringResult(
                addonName,
                addonContentRoot,
                sourceCopy.DestinationVmdlPath,
                replacedRenderMeshes.Count,
                vertexColorAppliedCount,
                vertexColorMissingCount,
                vertexColorSkippedCount,
                vertexColorWarnings,
                dmxMaterialReferences.Count,
                patchResult.ExistingMaterialRemapCount,
                patchResult.AddedMaterialRemapCount,
                compatibilityRemaps.Count,
                customMaterials.CustomMaterialCount,
                customMaterials.CreatedVmatCount,
                customMaterials.PreservedVmatCount,
                customMaterials.TextureSourceCount,
                customMaterials.MaterialContentFolder,
                sourceCopy.FilesCopied,
                gameOutputCleaned,
                logPath);
        }
        catch (Exception ex)
        {
            log.AppendLine();
            log.AppendLine($"RESULT: FAILED — {ex}");
            File.WriteAllText(logPath, log.ToString());
            throw;
        }
    }

    private static void BackupCustomMaterialsForCleanPrepare(
        ProjectManifest manifest,
        string addonContentRoot,
        string addonName,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var materialFolder = Path.Combine(addonContentRoot, "materials", addonName);
        if (!Directory.Exists(materialFolder))
        {
            log.AppendLine("Clean material prepare: no existing custom VMAT files required backup.");
            return;
        }

        var sourceFiles = Directory.EnumerateFiles(materialFolder, "*.vmat", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourceFiles.Length == 0)
        {
            log.AppendLine("Clean material prepare: no existing custom VMAT files required backup.");
            return;
        }

        var backupFolder = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            "backups",
            "materials",
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        Directory.CreateDirectory(backupFolder);

        foreach (var sourcePath in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(
                sourcePath,
                Path.Combine(backupFolder, Path.GetFileName(sourcePath)),
                overwrite: false);
        }

        log.AppendLine($"Clean material prepare backup: {sourceFiles.Length} VMAT file(s) -> {backupFolder}");
    }

    private static int FinalizeManagedCustomMaterials(
        CustomMaterialAuthoringResult customMaterials,
        string addonContentRoot,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var repairedCount = 0;

        foreach (var vmatResourcePath in customMaterials.VmatResourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var vmatPath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                vmatResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Final managed VMAT validation target");

            if (!File.Exists(vmatPath))
            {
                throw new FileNotFoundException(
                    $"Custom VMAT reported by PREPARE was not found for final validation: {vmatResourcePath}",
                    vmatPath);
            }

            var text = File.ReadAllText(vmatPath);
            if (!ManagedVmatMarkerPrefixes.Any(prefix =>
                    text.StartsWith(prefix, StringComparison.Ordinal)))
            {
                continue;
            }

            var repaired = ManagedVmatTextureSafetyNet.RepairMissingTextureSources(
                text,
                addonContentRoot,
                log,
                out var currentRepairs);

            if (!string.Equals(text, repaired, StringComparison.Ordinal))
            {
                File.WriteAllText(vmatPath, repaired);
            }

            repairedCount += currentRepairs;

            var unresolved = ManagedVmatTextureSafetyNet.FindMissingTextureSources(
                repaired,
                addonContentRoot);

            if (unresolved.Count > 0)
            {
                throw new InvalidDataException(
                    $"Managed custom VMAT '{vmatResourcePath}' still references missing texture source(s) after the final PREPARE safety pass: " +
                    string.Join(", ", unresolved));
            }
        }

        return repairedCount;
    }

    private void ValidateEnvironment(ProjectManifest manifest)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        if (!Directory.Exists(_paths.CsdkContentRoot))
        {
            throw new DirectoryNotFoundException($"CSDK content root was not found: {_paths.CsdkContentRoot}");
        }

        if (!Directory.Exists(_paths.CsdkGameRoot))
        {
            throw new DirectoryNotFoundException($"CSDK game root was not found: {_paths.CsdkGameRoot}");
        }
    }

    private static IReadOnlyList<string> DiscoverDmxMaterialReferences(IEnumerable<string> dmxFiles)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dmxPath in dmxFiles)
        {
            var raw = File.ReadAllBytes(dmxPath);
            var text = Encoding.Latin1.GetString(raw).Replace('\\', '/');

            foreach (Match match in DmxMaterialReferenceRegex.Matches(text))
            {
                var value = match.Value.TrimEnd('/', '.', '-', ' ');
                var extension = Path.GetExtension(value);

                if (extension.Length > 0
                    && !string.Equals(extension, ".vmat", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                references.Add(value);
            }
        }

        return references
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ContainsVertexColorToken(string materialReference)
    {
        var leaf = Path.GetFileNameWithoutExtension(materialReference.Replace('\\', '/'));
        var token = new string(leaf.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        return token.Contains("vertexcolor", StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> ExpandWallWormMaterialAliases(
        IReadOnlyList<string> dmxMaterialReferences)
    {
        var expanded = new HashSet<string>(dmxMaterialReferences, StringComparer.OrdinalIgnoreCase);

        foreach (var reference in dmxMaterialReferences)
        {
            if (TryGetDirectRootVmatAlias(reference, out var alias))
            {
                expanded.Add(alias);
            }
        }

        return expanded
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<VmdlMaterialRemap> ResolveExactCustomMaterialRemaps(
        IReadOnlyList<string> dmxMaterialReferences,
        IReadOnlyList<VmdlMaterialRemap> customRemaps,
        StringBuilder log)
    {
        var customByAlias = customRemaps
            .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<VmdlMaterialRemap>();
        foreach (var reference in dmxMaterialReferences)
        {
            if (!TryGetDirectRootVmatAlias(reference, out var alias)
                || !customByAlias.TryGetValue(alias, out var customRemap))
            {
                continue;
            }

            result.Add(new VmdlMaterialRemap(reference, customRemap.To));
            log.AppendLine($"Exact Wall Worm custom material remap: {reference} -> {customRemap.To} (authoring alias {alias})");
        }

        return result;
    }

    private static bool TryGetDirectRootVmatAlias(string reference, out string alias)
    {
        const string materialPrefix = "materials/";
        const string vmatExtension = ".vmat";

        alias = string.Empty;
        var normalized = reference.Replace('\\', '/').TrimStart('/');
        if (!normalized.StartsWith(materialPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = normalized[materialPrefix.Length..];
        if (relative.Contains('/')
            || !relative.EndsWith(vmatExtension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var name = relative[..^vmatExtension.Length].Trim();
        if (name.Length == 0)
        {
            return false;
        }

        alias = materialPrefix + name;
        return true;
    }

    private static IReadOnlyList<VmdlMaterialRemap> DiscoverRetailTemplateMaterialCandidates(
        string sourceVmdlPath,
        string sourceDumpRoot,
        StringBuilder log)
    {
        var retailDmxFiles = new List<string>();
        foreach (var renderMesh in RetailVmdlInheritance.ReadRenderMeshes(sourceVmdlPath))
        {
            var sourceDmxPath = SafePath.ResolveUnderRoot(
                sourceDumpRoot,
                renderMesh.Filename.Replace('/', Path.DirectorySeparatorChar),
                "Retail render-mesh source");

            if (File.Exists(sourceDmxPath))
            {
                retailDmxFiles.Add(sourceDmxPath);
            }
        }

        var materialReferences = DiscoverDmxMaterialReferences(retailDmxFiles);
        log.AppendLine($"Retail template material candidates discovered from original extracted DMX: {materialReferences.Count}");
        foreach (var reference in materialReferences)
        {
            log.AppendLine($"  retail template candidate {reference}");
        }

        return materialReferences
            .Select(reference => new VmdlMaterialRemap(reference, reference))
            .ToArray();
    }

    private static List<VmdlMaterialRemap> DiscoverMaterialRepairs(
        IReadOnlyList<string> dmxFiles,
        IReadOnlyList<string> dmxMaterialReferences,
        string vmdlPath,
        string hero,
        StringBuilder log)
    {
        var remaps = new Dictionary<string, VmdlMaterialRemap>(StringComparer.OrdinalIgnoreCase);

        foreach (var materialReference in dmxMaterialReferences)
        {
            var match = InvalidMaterialRegex.Match(materialReference);
            if (!match.Success || !string.Equals(match.Value, materialReference, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var to = materialReference["materials/".Length..];
            remaps.TryAdd(materialReference, new VmdlMaterialRemap(materialReference, to));
        }

        var existingRemaps = ReadMaterialRemaps(vmdlPath);
        var targetCandidates = existingRemaps
            .Concat(remaps.Values)
            .GroupBy(candidate => candidate.From, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var eyeFallback = DiscoverEyeFallbackRepair(
            dmxFiles,
            dmxMaterialReferences,
            existingRemaps,
            targetCandidates,
            hero,
            log);

        if (eyeFallback is not null)
        {
            remaps.TryAdd(eyeFallback.From, eyeFallback);
        }

        return remaps.Values
            .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static VmdlMaterialRemap? DiscoverEyeFallbackRepair(
        IReadOnlyList<string> dmxFiles,
        IReadOnlyList<string> dmxMaterialReferences,
        IReadOnlyList<VmdlMaterialRemap> existingRemaps,
        IReadOnlyList<VmdlMaterialRemap> targetCandidates,
        string hero,
        StringBuilder log)
    {
        var hasGenericFallback = dmxMaterialReferences.Any(reference => string.Equals(
            reference,
            GenericEyeFallbackMaterial,
            StringComparison.OrdinalIgnoreCase));

        if (!hasGenericFallback)
        {
            log.AppendLine("Eye fallback repair: generic dev material is not referenced by the artist DMX.");
            return null;
        }

        var hasEyeIdentifier = dmxFiles.Any(DmxContainsEyeIdentifier);
        if (!hasEyeIdentifier)
        {
            log.AppendLine("Eye fallback repair: generic dev material is present, but no eye-related mesh/token was found in the same artist DMX set.");
            return null;
        }

        if (existingRemaps.Any(remap => string.Equals(
                remap.From,
                GenericEyeFallbackMaterial,
                StringComparison.OrdinalIgnoreCase)))
        {
            log.AppendLine("Eye fallback repair: retail VMDL already contains the generic dev-material remap; no inferred repair needed.");
            return null;
        }

        var target = ChooseLikelyCharacterSurfaceMaterial(targetCandidates, hero);
        if (target is null)
        {
            log.AppendLine(
                "Eye fallback repair: artist DMX contains both an eye identifier and the generic dev material, " +
                "but no unique body/head/face/skin target could be inferred from either retail remaps or pending path repairs. " +
                "No automatic remap was added.");
            return null;
        }

        log.AppendLine(
            $"Eye fallback repair inferred from artist DMX material set: {GenericEyeFallbackMaterial} -> {target}");

        return new VmdlMaterialRemap(GenericEyeFallbackMaterial, target);
    }

    private static bool DmxContainsEyeIdentifier(string dmxPath)
    {
        var raw = File.ReadAllBytes(dmxPath);
        var text = Encoding.Latin1.GetString(raw);
        return EyeIdentifierRegex.IsMatch(text);
    }

    private static IReadOnlyList<VmdlMaterialRemap> ReadMaterialRemaps(string vmdlPath)
    {
        var text = File.ReadAllText(vmdlPath);
        return MaterialRemapRegex.Matches(text)
            .Select(match => new VmdlMaterialRemap(
                match.Groups["from"].Value.Replace('\\', '/'),
                match.Groups["to"].Value.Replace('\\', '/')))
            .ToArray();
    }

    private static string? ChooseLikelyCharacterSurfaceMaterial(
        IReadOnlyList<VmdlMaterialRemap> candidateRemaps,
        string hero)
    {
        var heroToken = NormalizeToken(hero);

        var scored = candidateRemaps
            .Select(remap => remap.To)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileToken = NormalizeToken(Path.GetFileNameWithoutExtension(path));
                var score = 0;

                if (fileToken.Contains("body", StringComparison.Ordinal))
                {
                    score += 500;
                }
                if (fileToken.Contains("skin", StringComparison.Ordinal))
                {
                    score += 420;
                }
                if (fileToken.Contains("head", StringComparison.Ordinal))
                {
                    score += 360;
                }
                if (fileToken.Contains("face", StringComparison.Ordinal))
                {
                    score += 320;
                }
                if (heroToken.Length > 0 && fileToken.Contains(heroToken, StringComparison.Ordinal))
                {
                    score += 50;
                }

                if (fileToken.Contains("wing", StringComparison.Ordinal)
                    || fileToken.Contains("gear", StringComparison.Ordinal)
                    || fileToken.Contains("weapon", StringComparison.Ordinal)
                    || fileToken.Contains("gun", StringComparison.Ordinal))
                {
                    score -= 300;
                }

                return (Path: path, Score: score);
            })
            .Where(item => item.Score >= 300)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scored.Length == 0)
        {
            return null;
        }

        if (scored.Length > 1 && scored[0].Score == scored[1].Score)
        {
            return null;
        }

        return scored[0].Path;
    }

    private static string NormalizeToken(string value) =>
        new(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

}
