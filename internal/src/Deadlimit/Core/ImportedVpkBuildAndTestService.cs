using System.Security.Cryptography;
using System.Text;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

internal sealed class ImportedVpkBuildAndTestService
{
    private readonly DeadlimitPaths _paths;

    public ImportedVpkBuildAndTestService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public BuildAndTestResult Build(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateImportedProject(manifest);

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"build-test-imported-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit Imported VPK Build & Test — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Release slot: {manifest.ReleaseTarget}");
        log.AppendLine("Mode: compiled payload (no CSDK authoring or ResourceCompiler)");
        log.AppendLine();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 3, LocalizedText.T(
                "Validating imported VPK project and adopted release slot...",
                "Проверка импортированного VPK-проекта и принятого release-слота..."));
            var slotGuard = new VpkSlotOwnershipService(_paths);
            var slotCheck = slotGuard.EnsureSlotAvailable(manifest);
            log.AppendLine($"Retail destination: {slotCheck.VpkPath}");
            log.AppendLine($"Existing retail VPK present: {slotCheck.ExistingFilePresent}");
            log.AppendLine($"Existing retail VPK owned by project: {slotCheck.OwnedByProject}");

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 15, LocalizedText.T(
                "Inspecting compiled model animation bindings against current retail Deadlock...",
                "Проверка animation bindings compiled-моделей по актуальному retail Deadlock..."));
            var inspectionService = new ImportedVpkRepairInspectionService(_paths);
            var inspection = inspectionService.InspectAndSave(manifest);
            var eligible = inspection.Targets
                .Where(target => target.Status is ImportedVpkRepairTargetStatus.BindingsMissing
                    or ImportedVpkRepairTargetStatus.BindingsDiffer)
                .ToArray();
            var warnings = inspection.Targets
                .Where(target => target.Status is ImportedVpkRepairTargetStatus.MissingRetailCounterpart
                    or ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable)
                .Select(target => $"{target.ResourcePath}: {target.Detail}")
                .ToArray();

            log.AppendLine($"Compiled character models inspected: {inspection.Targets.Count}");
            log.AppendLine($"Binding repair targets: {eligible.Length}");
            foreach (var target in inspection.Targets)
            {
                log.AppendLine($"  {target.Status}: {target.ResourcePath}");
            }

            var repairedCount = 0;
            if (eligible.Length > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Report(progress, 35, LocalizedText.T(
                    $"Repairing current-retail animation bindings in {eligible.Length} compiled model(s)...",
                    $"Исправление animation bindings по retail в compiled-моделях: {eligible.Length}..."));
                var repair = new ImportedVpkAnimationBindingRepairService(_paths).Repair(manifest);
                repairedCount = repair.ModifiedTargetCount;
                if (repairedCount != eligible.Length)
                {
                    throw new InvalidDataException(
                        $"Imported binding repair expected {eligible.Length} modified model(s), but committed {repairedCount}.");
                }
                log.AppendLine($"Compiled models repaired this run: {repairedCount}");
                log.AppendLine($"Repair report: {repair.ReportPath}");
            }
            else
            {
                // Do not run the repair service as a no-op here. If this payload was
                // repaired by an earlier build, its report is the provenance Stage 9
                // uses to prove why those bytes differ from the imported source.
                log.AppendLine("No binding repair required; existing repair provenance was preserved unchanged.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 58, LocalizedText.T(
                "Verifying preserved payload and rebuilding the VPK...",
                "Проверка сохранённого payload и пересборка VPK..."));
            var repack = new ImportedVpkRepackService().RebuildAndVerify(manifest, cancellationToken);
            log.AppendLine($"Repacked VPK: {repack.OutputVpkPath}");
            log.AppendLine($"VPK version: {repack.OutputVpkVersion}");
            log.AppendLine($"Payload entries: {repack.EntryCount}");
            log.AppendLine($"Entries differing from imported source due to recorded repair: {repack.ChangedEntryCount}");
            foreach (var entry in repack.Entries.Where(entry => entry.Status == ImportedVpkRepackEntryStatus.Repaired))
            {
                log.AppendLine($"  repaired entry: {entry.InternalPath}");
            }

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 78, LocalizedText.T(
                "Deploying verified VPK into the adopted retail release slot...",
                "Установка проверенного VPK в принятый retail release-слот..."));
            var deployedVpk = DeployVerifiedRepack(
                repack,
                slotCheck.VpkPath,
                cancellationToken);

            // Keep ownership and the deployed family in one successful operation for
            // imported projects. BuildFeature may record it again; that is idempotent.
            slotGuard.RecordSuccessfulDeployment(manifest, deployedVpk);

            Report(progress, 100, LocalizedText.T(
                "Imported VPK is repaired, verified and ready for retail testing.",
                "Импортированный VPK исправлен, проверен и готов к тесту в retail."));
            log.AppendLine();
            log.AppendLine("RESULT: IMPORTED BUILD & TEST SUCCESS");
            log.AppendLine($"VPK deployed: {deployedVpk}");
            File.WriteAllText(logPath, log.ToString());

            return new BuildAndTestResult(
                manifest.ProjectName,
                CompiledSourceCount: 0,
                RemovedCompiledOutputCount: 0,
                FullRebuild: false,
                Ag2Applied: repairedCount > 0,
                warnings,
                deployedVpk,
                logPath)
            {
                ImportedCompiledPayload = true,
                InspectedCompiledModelCount = inspection.Targets.Count,
                RepairedCompiledModelCount = repairedCount,
            };
        }
        catch (Exception exception)
        {
            log.AppendLine();
            log.AppendLine($"RESULT: FAILED — {exception}");
            File.WriteAllText(logPath, log.ToString());
            throw;
        }
    }

    private void ValidateImportedProject(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException("Imported VPK build path requires an ImportedVpk project.");
        }
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }
        if (!Directory.Exists(_paths.RetailDeadlockRoot))
        {
            throw new DirectoryNotFoundException($"Retail Deadlock root was not found: {_paths.RetailDeadlockRoot}");
        }
        var retailGameRoot = Path.Combine(_paths.RetailDeadlockRoot, "game");
        if (!Directory.Exists(retailGameRoot))
        {
            throw new DirectoryNotFoundException($"Retail Deadlock game root was not found: {retailGameRoot}");
        }
        if (!int.TryParse(manifest.ReleaseTarget?.Trim(), out var releaseSlot)
            || releaseSlot is < 1 or > 99)
        {
            throw new InvalidOperationException("Imported BUILD FOR TEST requires Release ID 01-99.");
        }
        if (!int.TryParse(manifest.ImportedVpk.SourceReleaseTarget?.Trim(), out var sourceSlot)
            || sourceSlot != releaseSlot)
        {
            throw new InvalidOperationException(
                "Imported BUILD FOR TEST must deploy back to the Release ID carried by the imported pak##_dir.vpk. " +
                "Changing the imported project's release slot is not supported by this repair path.");
        }
        _ = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidOperationException(
                "The imported project's original-vpk.json snapshot is missing or invalid.");
        var payloadRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            ImportedVpkPayloadService.PayloadFolderName,
            "Imported VPK payload folder");
        if (!Directory.Exists(payloadRoot))
        {
            throw new DirectoryNotFoundException(payloadRoot);
        }
    }

    private static string DeployVerifiedRepack(
        ImportedVpkRepackResult repack,
        string retailVpkPath,
        CancellationToken cancellationToken)
    {
        var sourceVpk = Path.GetFullPath(repack.OutputVpkPath);
        var destinationVpk = Path.GetFullPath(retailVpkPath);
        if (!File.Exists(sourceVpk))
        {
            throw new FileNotFoundException("Verified repacked VPK disappeared before deployment.", sourceVpk);
        }
        if (!string.Equals(
                Path.GetFileName(sourceVpk),
                Path.GetFileName(destinationVpk),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Repacked VPK name does not match the adopted retail slot: {Path.GetFileName(sourceVpk)} vs {Path.GetFileName(destinationVpk)}.");
        }

        var retailFolder = Path.GetDirectoryName(destinationVpk)
            ?? throw new InvalidOperationException("Retail VPK destination has no parent folder.");
        Directory.CreateDirectory(retailFolder);
        var stageFolder = Path.Combine(retailFolder, $".deadlimit-import-stage-{Guid.NewGuid():N}");
        var backupFolder = Path.Combine(retailFolder, $".deadlimit-import-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stageFolder);
        Directory.CreateDirectory(backupFolder);

        var sourceFamily = VpkArchiveIdentityService.EnumerateFamily(sourceVpk);
        if (!sourceFamily.Contains(sourceVpk, StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("Verified repacked VPK family is incomplete.", sourceVpk);
        }

        var stagedVpk = Path.Combine(stageFolder, Path.GetFileName(sourceVpk));
        var movedToRetail = new List<string>();
        var backedUp = new List<(string Original, string Backup)>();

        try
        {
            foreach (var source in sourceFamily)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var staged = Path.Combine(stageFolder, Path.GetFileName(source));
                File.Copy(source, staged, overwrite: false);
            }
            VerifyArchive(stagedVpk, repack);

            foreach (var existing in VpkArchiveIdentityService.EnumerateFamily(destinationVpk))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = Path.Combine(backupFolder, Path.GetFileName(existing));
                File.Move(existing, backup);
                backedUp.Add((existing, backup));
            }

            var stagedFamily = VpkArchiveIdentityService.EnumerateFamily(stagedVpk)
                .OrderBy(path => string.Equals(path, stagedVpk, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ToArray();
            foreach (var staged in stagedFamily)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(retailFolder, Path.GetFileName(staged));
                File.Move(staged, destination);
                movedToRetail.Add(destination);
            }

            VerifyArchive(destinationVpk, repack);
            return destinationVpk;
        }
        catch
        {
            foreach (var deployed in movedToRetail)
            {
                try
                {
                    if (File.Exists(deployed))
                    {
                        File.Delete(deployed);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Preserve the primary deployment error; restoration below still gets a chance.
                }
            }

            foreach (var backup in backedUp)
            {
                try
                {
                    if (File.Exists(backup.Backup))
                    {
                        File.Move(backup.Backup, backup.Original);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Preserve the primary deployment error.
                }
            }
            throw;
        }
        finally
        {
            DeleteDirectoryBestEffort(stageFolder);
            DeleteDirectoryBestEffort(backupFolder);
        }
    }

    private static void VerifyArchive(string vpkPath, ImportedVpkRepackResult repack)
    {
        using var package = new Package();
        package.Read(vpkPath);
        if (package.Version != repack.OutputVpkVersion)
        {
            throw new InvalidDataException(
                $"Deployed VPK version differs from verified repack: expected {repack.OutputVpkVersion}, found {package.Version}.");
        }
        if (package.Version == 2)
        {
            package.VerifyHashes();
        }
        package.VerifyFileChecksums();

        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        var actual = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, Path: NormalizeVpkPath(entry.GetFullPath())))
            .ToArray();
        var expected = repack.Entries.ToDictionary(entry => entry.InternalPath, StringComparer.Ordinal);
        if (actual.Length != expected.Count)
        {
            throw new InvalidDataException(
                $"Deployed VPK entry count differs from verified repack: expected {expected.Count}, found {actual.Length}.");
        }
        var actualPaths = actual.Select(item => item.Path).ToHashSet(StringComparer.Ordinal);
        if (!actualPaths.SetEquals(expected.Keys))
        {
            throw new InvalidDataException("Deployed VPK internal path set differs from the verified repack.");
        }

        foreach (var item in actual)
        {
            package.ReadEntry(item.Entry, out byte[] bytes);
            var expectedEntry = expected[item.Path];
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            if (!string.Equals(sha, expectedEntry.PayloadSha256, StringComparison.OrdinalIgnoreCase)
                || bytes.LongLength != expectedEntry.Size)
            {
                throw new InvalidDataException(
                    $"Deployed VPK entry bytes differ from the verified repack: {item.Path}");
            }
        }
    }

    private static string NormalizeVpkPath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "VPK internal path");

    private static void DeleteDirectoryBestEffort(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Staging/backup folders are reconstructable and outside the deployed archive family.
        }
    }

    private static void Report(
        IProgress<BuildAndTestProgress>? progress,
        int percent,
        string message) =>
        progress?.Report(new BuildAndTestProgress(message, percent));
}
