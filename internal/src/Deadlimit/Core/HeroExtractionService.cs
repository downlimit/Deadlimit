using System.Text.RegularExpressions;

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
    private static readonly Regex ModelPathRegex = new(
        @"(?<path>models/[A-Za-z0-9_./\-]+\.vmdl_c)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const string HeroModelPathFilter =
        "models/heroes/,models/heroes_wip/,models/heroes_staging/";

    private readonly DeadlimitPaths _paths;

    public HeroExtractionService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public async Task<HeroExtractionResult> ExtractAsync(
        ProjectManifest manifest,
        string source2ViewerCliPath,
        IProgress<HeroExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
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

        var adapter = new Source2ViewerAdapter(source2ViewerCliPath);
        progress?.Report(new HeroExtractionProgress("Checking Source 2 Viewer..."));
        var versionResult = await adapter.GetVersionAsync(cancellationToken);
        var source2ViewerVersion = FirstNonEmptyLine(versionResult.StandardOutput)
            ?? FirstNonEmptyLine(versionResult.StandardError);

        progress?.Report(new HeroExtractionProgress("Locating current retail hero model..."));
        var candidate = await FindMainModelAsync(adapter, retailGameRoot, hero, progress, cancellationToken);
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

        progress?.Report(new HeroExtractionProgress($"Decompiling {resourceFolder}..."));
        var extraction = await adapter.DecompileVpkFolderAsync(
            candidate.VpkPath,
            resourceFolder,
            stagingFolder,
            cancellationToken);

        if (!extraction.Success)
        {
            DeleteDirectoryIfExists(stagingFolder);
            throw new InvalidOperationException(BuildToolError("Source 2 Viewer extraction failed", extraction));
        }

        var extractedFileCount = Directory.EnumerateFiles(stagingFolder, "*", SearchOption.AllDirectories).Count();
        if (extractedFileCount == 0)
        {
            DeleteDirectoryIfExists(stagingFolder);
            throw new InvalidOperationException(
                "Source 2 Viewer completed without an error, but no files were written to the extraction folder.");
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
        manifest.Source2ViewerVersion = source2ViewerVersion;
        manifest.ExtractedSourceFileCount = extractedFileCount;
        ProjectStore.Save(manifest);

        progress?.Report(new HeroExtractionProgress("Hero source extraction complete."));

        return new HeroExtractionResult(
            candidate.ResourcePath,
            candidate.VpkPath,
            outputFolder,
            extractedFileCount,
            source2ViewerVersion);
    }

    private async Task<ModelCandidate?> FindMainModelAsync(
        Source2ViewerAdapter adapter,
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
        foreach (var vpk in vpks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new HeroExtractionProgress($"Scanning {Path.GetFileName(vpk)}..."));

            var listResult = await adapter.ListVpkResourcesAsync(
                vpk,
                HeroModelPathFilter,
                cancellationToken);

            if (!listResult.Success)
            {
                continue;
            }

            foreach (Match match in ModelPathRegex.Matches(listResult.StandardOutput.Replace('\\', '/')))
            {
                var resourcePath = match.Groups["path"].Value.Trim().Replace('\\', '/');
                var score = ScoreModelCandidate(resourcePath, hero);
                if (score <= 0)
                {
                    continue;
                }

                var candidate = new ModelCandidate(vpk, resourcePath, score);
                if (best is null || candidate.Score > best.Score)
                {
                    best = candidate;
                }
            }

            if (best is { Score: >= 1000 })
            {
                break;
            }
        }

        return best;
    }

    private static int ScoreModelCandidate(string resourcePath, string hero)
    {
        var normalizedHero = NormalizeToken(hero);
        if (normalizedHero.Length == 0)
        {
            return 0;
        }

        var path = resourcePath.Replace('\\', '/');
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

    private static string GetResourceFolder(string resourcePath)
    {
        var normalized = resourcePath.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        return slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
    }

    private static string BuildToolError(string heading, ExternalToolResult result)
    {
        var detail = FirstNonEmptyLine(result.StandardError)
            ?? FirstNonEmptyLine(result.StandardOutput)
            ?? $"exit code {result.ExitCode}";
        return $"{heading}: {detail}";
    }

    private static string? FirstNonEmptyLine(string text) =>
        text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private sealed record ModelCandidate(string VpkPath, string ResourcePath, int Score);
}
