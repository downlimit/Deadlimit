using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed class OriginalVpkSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string SourceVpkFileName { get; set; } = string.Empty;
    public string SourceVpkPath { get; set; } = string.Empty;
    public string SourceVpkSha256 { get; set; } = string.Empty;
    public string? SourceReleaseTarget { get; set; }
    public int SourceEntryCount { get; set; }
    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<OriginalVpkEntrySnapshot> Entries { get; set; } = [];
}

public sealed class OriginalVpkEntrySnapshot
{
    public string InternalPath { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}

public sealed record ImportedVpkPayloadResult(
    string PayloadFolder,
    string SnapshotPath,
    int ExtractedEntryCount);

public static class ImportedVpkPayloadService
{
    public const string PayloadFolderName = "payload";
    public const string OriginalVpkSnapshotFileName = "original-vpk.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ImportedVpkPayloadResult Extract(
        ProjectManifest manifest,
        VpkImportCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(candidate);

        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Compiled VPK payload extraction requires an ImportedVpk project manifest.");
        }

        var projectFolder = Path.GetFullPath(manifest.ProjectFolder);
        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException(projectFolder);
        }

        var payloadFolder = SafePath.ResolveUnderRoot(
            projectFolder,
            PayloadFolderName,
            "Imported VPK payload folder");
        if (Directory.Exists(payloadFolder) || File.Exists(payloadFolder))
        {
            throw new InvalidOperationException(
                $"Imported VPK payload destination already exists: {payloadFolder}");
        }

        var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
        Directory.CreateDirectory(metadataFolder);
        var stagingFolder = Path.Combine(metadataFolder, $"payload-staging-{Guid.NewGuid():N}");
        var snapshotPath = Path.Combine(metadataFolder, OriginalVpkSnapshotFileName);

        var source = VpkImportSourceValidator.Validate(candidate.SourceVpkPath);
        if (!string.Equals(source.SourceVpkSha256, candidate.SourceVpkSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected VPK changed before payload extraction. Import was cancelled.");
        }

        Directory.CreateDirectory(stagingFolder);
        try
        {
            var snapshots = ExtractRawEntries(source.SourceVpkPath, stagingFolder);
            if (snapshots.Count != source.EntryCount)
            {
                throw new InvalidDataException(
                    $"VPK entry count changed during extraction. Expected {source.EntryCount}, extracted {snapshots.Count}.");
            }

            var snapshot = new OriginalVpkSnapshot
            {
                SourceVpkFileName = source.SourceVpkFileName,
                SourceVpkPath = source.SourceVpkPath,
                SourceVpkSha256 = source.SourceVpkSha256,
                SourceReleaseTarget = source.ReleaseTarget,
                SourceEntryCount = snapshots.Count,
                CapturedUtc = DateTimeOffset.UtcNow,
                Entries = [.. snapshots],
            };

            AtomicFile.WriteJson(snapshotPath, snapshot, JsonOptions);
            Directory.Move(stagingFolder, payloadFolder);

            return new ImportedVpkPayloadResult(
                payloadFolder,
                snapshotPath,
                snapshots.Count);
        }
        catch
        {
            TryDeleteDirectory(stagingFolder);
            TryDeleteFile(snapshotPath);
            throw;
        }
    }

    public static OriginalVpkSnapshot? TryLoadSnapshot(string projectFolder)
    {
        var path = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), OriginalVpkSnapshotFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<OriginalVpkSnapshot>(File.ReadAllText(path), JsonOptions);
            if (snapshot is null
                || snapshot.SchemaVersion != 1
                || snapshot.Entries.Count != snapshot.SourceEntryCount)
            {
                return null;
            }
            return snapshot;
        }
        catch (Exception exception) when (exception is JsonException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyList<OriginalVpkEntrySnapshot> ExtractRawEntries(
        string sourceVpkPath,
        string stagingFolder)
    {
        using var package = new Package();
        package.Read(sourceVpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {sourceVpkPath}");
        var entries = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, InternalPath: NormalizeVpkPath(entry.GetFullPath())))
            .OrderBy(item => item.InternalPath, StringComparer.Ordinal)
            .ToArray();

        var collisions = entries
            .GroupBy(item => item.InternalPath, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => string.Join(", ", group.Select(item => item.InternalPath)))
            .ToArray();
        if (collisions.Length > 0)
        {
            throw new InvalidDataException(
                "The VPK contains internal paths that collide on the Windows filesystem: " +
                string.Join("; ", collisions.Take(4)));
        }

        var result = new List<OriginalVpkEntrySnapshot>(entries.Length);
        foreach (var item in entries)
        {
            package.ReadEntry(item.Entry, out byte[] rawData);
            var outputPath = SafePath.ResolveUnderRoot(
                stagingFolder,
                item.InternalPath.Replace('/', Path.DirectorySeparatorChar),
                "Imported VPK entry");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, rawData);

            result.Add(new OriginalVpkEntrySnapshot
            {
                InternalPath = item.InternalPath,
                Sha256 = Convert.ToHexString(SHA256.HashData(rawData)).ToLowerInvariant(),
                Size = rawData.LongLength,
            });
        }

        return result;
    }

    private static string NormalizeVpkPath(string value)
    {
        var original = value.Replace('\\', '/');
        var normalized = SafePath.NormalizeRelative(original, "VPK internal path");
        if (normalized.EndsWith('/', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"VPK entry path does not identify a file: '{value}'.");
        }
        return normalized;
    }

    private static void TryDeleteDirectory(string path)
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
            // The extraction failure remains authoritative.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The extraction failure remains authoritative.
        }
    }
}
