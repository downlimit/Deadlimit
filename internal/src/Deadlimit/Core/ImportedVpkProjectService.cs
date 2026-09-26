namespace Deadlimit.Core;

public sealed record ImportedVpkProjectResult(
    ProjectManifest Manifest,
    string ProjectFolder,
    string AuthoringFolder,
    string OriginalVpkSnapshotPath);

public static class ImportedVpkProjectService
{
    public static ImportedVpkProjectResult Create(
        VpkImportCandidate candidate,
        VpkImportIdentity identity,
        string projectsRoot,
        string projectName,
        IProgress<ImportedVpkImportProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Create(candidate, identity, projectsRoot, projectName, new DeadlimitPaths(), progress, cancellationToken);

    public static ImportedVpkProjectResult Create(
        VpkImportCandidate candidate,
        VpkImportIdentity identity,
        string projectsRoot,
        string projectName,
        DeadlimitPaths paths,
        IProgress<ImportedVpkImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(paths);

        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
        {
            throw new DirectoryNotFoundException(
                string.IsNullOrWhiteSpace(projectsRoot)
                    ? "Projects folder is not configured."
                    : projectsRoot);
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
                "The selected VPK changed after validation. Import was cancelled before creating a project.");
        }

        var root = Path.GetFullPath(projectsRoot.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var folderName = ResolveAvailableFolderName(root, projectName);
        var projectFolder = Path.Combine(root, folderName);
        var folderCreated = false;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 10, LocalizedText.T(
                "Creating the project structure...",
                "Создание структуры проекта..."));
            Directory.CreateDirectory(projectFolder);
            folderCreated = true;
            ProjectAuthoringLayout.EnsureStructure(projectFolder);

            var manifest = new ProjectManifest
            {
                SchemaVersion = 4,
                Mode = ProjectMode.ImportedVpk,
                ProjectId = AddonIdentityService.CreateProjectId(),
                AddonId = string.Empty,
                ProjectName = folderName,
                ProjectFolder = projectFolder,
                Hero = identity.HeroLookupName ?? string.Empty,
                ReleaseTarget = refreshedCandidate.ReleaseTarget,
                RetailMainModel = identity.HeroLookupName is not null
                    && identity.PrimaryModelResources.Count == 1
                        ? identity.PrimaryModelResources[0]
                        : null,
                ImportedVpk = new ImportedVpkMetadata
                {
                    SourceVpkFileName = refreshedCandidate.SourceVpkFileName,
                    SourceVpkPath = refreshedCandidate.SourceVpkPath,
                    SourceReleaseTarget = refreshedCandidate.ReleaseTarget,
                    OriginalVpkSha256 = refreshedCandidate.SourceVpkSha256,
                    SourceEntryCount = refreshedCandidate.EntryCount,
                    ImportedUtc = DateTimeOffset.UtcNow,
                    ImporterVersion = typeof(ImportedVpkProjectService).Assembly.GetName().Version?.ToString()
                        ?? "unknown",
                    InferredHeroes = [.. identity.DetectedHeroLookupNames],
                    PrimaryModelResources = [.. identity.PrimaryModelResources],
                },
            };

            ProjectStore.Save(manifest);
            var payload = ImportedVpkPayloadService.Extract(
                manifest,
                refreshedCandidate,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 86, LocalizedText.T(
                "Indexing reconstructed project files...",
                "Индексация восстановленных файлов проекта..."));
            var scan = ProjectScanner.Scan(projectFolder);
            manifest.DmxFiles = [.. scan.DmxFiles];
            manifest.FbxFiles = [.. scan.FbxFiles];
            manifest.GltfFiles = [.. scan.GltfFiles];
            manifest.PngTextures = [.. scan.PngTextures];
            ProjectStore.Save(manifest);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 89, LocalizedText.T(
                "Recording ownership of the imported VPK slot...",
                "Регистрация импортированного слота VPK..."));
            // Imported retail slots are claimed only after the raw working-files snapshot exists.
            // Adoption compares the current retail VPK entry-by-entry against that snapshot,
            // then records a fingerprint for the complete VPK family before any mutation.
            new VpkSlotOwnershipService(paths).AdoptImportedSource(manifest);

            // Stage 7 is inspection-only: exact current-retail model paths are compared
            // against preserved compiled models and the result is written to metadata.
            // No imported working-file bytes are changed here.
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 95, LocalizedText.T(
                "Inspecting imported models...",
                "Проверка импортированных моделей..."));
            new ImportedVpkRepairInspectionService(paths).InspectAndSave(manifest);

            Report(progress, 100, LocalizedText.T(
                "VPK project import complete.",
                "Импорт проекта из VPK завершён."));

            return new ImportedVpkProjectResult(
                manifest,
                projectFolder,
                payload.AuthoringFolder,
                payload.SnapshotPath);
        }
        catch
        {
            if (folderCreated)
            {
                TryDeleteFailedImportFolder(projectFolder);
            }
            throw;
        }
    }

    private static string ResolveAvailableFolderName(string projectsRoot, string suggestedName)
    {
        var baseName = NormalizeFolderName(suggestedName);
        for (var index = 1; index <= 999; index++)
        {
            var name = index == 1 ? baseName : $"{baseName} ({index})";
            var candidate = Path.Combine(projectsRoot, name);
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return name;
            }
        }

        throw new IOException(
            $"Could not allocate a unique project folder for '{baseName}' inside the configured Projects folder.");
    }

    private static string NormalizeFolderName(string value)
    {
        var name = value.Trim();
        if (name.Length == 0
            || Path.IsPathRooted(name)
            || name.Contains(Path.DirectorySeparatorChar)
            || name.Contains(Path.AltDirectorySeparatorChar)
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidDataException($"Invalid imported project folder name: '{value}'.");
        }

        name = name.TrimEnd(' ', '.');
        if (name.Length == 0)
        {
            throw new InvalidDataException($"Invalid imported project folder name: '{value}'.");
        }
        return name;
    }

    private static void TryDeleteFailedImportFolder(string projectFolder)
    {
        try
        {
            if (Directory.Exists(projectFolder))
            {
                Directory.Delete(projectFolder, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The original import failure remains authoritative. The folder was allocated
            // exclusively for this import attempt and contains no user-authored payload yet.
        }
    }

    private static void Report(
        IProgress<ImportedVpkImportProgress>? progress,
        int percent,
        string message) =>
        progress?.Report(new ImportedVpkImportProgress(message, Math.Clamp(percent, 0, 100)));
}
