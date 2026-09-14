using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed record ImportedVpkAnimationBindingRepairEntry(
    string ResourcePath,
    ImportedVpkRepairTargetStatus InspectionStatus,
    bool Modified,
    string BeforeSha256,
    string AfterSha256,
    string Detail);

public sealed record ImportedVpkAnimationBindingRepairResult(
    DateTimeOffset RepairedUtc,
    int EligibleTargetCount,
    int ModifiedTargetCount,
    IReadOnlyList<ImportedVpkAnimationBindingRepairEntry> Entries,
    string ReportPath);

internal sealed class ImportedVpkAnimationBindingRepairSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset RepairedUtc { get; set; }
    public List<ImportedVpkAnimationBindingRepairEntry> Entries { get; set; } = [];
}

public sealed class ImportedVpkAnimationBindingRepairService
{
    public const string ReportFileName = "repair-report.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeadlimitPaths _paths;

    public ImportedVpkAnimationBindingRepairService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public ImportedVpkAnimationBindingRepairResult Repair(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Animation-binding repair requires an ImportedVpk project.");
        }

        var inspection = new ImportedVpkRepairInspectionService(_paths).InspectAndSave(manifest);
        var payloadRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            ImportedVpkPayloadService.PayloadFolderName,
            "Imported VPK payload folder");
        if (!Directory.Exists(payloadRoot))
        {
            throw new DirectoryNotFoundException(payloadRoot);
        }

        var eligible = inspection.Targets
            .Where(target => target.Status is ImportedVpkRepairTargetStatus.BindingsMissing
                or ImportedVpkRepairTargetStatus.BindingsDiffer)
            .ToArray();
        var staged = new List<StagedRepair>(eligible.Length);
        var entries = new List<ImportedVpkAnimationBindingRepairEntry>(inspection.Targets.Count);

        foreach (var target in inspection.Targets)
        {
            if (target.Status == ImportedVpkRepairTargetStatus.BindingsAlreadyCurrent)
            {
                var path = ResolvePayloadModel(payloadRoot, target.ResourcePath);
                var hash = File.Exists(path) ? ComputeSha256(File.ReadAllBytes(path)) : string.Empty;
                entries.Add(new ImportedVpkAnimationBindingRepairEntry(
                    target.ResourcePath,
                    target.Status,
                    Modified: false,
                    hash,
                    hash,
                    "Bindings already match current retail; compiled model bytes were not serialized or rewritten."));
                continue;
            }

            if (target.Status is not (ImportedVpkRepairTargetStatus.BindingsMissing
                or ImportedVpkRepairTargetStatus.BindingsDiffer))
            {
                entries.Add(new ImportedVpkAnimationBindingRepairEntry(
                    target.ResourcePath,
                    target.Status,
                    Modified: false,
                    string.Empty,
                    string.Empty,
                    target.Detail));
                continue;
            }

            if (!target.RetailMatched
                || string.IsNullOrWhiteSpace(target.RetailVpkPath)
                || target.RetailBindings is null)
            {
                throw new InvalidOperationException(
                    $"Repair target '{target.ResourcePath}' was marked eligible without a verified current-retail counterpart.");
            }

            var payloadPath = ResolvePayloadModel(payloadRoot, target.ResourcePath);
            if (!File.Exists(payloadPath))
            {
                throw new FileNotFoundException(
                    "Imported repair target disappeared from the preserved payload.",
                    payloadPath);
            }

            var originalBytes = File.ReadAllBytes(payloadPath);
            var retailBytes = ReadExactVpkEntry(target.RetailVpkPath, target.ResourcePath);
            var retailNow = CompiledModelAnimationBindingRepair.ReadSnapshot(retailBytes, target.ResourcePath);
            if (!CompiledModelAnimationBindingRepair.SnapshotsEqual(retailNow, target.RetailBindings))
            {
                throw new InvalidOperationException(
                    $"Current retail bindings changed while repair was being prepared: {target.ResourcePath}. " +
                    "No imported payload bytes were changed; inspect the updated Deadlock build and retry.");
            }

            var repaired = CompiledModelAnimationBindingRepair.Repair(
                originalBytes,
                retailBytes,
                target.ResourcePath);
            if (!repaired.Modified)
            {
                throw new InvalidOperationException(
                    $"Repair inspection marked '{target.ResourcePath}' as {target.Status}, but a fresh semantic comparison found no binding difference. " +
                    "No payload bytes were changed.");
            }

            var beforeHash = ComputeSha256(originalBytes);
            var afterHash = ComputeSha256(repaired.Bytes);
            if (string.Equals(beforeHash, afterHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Binding repair reported a modification without changing compiled model bytes: {target.ResourcePath}");
            }

            staged.Add(new StagedRepair(
                target.ResourcePath,
                payloadPath,
                originalBytes,
                repaired.Bytes));
            entries.Add(new ImportedVpkAnimationBindingRepairEntry(
                target.ResourcePath,
                target.Status,
                Modified: true,
                beforeHash,
                afterHash,
                target.Status == ImportedVpkRepairTargetStatus.BindingsMissing
                    ? "Missing AG2/NmSkeleton bindings were replaced with exact current-retail values."
                    : "Stale AG2/NmSkeleton bindings were replaced with exact current-retail values."));
        }

        CommitRepairs(staged);

        // Refresh the inspection after commit. Every successfully repaired target must now
        // compare as current retail truth; this also catches a serialization/write mismatch.
        var postInspection = new ImportedVpkRepairInspectionService(_paths).InspectAndSave(manifest);
        var remainingEligible = postInspection.Targets
            .Where(target => staged.Any(stage => string.Equals(
                stage.ResourcePath,
                target.ResourcePath,
                StringComparison.OrdinalIgnoreCase)))
            .Where(target => target.Status != ImportedVpkRepairTargetStatus.BindingsAlreadyCurrent)
            .ToArray();
        if (remainingEligible.Length > 0)
        {
            RollbackRepairs(staged);
            throw new InvalidDataException(
                "One or more compiled models no longer matched current retail bindings after repair; payload changes were rolled back.\n\n" +
                string.Join("\n", remainingEligible.Select(target => $"- {target.ResourcePath}: {target.Status}")));
        }

        var repairedUtc = DateTimeOffset.UtcNow;
        var reportPath = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            ReportFileName);
        AtomicFile.WriteJson(
            reportPath,
            new ImportedVpkAnimationBindingRepairSnapshot
            {
                RepairedUtc = repairedUtc,
                Entries = entries,
            },
            JsonOptions);

        return new ImportedVpkAnimationBindingRepairResult(
            repairedUtc,
            eligible.Length,
            staged.Count,
            entries,
            reportPath);
    }

    private static string ResolvePayloadModel(string payloadRoot, string resourcePath) =>
        SafePath.ResolveUnderRoot(
            payloadRoot,
            SafePath.NormalizeRelative(resourcePath, "Imported repair target")
                .Replace('/', Path.DirectorySeparatorChar),
            "Imported repair target");

    private static byte[] ReadExactVpkEntry(string vpkPath, string resourcePath)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var entries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        var normalizedTarget = SafePath.NormalizeRelative(
            resourcePath.Replace('\\', '/'),
            "Current retail model resource path");
        var matches = entries
            .SelectMany(group => group.Value)
            .Where(entry => string.Equals(
                SafePath.NormalizeRelative(
                    entry.GetFullPath().Replace('\\', '/'),
                    "Current retail VPK entry path"),
                normalizedTarget,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one current-retail entry for '{normalizedTarget}' in {vpkPath}, found {matches.Length}.");
        }

        package.ReadEntry(matches[0], out byte[] bytes);
        return bytes;
    }

    private static void CommitRepairs(IReadOnlyList<StagedRepair> staged)
    {
        var committed = new List<StagedRepair>(staged.Count);
        try
        {
            foreach (var repair in staged)
            {
                AtomicFile.WriteAllBytes(repair.PayloadPath, repair.RepairedBytes);
                committed.Add(repair);
            }
        }
        catch
        {
            RollbackRepairs(committed);
            throw;
        }
    }

    private static void RollbackRepairs(IEnumerable<StagedRepair> staged)
    {
        var failures = new List<Exception>();
        foreach (var repair in staged.Reverse())
        {
            try
            {
                AtomicFile.WriteAllBytes(repair.PayloadPath, repair.OriginalBytes);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                failures.Add(exception);
            }
        }

        if (failures.Count > 0)
        {
            throw new AggregateException(
                "Binding repair rollback was incomplete. Inspect the imported payload before rebuilding its VPK.",
                failures);
        }
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record StagedRepair(
        string ResourcePath,
        string PayloadPath,
        byte[] OriginalBytes,
        byte[] RepairedBytes);
}
