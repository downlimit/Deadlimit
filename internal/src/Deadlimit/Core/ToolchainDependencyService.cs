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
    int? AvailableGeneration = null,
    string? InstalledVersion = null,
    string? AvailableVersion = null);

public sealed record ToolchainInstallResult(string RootPath, ToolchainStatus Status);

public sealed class ToolchainDependencyService
{
    private const string CsdkCatalogUrl = "https://deadlockmodding.pages.dev/modding-tools/";
    private const string CsdkFallbackPage = "https://deadlockmodding.pages.dev/modding-tools/csdk-12";
    private const string CsdkFallbackDriveId = "1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS";
    private const string DeadlockToolsRepositoryUrl = "https://github.com/dotryen/DeadlockTools.git";
    private const string DeadlockToolsCommitApiUrl = "https://api.github.com/repos/dotryen/DeadlockTools/commits/master";
    private const string DeadlockToolsLatestReleaseApiUrl = "https://api.github.com/repos/dotryen/DeadlockTools/releases/latest";
    private const string DeadlockToolsWindowsAssetName = "DeadlockTools-windows-x64.zip";
    private const string DepotDownloaderLatestReleaseApiUrl = "https://api.github.com/repos/SteamRE/DepotDownloader/releases/latest";
    private const string CsdkMarkerFileName = ".deadlimit-csdk.json";
    private const string CsdkSetupMarkerFileName = ".deadlimit-csdk-setup.json";
    private const string DeadlockToolsMarkerFileName = ".deadlimit-deadlocktools.json";

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
        return new(ToolchainStatusKind.Ready, "Deadlock game client installation detected.");
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

        var executable = GetDeadlockToolsExecutable(root);
        if (!Directory.Exists(root) || !File.Exists(executable))
        {
            return new(ToolchainStatusKind.InvalidPath, "DeadlockTools.exe was not found in the selected DeadlockTools folder.");
        }

        var installedRelease = TryReadDeadlockToolsVersion(root);
        try
        {
            var latestRelease = await GetLatestDeadlockToolsReleaseAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(installedRelease))
            {
                if (string.Equals(installedRelease, latestRelease.TagName, StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        ToolchainStatusKind.UpToDate,
                        $"Installed DeadlockTools release: {installedRelease}.",
                        true,
                        InstalledVersion: installedRelease,
                        AvailableVersion: latestRelease.TagName);
                }

                return new(
                    ToolchainStatusKind.UpdateAvailable,
                    $"Installed DeadlockTools {installedRelease}; {latestRelease.TagName} is available.",
                    true,
                    InstalledVersion: installedRelease,
                    AvailableVersion: latestRelease.TagName);
            }

            if (Directory.Exists(Path.Combine(root, ".git")))
            {
                var localCommit = (await RunForOutputAsync(
                    "git",
                    $"-C {Quote(root)} rev-parse HEAD",
                    root,
                    cancellationToken).ConfigureAwait(false)).Trim();
                var remoteCommit = await GetDeadlockToolsRemoteCommitAsync(cancellationToken).ConfigureAwait(false);
                return string.Equals(localCommit, remoteCommit, StringComparison.OrdinalIgnoreCase)
                    ? new(
                        ToolchainStatusKind.UpToDate,
                        $"Git checkout is at the latest upstream commit ({ShortSha(localCommit)}). Latest packaged release: {latestRelease.TagName}.",
                        true,
                        AvailableVersion: latestRelease.TagName)
                    : new(
                        ToolchainStatusKind.UpdateAvailable,
                        $"Git checkout {ShortSha(localCommit)} is behind upstream {ShortSha(remoteCommit)}. Latest packaged release: {latestRelease.TagName}.",
                        true,
                        AvailableVersion: latestRelease.TagName);
            }

            return new(
                ToolchainStatusKind.Installed,
                $"DeadlockTools is present, but its version cannot be identified. Latest official release: {latestRelease.TagName}. Use INSTALL to switch to a Deadlimit-managed release installation.",
                true,
                AvailableVersion: latestRelease.TagName);
        }
        catch (Exception exception) when (IsNetworkException(exception))
        {
            return new(
                ToolchainStatusKind.NetworkIssue,
                "DeadlockTools is installed, but freshness could not be checked because GitHub is unavailable.",
                InstalledVersion: installedRelease);
        }
        catch (InvalidOperationException exception)
        {
            return new(
                ToolchainStatusKind.Installed,
                exception.Message,
                InstalledVersion: installedRelease);
        }
    }

    public async Task<ToolchainInstallResult> InstallCsdkAsync(
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.Csdk,
            cancellationToken,
            ProgressText("Preparing Reduced CSDK installation…", "Подготовка установки Reduced CSDK…"));
        var destinationExisted = Directory.Exists(destinationRoot);
        try
        {
            EnsureEmptyDestination(destinationRoot, "Reduced CSDK");
            Directory.CreateDirectory(destinationRoot);
            Report(operation, progress, ProgressText("Checking current CSDK release…", "Проверка актуального релиза CSDK…"), 3);
            var catalog = await GetLatestCsdkCatalogAsync(operation.Token).ConfigureAwait(false);
            Report(operation, progress, ProgressText($"Downloading CSDK {catalog.Generation}…", $"Загрузка CSDK {catalog.Generation}…"), 7);
            await InstallCsdkArchiveAsync(catalog, destinationRoot, false, operation, progress, 7, 96).ConfigureAwait(false);
            WriteCsdkMarker(destinationRoot, catalog, setup: false);
            var complete = ProgressText($"CSDK {catalog.Generation} installed.", $"CSDK {catalog.Generation} установлен.");
            ToolchainOperationHub.Complete(operation, complete);
            return new(
                Path.GetFullPath(destinationRoot),
                new(ToolchainStatusKind.UpToDate, $"Installed CSDK generation: {catalog.Generation}.", true, catalog.Generation, catalog.Generation));
        }
        catch (OperationCanceledException)
        {
            if (!destinationExisted)
            {
                TryDeleteDirectory(destinationRoot);
            }
            var cancelled = ProgressText("CSDK installation cancelled.", "Установка CSDK отменена.");
            ToolchainOperationHub.Cancelled(operation, cancelled);
            return new(string.Empty, new(ToolchainStatusKind.NotSpecified, cancelled));
        }
        catch (Exception exception)
        {
            ToolchainOperationHub.Fail(operation, exception.Message);
            throw;
        }
    }

    public async Task<ToolchainInstallResult> UpdateCsdkAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.Csdk,
            cancellationToken,
            ProgressText("Preparing CSDK update…", "Подготовка обновления CSDK…"));
        CsdkCatalog? catalog = null;
        try
        {
            ValidateCsdkRoot(root);
            Report(operation, progress, ProgressText("Checking current CSDK release…", "Проверка актуального релиза CSDK…"), 3);
            catalog = await GetLatestCsdkCatalogAsync(operation.Token).ConfigureAwait(false);
            Report(operation, progress, ProgressText($"Downloading CSDK {catalog.Generation}…", $"Загрузка CSDK {catalog.Generation}…"), 7);
            await InstallCsdkArchiveAsync(catalog, root, true, operation, progress, 7, 96).ConfigureAwait(false);
            WriteCsdkMarker(root, catalog, setup: false);
            var complete = ProgressText($"CSDK {catalog.Generation} updated.", $"CSDK {catalog.Generation} обновлён.");
            ToolchainOperationHub.Complete(operation, complete);
            return new(
                Path.GetFullPath(root),
                new(ToolchainStatusKind.UpToDate, $"Installed CSDK generation: {catalog.Generation}.", true, catalog.Generation, catalog.Generation));
        }
        catch (OperationCanceledException)
        {
            var cancelled = ProgressText("CSDK update cancelled.", "Обновление CSDK отменено.");
            ToolchainOperationHub.Cancelled(operation, cancelled);
            return new(
                Path.GetFullPath(root),
                new(
                    ToolchainStatusKind.Installed,
                    cancelled,
                    catalog is not null,
                    TryReadCsdkGeneration(root),
                    catalog?.Generation));
        }
        catch (Exception exception)
        {
            ToolchainOperationHub.Fail(operation, exception.Message);
            throw;
        }
    }

    public async Task<ToolchainInstallResult> InstallDeadlockToolsAsync(
        string destinationRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.DeadlockTools,
            cancellationToken,
            ProgressText("Preparing DeadlockTools installation…", "Подготовка установки DeadlockTools…"));
        var installRoot = ResolveDeadlockToolsInstallRoot(destinationRoot);
        var rootExisted = Directory.Exists(installRoot);
        try
        {
            EnsureEmptyDestination(installRoot, "DeadlockTools");
            Directory.CreateDirectory(installRoot);
            Report(operation, progress, ProgressText("Checking latest DeadlockTools release…", "Проверка последнего релиза DeadlockTools…"), 3);
            var release = await GetLatestDeadlockToolsReleaseAsync(operation.Token).ConfigureAwait(false);
            Report(operation, progress, ProgressText($"Downloading DeadlockTools {release.TagName}…", $"Загрузка DeadlockTools {release.TagName}…"), 7);
            await InstallDeadlockToolsReleaseAsync(release, installRoot, overwrite: false, operation, progress, 7, 96).ConfigureAwait(false);
            WriteDeadlockToolsMarker(installRoot, release);
            var complete = ProgressText($"DeadlockTools {release.TagName} installed.", $"DeadlockTools {release.TagName} установлен.");
            ToolchainOperationHub.Complete(operation, complete);
            return new(
                Path.GetFullPath(installRoot),
                new(
                    ToolchainStatusKind.UpToDate,
                    $"Installed DeadlockTools release: {release.TagName}.",
                    true,
                    InstalledVersion: release.TagName,
                    AvailableVersion: release.TagName));
        }
        catch (OperationCanceledException)
        {
            if (!rootExisted)
            {
                TryDeleteDirectory(installRoot);
            }
            var cancelled = ProgressText("DeadlockTools installation cancelled.", "Установка DeadlockTools отменена.");
            ToolchainOperationHub.Cancelled(operation, cancelled);
            return new(string.Empty, new(ToolchainStatusKind.NotSpecified, cancelled));
        }
        catch (Exception exception)
        {
            ToolchainOperationHub.Fail(operation, exception.Message);
            throw;
        }
    }

    public async Task<ToolchainStatus> UpdateDeadlockToolsAsync(
        string root,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.DeadlockTools,
            cancellationToken,
            ProgressText("Preparing DeadlockTools update…", "Подготовка обновления DeadlockTools…"));
        DeadlockToolsRelease? release = null;
        try
        {
            var executable = GetDeadlockToolsExecutable(root);
            if (!Directory.Exists(root) || !File.Exists(executable))
            {
                throw new InvalidOperationException("A valid DeadlockTools installation is required.");
            }

            if (Directory.Exists(Path.Combine(root, ".git")) && string.IsNullOrWhiteSpace(TryReadDeadlockToolsVersion(root)))
            {
                Report(operation, progress, ProgressText("Updating DeadlockTools Git checkout…", "Обновление Git checkout DeadlockTools…"), null);
                await RunAsync("git", $"-C {Quote(root)} pull --ff-only origin master", root, operation.Token).ConfigureAwait(false);
                Report(operation, progress, ProgressText("Building DeadlockTools Release…", "Сборка DeadlockTools Release…"), null);
                await BuildDeadlockToolsAsync(root, operation.Token).ConfigureAwait(false);
                var result = await CheckDeadlockToolsAsync(root, operation.Token).ConfigureAwait(false);
                ToolchainOperationHub.Complete(operation, ProgressText("DeadlockTools updated.", "DeadlockTools обновлён."));
                return result;
            }

            var installedVersion = TryReadDeadlockToolsVersion(root);
            if (string.IsNullOrWhiteSpace(installedVersion))
            {
                throw new InvalidOperationException("This DeadlockTools installation has no managed release metadata. Use INSTALL to install the current official release.");
            }

            Report(operation, progress, ProgressText("Checking latest DeadlockTools release…", "Проверка последнего релиза DeadlockTools…"), 3);
            release = await GetLatestDeadlockToolsReleaseAsync(operation.Token).ConfigureAwait(false);
            Report(operation, progress, ProgressText($"Downloading DeadlockTools {release.TagName}…", $"Загрузка DeadlockTools {release.TagName}…"), 7);
            await InstallDeadlockToolsReleaseAsync(release, root, overwrite: true, operation, progress, 7, 96).ConfigureAwait(false);
            WriteDeadlockToolsMarker(root, release);
            ToolchainOperationHub.Complete(operation, ProgressText($"DeadlockTools {release.TagName} updated.", $"DeadlockTools {release.TagName} обновлён."));
            return new(
                ToolchainStatusKind.UpToDate,
                $"Installed DeadlockTools release: {release.TagName}.",
                true,
                InstalledVersion: release.TagName,
                AvailableVersion: release.TagName);
        }
        catch (OperationCanceledException)
        {
            var cancelled = ProgressText("DeadlockTools update cancelled.", "Обновление DeadlockTools отменено.");
            ToolchainOperationHub.Cancelled(operation, cancelled);
            return new(
                ToolchainStatusKind.Installed,
                cancelled,
                release is not null,
                InstalledVersion: TryReadDeadlockToolsVersion(root),
                AvailableVersion: release?.TagName);
        }
        catch (Exception exception)
        {
            ToolchainOperationHub.Fail(operation, exception.Message);
            throw;
        }
    }

    public async Task SetupCsdkAsync(
        string csdkRoot,
        string retailDeadlockRoot,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ReleaseChannelPolicy.RequireUnverifiedToolchainAutomation();
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.Csdk,
            cancellationToken,
            ProgressText("Preparing full CSDK setup…", "Подготовка полной настройки CSDK…"));
        try
        {
            ValidateCsdkRoot(csdkRoot);
            if (CheckRetailDeadlock(retailDeadlockRoot).Kind != ToolchainStatusKind.Ready)
            {
                throw new InvalidOperationException("A valid Deadlock game client path is required before CSDK setup can run.");
            }

            Report(operation, progress, ProgressText("Reading current CSDK setup guide…", "Чтение актуальной инструкции CSDK…"), 2);
            var catalog = await GetLatestCsdkCatalogAsync(operation.Token).ConfigureAwait(false);
            if (catalog.Depots.Count == 0)
            {
                throw new InvalidOperationException("The current CSDK guide does not expose the required full-game depot manifests.");
            }

            Report(operation, progress, ProgressText("Preparing DepotDownloader…", "Подготовка DepotDownloader…"), 5);
            var depotDownloader = await EnsureDepotDownloaderAsync(operation, progress).ConfigureAwait(false);
            var fallbackApplied = false;
            for (var depotIndex = 0; depotIndex < catalog.Depots.Count; depotIndex++)
            {
                var depot = catalog.Depots[depotIndex];
                Report(
                    operation,
                    progress,
                    ProgressText(
                        $"Downloading Deadlock depot {depot.DepotId} ({depotIndex + 1}/{catalog.Depots.Count})…",
                        $"Загрузка депо Deadlock {depot.DepotId} ({depotIndex + 1}/{catalog.Depots.Count})…"),
                    null);
                try
                {
                    await RunInteractiveAsync(
                        depotDownloader,
                        DepotArguments(depot, csdkRoot),
                        Path.GetDirectoryName(depotDownloader)!,
                        operation.Token).ConfigureAwait(false);
                }
                catch (InvalidOperationException) when (!fallbackApplied && catalog.ManifestArchiveUri is not null)
                {
                    Report(operation, progress, ProgressText("Applying manifest fallback…", "Применение fallback-манифестов…"), 38);
                    await ApplyManifestFallbackAsync(catalog.ManifestArchiveUri, csdkRoot, operation, progress).ConfigureAwait(false);
                    fallbackApplied = true;
                    await RunInteractiveAsync(
                        depotDownloader,
                        DepotArguments(depot, csdkRoot),
                        Path.GetDirectoryName(depotDownloader)!,
                        operation.Token).ConfigureAwait(false);
                }
            }

            var citadelRoot = Path.Combine(csdkRoot, "game", "citadel");
            var citadelVpk = Path.Combine(citadelRoot, "pak01_dir.vpk");
            if (!File.Exists(citadelVpk))
            {
                throw new FileNotFoundException("DepotDownloader completed, but game\\citadel\\pak01_dir.vpk was not found.", citadelVpk);
            }

            Report(operation, progress, ProgressText("Extracting full game files from VPK…", "Извлечение полных файлов игры из VPK…"), 52);
            ExtractVpkAsIs(citadelVpk, citadelRoot, operation, progress, operation.Token, 52, 80);
            DeletePak01Vpks(citadelRoot);
            DeletePak01Vpks(Path.Combine(csdkRoot, "game", "core"));

            Report(operation, progress, ProgressText("Re-applying current Reduced CSDK files…", "Повторное наложение актуальных файлов Reduced CSDK…"), 82);
            await InstallCsdkArchiveAsync(catalog, csdkRoot, true, operation, progress, 82, 98).ConfigureAwait(false);
            WriteCsdkMarker(csdkRoot, catalog, setup: true);
            progress?.Report("CSDK setup complete.");
            ToolchainOperationHub.Complete(operation, ProgressText("CSDK setup complete.", "Настройка CSDK завершена."));
        }
        catch (OperationCanceledException)
        {
            var cancelled = ProgressText("CSDK setup cancelled.", "Настройка CSDK отменена.");
            progress?.Report(cancelled);
            ToolchainOperationHub.Cancelled(operation, cancelled);
        }
        catch (Exception exception)
        {
            ToolchainOperationHub.Fail(operation, exception.Message);
            throw;
        }
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

    private async Task<DeadlockToolsRelease> GetLatestDeadlockToolsReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(DeadlockToolsLatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("DeadlockTools latest release response did not contain tag_name.");
        var htmlUrl = root.GetProperty("html_url").GetString()
            ?? throw new InvalidDataException("DeadlockTools latest release response did not contain html_url.");
        var assetUrl = root.GetProperty("assets")
            .EnumerateArray()
            .Where(asset => string.Equals(asset.GetProperty("name").GetString(), DeadlockToolsWindowsAssetName, StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException($"DeadlockTools {tagName} does not contain {DeadlockToolsWindowsAssetName}.");
        return new(tagName, new Uri(htmlUrl), new Uri(assetUrl));
    }

    private async Task InstallCsdkArchiveAsync(
        CsdkCatalog catalog,
        string destinationRoot,
        bool overwrite,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        var workRoot = CreateTempFolder("csdk");
        var archive = Path.Combine(workRoot, "csdk.zip");
        var extract = Path.Combine(workRoot, "extract");
        Directory.CreateDirectory(extract);
        try
        {
            var downloadEnd = Math.Max(startPercent + 1, endPercent - 18);
            await DownloadFileAsync(
                catalog.DownloadUri,
                archive,
                operation,
                progress,
                ProgressText($"Downloading CSDK {catalog.Generation}", $"Загрузка CSDK {catalog.Generation}"),
                startPercent,
                downloadEnd).ConfigureAwait(false);
            Report(operation, progress, ProgressText("Extracting CSDK archive…", "Распаковка архива CSDK…"), downloadEnd + 1);
            await ExtractZipAsync(archive, extract, true, operation.Token, operation, progress, downloadEnd + 1, endPercent - 8).ConfigureAwait(false);
            var launcher = Directory.EnumerateFiles(extract, "csdkcfg.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The downloaded CSDK archive does not contain csdkcfg.exe.");
            Report(operation, progress, ProgressText("Applying CSDK files…", "Применение файлов CSDK…"), endPercent - 7);
            CopyDirectory(Path.GetDirectoryName(launcher)!, destinationRoot, overwrite, operation.Token, operation, progress, endPercent - 7, endPercent);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task InstallDeadlockToolsReleaseAsync(
        DeadlockToolsRelease release,
        string destinationRoot,
        bool overwrite,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        var workRoot = CreateTempFolder("deadlocktools");
        var archive = Path.Combine(workRoot, DeadlockToolsWindowsAssetName);
        var extract = Path.Combine(workRoot, "extract");
        Directory.CreateDirectory(extract);
        try
        {
            var downloadEnd = Math.Max(startPercent + 1, endPercent - 18);
            await DownloadFileAsync(
                release.DownloadUri,
                archive,
                operation,
                progress,
                ProgressText($"Downloading DeadlockTools {release.TagName}", $"Загрузка DeadlockTools {release.TagName}"),
                startPercent,
                downloadEnd).ConfigureAwait(false);
            Report(operation, progress, ProgressText("Extracting DeadlockTools…", "Распаковка DeadlockTools…"), downloadEnd + 1);
            await ExtractZipAsync(archive, extract, true, operation.Token, operation, progress, downloadEnd + 1, endPercent - 8).ConfigureAwait(false);
            var executable = Directory.EnumerateFiles(extract, "DeadlockTools.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The downloaded DeadlockTools release does not contain DeadlockTools.exe.");
            Report(operation, progress, ProgressText("Installing DeadlockTools files…", "Установка файлов DeadlockTools…"), endPercent - 7);
            CopyDirectory(Path.GetDirectoryName(executable)!, destinationRoot, overwrite, operation.Token, operation, progress, endPercent - 7, endPercent);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task ApplyManifestFallbackAsync(
        Uri uri,
        string csdkRoot,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress)
    {
        var workRoot = CreateTempFolder("depot-manifests");
        var archive = Path.Combine(workRoot, "DepotDownloaderManifests.zip");
        try
        {
            await DownloadFileAsync(
                uri,
                archive,
                operation,
                progress,
                ProgressText("Downloading manifest fallback", "Загрузка fallback-манифестов"),
                38,
                43).ConfigureAwait(false);
            await ExtractZipAsync(archive, csdkRoot, true, operation.Token, operation, progress, 43, 45).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task<string> EnsureDepotDownloaderAsync(
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress)
    {
        var cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Deadlimit", "tools", "DepotDownloader");
        var executable = Path.Combine(cacheRoot, "DepotDownloader.exe");
        if (File.Exists(executable))
        {
            return executable;
        }

        using var response = await _http.GetAsync(DepotDownloaderLatestReleaseApiUrl, operation.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: operation.Token).ConfigureAwait(false);
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
            await DownloadFileAsync(
                new Uri(assetUrl),
                archive,
                operation,
                progress,
                ProgressText("Downloading DepotDownloader", "Загрузка DepotDownloader"),
                5,
                12).ConfigureAwait(false);
            Directory.CreateDirectory(cacheRoot);
            await ExtractZipAsync(archive, cacheRoot, true, operation.Token, operation, progress, 12, 15).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
        return File.Exists(executable)
            ? executable
            : throw new FileNotFoundException("DepotDownloader.exe was not found after extraction.", executable);
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        string label,
        int startPercent,
        int endPercent)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, operation.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (string.Equals(response.Content.Headers.ContentType?.MediaType, "text/html", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The download provider returned an HTML page instead of an archive.");
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[256 * 1024];
        long transferred = 0;
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var read = await source.ReadAsync(buffer, operation.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), operation.Token).ConfigureAwait(false);
            transferred += read;
            if (total is > 0)
            {
                var fraction = Math.Clamp((double)transferred / total.Value, 0d, 1d);
                var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                var message = FormatDownloadMessage(label, transferred, total.Value, stopwatch.Elapsed);
                Report(operation, progress, message, percent);
            }
            else
            {
                Report(operation, progress, $"{label}… {FormatBytes(transferred)}", null);
            }
        }

        Report(operation, progress, total is > 0
            ? FormatDownloadMessage(label, transferred, total.Value, stopwatch.Elapsed)
            : $"{label}… {FormatBytes(transferred)}", endPercent);
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

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        });

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

    private static void ExtractVpkAsIs(
        string vpkPath,
        string outputRoot,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        CancellationToken cancellationToken,
        int startPercent,
        int endPercent)
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
                var fraction = entries.Length == 0 ? 1d : (double)(index + 1) / entries.Length;
                var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                var message = ProgressText(
                    $"Extracting full game files: {index + 1}/{entries.Length}",
                    $"Извлечение полных файлов игры: {index + 1}/{entries.Length}");
                Report(operation, progress, message, percent);
            }
        }
    }

    private static async Task ExtractZipAsync(
        string archivePath,
        string outputRoot,
        bool overwrite,
        CancellationToken cancellationToken,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entries = archive.Entries;
        for (var index = 0; index < entries.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entry = entries[index];
            var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            var outputPath = SafePath.ResolveUnderRoot(outputRoot, relative, "toolchain ZIP extraction");
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(outputPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                if (!overwrite && File.Exists(outputPath))
                {
                    throw new IOException($"File already exists: {outputPath}");
                }
                await using var source = entry.Open();
                await using var target = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024, true);
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            }

            if (index == 0 || (index + 1) % 50 == 0 || index == entries.Count - 1)
            {
                var fraction = entries.Count == 0 ? 1d : (double)(index + 1) / entries.Count;
                var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                Report(operation, progress, ProgressText("Extracting archive…", "Распаковка архива…"), percent);
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

    private static void CopyDirectory(
        string sourceRoot,
        string destinationRoot,
        bool overwrite,
        CancellationToken cancellationToken,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        Directory.CreateDirectory(destinationRoot);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        for (var index = 0; index < files.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = files[index];
            var destination = Path.Combine(destinationRoot, Path.GetRelativePath(sourceRoot, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite);
            if (index == 0 || (index + 1) % 50 == 0 || index == files.Length - 1)
            {
                var fraction = files.Length == 0 ? 1d : (double)(index + 1) / files.Length;
                var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                Report(operation, progress, ProgressText("Applying files…", "Применение файлов…"), percent);
            }
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

    private static string? TryReadDeadlockToolsVersion(string root)
    {
        var marker = Path.Combine(root, DeadlockToolsMarkerFileName);
        if (!File.Exists(marker))
        {
            return null;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            return document.RootElement.TryGetProperty("tag", out var tag)
                ? tag.GetString()
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
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

    private static void WriteDeadlockToolsMarker(string root, DeadlockToolsRelease release)
    {
        var marker = JsonSerializer.Serialize(new
        {
            tag = release.TagName,
            source = release.PageUri.ToString(),
            asset = DeadlockToolsWindowsAssetName,
            updatedUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(root, DeadlockToolsMarkerFileName), marker);
    }

    private static void EnsureEmptyDestination(string path, string toolName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{toolName} installation folder is empty.", nameof(path));
        }
        if (Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
        {
            throw new InvalidOperationException($"{toolName} installation target already exists and is not empty: {path}");
        }
    }

    private static void ValidateCsdkRoot(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root) || !File.Exists(Path.Combine(root, "csdkcfg.exe")))
        {
            throw new InvalidOperationException("A valid Reduced CSDK installation is required.");
        }
    }

    private static string ResolveDeadlockToolsInstallRoot(string selectedPath)
    {
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            throw new ArgumentException("DeadlockTools installation location is empty.", nameof(selectedPath));
        }

        var full = Path.GetFullPath(selectedPath.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(Path.GetFileName(full), "DeadlockTools", StringComparison.OrdinalIgnoreCase)
            ? full
            : Path.Combine(full, "DeadlockTools");
    }

    private static string GetDeadlockToolsExecutable(string root)
    {
        var managedRelease = Path.Combine(root, "DeadlockTools.exe");
        if (File.Exists(managedRelease))
        {
            return managedRelease;
        }

        return Path.Combine(root, "DeadlockTools", "bin", "Release", "net10.0", "DeadlockTools.exe");
    }

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

    private static void Report(
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        string message,
        int? percent)
    {
        progress?.Report(message);
        ToolchainOperationHub.Report(operation, message, percent);
    }

    private static string FormatDownloadMessage(string label, long transferred, long total, TimeSpan elapsed)
    {
        var fraction = total <= 0 ? 0d : Math.Clamp((double)transferred / total, 0d, 1d);
        var percent = (int)Math.Round(fraction * 100d);
        var eta = string.Empty;
        if (transferred > 0 && elapsed.TotalSeconds > 0.5 && transferred < total)
        {
            var bytesPerSecond = transferred / elapsed.TotalSeconds;
            if (bytesPerSecond > 1)
            {
                var remaining = TimeSpan.FromSeconds((total - transferred) / bytesPerSecond);
                eta = ProgressText(
                    $" · ~{FormatDuration(remaining)} left",
                    $" · осталось ~{FormatDuration(remaining)}");
            }
        }

        return $"{label}… {percent}% · {FormatBytes(transferred)}/{FormatBytes(total)}{eta}";
    }

    private static string FormatBytes(long bytes)
    {
        const double mb = 1024d * 1024d;
        return $"{bytes / mb:0.0} MB";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{Math.Ceiling(duration.TotalMinutes):0} min";
        }
        return $"{Math.Max(1, Math.Ceiling(duration.TotalSeconds)):0} s";
    }

    private static string ProgressText(string english, string russian) =>
        string.Equals(ProjectStore.GetToolPathSettings().UiLanguage, "ru", StringComparison.OrdinalIgnoreCase)
            ? russian
            : english;

    private static bool IsNetworkException(Exception exception) =>
        exception is HttpRequestException or TaskCanceledException;

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"")}\"";

    private static string ShortSha(string value) => value.Length <= 8 ? value : value[..8];

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
    private sealed record DepotManifest(string AppId, string DepotId, string ManifestId);
    private sealed record DeadlockToolsRelease(string TagName, Uri PageUri, Uri DownloadUri);
    private sealed record CsdkCatalog(
        int Generation,
        Uri PageUri,
        Uri DownloadUri,
        IReadOnlyList<DepotManifest> Depots,
        Uri? ManifestArchiveUri);
}
