using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

public sealed record HeroExtractionProgress(string Message);

public sealed record HeroExtractionResult(
    string MainModelResourcePath,
    string SourceVpkPath,
    string OutputFolder,
    int ExtractedFileCount,
    string? Source2ViewerVersion);

public sealed class HeroExtractionService
{
    private readonly DeadlimitPaths _paths;

    public HeroExtractionService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<HeroExtractionResult> ExtractAsync(
        ProjectManifest manifest,
        IProgress<HeroExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Extract(manifest, progress, cancellationToken), cancellationToken);

    private HeroExtractionResult Extract(
        ProjectManifest manifest,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        var hero = manifest.Hero.Trim();
        if (hero.Length == 0)
        {
            throw new InvalidOperationException("Project hero is empty.");
        }

        var retailGameRoot = Path.Combine(_paths.RetailDeadlockRoot, "game");
        if (!Directory.Exists(retailGameRoot))
        {
            throw new DirectoryNotFoundException($"Retail Deadlock game folder was not found: {retailGameRoot}");
        }

        var vrfVersion = typeof(Resource).Assembly.GetName().Version?.ToString();

        progress?.Report(new HeroExtractionProgress("Locating current retail hero model..."));
        var candidate = FindMainModel(retailGameRoot, hero, progress, cancellationToken);
        if (candidate is null)
        {
            throw new InvalidOperationException(
                $"Could not find a retail .vmdl_c candidate for hero '{hero}' in the current Deadlock VPKs.");
        }

        var resourceFolder = GetResourceFolder(candidate.ResourcePath);
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var stagingFolder = Path.Combine(metadataFolder, "source-extract-staging");
        var outputFolder = Path.Combine(manifest.ProjectFolder, manifest.SourceDumpFolderName);
        var previousFolder = Path.Combine(metadataFolder, "0source.previous");

        Directory.CreateDirectory(metadataFolder);
        DeleteDirectoryIfExists(stagingFolder);
        Directory.CreateDirectory(stagingFolder);

        try
        {
            progress?.Report(new HeroExtractionProgress($"Decompiling {resourceFolder}..."));
            ExtractResourceFolder(
                candidate.VpkPath,
                resourceFolder,
                stagingFolder,
                progress,
                cancellationToken);

            var extractedFileCount = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories).Count();
            if (extractedFileCount == 0)
            {
                throw new InvalidOperationException(
                    "ValveResourceFormat completed without an error, but no files were written to the extraction folder.");
            }

            progress?.Report(new HeroExtractionProgress("Publishing refreshed 0source..."));

            DeleteDirectoryIfExists(previousFolder);
            if (Directory.Exists(outputFolder))
            {
                Directory.Move(outputFolder, previousFolder);
            }

            try
            {
                Directory.Move(stagingFolder, outputFolder);
            }
            catch
            {
                if (!Directory.Exists(outputFolder) && Directory.Exists(previousFolder))
                {
                    Directory.Move(previousFolder, outputFolder);
                }

                throw;
            }

            manifest.SchemaVersion = Math.Max(manifest.SchemaVersion, 2);
            manifest.RetailMainModel = candidate.ResourcePath;
            manifest.RetailSourceVpk = candidate.VpkPath;
            manifest.LastSourceExtractionUtc = DateTimeOffset.UtcNow;
            manifest.Source2ViewerVersion = vrfVersion is null ? "ValveResourceFormat" : $"ValveResourceFormat {vrfVersion}";
            manifest.ExtractedSourceFileCount = extractedFileCount;
            ProjectStore.Save(manifest);

            progress?.Report(new HeroExtractionProgress("Hero source extraction complete."));

            return new HeroExtractionResult(
                candidate.ResourcePath,
                candidate.VpkPath,
                outputFolder,
                extractedFileCount,
                manifest.Source2ViewerVersion);
        }
        catch
        {
            DeleteDirectoryIfExists(stagingFolder);
            throw;
        }
    }

    private ModelCandidate? FindMainModel(
        string retailGameRoot,
        string hero,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        var primaryVpk = Path.Combine(retailGameRoot, "citadel", "pak01_dir.vpk");
        var vpks = new List<string>();

        if (File.Exists(primaryVpk))
        {
            vpks.Add(primaryVpk);
        }

        foreach (var vpk in Directory.EnumerateFiles(retailGameRoot, "*_dir.vpk", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            if (!vpks.Contains(vpk, StringComparer.OrdinalIgnoreCase))
            {
                vpks.Add(vpk);
            }
        }

        ModelCandidate? best = null;
        foreach (var vpkPath in vpks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new HeroExtractionProgress($"Scanning {Path.GetFileName(vpkPath)}..."));

            try
            {
                using var package = new Package();
                package.Read(vpkPath);

                foreach (var entry in package.Entries.SelectMany(group => group.Value))
                {
                    var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                    if (!IsHeroModelPath(resourcePath))
                    {
                        continue;
                    }

                    var score = ScoreModelCandidate(resourcePath, hero);
                    if (score <= 0)
                    {
                        continue;
                    }

                    var candidate = new ModelCandidate(vpkPath, resourcePath, score);
                    if (best is null || candidate.Score > best.Score)
                    {
                        best = candidate;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Skipping unreadable VPK {Path.GetFileName(vpkPath)}: {ex.Message}"));
            }

            if (best is { Score: >= 1000 })
            {
                break;
            }
        }

        return best;
    }

    private static void ExtractResourceFolder(
        string vpkPath,
        string resourceFolder,
        string outputRoot,
        IProgress<HeroExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(vpkPath);
        using var fileLoader = new GameFileLoader(package, package.FileName);

        var entries = package.Entries
            .SelectMany(group => group.Value)
            .Select(entry => (Entry: entry, Path: NormalizeResourcePath(entry.GetFullPath())))
            .Where(item => item.Path.StartsWith(resourceFolder, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (entries.Length == 0)
        {
            throw new InvalidOperationException($"No VPK entries matched '{resourceFolder}'.");
        }

        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (entry, filePath) = entries[index];

            if (index == 0 || (index + 1) % 25 == 0 || index == entries.Length - 1)
            {
                progress?.Report(new HeroExtractionProgress(
                    $"Decompiling {index + 1}/{entries.Length}: {Path.GetFileName(filePath)}"));
            }

            try
            {
                package.ReadEntry(entry, out byte[] rawData);

                if (!entry.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    WriteFile(Path.Combine(outputRoot, ToWindowsPath(filePath)), rawData);
                    continue;
                }

                using var stream = new MemoryStream(rawData, writable: false);
                using var resource = new Resource { FileName = filePath };
                resource.Read(stream);

                var outputExtension = FileExtract.GetExtension(resource) ?? entry.TypeName[..^2];
                var decompiledPath = Path.ChangeExtension(filePath, outputExtension);
                var outputPath = Path.Combine(outputRoot, ToWindowsPath(decompiledPath));

                using var contentFile = resource.ResourceType == ResourceType.Texture
                    ? new TextureExtract(resource).ToContentFile()
                    : FileExtract.Extract(resource, fileLoader, null);

                DumpContentFile(outputRoot, outputPath, contentFile);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ValveResourceFormat failed while decompiling '{filePath}': {ex.Message}",
                    ex);
            }
        }
    }

    private static void DumpContentFile(string outputRoot, string path, ContentFile contentFile)
    {
        if (contentFile.Data is not null)
        {
            WriteFile(path, contentFile.Data);
        }

        foreach (var additionalFile in contentFile.AdditionalFiles)
        {
            var additionalPath = additionalFile.KeepFullPath
                ? Path.Combine(outputRoot, ToWindowsPath(additionalFile.FileName))
                : Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileName(additionalFile.FileName));

            DumpContentFile(outputRoot, additionalPath, additionalFile);
        }

        foreach (var subFile in contentFile.SubFiles)
        {
            var data = subFile.Extract?.Invoke();
            if (data is not null)
            {
                WriteFile(Path.Combine(Path.GetDirectoryName(path)!, subFile.FileName), data);
            }
        }
    }

    private static void WriteFile(string path, ReadOnlySpan<byte> data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data.ToArray());
    }

    private static bool IsHeroModelPath(string path) =>
        path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase));

    private static int ScoreModelCandidate(string resourcePath, string hero)
    {
        var normalizedHero = NormalizeToken(hero);
        if (normalizedHero.Length == 0)
        {
            return 0;
        }

        var path = NormalizeResourcePath(resourcePath);
        var fileName = Path.GetFileNameWithoutExtension(path);
        if (fileName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);
        }

        var normalizedFileName = NormalizeToken(fileName);
        var normalizedPath = NormalizeToken(path);

        var score = 0;
        if (string.Equals(normalizedFileName, normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }
        else if (normalizedFileName.Contains(normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 350;
        }

        if (normalizedPath.Contains(normalizedHero, StringComparison.OrdinalIgnoreCase))
        {
            score += 200;
        }

        if (path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }
        else if (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }
        else if (path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (path.Contains("/lod", StringComparison.OrdinalIgnoreCase))
        {
            score -= 100;
        }

        return score;
    }

    private static string NormalizeToken(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string NormalizeResourcePath(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string GetResourceFolder(string resourcePath)
    {
        var normalized = NormalizeResourcePath(resourcePath);
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
    }

    private static string ToWindowsPath(string resourcePath) =>
        resourcePath.Replace('/', Path.DirectorySeparatorChar);

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record ModelCandidate(string VpkPath, string ResourcePath, int Score);
}
