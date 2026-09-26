using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed class OriginalVpkSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public string SourceVpkFileName { get; set; } = string.Empty;
    public string SourceVpkPath { get; set; } = string.Empty;
    public string SourceVpkSha256 { get; set; } = string.Empty;
    public string? SourceReleaseTarget { get; set; }
    public uint? SourceVpkVersion { get; set; }
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
    string AuthoringFolder,
    string CompiledFolder,
    string SnapshotPath,
    int ExtractedEntryCount);

public sealed record ImportedVpkImportProgress(string Message, int Percent);

public sealed class ImportedVpkAuthoringMap
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ImportedVpkAuthoringMapEntry> Entries { get; set; } = [];
    public List<ImportedVpkAuthoringFileSnapshot> AuthoringFiles { get; set; } = [];
}

public sealed record ImportedVpkAuthoringMapEntry(
    string CompiledPath,
    string? AuthoringPath,
    string? Warning);

public sealed record ImportedVpkAuthoringFileSnapshot(
    string RelativePath,
    string Sha256,
    long Size);

public static class ImportedVpkPayloadService
{
    public const string AuthoringFolderName = ProjectAuthoringLayout.AuthoringFolderName;
    public const string CompiledFolderName = "imported-compiled";
    public const string BuiltCompiledFolderName = "imported-build-compiled";
    public const string AuthoringMapFileName = "imported-authoring-map.json";
    public const string OriginalVpkSnapshotFileName = "original-vpk.json";

    private static readonly Regex SourceTextureSuffix = new(
        @"_(?<extension>png|tga|psd|jpg|jpeg)_(?<hash>[0-9a-f]{7,8})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex GeneratedHashSuffix = new(
        @"_(?<hash>[0-9a-f]{7,8})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ImportedVpkPayloadResult Extract(
        ProjectManifest manifest,
        VpkImportCandidate candidate,
        IProgress<ImportedVpkImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(candidate);

        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Compiled VPK extraction requires an ImportedVpk project manifest.");
        }

        var projectFolder = Path.GetFullPath(manifest.ProjectFolder);
        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException(projectFolder);
        }

        var authoringFolder = SafePath.ResolveUnderRoot(
            projectFolder,
            AuthoringFolderName,
            "Imported VPK working-files folder");
        if (File.Exists(authoringFolder)
            || (Directory.Exists(authoringFolder)
                && Directory.EnumerateFileSystemEntries(authoringFolder).Any()))
        {
            throw new InvalidOperationException(
                $"Imported VPK working-files destination is not empty: {authoringFolder}");
        }

        var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
        Directory.CreateDirectory(metadataFolder);
        var compiledFolder = Path.Combine(metadataFolder, CompiledFolderName);
        if (Directory.Exists(compiledFolder) || File.Exists(compiledFolder))
        {
            throw new InvalidOperationException(
                $"Imported VPK compiled snapshot already exists: {compiledFolder}");
        }

        var compiledStagingFolder = Path.Combine(metadataFolder, $"compiled-staging-{Guid.NewGuid():N}");
        var authoringStagingFolder = Path.Combine(metadataFolder, $"authoring-staging-{Guid.NewGuid():N}");
        var snapshotPath = Path.Combine(metadataFolder, OriginalVpkSnapshotFileName);
        var authoringMapPath = Path.Combine(metadataFolder, AuthoringMapFileName);

        Report(progress, 18, LocalizedText.T(
            "Validating the selected VPK before extraction...",
            "Проверка выбранного VPK перед распаковкой..."));
        cancellationToken.ThrowIfCancellationRequested();
        var source = VpkImportSourceValidator.Validate(candidate.SourceVpkPath);
        if (!string.Equals(source.SourceVpkSha256, candidate.SourceVpkSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The selected VPK changed before extraction. Import was cancelled.");
        }

        Directory.CreateDirectory(compiledStagingFolder);
        Directory.CreateDirectory(authoringStagingFolder);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 24, LocalizedText.T(
                "Reading the VPK entry table...",
                "Чтение таблицы файлов VPK..."));
            var sourceVpkVersion = ReadVpkVersion(source.SourceVpkPath);
            var snapshots = ExtractRawEntries(
                source.SourceVpkPath,
                compiledStagingFolder,
                progress,
                cancellationToken);
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
                SourceVpkVersion = sourceVpkVersion,
                SourceEntryCount = snapshots.Count,
                CapturedUtc = DateTimeOffset.UtcNow,
                Entries = [.. snapshots],
            };

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 73, LocalizedText.T(
                "Converting imported resources into editable files...",
                "Преобразование импортированных ресурсов в редактируемые файлы..."));
            var authoringMap = ExtractAuthoringFiles(
                source.SourceVpkPath,
                authoringStagingFolder,
                progress,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 84, LocalizedText.T(
                "Saving the original VPK snapshot...",
                "Сохранение снимка исходного VPK..."));
            AtomicFile.WriteJson(snapshotPath, snapshot, JsonOptions);
            AtomicFile.WriteJson(authoringMapPath, authoringMap, JsonOptions);
            if (Directory.Exists(authoringFolder))
            {
                Directory.Delete(authoringFolder, recursive: false);
            }
            Directory.Move(compiledStagingFolder, compiledFolder);
            Directory.Move(authoringStagingFolder, authoringFolder);

            return new ImportedVpkPayloadResult(
                authoringFolder,
                compiledFolder,
                snapshotPath,
                snapshots.Count);
        }
        catch
        {
            TryDeleteDirectory(compiledStagingFolder);
            TryDeleteDirectory(authoringStagingFolder);
            TryDeleteDirectory(compiledFolder);
            TryDeleteFile(snapshotPath);
            TryDeleteFile(authoringMapPath);
            TryCreateDirectory(authoringFolder);
            throw;
        }
    }

    public static string ResolveCompiledFolder(string projectFolder)
    {
        var builtCompiled = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), BuiltCompiledFolderName);
        if (Directory.Exists(builtCompiled))
        {
            return builtCompiled;
        }

        return ResolveOriginalCompiledFolder(projectFolder);
    }

    public static string ResolveOriginalCompiledFolder(string projectFolder)
    {
        var metadataCompiled = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), CompiledFolderName);
        if (Directory.Exists(metadataCompiled))
        {
            return metadataCompiled;
        }

        var legacyPayload = Path.Combine(projectFolder, "payload");
        if (Directory.Exists(legacyPayload))
        {
            return legacyPayload;
        }

        var legacyAuthoring = Path.Combine(projectFolder, AuthoringFolderName);
        if (Directory.Exists(legacyAuthoring)
            && Directory.EnumerateFiles(legacyAuthoring, "*_c", SearchOption.AllDirectories).Any())
        {
            return legacyAuthoring;
        }

        return metadataCompiled;
    }

    public static ImportedVpkAuthoringMap? TryLoadAuthoringMap(string projectFolder)
    {
        var path = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), AuthoringMapFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var map = JsonSerializer.Deserialize<ImportedVpkAuthoringMap>(File.ReadAllText(path), JsonOptions);
            return map is { SchemaVersion: 1 } ? map : null;
        }
        catch (Exception exception) when (exception is JsonException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return null;
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

    private static uint ReadVpkVersion(string sourceVpkPath)
    {
        using var package = new Package();
        package.Read(sourceVpkPath);
        return package.Version;
    }

    private static IReadOnlyList<OriginalVpkEntrySnapshot> ExtractRawEntries(
        string sourceVpkPath,
        string stagingFolder,
        IProgress<ImportedVpkImportProgress>? progress,
        CancellationToken cancellationToken)
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
        var lastPercent = -1;
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = entries[index];
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

            var percent = 26 + (int)Math.Round(46d * (index + 1) / Math.Max(entries.Length, 1));
            if (percent != lastPercent)
            {
                lastPercent = percent;
                Report(
                    progress,
                    percent,
                    LocalizedText.T(
                        $"Extracting VPK files: {index + 1}/{entries.Length}...",
                        $"Распаковка файлов VPK: {index + 1}/{entries.Length}..."));
            }
        }

        return result;
    }

    private static ImportedVpkAuthoringMap ExtractAuthoringFiles(
        string sourceVpkPath,
        string authoringRoot,
        IProgress<ImportedVpkImportProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(sourceVpkPath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK entry table was not available: {sourceVpkPath}");
        using var fileLoader = new GameFileLoader(package, package.FileName);
        var entries = packageEntries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, InternalPath: NormalizeVpkPath(entry.GetFullPath())))
            .OrderBy(item => item.InternalPath, StringComparer.Ordinal)
            .ToArray();

        var mapped = new List<ImportedVpkAuthoringMapEntry>(entries.Length);
        var occupiedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastPercent = -1;
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = entries[index];
            string? authoringPath = null;
            string? warning = null;
            try
            {
                package.ReadEntry(item.Entry, out byte[] rawData);
                if (!item.Entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    authoringPath = AllocateAuthoringPath(item.InternalPath, item.InternalPath, occupiedPaths);
                    WriteFile(
                        SafePath.ResolveUnderRoot(authoringRoot, ToWindowsPath(authoringPath), "Imported authoring file"),
                        rawData);
                }
                else
                {
                    using var stream = new MemoryStream(rawData, writable: false);
                    using var resource = new Resource { FileName = item.InternalPath };
                    resource.Read(stream);

                    var outputExtension = FileExtract.GetExtension(resource) ?? item.Entry.TypeName[..^2];
                    var decompiledPath = Path.ChangeExtension(item.InternalPath, outputExtension);
                    if (resource.ResourceType == ResourceType.Texture)
                    {
                        decompiledPath = NormalizeTextureAuthoringPath(decompiledPath);
                    }
                    authoringPath = AllocateAuthoringPath(decompiledPath, item.InternalPath, occupiedPaths);
                    var outputPath = SafePath.ResolveUnderRoot(
                        authoringRoot,
                        ToWindowsPath(authoringPath),
                        "Decompiled imported VPK resource");

                    using var contentFile = resource.ResourceType == ResourceType.Texture
                        ? new TextureExtract(resource).ToContentFile()
                        : FileExtract.Extract(resource, fileLoader, null);
                    DumpContentFile(authoringRoot, outputPath, contentFile);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException
                                               && exception is not OutOfMemoryException)
            {
                warning = exception.Message;
            }

            mapped.Add(new ImportedVpkAuthoringMapEntry(item.InternalPath, authoringPath, warning));
            var percent = 73 + (int)Math.Round(10d * (index + 1) / Math.Max(entries.Length, 1));
            if (percent != lastPercent)
            {
                lastPercent = percent;
                Report(
                    progress,
                    percent,
                    LocalizedText.T(
                        $"Converting VPK resources: {index + 1}/{entries.Length}...",
                        $"Преобразование ресурсов VPK: {index + 1}/{entries.Length}..."));
            }
        }

        return new ImportedVpkAuthoringMap
        {
            CreatedUtc = DateTimeOffset.UtcNow,
            Entries = mapped,
            AuthoringFiles = CaptureAuthoringFiles(authoringRoot, cancellationToken),
        };
    }

    private static List<ImportedVpkAuthoringFileSnapshot> CaptureAuthoringFiles(
        string authoringRoot,
        CancellationToken cancellationToken)
    {
        var result = new List<ImportedVpkAuthoringFileSnapshot>();
        foreach (var path in Directory.EnumerateFiles(authoringRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            result.Add(new ImportedVpkAuthoringFileSnapshot(
                NormalizeVpkPath(Path.GetRelativePath(authoringRoot, path)),
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
                stream.Length));
        }
        return result;
    }

    private static string NormalizeTextureAuthoringPath(string path)
    {
        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var sourceMatch = SourceTextureSuffix.Match(stem);
        if (sourceMatch.Success)
        {
            stem = stem[..sourceMatch.Index];
        }
        else
        {
            var generatedMatch = GeneratedHashSuffix.Match(stem);
            if (generatedMatch.Success)
            {
                stem = stem[..generatedMatch.Index];
            }
        }

        var filename = stem + extension;
        return string.IsNullOrEmpty(directory) ? filename : directory + "/" + filename;
    }

    private static string AllocateAuthoringPath(
        string preferredPath,
        string compiledPath,
        HashSet<string> occupiedPaths)
    {
        var normalized = NormalizeVpkPath(preferredPath);
        if (occupiedPaths.Add(normalized))
        {
            return normalized;
        }

        var directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        var filename = Path.GetFileName(normalized);
        var compiledStem = Path.GetFileNameWithoutExtension(compiledPath);
        var hashMatch = GeneratedHashSuffix.Match(compiledStem);
        var variant = hashMatch.Success ? hashMatch.Groups["hash"].Value : ComputePathTag(compiledPath);
        var variantPath = string.IsNullOrEmpty(directory)
            ? $"_variants/{variant}/{filename}"
            : $"{directory}/_variants/{variant}/{filename}";
        occupiedPaths.Add(variantPath);
        return variantPath;
    }

    private static string ComputePathTag(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..8].ToLowerInvariant();

    private static void DumpContentFile(string outputRoot, string path, ContentFile contentFile)
    {
        if (contentFile.Data is not null)
        {
            WriteFile(path, contentFile.Data);
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalFileName = NormalizeVpkPath(additionalFile.FileName);
            var preserveTextureResourceDirectory = additionalFile is TextureContentFile
                && additionalFileName.Contains('/');
            var additionalPath = additionalFile.KeepFullPath || preserveTextureResourceDirectory
                ? SafePath.ResolveUnderRoot(
                    outputRoot,
                    ToWindowsPath(additionalFileName),
                    "Additional imported resource")
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFileName));
            DumpContentFile(outputRoot, additionalPath, additionalFile);
        }

        foreach (var subFile in contentFile.SubFiles)
        {
            var data = subFile.Extract?.Invoke();
            if (data is null)
            {
                continue;
            }

            var parent = SafePath.EnsureUnderRoot(
                outputRoot,
                Path.GetDirectoryName(path)!,
                "Imported subfile parent");
            WriteFile(
                SafePath.ResolveUnderRoot(parent, Path.GetFileName(subFile.FileName), "Imported subfile"),
                data);
        }
    }

    private static void WriteFile(string path, byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
    }

    private static string ToWindowsPath(string value) => value.Replace('/', Path.DirectorySeparatorChar);

    private static string NormalizeVpkPath(string value)
    {
        var original = value.Replace('\\', '/');
        var normalized = SafePath.NormalizeRelative(original, "VPK internal path");
        if (normalized.EndsWith("/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"VPK entry path does not identify a file: '{value}'.");
        }
        return normalized;
    }

    private static void Report(
        IProgress<ImportedVpkImportProgress>? progress,
        int percent,
        string message) =>
        progress?.Report(new ImportedVpkImportProgress(message, Math.Clamp(percent, 0, 100)));

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

    private static void TryCreateDirectory(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The extraction failure remains authoritative.
        }
    }
}
