using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deadlimit.Core;

// Imported resources already carry authored paths, material groups and physics.
// Preparing them through the retail-template overlay loses those decisions.
internal sealed class ImportedVpkPrepareService(DeadlimitPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken,
        PrepareAuthoringOptions options)
    {
        if (options.ResetSections != PrepareResetSections.None)
            throw new InvalidOperationException("Imported VPK projects support preserving PREPARE. Section regeneration from retail templates is unavailable for imported resources.");

        var map = ImportedVpkPayloadService.TryLoadAuthoringMap(manifest.ProjectFolder)
            ?? throw new InvalidDataException("Imported authoring map is missing. Extract the VPK before PREPARE.");
        var authoringRoot = ProjectAuthoringLayout.GetAuthoringRoot(manifest);
        var compiledRoot = ImportedVpkPayloadService.ResolveCompiledFolder(manifest.ProjectFolder);
        var primary = manifest.ImportedVpk?.PrimaryModelResources.FirstOrDefault(
            path => string.Equals(path, manifest.RetailMainModel, StringComparison.OrdinalIgnoreCase))
            ?? manifest.ImportedVpk?.PrimaryModelResources.FirstOrDefault();
        if (primary is null)
            throw new InvalidDataException("The imported VPK has no primary model to prepare for CSDK.");
        var mainSource = map.Entries.FirstOrDefault(entry =>
            string.Equals(entry.CompiledPath, primary, StringComparison.OrdinalIgnoreCase))?.AuthoringPath
            ?? throw new InvalidDataException("The imported primary model has no editable source.");
        if (!File.Exists(SafePath.ResolveUnderRoot(authoringRoot, mainSource, "Imported primary model")))
            throw new FileNotFoundException("The imported primary model source is missing.");
        if (!Directory.Exists(compiledRoot))
            throw new DirectoryNotFoundException(compiledRoot);

        var addon = new AddonIdentityService(paths).ResolveAndClaim(manifest);
        var metadata = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var statePath = Path.Combine(metadata, "imported-prepare-state.json");
        var state = File.Exists(statePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(statePath))
                ?? throw new InvalidDataException("Invalid imported PREPARE state.")
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var staging = Path.Combine(metadata, "imported-prepare-staging-" + Guid.NewGuid().ToString("N"));
        var log = new StringBuilder();
        var logFolder = Path.Combine(metadata, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"prepare-imported-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        log.AppendLine($"Imported VPK PREPARE — {DateTimeOffset.Now:O}");
        log.AppendLine($"Content: {addon.ContentRoot}");
        log.AppendLine($"Game: {addon.GameRoot}");
        var materialsCreated = 0;
        var materialsPreserved = 0;
        var dmxCount = 0;
        var textureCount = 0;
        var remapCount = 0;
        try
        {
            progress?.Report(new PrepareAuthoringProgress(LocalizedText.T(
                "Preparing imported resources with their original paths and material assignments...",
                "Подготовка импортированных ресурсов с исходными путями и назначениями материалов...")));
            foreach (var source in Directory.EnumerateFiles(authoringRoot, "*", SearchOption.AllDirectories)
                         .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(authoringRoot, source);
                var staged = SafePath.ResolveUnderRoot(staging, relative, "Imported PREPARE staging");
                Directory.CreateDirectory(Path.GetDirectoryName(staged)!);
                File.Copy(source, staged);
                if (Path.GetExtension(source).Equals(".vmdl", StringComparison.OrdinalIgnoreCase))
                {
                    if (RetailVmdlInheritance.ContainsRootNode(staged, "AnimGraph2List")
                        || RetailVmdlInheritance.ContainsRootNode(staged, "NmSkeletonList"))
                    {
                        var patch = RetailVmdlInheritance.PatchAuthoringVmdl(staged, []);
                        remapCount += patch.ExistingMaterialRemapCount;
                        log.AppendLine($"Model {relative}: preserved {patch.ExistingMaterialRemapCount} material remaps; removed CSDK-unsupported classes: {string.Join(", ", patch.RemovedClasses)}");
                    }
                }
                var target = SafePath.ResolveUnderRoot(addon.ContentRoot, relative, "Imported CSDK content");
                var copied = Synchronize(staged, target, "content/" + relative.Replace('\\', '/'), state, log);
                if (Path.GetExtension(source).Equals(".vmat", StringComparison.OrdinalIgnoreCase))
                {
                    if (copied) materialsCreated++; else materialsPreserved++;
                }
                if (Path.GetExtension(source).Equals(".dmx", StringComparison.OrdinalIgnoreCase)) dmxCount++;
                if (new[] { ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr" }
                    .Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase)) textureCount++;
            }

            // Seed exact runtime assets for preview, including nested prefab VPKs,
            // particles and resources whose newer source format CSDK cannot compile.
            var runtimeCount = 0;
            foreach (var source in Directory.EnumerateFiles(compiledRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relative = Path.GetRelativePath(compiledRoot, source);
                Synchronize(source, SafePath.ResolveUnderRoot(addon.GameRoot, relative, "Imported CSDK runtime"),
                    "game/" + relative.Replace('\\', '/'), state, log);
                runtimeCount++;
            }
            HeroSelectScenePreparationResult? scene = null;
            if (options.PrepareHeroSelectScene)
                scene = new HeroSelectScenePreparationService(paths).Prepare(
                    manifest, addon.ContentRoot, addon.GameRoot, cancellationToken);

            manifest.RetailMainModel = primary;
            manifest.SourceVmdl = SafePath.ResolveUnderRoot(addon.ContentRoot, mainSource, "Prepared imported model");
            manifest.CompiledVmdl = SafePath.ResolveUnderRoot(addon.GameRoot, primary, "Prepared imported runtime model");
            ProjectStore.Save(manifest);
            AtomicFile.WriteJson(statePath, state, JsonOptions);
            log.AppendLine($"Prepared DMX: {dmxCount}; textures: {textureCount}; materials copied: {materialsCreated}; materials preserved: {materialsPreserved}; runtime files: {runtimeCount}.");
            log.AppendLine("Imported 1authoring and compiled baseline were not modified. BUILD retains its imported-payload provenance checks.");
            log.AppendLine("RESULT: SUCCESS");
            return new PrepareAuthoringResult(addon.AddonId, addon.ContentRoot, manifest.SourceVmdl,
                dmxCount, 0, 0, dmxCount, [], 0, remapCount, 0, 0,
                materialsCreated + materialsPreserved, materialsCreated, materialsPreserved,
                textureCount, Path.Combine(addon.ContentRoot, "materials"), 0, false, scene, logPath);
        }
        catch (Exception exception)
        {
            log.AppendLine(exception.ToString());
            throw;
        }
        finally
        {
            File.WriteAllText(logPath, log.ToString());
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static bool Synchronize(string source, string target, string key,
        Dictionary<string, string> state, StringBuilder log)
    {
        var sourceHash = Hash(source);
        if (File.Exists(target))
        {
            var targetHash = Hash(target);
            if (targetHash == sourceHash)
            {
                state[key] = targetHash;
                return false;
            }
            if (!state.TryGetValue(key, out var previousHash) || previousHash != targetHash)
            {
                log.AppendLine($"Preserved existing CSDK edit: {key}");
                return false;
            }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, overwrite: true);
        state[key] = sourceHash;
        return true;
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
