namespace Deadlimit.Core;

public sealed record ImportedVpkProjectResult(
    ProjectManifest Manifest,
    string ProjectFolder,
    string AuthoringFolder,
    string OriginalVpkSnapshotPath);

public static class ImportedVpkProjectService
{
    public static ImportedVpkProjectResult ExtractIntoExisting(
        ProjectManifest currentManifest,
        VpkImportCandidate candidate,
        VpkImportIdentity identity,
        DeadlimitPaths paths,
        IProgress<ImportedVpkImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(currentManifest);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(paths);

        var projectFolder = Path.GetFullPath(currentManifest.ProjectFolder);
        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException(projectFolder);
        }

        Report(progress, 4, LocalizedText.T(
            "Validating the selected VPK...",
            "Проверка выбранного VPK..."));
        cancellationToken.ThrowIfCancellationRequested();
        var refreshedCandidate = VpkImportSourceValidator.Validate(candidate.SourceVpkPath);
        if (!string.Equals(
                refreshedCandidate.SourceVpkSha256,
                candidate.SourceVpkSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected VPK changed after validation. Extraction was cancelled.");
        }

        ProjectAuthoringLayout.EnsureStructure(projectFolder);
        var importedManifest = CreateImportedManifest(currentManifest, refreshedCandidate, identity);
        var payload = ImportedVpkPayloadService.Extract(
            importedManifest,
            refreshedCandidate,
            progress,
            cancellationToken);

        // Persist the imported-VPK contract immediately after the atomic payload
        // extraction. Later indexing and inspection can safely be retried.
        ProjectStore.Save(importedManifest);

        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, 86, LocalizedText.T(
            "Indexing reconstructed project files...",
            "Индексация восстановленных файлов проекта..."));
        var scan = ProjectScanner.Scan(projectFolder);
        importedManifest.DmxFiles = [.. scan.DmxFiles];
        importedManifest.FbxFiles = [.. scan.FbxFiles];
        importedManifest.GltfFiles = [.. scan.GltfFiles];
        importedManifest.PngTextures = [.. scan.PngTextures];
        ProjectStore.Save(importedManifest);

        cancellationToken.ThrowIfCancellationRequested();
        if (string.Equals(
                importedManifest.ReleaseTarget,
                importedManifest.ImportedVpk!.SourceReleaseTarget,
                StringComparison.OrdinalIgnoreCase))
        {
            Report(progress, 89, LocalizedText.T(
                "Recording ownership of the source VPK slot...",
                "Регистрация слота исходного VPK..."));
            new VpkSlotOwnershipService(paths).AdoptImportedSource(importedManifest);
        }

        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, 95, LocalizedText.T(
            "Inspecting extracted models...",
            "Проверка извлечённых моделей..."));
        new ImportedVpkRepairInspectionService(paths).InspectAndSave(importedManifest);

        Report(progress, 100, LocalizedText.T(
            "VPK extraction into the project is complete.",
            "Извлечение VPK в проект завершено."));

        return new ImportedVpkProjectResult(
            importedManifest,
            projectFolder,
            payload.AuthoringFolder,
            payload.SnapshotPath);
    }

    private static ProjectManifest CreateImportedManifest(
        ProjectManifest source,
        VpkImportCandidate candidate,
        VpkImportIdentity identity) =>
        new()
        {
            SchemaVersion = Math.Max(source.SchemaVersion, 5),
            Mode = ProjectMode.ImportedVpk,
            ImportedVpk = new ImportedVpkMetadata
            {
                SourceVpkFileName = candidate.SourceVpkFileName,
                SourceVpkPath = candidate.SourceVpkPath,
                SourceReleaseTarget = candidate.ReleaseTarget,
                OriginalVpkSha256 = candidate.SourceVpkSha256,
                SourceEntryCount = candidate.EntryCount,
                ImportedUtc = DateTimeOffset.UtcNow,
                ImporterVersion = typeof(ImportedVpkProjectService).Assembly.GetName().Version?.ToString()
                    ?? "unknown",
                InferredHeroes = [.. identity.DetectedHeroLookupNames],
                PrimaryModelResources = [.. identity.PrimaryModelResources],
            },
            ProjectId = source.ProjectId,
            AddonId = source.AddonId,
            ProjectName = source.ProjectName,
            ProjectFolder = source.ProjectFolder,
            Hero = source.Hero,
            ReleaseTarget = source.ReleaseTarget,
            SourceDumpFolderName = source.SourceDumpFolderName,
            DmxFiles = [.. source.DmxFiles],
            FbxFiles = [.. source.FbxFiles],
            GltfFiles = [.. source.GltfFiles],
            PngTextures = [.. source.PngTextures],
            TextureTargetBindings = source.TextureTargetBindings.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.OrdinalIgnoreCase),
            CreatedUtc = source.CreatedUtc,
            UpdatedUtc = source.UpdatedUtc,
            RetailMainModel = source.RetailMainModel,
            RetailSourceVpk = source.RetailSourceVpk,
            LastSourceExtractionUtc = source.LastSourceExtractionUtc,
            Source2ViewerVersion = source.Source2ViewerVersion,
            ExtractedSourceFileCount = source.ExtractedSourceFileCount,
            LastSourceExtractionIncludedTextures = source.LastSourceExtractionIncludedTextures,
            LastSourceExtractionIncludedAbilities = source.LastSourceExtractionIncludedAbilities,
            SourceVmdl = source.SourceVmdl,
            CompiledVmdl = source.CompiledVmdl,
            AnimGraph2Refs = [.. source.AnimGraph2Refs],
            NmSkeletonRef = source.NmSkeletonRef,
        };

    private static void Report(
        IProgress<ImportedVpkImportProgress>? progress,
        int percent,
        string message) =>
        progress?.Report(new ImportedVpkImportProgress(message, Math.Clamp(percent, 0, 100)));
}
