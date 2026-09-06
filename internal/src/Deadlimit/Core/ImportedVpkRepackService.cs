using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public enum ImportedVpkRepackEntryStatus
{
    Unchanged,
    Repaired,
}

public sealed record ImportedVpkRepackEntry(
    string InternalPath,
    ImportedVpkRepackEntryStatus Status,
    string OriginalSha256,
    string PayloadSha256,
    long Size);

public sealed record ImportedVpkRepackResult(
    DateTimeOffset RepackedUtc,
    string OutputVpkPath,
    uint OutputVpkVersion,
    bool SourceVersionPreserved,
    int EntryCount,
    int ChangedEntryCount,
    IReadOnlyList<ImportedVpkRepackEntry> Entries,
    string ReportPath);

internal sealed class ImportedVpkRepackSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset RepackedUtc { get; set; }
    public string OutputVpkPath { get; set; } = string.Empty;
    public uint OutputVpkVersion { get; set; }
    public bool SourceVersionPreserved { get; set; }
    public int EntryCount { get; set; }
    public int ChangedEntryCount { get; set; }
    public List<ImportedVpkRepackEntry> Entries { get; set; } = [];
}

public sealed class ImportedVpkRepackService
{
    public const string RepackFolderName = "repack";
    public const string ReportFileName = "repack-report.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public ImportedVpkRepackResult RebuildAndVerify(
        ProjectManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Compiled-payload repack requires an ImportedVpk project.");
        }

        var original = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidOperationException(
                "The imported VPK source snapshot is missing or invalid. Re-import the source VPK before rebuilding it.");
        var payloadRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            ImportedVpkPayloadService.PayloadFolderName,
            "Imported VPK payload folder");
        if (!Directory.Exists(payloadRoot))
        {
            throw new DirectoryNotFoundException(payloadRoot);
        }

        var expected = original.Entries.ToDictionary(
            entry => NormalizeVpkPath(entry.InternalPath),
            StringComparer.Ordinal);
        var payloadFiles = EnumeratePayload(payloadRoot, cancellationToken);
        ValidatePathSet(expected.Keys, payloadFiles.Keys);

        var repairSnapshot = TryLoadRepairSnapshot(manifest.ProjectFolder);
        var repairedByPath = (repairSnapshot?.Entries ?? [])
            .Where(entry => entry.Modified)
            .GroupBy(entry => NormalizeVpkPath(entry.ResourcePath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last(),
                StringComparer.OrdinalIgnoreCase);

        var entries = new List<ImportedVpkRepackEntry>(expected.Count);
        foreach (var originalEntry in original.Entries.OrderBy(entry => entry.InternalPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = NormalizeVpkPath(originalEntry.InternalPath);
            var payload = payloadFiles[path];
            var payloadHash = ComputeSha256(payload.AbsolutePath);
            var payloadSize = new FileInfo(payload.AbsolutePath).Length;
            var unchanged = string.Equals(payloadHash, originalEntry.Sha256, StringComparison.OrdinalIgnoreCase)
                && payloadSize == originalEntry.Size;

            if (unchanged)
            {
                entries.Add(new ImportedVpkRepackEntry(
                    path,
                    ImportedVpkRepackEntryStatus.Unchanged,
                    originalEntry.Sha256,
                    payloadHash,
                    payloadSize));
                continue;
            }

            if (!repairedByPath.TryGetValue(path, out var repair))
            {
                throw new InvalidOperationException(
                    $"Imported payload entry changed without a recorded animation-binding repair: {path}. " +
                    "Repack was blocked before any retail deployment.");
            }
            if (!path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Repair provenance unexpectedly points to a non-model payload entry: {path}");
            }
            if (!string.Equals(repair.BeforeSha256, originalEntry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Repair provenance does not start from the imported source bytes: {path}");
            }
            if (!string.Equals(repair.AfterSha256, payloadHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Payload bytes no longer match the recorded repair output: {path}");
            }

            entries.Add(new ImportedVpkRepackEntry(
                path,
                ImportedVpkRepackEntryStatus.Repaired,
                originalEntry.Sha256,
                payloadHash,
                payloadSize));
        }

        var changedPaths = entries
            .Where(entry => entry.Status == ImportedVpkRepackEntryStatus.Repaired)
            .Select(entry => entry.InternalPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var staleRepairEntries = repairedByPath.Keys
            .Where(path => !changedPaths.Contains(path))
            .ToArray();
        if (staleRepairEntries.Length > 0)
        {
            throw new InvalidDataException(
                "Repair report contains modified entries that no longer match changed payload bytes: " +
                string.Join(", ", staleRepairEntries.Take(4)));
        }

        var outputVersion = original.SourceVpkVersion is 1 or 2
            ? original.SourceVpkVersion.Value
            : 2u;
        var sourceVersionPreserved = original.SourceVpkVersion is 1 or 2;
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var repackFolder = Path.Combine(metadataFolder, RepackFolderName);
        Directory.CreateDirectory(repackFolder);
        var outputName = ResolveOutputFileName(original);
        var outputVpk = Path.Combine(repackFolder, outputName);
        var stagedVpk = CreateStagedVpkPath(repackFolder, outputName);

        try
        {
            WritePackage(payloadFiles, stagedVpk, outputVersion, cancellationToken);
            VerifyPackage(stagedVpk, outputVersion, entries, cancellationToken);
            CommitVerifiedFamily(stagedVpk, outputVpk);
            VerifyPackage(outputVpk, outputVersion, entries, cancellationToken);
        }
        finally
        {
            DeleteFamilyBestEffort(stagedVpk);
        }

        var repackedUtc = DateTimeOffset.UtcNow;
        var reportPath = Path.Combine(metadataFolder, ReportFileName);
        var snapshot = new ImportedVpkRepackSnapshot
        {
            RepackedUtc = repackedUtc,
            OutputVpkPath = outputVpk,
            OutputVpkVersion = outputVersion,
            SourceVersionPreserved = sourceVersionPreserved,
            EntryCount = entries.Count,
            ChangedEntryCount = changedPaths.Count,
            Entries = entries,
        };
        AtomicFile.WriteJson(reportPath, snapshot, JsonOptions);

        return new ImportedVpkRepackResult(
            repackedUtc,
            outputVpk,
            outputVersion,
            sourceVersionPreserved,
            entries.Count,
            changedPaths.Count,
            entries,
            reportPath);
    }

    private static Dictionary<string, PayloadFile> EnumeratePayload(
        string payloadRoot,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(payloadRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(path => new PayloadFile(
                NormalizeVpkPath(Path.GetRelativePath(payloadRoot, path)),
                path))
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException("Imported VPK payload is empty.");
        }

        var collisions = files
            .GroupBy(file => file.InternalPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(file => file.InternalPath)))
            .ToArray();
        if (collisions.Length > 0)
        {
            throw new InvalidDataException(
                "Imported payload contains paths that collide case-insensitively: " +
                string.Join("; ", collisions.Take(4)));
        }

        var result = new Dictionary<string, PayloadFile>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(file.InternalPath, file);
        }
        return result;
    }

    private static void ValidatePathSet(
        IEnumerable<string> expectedPaths,
        IEnumerable<string> payloadPaths)
    {
        var expected = expectedPaths.ToHashSet(StringComparer.Ordinal);
        var actual = payloadPaths.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(path => !actual.Contains(path)).Take(4).ToArray();
        var extra = actual.Where(path => !expected.Contains(path)).Take(4).ToArray();
        if (missing.Length == 0 && extra.Length == 0)
        {
            return;
        }

        var details = new List<string>();
        if (missing.Length > 0)
        {
            details.Add("missing: " + string.Join(", ", missing));
        }
        if (extra.Length > 0)
        {
            details.Add("extra: " + string.Join(", ", extra));
        }
        throw new InvalidOperationException(
            "Imported payload internal path set differs from the source VPK (" +
            string.Join("; ", details) + "). Repack was blocked.");
    }

    private static ImportedVpkAnimationBindingRepairSnapshot? TryLoadRepairSnapshot(string projectFolder)
    {
        var path = Path.Combine(
            ProjectStore.GetMetadataFolder(projectFolder),
            ImportedVpkAnimationBindingRepairService.ReportFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ImportedVpkAnimationBindingRepairSnapshot>(
                File.ReadAllText(path),
                JsonOptions);
            return snapshot is { SchemaVersion: 1 } ? snapshot : null;
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WritePackage(
        IReadOnlyDictionary<string, PayloadFile> payloadFiles,
        string outputVpk,
        uint version,
        CancellationToken cancellationToken)
    {
        using var package = new Package { Version = version };
        foreach (var file in payloadFiles.Values.OrderBy(file => file.InternalPath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            package.AddFile(file.InternalPath, File.ReadAllBytes(file.AbsolutePath));
        }
        package.Write(outputVpk);
        if (!File.Exists(outputVpk))
        {
            throw new InvalidOperationException(
                $"ValvePak completed without creating the expected directory archive: {outputVpk}");
        }
    }

    private static void VerifyPackage(
        string vpkPath,
        uint expectedVersion,
        IReadOnlyList<ImportedVpkRepackEntry> expectedEntries,
        CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(vpkPath);
        if (package.Version != expectedVersion)
        {
            throw new InvalidDataException(
                $"Rebuilt VPK version mismatch. Expected {expectedVersion}, found {package.Version}.");
        }
        package.VerifyHashes();
        package.VerifyFileChecksums();

        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        var actual = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, Path: NormalizeVpkPath(entry.GetFullPath())))
            .ToArray();
        var collisions = actual
            .GroupBy(item => item.Path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .ToArray();
        if (collisions.Length > 0)
        {
            throw new InvalidDataException("Rebuilt VPK contains duplicate internal paths.");
        }

        var expectedByPath = expectedEntries.ToDictionary(
            entry => entry.InternalPath,
            StringComparer.Ordinal);
        ValidatePathSet(expectedByPath.Keys, actual.Select(item => item.Path));

        foreach (var item in actual)
        {
            cancellationToken.ThrowIfCancellationRequested();
            package.ReadEntry(item.Entry, out byte[] bytes);
            var hash = ComputeSha256(bytes);
            var expected = expectedByPath[item.Path];
            if (!string.Equals(hash, expected.PayloadSha256, StringComparison.OrdinalIgnoreCase)
                || bytes.LongLength != expected.Size)
            {
                throw new InvalidDataException(
                    $"Rebuilt VPK entry bytes differ from the verified payload: {item.Path}");
            }
        }
    }

    private static void CommitVerifiedFamily(string stagedVpk, string outputVpk)
    {
        var stagedFamily = VpkArchiveIdentityService.EnumerateFamily(stagedVpk);
        if (!stagedFamily.Contains(Path.GetFullPath(stagedVpk), StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "Verified staged VPK disappeared before repack commit.",
                stagedVpk);
        }

        var stagedBase = GetVpkBaseName(stagedVpk);
        var outputBase = GetVpkBaseName(outputVpk);
        var outputDirectory = Path.GetDirectoryName(outputVpk)!;
        var mappings = stagedFamily
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var outputName = string.Equals(path, stagedVpk, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(outputVpk)
                    : outputBase + fileName[stagedBase.Length..];
                return (Source: path, Target: Path.Combine(outputDirectory, outputName));
            })
            .OrderBy(mapping => string.Equals(mapping.Source, stagedVpk, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ToArray();

        foreach (var previous in VpkArchiveIdentityService.EnumerateFamily(outputVpk))
        {
            File.Delete(previous);
        }

        var moved = new List<string>();
        try
        {
            foreach (var mapping in mappings)
            {
                File.Move(mapping.Source, mapping.Target);
                moved.Add(mapping.Target);
            }
        }
        catch
        {
            foreach (var target in moved)
            {
                try
                {
                    File.Delete(target);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Repack cache is reconstructable; preserve the original move failure.
                }
            }
            throw;
        }
    }

    private static string ResolveOutputFileName(OriginalVpkSnapshot snapshot)
    {
        var fileName = Path.GetFileName(snapshot.SourceVpkFileName);
        if (!string.IsNullOrWhiteSpace(fileName)
            && fileName.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
        {
            return fileName;
        }
        if (!string.IsNullOrWhiteSpace(snapshot.SourceReleaseTarget)
            && int.TryParse(snapshot.SourceReleaseTarget, out var slot)
            && slot is >= 1 and <= 99)
        {
            return $"pak{slot:D2}_dir.vpk";
        }
        return "repacked_dir.vpk";
    }

    private static string CreateStagedVpkPath(string folder, string outputName)
    {
        var outputBase = GetVpkBaseName(outputName);
        return Path.Combine(folder, $"{outputBase}_deadlimit_{Guid.NewGuid():N}_dir.vpk");
    }

    private static string GetVpkBaseName(string dirVpkPath)
    {
        var fileName = Path.GetFileName(dirVpkPath);
        const string suffix = "_dir.vpk";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static void DeleteFamilyBestEffort(string dirVpkPath)
    {
        foreach (var path in VpkArchiveIdentityService.EnumerateFamily(dirVpkPath))
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Staging cleanup does not replace the primary repack result.
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string ComputeSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string NormalizeVpkPath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "VPK internal path");

    private sealed record PayloadFile(string InternalPath, string AbsolutePath);
}
