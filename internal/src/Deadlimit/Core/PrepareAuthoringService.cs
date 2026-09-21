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
    HeroSelectScenePreparationResult? HeroSelectScene,
    string LogPath);

public sealed class PrepareAuthoringService
{
    private const string GenericEyeFallbackMaterial = "materials/dev/vertcolor_pbr_basic.vmat";
    private const string GenericEyeFallbackMaterialStem = "materials/dev/vertcolor_pbr_basic";
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
        PrepareAuthoringOptions? options = null) =>
        Task.Run(
            () => Prepare(
                manifest,
                progress,
                cancellationToken,
                options ?? PrepareAuthoringOptions.PreserveArtistWork),
            cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken,
        PrepareAuthoringOptions options)
    {
        var regenerateCustomMaterials = options.Resets(PrepareResetSections.Materials);
        ValidateEnvironment(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var authoringFiles = ProjectAuthoringLayout.EnumerateAuthoringFiles(manifest).ToArray();
        var rootDmxFiles = ProjectAuthoringLayout.SelectFirstByFileName(manifest, authoringFiles
            .Where(path => Path.GetExtension(path).Equals(".dmx", StringComparison.OrdinalIgnoreCase))
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
            ).ToArray();
        var rootFbxFiles = ProjectAuthoringLayout.SelectFirstByFileName(manifest, authoringFiles
            .Where(path => Path.GetExtension(path).Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
            ).ToArray();
        var rootGltfFiles = ProjectAuthoringLayout.SelectFirstByFileName(manifest, authoringFiles
            .Where(path => Path.GetExtension(path).Equals(".gltf", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            ).ToArray();

        if (rootDmxFiles.Length + rootFbxFiles.Length + rootGltfFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "No DMX, FBX, glTF, or GLB model files were found in 1authoring. Export the current artist model there first.");
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
        log.AppendLine($"1authoring model sources: DMX={rootDmxFiles.Length}, FBX={rootFbxFiles.Length}, glTF/GLB={rootGltfFiles.Length}");
        log.AppendLine($"Reset sections: {options.ResetSections}");
        log.AppendLine($"Prepare hero-select scene: {options.PrepareHeroSelectScene}");
        log.AppendLine($"Selected-section backup: {(options.CreateBackup && options.ResetSections != PrepareResetSections.None ? "enabled" : "disabled")}");
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
            log.AppendLine("Vertex Color sidecar policy: *_vertexcolor.fbx is required only for DMX meshes that use a Vertex Color material but do not contain a validated embedded color stream.");
            log.AppendLine("Primary FBX policy: a normal artist FBX is self-contained; materials whose names contain 'vertexcolor' do not require a separate *_vertexcolor.fbx pair and never block PREPARE for that reason.");
            log.AppendLine("Vertex Color FBX policy: *_vertexcolor.fbx is a persistent project source file. PREPARE never deletes it.");
            log.AppendLine();

            cancellationToken.ThrowIfCancellationRequested();
            var cleanGameOutput = options.ResetSections != PrepareResetSections.None;
            progress?.Report(new PrepareAuthoringProgress(cleanGameOutput
                ? LocalizedText.T("Cleaning compiled output for the selected reset...", "Очистка compiled output для выбранного сброса...")
                : LocalizedText.T("Preserving compiled output for incremental builds...", "Сохранение compiled output для инкрементальных сборок...")));

            var gameOutputCleaned = false;
            if (cleanGameOutput && Directory.Exists(addonGameRoot))
            {
                Directory.Delete(addonGameRoot, recursive: true);
                gameOutputCleaned = true;
            }

            if (cleanGameOutput)
            {
                log.AppendLine(gameOutputCleaned
                    ? $"Removed addon runtime output for explicit reset: {addonGameRoot}"
                    : $"No addon runtime output existed for explicit reset: {addonGameRoot}");
            }
            else
            {
                log.AppendLine($"Ordinary PREPARE preserved addon runtime output for incremental BUILD & TEST: {addonGameRoot}");
            }
            log.AppendLine("Deadlimit does not compile content during PREPARE FOR CSDK; CSDK12 rebuilds changed game output from content when launched or compiled.");

            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Refreshing retail authoring template in CSDK content...", "Обновление retail-шаблона модели в CSDK content...")));
            Directory.CreateDirectory(addonContentRoot);

            if (options.CreateBackup && options.ResetSections != PrepareResetSections.None)
            {
                BackupSelectedSections(
                    manifest,
                    addonContentRoot,
                    addonName,
                    options.ResetSections,
                    log,
                    cancellationToken);
            }
            else if (options.ResetSections != PrepareResetSections.None)
            {
                log.AppendLine("Selected-section backup skipped by explicit user choice.");
            }

            HeroSelectScenePreparationResult? heroSelectScene = null;
            if (options.PrepareHeroSelectScene)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new PrepareAuthoringProgress(LocalizedText.T(
                    "Preparing an editable hero-select scene...",
                    "Подготовка редактируемой сцены выбора героя...")));
                heroSelectScene = new HeroSelectScenePreparationService(_paths).Prepare(
                    manifest,
                    addonContentRoot,
                    addonGameRoot,
                    cancellationToken);
                log.AppendLine($"Hero-select prefab: {heroSelectScene.HeroPrefabId}");
                log.AppendLine($"Hero-select source VPK: {heroSelectScene.SourceVpkPath}");
                log.AppendLine(
                    $"Hero-select VMAP: created={heroSelectScene.CreatedCount}; preserved={heroSelectScene.PreservedCount}");
                log.AppendLine(
                    $"Hero-select authoring resources: created={heroSelectScene.AuthoringResourceCreatedCount}; preserved={heroSelectScene.AuthoringResourcePreservedCount}");
                log.AppendLine(
                    $"Hero-select runtime resources: created={heroSelectScene.RuntimeCreatedCount}; preserved={heroSelectScene.RuntimePreservedCount}");
                foreach (var scenePath in heroSelectScene.ScenePaths)
                {
                    log.AppendLine($"Hero-select scene: {scenePath}");
                }
            }

            var sourceCopy = RetailVmdlInheritance.CopyRetailModelSourceTree(
                manifest,
                addonContentRoot,
                preserveExistingPhysics: !options.Resets(PrepareResetSections.Physics),
                preserveExistingEffects: !options.Resets(PrepareResetSections.Effects));
            log.AppendLine($"Retail source template: {sourceCopy.SourceVmdlPath}");
            log.AppendLine($"Retail source files copied: {sourceCopy.FilesCopied}");
            log.AppendLine($"Destination VMDL: {sourceCopy.DestinationVmdlPath}");

            var retailPhysics = RetailPhysicsAuthoringService.EnsureRetailJoints(
                manifest,
                sourceCopy.DestinationVmdlPath,
                replaceExisting: options.Resets(PrepareResetSections.Physics));
            log.AppendLine(retailPhysics.Added
                ? $"Retail physics initialized: {retailPhysics.JointCount} ragdoll joints, " +
                  $"{retailPhysics.ClothChainCount} cloth chains"
                : "Existing authoring physics preserved.");
            foreach (var warning in retailPhysics.Warnings)
            {
                log.AppendLine($"Retail physics warning: {warning}.");
            }
            var repairedClothChains = RetailPhysicsAuthoringService.RepairInvalidClothParentAnchors(
                sourceCopy.DestinationVmdlPath);
            log.AppendLine($"Invalid ClothChain parent anchors repaired: {repairedClothChains}");

            if (options.Resets(PrepareResetSections.Effects))
            {
                ResetExistingParticleEffects(manifest, addonContentRoot, log, cancellationToken);
            }

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Overlaying 1authoring model sources on matching retail render meshes...", "Подготовка моделей из 1authoring для соответствующих retail render mesh...")));

            var replacedRenderMeshes = RetailVmdlInheritance.OverlayArtistDmx(
                sourceCopy,
                addonContentRoot,
                manifest.Hero,
                rootDmxFiles);

            var replacedFbxMeshes = RetailVmdlInheritance.OverlayArtistFbx(
                sourceCopy,
                addonContentRoot,
                manifest.Hero,
                rootFbxFiles);

            var gltfOverlay = GltfAuthoringAdapter.Overlay(
                manifest,
                sourceCopy,
                addonContentRoot,
                rootGltfFiles,
                log,
                cancellationToken);

            var duplicateTargets = replacedRenderMeshes.Select(value => value.ResourcePath)
                .Concat(replacedFbxMeshes.Select(value => value.ResourcePath))
                .Concat(gltfOverlay.PreparedResources)
                .GroupBy(value => Path.ChangeExtension(value, null), StringComparer.OrdinalIgnoreCase)
                .Where(group => group.Count() > 1)
                .Select(group => string.Join(", ", group))
                .ToArray();
            if (duplicateTargets.Length > 0)
            {
                throw new InvalidOperationException(
                    "More than one 1authoring model source replaces the same retail render mesh. Keep one authoring format per target:\n" +
                    string.Join("\n", duplicateTargets));
            }

            log.AppendLine($"Artist DMX overlays: {replacedRenderMeshes.Count}");
            foreach (var overlay in replacedRenderMeshes)
            {
                log.AppendLine($"  replace {overlay.ResourcePath}");
                log.AppendLine(
                    $"    vertex color [{overlay.VertexColor.Status}]: {overlay.VertexColor.Message} | " +
                    $"sidecar {overlay.VertexColor.SidecarPath}");
            }
            log.AppendLine($"Artist FBX overlays: {replacedFbxMeshes.Count}");
            foreach (var overlay in replacedFbxMeshes)
            {
                log.AppendLine($"  replace {overlay.ResourcePath} from {Path.GetFileName(overlay.ArtistFbxPath)}");
            }

            var fbxMaterials = FbxMaterialReferenceReader.ReadMany(rootFbxFiles);
            log.AppendLine($"FBX material slots detected: {fbxMaterials.Count}");
            foreach (var material in fbxMaterials)
            {
                log.AppendLine($"  FBX material {material.SourceName} -> {material.AuthoringReference}");
            }

            log.AppendLine(
                $"Artist glTF overlays: files={gltfOverlay.GltfFileCount}, primitives={gltfOverlay.PrimitiveCount}, preparedDMX={gltfOverlay.PreparedDmxCount}");

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

            var fbxMaterialReferences = fbxMaterials
                .Select(material => material.AuthoringReference)
                .ToArray();

            var allMaterialReferences = dmxMaterialReferences
                .Concat(fbxMaterialReferences)
                .Concat(gltfOverlay.MaterialReferences)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var authoringMaterialReferences = ExpandWallWormMaterialAliases(allMaterialReferences);

            var compatibilityRemaps = DiscoverMaterialRepairs(
                rootDmxFiles,
                allMaterialReferences,
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
                ProjectTextureBindingService.MarkLegacyManagedMaterialsForMigration(
                    addonContentRoot,
                    addonName,
                    log,
                    cancellationToken);
            }

            var previousOwnership = regenerateCustomMaterials
                ? ManagedCustomMaterialRegistryStore.Load(manifest)
                : ManagedCustomMaterialRegistryStore.LoadPreservingMaterials(manifest);
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
                cancellationToken,
                mutateExistingMaterials: regenerateCustomMaterials);

            var finalTextureRepairs = regenerateCustomMaterials
                ? FinalizeManagedCustomMaterials(
                    customMaterials,
                    addonContentRoot,
                    log,
                    cancellationToken)
                : 0;
            log.AppendLine($"Managed custom VMAT final missing-source repairs: {finalTextureRepairs}");

            var exactCustomMaterialRemaps = ResolveExactCustomMaterialRemaps(
                allMaterialReferences,
                customMaterials.Remaps,
                log);
            var exactFbxMaterialRemaps = ResolveExactFbxCustomMaterialRemaps(
                fbxMaterials,
                customMaterials.Remaps,
                log);

            var generatedRemaps = compatibilityRemaps
                .Concat(customMaterials.Remaps)
                .Concat(exactCustomMaterialRemaps)
                .Concat(exactFbxMaterialRemaps)
                .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // MaterialGroup remaps belong to the parent VMDL. When ModelDoc opens a
            // RenderMeshFile DMX directly, those remaps are unavailable, so rewrite
            // only the staged DMX copy to the same resolved VMAT targets.
            var directDmxRemaps = existingRemapsBeforePatch
                .Concat(generatedRemaps)
                .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
            var directDmxMaterialRewriteCount = 0;
            foreach (var overlay in replacedRenderMeshes)
            {
                var rewritten = PreparedDmxMaterialRemapService.Apply(
                    overlay.PreparedDmxPath,
                    directDmxRemaps);
                directDmxMaterialRewriteCount += rewritten;
                log.AppendLine(
                    $"Prepared DMX direct material paths: {Path.GetFileName(overlay.PreparedDmxPath)} | rewritten material elements={rewritten}");
            }

            log.AppendLine($"Prepared DMX direct material elements rewritten: {directDmxMaterialRewriteCount}");
            log.AppendLine($"Compatibility material remaps generated: {compatibilityRemaps.Count}");
            log.AppendLine($"Custom material remaps generated: {customMaterials.Remaps.Count}");
            log.AppendLine($"Exact custom DMX/glTF material remaps generated: {exactCustomMaterialRemaps.Count}");
            log.AppendLine($"Exact custom FBX material remaps generated: {exactFbxMaterialRemaps.Count}");

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
            log.AppendLine("Material policy: authoring material-reference counts are diagnostic only; VMDL remaps are a separate concept.");
            log.AppendLine("Material policy: preserve retail reuse, generate narrow compatibility repairs, and route unresolved custom slots from DMX, FBX and glTF to addon-owned VMAT files.");
            log.AppendLine("Material policy: FBX slot names are paired with materials/<name> authoring aliases; both the raw FBX slot and the normalized alias remap to the same addon-owned VMAT.");
            log.AppendLine("Material policy: direct materials/<name>.vmat references from Wall Worm are paired with an extensionless authoring alias, so spaces and the explicit .vmat suffix survive into the final VMDL remap.");
            log.AppendLine("Material policy: prepared DMX copies rewrite resolved material references to final VMAT targets so RenderMeshFile preview works without relying on parent-VMDL MaterialGroup remaps; artist source DMX files are never changed.");
            log.AppendLine("Material policy: ordinary PREPARE leaves every existing addon-owned VMAT byte-for-byte unchanged and only synchronizes project texture source files. Shift+PREPARE may regenerate or migrate VMAT files only when Materials is explicitly checked.");
            log.AppendLine("Render-mesh policy: preserve retail RenderMeshList/bodygroups/LODs; overlay root DMX directly, reference root FBX directly, and adapt root glTF/GLB through its extracted DMX companion.");
            log.AppendLine("glTF policy: preserve primitive/material separation, COLOR_0 and skin streams; retain the retail skeleton and animation bindings for CSDK compilation.");
            log.AppendLine("Vertex Color policy: *_vertexcolor.fbx stays beside the artist DMX as persistent source data; repeated PREPARE, BUILD FOR TEST and ONLINE activation may reuse it safely.");

            manifest.SourceVmdl = sourceCopy.DestinationVmdlPath;
            manifest.CompiledVmdl = null;
            ProjectStore.Save(manifest);
            if (regenerateCustomMaterials)
            {
                ManagedCustomMaterialRegistryStore.Save(manifest, knownOwnership);
            }
            else
            {
                ManagedCustomMaterialRegistryStore.SavePreservingMaterials(manifest, knownOwnership);
            }

            log.AppendLine();
            log.AppendLine(gameOutputCleaned
                ? "RESULT: AUTHORING CONTENT PREPARED; ADDON GAME OUTPUT CLEANED FOR EXPLICIT RESET"
                : "RESULT: AUTHORING CONTENT PREPARED; ADDON GAME OUTPUT PRESERVED");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T("Authoring content prepared. Compiled output was preserved for incremental rebuilds.", "Файлы проекта подготовлены. Compiled output сохранён для инкрементальной пересборки.")));

            return new PrepareAuthoringResult(
                addonName,
                addonContentRoot,
                sourceCopy.DestinationVmdlPath,
                replacedRenderMeshes.Count + replacedFbxMeshes.Count + gltfOverlay.PreparedDmxCount,
                vertexColorAppliedCount,
                vertexColorMissingCount,
                vertexColorSkippedCount,
                vertexColorWarnings,
                allMaterialReferences.Length,
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
                heroSelectScene,
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

    private static void BackupSelectedSections(
        ProjectManifest manifest,
        string addonContentRoot,
        string addonName,
        PrepareResetSections sections,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var backupRoot = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            "backups",
            "reprepare",
            DateTime.Now.ToString("yyyyMMdd-HHmmssfff"));
        var copied = 0;

        if (sections.HasFlag(PrepareResetSections.Materials))
        {
            copied += BackupFiles(
                Path.Combine(addonContentRoot, "materials", addonName),
                "*.vmat",
                SearchOption.TopDirectoryOnly,
                Path.Combine(backupRoot, "materials"),
                cancellationToken);
        }

        if (sections.HasFlag(PrepareResetSections.Physics))
        {
            var currentVmdl = ResolvePreparedMainVmdl(manifest, addonContentRoot);
            if (File.Exists(currentVmdl))
            {
                Directory.CreateDirectory(Path.Combine(backupRoot, "physics"));
                File.Copy(currentVmdl, Path.Combine(backupRoot, "physics", Path.GetFileName(currentVmdl)), overwrite: false);
                copied++;
            }
        }

        if (sections.HasFlag(PrepareResetSections.Effects))
        {
            copied += BackupFiles(
                addonContentRoot,
                "*.vpcf",
                SearchOption.AllDirectories,
                Path.Combine(backupRoot, "effects"),
                cancellationToken);
        }

        log.AppendLine(copied == 0
            ? "Selected-section backup: no existing files required backup."
            : $"Selected-section backup: {copied} file(s) -> {backupRoot}");
    }

    private static int BackupFiles(
        string sourceRoot,
        string searchPattern,
        SearchOption searchOption,
        string destinationRoot,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(sourceRoot))
        {
            return 0;
        }

        var files = Directory.EnumerateFiles(sourceRoot, searchPattern, searchOption).ToArray();
        foreach (var sourcePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destination = SafePath.ResolveUnderRoot(
                destinationRoot,
                Path.GetRelativePath(sourceRoot, sourcePath),
                "Prepare backup destination");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourcePath, destination, overwrite: false);
        }
        return files.Length;
    }

    private static void ResetExistingParticleEffects(
        ProjectManifest manifest,
        string addonContentRoot,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var retailSourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");
        var restored = 0;
        var missing = 0;

        foreach (var destination in Directory.EnumerateFiles(addonContentRoot, "*.vpcf", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(addonContentRoot, destination);
            var source = SafePath.ResolveUnderRoot(retailSourceRoot, relative, "Retail particle-effect source");
            if (!File.Exists(source))
            {
                missing++;
                continue;
            }
            File.Copy(source, destination, overwrite: true);
            restored++;
        }

        log.AppendLine($"Retail particle effects restored: {restored}; no retail counterpart: {missing}.");
    }

    private static string ResolvePreparedMainVmdl(ProjectManifest manifest, string addonContentRoot)
    {
        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("Retail main model is unknown.");
        }

        var sourceResource = manifest.RetailMainModel.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
            ? manifest.RetailMainModel[..^2]
            : manifest.RetailMainModel;
        return SafePath.ResolveUnderRoot(
            addonContentRoot,
            sourceResource.Replace('/', Path.DirectorySeparatorChar),
            "Prepared main VMDL");
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

        CsdkAssetWatcherCompatibility.EnsureLuaUnlockerContentMirror(_paths.CsdkRoot);
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

    private static IReadOnlyList<VmdlMaterialRemap> ResolveExactFbxCustomMaterialRemaps(
        IReadOnlyList<FbxMaterialReference> fbxMaterials,
        IReadOnlyList<VmdlMaterialRemap> customRemaps,
        StringBuilder log)
    {
        var customByReference = customRemaps
            .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var result = new List<VmdlMaterialRemap>();
        foreach (var material in fbxMaterials)
        {
            if (!customByReference.TryGetValue(material.AuthoringReference, out var customRemap))
            {
                continue;
            }

            var sourceAliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                material.SourceName,
            };
            if (string.IsNullOrEmpty(Path.GetExtension(material.SourceName)))
            {
                // ModelDoc's FBX importer may expose a bare material name as <name>.vmat
                // when resolving MaterialGroup remaps. Cover both source spellings.
                sourceAliases.Add(material.SourceName + ".vmat");
            }

            foreach (var sourceAlias in sourceAliases)
            {
                if (string.Equals(sourceAlias, customRemap.From, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                result.Add(new VmdlMaterialRemap(sourceAlias, customRemap.To));
                log.AppendLine(
                    $"Exact FBX custom material remap: {sourceAlias} -> {customRemap.To} " +
                    $"(source slot {material.SourceName}, authoring alias {material.AuthoringReference})");
            }
        }

        return result
            .GroupBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
        var sourceRoot = sourceDumpRoot;
        foreach (var pipelineName in new[]
                 {
                     ExtractedSourceLayout.GltfPipelineFolderName,
                     ExtractedSourceLayout.LegacyGltfPipelineFolderName,
                 })
        {
            var candidate = Path.Combine(sourceDumpRoot, pipelineName);
            var normalizedCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(sourceVmdlPath).StartsWith(normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                sourceRoot = candidate;
                break;
            }
        }

        var retailDmxFiles = new List<string>();
        foreach (var renderMesh in RetailVmdlInheritance.ReadRenderMeshes(sourceVmdlPath))
        {
            var sourceDmxPath = ExtractedSourceLayout.ResolveResource(sourceRoot, renderMesh.Filename);

            if (sourceDmxPath is not null && File.Exists(sourceDmxPath))
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
        var genericFallbackReference = dmxMaterialReferences.FirstOrDefault(IsGenericEyeFallbackReference);
        if (genericFallbackReference is null)
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

        if (existingRemaps.Any(remap => IsGenericEyeFallbackReference(remap.From)))
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
            $"Eye fallback repair inferred from artist DMX material set: {genericFallbackReference} -> {target}");

        return new VmdlMaterialRemap(genericFallbackReference, target);
    }

    private static bool IsGenericEyeFallbackReference(string materialReference)
    {
        var normalized = materialReference.Replace('\\', '/').Trim().TrimStart('/');
        if (normalized.EndsWith(".vmat", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^".vmat".Length];
        }

        return string.Equals(
            normalized,
            GenericEyeFallbackMaterialStem,
            StringComparison.OrdinalIgnoreCase);
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
