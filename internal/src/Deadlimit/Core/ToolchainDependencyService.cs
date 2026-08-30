using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public enum ToolchainStatusKind
{
    NotSpecified,
    Installed,
    UpToDate,
    UpdateAvailable,
    InvalidPath,
    NetworkIssue,
    Checking,
    Working,
    Ready,
}

public sealed record ToolchainStatus(
    ToolchainStatusKind Kind,
    string Detail = "",
    bool NetworkAvailable = false,
    int? InstalledGeneration = null,
    int? AvailableGeneration = null);

public sealed record ToolchainInstallResult(string RootPath, ToolchainStatus Status);

public sealed class ToolchainDependencyService
{
    private const string CsdkCatalogUrl = "https://deadlockmodding.pages.dev/modding-tools/";
    private const string CsdkFallbackPage = "https://deadlockmodding.pages.dev/modding-tools/csdk-12";
    private const string CsdkFallbackDriveId = "1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS";
    private const string DeadlockToolsRepositoryUrl = "https://github.com/dotryen/DeadlockTools.git";
    private const string DeadlockToolsCommitApiUrl = "https://api.github.com/repos/dotryen/DeadlockTools/commits/master";
    private const string DepotDownloaderLatestReleaseApiUrl = "https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest";
    private const string CsdkMarkerFileName = ".deadlimit-csdk.json";
    private const string CsdkSetupMarkerFileName = ".deadlimit-csdk-setup.json";

    private static readonly Regex CsdkGenerationRegex = new(
        "\\bCSDK\\s+(?<generation>\\d+)\\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex CsdkGenerationFromPathRegex = new(
        "(?:reduced[_\\s-]*)?csdk[_\\s-]*(?<generation>\\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex GoogleDriveFileRegex = new(
        "https://drive\\.google\\.com/file/d/(?<id>[A-Za-z0-9_-]+)/view",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DepotManifestRegex = new(
        "-app\\s+(?<app>\\d+)\\s+-depot\\s+(?<depot>\\d+)\\s+-manifest\\s+(?<manifest>\\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DepotManifestArchiveRegex = new(
        "href=[\"'](?<href>[^\"']*DepotDownloaderManifests\\.zip)[\"']",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    public ToolchainDependencyService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlimitManager/1.0");
    }

    public ToolchainStatus CheckRetailDeadlock(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new(ToolchainStatusKind.NotSpecified);
        }
        if (!Directory.Exists(root))
        {
            return new(ToolchainStatusKind.InvalidPath, "The selected folder does not exist.");
        }
        if (!Directory.Exists(Path.Combine(root, "game", "citadel")))
        {
            return new(ToolchainStatusKind.InvalidPath, "The selected folder does not contain game\\citadel.");
        }
        return new(ToolchainStatusKind.Ready, "Retail Deadlock installation detected.");
    }

    public ToolchainStatus CheckProjectsRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new(ToolchainStatusKind.NotSpecified);
        }
        return Directory.Exists(root)
            ? new(ToolchainStatusKind.Ready, "Projects folder is available.")
            : new(ToolchainStatusKind.InvalidPath, "The selected projects folder does not exist.");
    }

    public async Task<ToolchainStatus> CheckCsdkAsync(string root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new(ToolchainStatusKind.NotSpecified);
        }
        if (!Directory.Exists(root) || !File.Exists(Path.Combine(root, "csdkcfg.exe")))
        {
            return new(ToolchainStatusKind.InvalidPath, "csdkcfg.exe was not found in the selected Reduced CSDK folder.");
        }

        var installedGeneration = TryReadCsdkGeneration(root);
        try
        {
            var catalog = await GetLatestCsdkCatalogAsync(cancellationToken).ConfigureAwait(false);
            if (installedGeneration is null)
            {
                return new(
                    ToolchainStatusKind.Installed,
                    $"CSDK is valid. Latest published generation is {catalog.Generation}, but the local generation could not be identified.",
                    true,
                    AvailableGeneration: catalog.Generation);
            }
            if (installedGeneration.Value < catalog.Generation)
            {
                return new(
                    ToolchainStatusKind.UpdateAvailable,
                    $"Installed CSDK {installedGeneration.Value}; CSDK {catalog.Generation} is available.",
                    true,
                    installedGeneration,
                    catalog.Generation);
            }
            return new(
                ToolchainStatusKind.UpToDate,
                $"Installed CSDK generation: {installedGeneration.Value}.",
                true,
                installedGeneration,
                catalog.Generation);
        }
        catch (Exception exception) when (IsNetworkException(exception))
        {
            return new(
                ToolchainStatusKind.NetworkIssue,
                "CSDK is installed, but freshness could not be checked because the update source is unavailable.");
        }
    }

    public async Task<ToolchainStatus> CheckDeadlockToolsAsync(string root, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return new(ToolchainStatusKind.NotSpecified);
        }
        if (!Directory.Exists(root) || !File.Exists(GetDeadlockToolsExecutable(root)))
        {
            return new(ToolchainStatusKind.InvalidPath, "DeadlockTools.exe was not found at the expected Release build path.");
        }
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            return new(
                ToolchainStatusKind.Installed,
                "DeadlockTools is valid, but this folder is not a Git checkout, so freshness cannot be verified automatically.");
        }

        try
        {
            var localCommit = (await RunForOutputAsync(
                "git",
                $"-C {Quote(root)} rev-parse HEAD",
                root,
                cancellationToken).ConfigureAwait(false)).Trim();
            var remoteCommit = await GetDeadlockToolsRemoteCommitAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(localCommit, remoteCommit, StringComparison.OrdinalIgnoreCase)
                ? new(ToolchainStatusKind.UpToDate, $"Current commit: {ShortSha(localCommit)}.", true)
                : new(ToolchainStatusKind.UpdateAvailable, $"Installed: {ShortSha(localCommit)}. Available: {ShortSha(remoteCommit)}.", true);
        }
        catch (Exception exception) when (IsNetworkException(exception))
        {
            return new(
                ToolchainStatusKind.NetworkIssue,
                "DeadlockTools is installed, but freshness could not be checked because the update source is unavailable.");
        }
        catch (InvalidOperationException exception)
        {
            return new(ToolchainStatusKind.Installed, exception.Message);
        }
    }

    public async Task<ToolchainInstallResult> InstallCsdkAsync(
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureEmptyDestination(destinationRoot, "Reduced CSDK");
        Directory.CreateDirectory(destinationRoot);
        var catalog = await GetLatestCsdkCatalogAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report($"Downloading CSDK {catalog.Generation}...");
        await InstallCsdkArchiveAsync(catalog, destinationRoot, false, cancellationToken).ConfigureAwait(false);
        WriteCsdkMarker(destinationRoot, catalog, setup: false);
        return new(
            Path.GetFullPath(destinationRoot),
            new(ToolchainStatusKind.UpToDate, $"Installed CSDK generation: {catalog.Generation}.", true, catalog.Generation, catalog.Generation));
    }

    public async Task<ToolchainInstallResult> UpdateCsdkAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateCsdkRoot(root);
        var catalog = await GetLatestCsdkCatalogAsync(cancellationToken).ConfigureAwait(false);
        progress?.Report($"Downloading CSDK {catalog.Generation}...");
        await InstallCsdkArchiveAsync(catalog, root, true, cancellationToken).ConfigureAwait(false);
        WriteCsdkMarker(root, catalog, setup: false);
        return new(
            Path.GetFullPath(root),
            new(ToolchainStatusKind.UpToDate, $"Installed CSDK generation: {catalog.Generation}.", true, catalog.Generation, catalog.Generation));
    }

    public async Task<ToolchainInstallResult> InstallDeadlockToolsAsync(
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        EnsureEmptyDestination(destinationRoot, "DeadlockTools");
        Directory.CreateDirectory(destinationRoot);
        progress?.Report("Cloning DeadlockTools...");
        await RunAsync("git", $"clone {Quote(DeadlockToolsRepositoryUrl)} .", destinationRoot, cancellationToken).ConfigureAwait(false);
        progress?.Report("Building DeadlockTools Release...");
        await BuildDeadlockToolsAsync(destinationRoot, cancellationToken).ConfigureAwait(false);
        if (!File.Exists(GetDeadlockToolsExecutable(destinationRoot)))
        {
            throw new InvalidOperationException("DeadlockTools build did not produce the expected executable.");
        }
        return new(Path.GetFullPath(destinationRoot), await CheckDeadlockToolsAsync(destinationRoot, cancellationToken).ConfigureAwait(false));
    }

    public async Task<ToolchainStatus> UpdateDeadlockToolsAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(Path.Combine(root, ".git")))
        {
            throw new InvalidOperationException("This DeadlockTools installation is not a Git checkout and cannot be updated in place.");
        }
        progress?.Report("Updating DeadlockTools...");
        await RunAsync("git", $"-C {Quote(root)} pull --ff-only origin master", root, cancellationToken).ConfigureAwait(false);
        progress?.Report("Building DeadlockTools Release...");
        await BuildDeadlockToolsAsync(root, cancellationToken).ConfigureAwait(false);
        return await CheckDeadlockToolsAsync(root, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetupCsdkAsync(
        string csdkRoot,
        string retailDeadlockRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateCsdkRoot(csdkRoot);
        if (CheckRetailDeadlock(retailDeadlockRoot).Kind != ToolchainStatusKind.Ready)
        {
            throw new InvalidOperationException("A valid Retail Deadlock path is required before CSDK setup can run.");
        }

        var catalog = await GetLatestCsdkCatalogAsync(cancellationToken).ConfigureAwait(false);
        if (catalog.Depots.Count == 0)
        {
            throw new InvalidOperationException("The current CSDK guide does not expose the required full-game depot manifests.");
        }

        progress?.Report("Preparing DepotDownloader...");
        var depotDownloader = await EnsureDepotDownloaderAsync(cancellationToken).ConfigureAwait(false);
        var fallbackApplied = false;
        foreach (var depot in catalog.Depots)
        {
            progress?.Report($"Downloading Deadlock depot {depot.DepotId}...");
            try
            {
                await RunInteractiveAsync(
                    depotDownloader,
                    DepotArguments(depot, csdkRoot),
                    Path.GetDirectoryName(depotDownloader)!,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException) when (!fallbackApplied && catalog.ManifestArchiveUri is not null)
            {
                progress?.Report("Applying the manifest fallback published with the current CSDK guide...");
                await ApplyManifestFallbackAsync(catalog.ManifestArchiveUri, csdkRoot, cancellationToken).ConfigureAwait(false);
                fallbackApplied = true;
                await RunInteractiveAsync(
                    depotDownloader,
                    DepotArguments(depot, csdkRoot),
                    Path.GetDirectoryName(depotDownloader)!,
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var citadelRoot = Path.Combine(csdkRoot, "game", "citadel");
        var citadelVpk = Path.Combine(citadelRoot, "pak01_dir.vpk");
        if (!File.Exists(citadelVpk))
        {
            throw new FileNotFoundException("DepotDownloader completed, but game\\citadel\\pak01_dir.vpk was not found.", citadelVpk);
        }

        progress?.Report("Extracting full game files from the downloaded VPK...");
        ExtractVpkAsIs(citadelVpk, citadelRoot, progress, cancellationToken);
        DeletePak01Vpks(citadelRoot);
        DeletePak01Vpks(Path.Combine(csdkRoot, "game", "core"));

        progress?.Report("Re-applying the current Reduced CSDK files...");
        await InstallCsdkArchiveAsync(catalog, csdkRoot, true, cancellationToken).ConfigureAwait(false);
        WriteCsdkMarker(csdkRoot, catalog, setup: true);
        progress?.Report("CSDK setup complete.");
    }

    private async Task<CsdkCatalog> GetLatestCsdkCatalogAsync(CancellationToken cancellationToken)
    {
        string html;
        try
        {
            html = await _http.GetStringAsync(CsdkCatalogUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return await ReadCsdkPageAsync(12, new Uri(CsdkFallbackPage), CsdkFallbackDriveId, cancellationToken).ConfigureAwait(false);
        }

        var generations = CsdkGenerationRegex.Matches(WebUtility.HtmlDecode(html))
            .Select(match => int.Parse(match.Groups["generation"].Value, System.Globalization.CultureInfo.InvariantCulture))
            .Distinct()
            .OrderByDescending(value => value)
            .ToArray();
        if (generations.Length == 0)
        {
            return await ReadCsdkPageAsync(12, new Uri(CsdkFallbackPage), CsdkFallbackDriveId, cancellationToken).ConfigureAwait(false);
        }
        var generation = generations[0];
        return await ReadCsdkPageAsync(
            generation,
            new Uri($"https://deadlockmodding.pages.dev/modding-tools/csdk-{generation}"),
            generation == 12 ? CsdkFallbackDriveId : null,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<CsdkCatalog> ReadCsdkPageAsync(
        int generation,
        Uri pageUri,
        string? fallbackDriveId,
        CancellationToken cancellationToken)
    {
        var html = WebUtility.HtmlDecode(await _http.GetStringAsync(pageUri, cancellationToken).ConfigureAwait(false));
        var driveMatch = GoogleDriveFileRegex.Match(html);
        var driveId = driveMatch.Success ? driveMatch.Groups["id"].Value : fallbackDriveId;
        if (string.IsNullOrWhiteSpace(driveId))
        {
            throw new InvalidOperationException($"Could not locate the CSDK {generation} archive on the current installation page.");
        }

        var depots = DepotManifestRegex.Matches(html)
            .Select(match => new DepotManifest(
                match.Groups["app"].Value,
                match.Groups["depot"].Value,
                match.Groups["manifest"].Value))
            .Distinct()
            .ToArray();
        var fallbackMatch = DepotManifestArchiveRegex.Match(html);
        Uri? fallbackUri = null;
        if (fallbackMatch.Success)
        {
            fallbackUri = new Uri(pageUri, WebUtility.HtmlDecode(fallbackMatch.Groups["href"].Value));
        }

        var downloadUri = new Uri($"https://drive.usercontent.google.com/download?id={Uri.EscapeDataString(driveId)}&export=download&confirm=t");
        return new(generation, pageUri, downloadUri, depots, fallbackUri);
    }

    private async Task InstallCsdkArchiveAsync(CsdkCatalog catalog, string destinationRoot, bool overwrite, CancellationToken cancellationToken)
    {
        var workRoot = CreateTempFolder("csdk");
        var archive = Path.Combine(workRoot, "csdk.zip");
        var extract = Path.Combine(workRoot, "extract");
        Directory.CreateDirectory(extract);
        try
        {
            await DownloadFileAsync(catalog.DownloadUri, archive, cancellationToken).ConfigureAwait(false);
            ZipFile.ExtractToDirectory(archive, extract, true);
            var launcher = Directory.EnumerateFiles(extract, "csdkcfg.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The downloaded CSDK archive does not contain csdkcfg.exe.");
            CopyDirectory(Path.GetDirectoryName(launcher)!, destinationRoot, overwrite);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task ApplyManifestFallbackAsync(Uri uri, string csdkRoot, CancellationToken cancellationToken)
    {
        var workRoot = CreateTempFolder("depot-manifests");
        var archive = Path.Combine(workRoot, "DepotDownloaderManifests.zip");
        try
        {
            await DownloadFileAsync(uri, archive, cancellationToken).ConfigureAwait(false);
            ZipFile.ExtractToDirectory(archive, csdkRoot, true);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task<string> EnsureDepotDownloaderAsync(CancellationToken cancellationToken)
    {
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deadlimit", "tools", "DepotDownloader");
        var executable = Path.Combine(cacheRoot, "DepotDownloader.exe");
        if (File.Exists(executable))
        {
            return executable;
        }

        using var response = await _http.GetAsync(DepotDownloaderLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var assetUrl = document.RootElement.GetProperty("assets")
            .EnumerateArray()
            .Where(asset => string.Equals(asset.GetProperty("name").GetString(), "DepotDownloader-windows-x64.zip", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException("The latest DepotDownloader release does not contain a Windows x64 archive.");

        var workRoot = CreateTempFolder("depotdownloader");
        var archive = Path.Combine(workRoot, "DepotDownloader.zip");
        try
        {
            await DownloadFileAsync(new Uri(assetUrl), archive, cancellationToken).ConfigureAwait(false);
            Directory.CreateDirectory(cacheRoot);
            ZipFile.ExtractToDirectory(archive, cacheRoot, true);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
        return File.Exists(executable)
            ? executable
            : throw new FileNotFoundException("DepotDownloader.exe was not found after extraction.", executable);
    }

    private async Task DownloadFileAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The download provider returned an HTML page instead of an archive.");
        }
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetDeadlockToolsRemoteCommitAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(DeadlockToolsCommitApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("sha").GetString()
            ?? throw new InvalidDataException("DeadlockTools commit response did not contain a SHA.");
    }

    private static Task BuildDeadlockToolsAsync(string root, CancellationToken cancellationToken) =>
        RunAsync(
            "dotnet",
            $"build {Quote(Path.Combine(root, "DeadlockTools", "DeadlockTools.csproj"))} -c Release --nologo --verbosity minimal",
            root,
            cancellationToken);

    private static async Task RunAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, workingDirectory, false, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            throw new InvalidOperationException($"{fileName} failed with exit code {result.ExitCode}: {detail.Trim()}");
        }
    }

    private static async Task<string> RunForOutputAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, workingDirectory, false, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{fileName} failed: {(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()}");
        }
        return result.Output;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        string arguments,
        string workingDirectory,
        bool interactive,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = interactive,
                RedirectStandardOutput = !interactive,
                RedirectStandardError = !interactive,
                CreateNoWindow = !interactive,
                WindowStyle = interactive ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
            },
        };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {fileName}.");
            }
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException($"Required command '{fileName}' is not available.", exception);
        }

        var outputTask = interactive ? Task.FromResult(string.Empty) : process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = interactive ? Task.FromResult(string.Empty) : process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new(process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static async Task RunInteractiveAsync(string fileName, string arguments, string workingDirectory, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync(fileName, arguments, workingDirectory, true, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"{Path.GetFileName(fileName)} failed with exit code {result.ExitCode}.");
        }
    }

    private static void ExtractVpkAsIs(string vpkPath, string outputRoot, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(vpkPath);
        var entries = package.Entries?.SelectMany(group => group.Value).ToArray()
            ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
        for (var index = 0; index < entries.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var relative = entry.GetFullPath().Replace('/', Path.DirectorySeparatorChar);
            package.ReadEntry(entry, out byte[] data);
            var outputPath = SafePath.ResolveUnderRoot(outputRoot, relative, "CSDK full-game VPK extraction");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllBytes(outputPath, data);
            if (index == 0 || (index + 1) % 500 == 0 || index == entries.Length - 1)
            {
                progress?.Report($"Extracting full game files: {index + 1}/{entries.Length}");
            }
        }
    }

    private static void DeletePak01Vpks(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }
        foreach (var file in Directory.EnumerateFiles(folder, "pak01_*.vpk", SearchOption.TopDirectoryOnly))
        {
            File.Delete(file);
        }
    }

    private static void CopyDirectory(string sourceRoot, string destinationRoot, bool overwrite)
    {
        Directory.CreateDirectory(destinationRoot);
        foreach (var source in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite);
        }
    }

    private static int? TryReadCsdkGeneration(string root)
    {
        var marker = Path.Combine(root, CsdkMarkerFileName);
        if (File.Exists(marker))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(marker));
                if (document.RootElement.TryGetProperty("generation", out var generation) && generation.TryGetInt32(out var parsed))
                {
                    return parsed;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
        var match = CsdkGenerationFromPathRegex.Match(new DirectoryInfo(root).Name);
        return match.Success
            ? int.Parse(match.Groups["generation"].Value, System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    private static void WriteCsdkMarker(string root, CsdkCatalog catalog, bool setup)
    {
        var marker = JsonSerializer.Serialize(new
        {
            generation = catalog.Generation,
            source = catalog.PageUri.ToString(),
            updatedUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, CsdkMarkerFileName), marker);
        if (!setup)
        {
            return;
        }
        var setupMarker = JsonSerializer.Serialize(new
        {
            generation = catalog.Generation,
            completedUtc = DateTimeOffset.UtcNow,
            depots = catalog.Depots,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, CsdkSetupMarkerFileName), setupMarker);
    }

    private static void EnsureEmptyDestination(string path, string toolName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{toolName} installation folder is empty.", nameof(path));
        }
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{toolName} installation requires an empty destination folder.");
        }
    }

    private static void ValidateCsdkRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !File.Exists(Path.Combine(root, "csdkcfg.exe")))
        {
            throw new InvalidOperationException("A valid Reduced CSDK installation is required.");
        }
    }

    private static string GetDeadlockToolsExecutable(string root) =>
        Path.Combine(root, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");

    private static string DepotArguments(DepotManifest depot, string csdkRoot) =>
        $"-app {depot.AppId} -depot {depot.DepotId} -manifest {depot.ManifestId} -qr -dir {Quote(csdkRoot)}";

    private static string CreateTempFolder(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), "Deadlimit", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool IsNetworkException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string ShortSha(string value) => value.Length <= 8 ? value : value[..8];

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record DepotManifest(string AppId, string DepotId, string ManifestId);
    private sealed record CsdkCatalog(
        int Generation,
        Uri PageUri,
        Uri DownloadUri,
        IReadOnlyList<DepotManifest> Depots,
        Uri? ManifestArchiveUri);
}
