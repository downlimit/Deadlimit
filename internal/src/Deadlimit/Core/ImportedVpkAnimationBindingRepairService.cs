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
    string Detail,
    bool BindingsModified = false,
    bool CameraModified = false,
    string CameraStatus = "",
    bool SoundSettingsModified = false,
    string SoundSettingsStatus = "");

public sealed record ImportedVpkAnimationBindingRepairResult(
    DateTimeOffset RepairedUtc,
    int EligibleTargetCount,
    int ModifiedTargetCount,
    IReadOnlyList<ImportedVpkAnimationBindingRepairEntry> Entries,
    string ReportPath,
    int BindingModifiedTargetCount = 0,
    int CameraModifiedTargetCount = 0,
    int SoundSettingsModifiedTargetCount = 0);

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

    public ImportedVpkAnimationBindingRepairResult Repair(ProjectManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Animation-binding repair requires an ImportedVpk project.");
        }

        var inspection = new ImportedVpkRepairInspectionService(_paths).InspectAndSave(manifest);
        var payloadRoot = ImportedVpkPayloadService.ResolveCompiledFolder(manifest.ProjectFolder);
        if (!Directory.Exists(payloadRoot))
        {
            throw new DirectoryNotFoundException(payloadRoot);
        }

        var sourceSnapshot = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidDataException("Missing imported source snapshot.");
        var reportPath = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), ReportFileName);
        var previousReportBytes = File.Exists(reportPath) ? File.ReadAllBytes(reportPath) : null;
        var previous = previousReportBytes is null ? null
            : JsonSerializer.Deserialize<ImportedVpkAnimationBindingRepairSnapshot>(previousReportBytes, JsonOptions)
                ?? throw new InvalidDataException("Invalid compiled model repair report.");
        if (previous is not null && previous.SchemaVersion is not (1 or 2))
            throw new InvalidDataException("Unsupported compiled model repair report version.");
        var recorded = (previous?.Entries ?? []).Where(entry => entry.Modified)
            .ToDictionary(entry => entry.ResourcePath, StringComparer.OrdinalIgnoreCase);
        var originals = sourceSnapshot.Entries.ToDictionary(entry => entry.InternalPath, StringComparer.OrdinalIgnoreCase);
        var baseline = sourceSnapshot.Entries.ToDictionary(entry => entry.InternalPath, entry => entry.Sha256, StringComparer.OrdinalIgnoreCase);
        var authoring = ImportedVpkAuthoringBuildService.TryLoadSnapshot(manifest.ProjectFolder);
        foreach (var built in authoring?.Entries ?? [])
        {
            if (originals.TryGetValue(built.InternalPath, out var original))
            {
                if (!string.Equals(built.OriginalSha256, original.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Authoring provenance does not match imported source: {built.InternalPath}");
            }
            else if (built.OriginalSha256 is not null)
                throw new InvalidDataException($"Authoring provenance references an unknown original: {built.InternalPath}");
            baseline[built.InternalPath] = built.BuiltSha256;
        }

        // Validate the entire payload before accepting it as the next repair input.
        // A later game update can extend an earlier repair; provenance always starts
        // at the originally imported bytes and ends at the latest verified output.
        foreach (var entry in baseline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = ResolvePayloadModel(payloadRoot, entry.Key);
            var expected = entry.Value;
            if (recorded.TryGetValue(entry.Key, out var record))
            {
                if (!string.Equals(record.BeforeSha256, entry.Value, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Repair provenance does not start at imported source: {entry.Key}");
                expected = record.AfterSha256;
            }
            if (!File.Exists(path) || !string.Equals(ComputeSha256(File.ReadAllBytes(path)), expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Payload differs from its verified import/repair: {entry.Key}");
        }
        var staged = new List<StagedRepair>();
        var entries = new List<ImportedVpkAnimationBindingRepairEntry>();
        var bindingsChanged = 0;
        var camerasChanged = 0;
        var soundsChanged = 0;
        foreach (var target in inspection.Targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!target.RetailMatched || string.IsNullOrWhiteSpace(target.RetailVpkPath) || target.RetailBindings is null)
            {
                entries.Add(new(target.ResourcePath, target.Status, false, "", "", target.Detail));
                continue;
            }
            var payloadPath = ResolvePayloadModel(payloadRoot, target.ResourcePath);
            var input = File.ReadAllBytes(payloadPath);
            var retail = ReadExactVpkEntry(target.RetailVpkPath, target.ResourcePath);
            if (!CompiledModelAnimationBindingRepair.SnapshotsEqual(
                CompiledModelAnimationBindingRepair.ReadSnapshot(retail, target.ResourcePath), target.RetailBindings))
                throw new InvalidOperationException($"Retail model changed during repair: {target.ResourcePath}");
            var binding = CompiledModelAnimationBindingRepair.Repair(input, retail, target.ResourcePath);
            var camera = CompiledModelCameraRepair.Repair(binding.Bytes, retail, target.ResourcePath);
            var sound = CompiledModelSoundRepair.Repair(camera.Bytes, retail, target.ResourcePath);
            if (binding.Modified) bindingsChanged++;
            if (camera.Modified) camerasChanged++;
            if (sound.Modified) soundsChanged++;
            var modified = binding.Modified || camera.Modified || sound.Modified;
            var currentHash = ComputeSha256(input);
            var finalHash = ComputeSha256(sound.Bytes);
            var oldRecord = recorded.GetValueOrDefault(target.ResourcePath);
            var entry = new ImportedVpkAnimationBindingRepairEntry(
                target.ResourcePath, target.Status, modified || oldRecord is not null,
                baseline[target.ResourcePath], finalHash,
                $"AG2/NmSkeleton {(binding.Modified ? "repaired" : "current")}. {camera.Status}: {camera.Detail} Tagged sounds {sound.Status}: {sound.Detail}",
                binding.Modified || (oldRecord?.BindingsModified ?? false),
                camera.Modified || (oldRecord?.CameraModified ?? false), camera.Status.ToString(),
                sound.Modified || (oldRecord?.SoundSettingsModified ?? false), sound.Status.ToString());
            entries.Add(entry);
            if (!modified) continue;
            if (string.Equals(currentHash, finalHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Repair did not change bytes: {target.ResourcePath}");
            staged.Add(new(target.ResourcePath, payloadPath, input, sound.Bytes));
            recorded[target.ResourcePath] = entry;
        }
        var repairedUtc = DateTimeOffset.UtcNow;
        if (staged.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitRepairs(staged);
            try
            {
                var post = new ImportedVpkRepairInspectionService(_paths).InspectAndSave(manifest);
                if (post.Targets.Any(target => staged.Any(stage => stage.ResourcePath == target.ResourcePath)
                        && target.Status != ImportedVpkRepairTargetStatus.BindingsAlreadyCurrent))
                    throw new InvalidDataException("Repaired payload failed current-retail binding verification.");
                AtomicFile.WriteJson(reportPath, new ImportedVpkAnimationBindingRepairSnapshot
                {
                    SchemaVersion = 2,
                    RepairedUtc = repairedUtc,
                    Entries = recorded.Values.OrderBy(entry => entry.ResourcePath, StringComparer.Ordinal).ToList(),
                }, JsonOptions);
            }
            catch
            {
                RollbackRepairs(staged);
                if (previousReportBytes is not null) AtomicFile.WriteAllBytes(reportPath, previousReportBytes);
                else if (File.Exists(reportPath)) File.Delete(reportPath);
                throw;
            }
        }
        return new(repairedUtc, staged.Count, staged.Count, entries, reportPath, bindingsChanged, camerasChanged, soundsChanged);
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
                entry.GetFullPath().Replace('\\', '/'),
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
                if (!File.ReadAllBytes(repair.PayloadPath).AsSpan().SequenceEqual(repair.OriginalBytes))
                    throw new IOException($"Imported payload changed during repair: {repair.ResourcePath}");
                AtomicFile.WriteAllBytes(repair.PayloadPath, repair.RepairedBytes);
                committed.Add(repair);
                if (!File.ReadAllBytes(repair.PayloadPath).AsSpan().SequenceEqual(repair.RepairedBytes))
                    throw new IOException($"Imported payload write verification failed: {repair.ResourcePath}");
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
