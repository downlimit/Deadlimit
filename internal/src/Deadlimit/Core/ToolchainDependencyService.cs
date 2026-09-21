using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
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
    Cancelled,
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

public sealed class GoogleDriveDownloadUnavailableException : InvalidOperationException
{
    public GoogleDriveDownloadUnavailableException(string providerMessage, Uri browserUri)
        : base($"Google Drive refused the download: {providerMessage}")
    {
        ProviderMessage = providerMessage;
        BrowserUri = browserUri;
    }

    public string ProviderMessage { get; }
    public Uri BrowserUri { get; }
}

public sealed class ToolchainDependencyService
{
    private const int PinnedCsdkGeneration = 12;
    private const string CsdkInstallFolderName = "Reduced_CSDK_12";
    private const string CsdkPinnedPage = "https://deadlockmodding.pages.dev/modding-tools/csdk-12";
    private const string CsdkPinnedDriveId = "1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS";
    private const string CsdkPinnedDrivePage = "https://drive.google.com/file/d/1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS/view";
    private const string CsdkPinnedManifestArchiveUrl = "https://deadlockmodding.pages.dev/attachments/csdk12/DepotDownloaderManifests.zip";
    private const string DeadlockToolsRepositoryUrl = "https://github.com/dotryen/DeadlockTools.git";
    private const string DeadlockToolsCommitApiUrl = "https://api.github.com/repos/dotryen/DeadlockTools/commits/master";
    private const string DeadlockToolsPinnedTag = "v1.1.0";
    private const string DeadlockToolsReleaseApiUrl = "https://api.github.com/repos/dotryen/DeadlockTools/releases/tags/v1.1.0";
    private const string DeadlockToolsWindowsAssetName = "DeadlockTools-windows-x64.zip";
    private const string DeadlockToolsWindowsSha256 = "7E4668DA796E4CA67B1EE684CF03270E07FECEBECCF66D04DDF1F3A3E7409DCF";
    private const string DepotDownloaderPinnedTag = "DepotDownloader_3.4.0";
    private const string DepotDownloaderReleaseApiUrl = "https://api.github.com/repos/SteamRE/DepotDownloader/releases/tags/DepotDownloader_3.4.0";
    private const string DepotDownloaderWindowsSha256 = "41C9E9F0DF54B3AD02E67A11726756E5C73283BD7C2E1B04ACFA5AE4C2ED3767";
    private const string CsdkMarkerFileName = ".deadlimit-csdk.json";
    private const string CsdkSetupMarkerFileName = ".deadlimit-csdk-setup.json";
    private const string CsdkArchiveCacheFileName = "csdk.zip";
    private const string CsdkArchiveCacheMarkerFileName = ".deadlimit-csdk-cache.json";
    private const string DeadlockToolsMarkerFileName = ".deadlimit-deadlocktools.json";
    private const string DepotDownloaderMarkerFileName = ".deadlimit-depotdownloader.json";

    private static readonly Regex CsdkGenerationFromPathRegex = new(
        "(?:reduced[_\\s-]*)?csdk[_\\s-]*(?<generation>\\d+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromMinutes(30),
    };

    public ToolchainDependencyService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlimitManager/1.0");
    }

    internal static int RunCsdkInstallDownloadSmoke()
    {
        var parent = Path.Combine(Path.GetTempPath(), "Deadlimit-Smoke-Parent");
        var expectedRoot = Path.Combine(Path.GetFullPath(parent), CsdkInstallFolderName);
        if (!string.Equals(
                ResolveCsdkInstallRoot(parent),
                expectedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        const string formHtml = """
            <html><body>
            <form id="download-form" action="https://drive.usercontent.google.com/download">
              <input type="hidden" name="id" value="file-id">
              <input type="hidden" name="export" value="download">
              <input type="hidden" name="confirm" value="t">
              <input type="hidden" name="uuid" value="test-uuid">
            </form>
            </body></html>
            """;
        var formUri = TryGetGoogleDriveConfirmationUri(
            formHtml,
            new Uri("https://drive.google.com/uc?export=download&id=file-id"));
        if (formUri is null
            || !string.Equals(formUri.Host, "drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase)
            || !formUri.Query.Contains("id=file-id", StringComparison.Ordinal)
            || !formUri.Query.Contains("confirm=t", StringComparison.Ordinal)
            || !formUri.Query.Contains("uuid=test-uuid", StringComparison.Ordinal))
        {
            return 2;
        }

        const string hrefHtml = """
            <a href="/uc?export=download&amp;id=file-id&amp;confirm=t">Download</a>
            """;
        var hrefUri = TryGetGoogleDriveConfirmationUri(
            hrefHtml,
            new Uri("https://drive.google.com/uc?id=file-id"));
        if (hrefUri is null
            || !string.Equals(hrefUri.Host, "docs.google.com", StringComparison.OrdinalIgnoreCase)
            || !hrefUri.Query.Contains("confirm=t", StringComparison.Ordinal))
        {
            return 3;
        }

        const string jsonHtml = """
            <script>{"downloadUrl":"https://drive.usercontent.google.com/download?id\u003dfile-id\u0026confirm\u003dt\u0026uuid\u003djson-uuid"}</script>
            """;
        var jsonUri = TryGetGoogleDriveConfirmationUri(
            jsonHtml,
            new Uri("https://drive.google.com/uc?id=file-id"));
        if (jsonUri is null
            || !string.Equals(jsonUri.Host, "drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase)
            || !jsonUri.Query.Contains("uuid=json-uuid", StringComparison.Ordinal))
        {
            return 4;
        }

        const string errorHtml = """
            <p class="uc-error-subcaption">Too many users have viewed or downloaded this file recently.</p>
            """;
        var providerError = TryGetGoogleDriveError(errorHtml);
        if (string.IsNullOrWhiteSpace(providerError)
            || !providerError.Contains("Too many users", StringComparison.Ordinal))
        {
            return 5;
        }

        var cacheSmokeRoot = Path.Combine(
            Path.GetTempPath(),
            "Deadlimit-Csdl-Cache-Smoke",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(cacheSmokeRoot);
            var archive = Path.Combine(cacheSmokeRoot, CsdkArchiveCacheFileName);
            using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("csdkcfg.exe");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("smoke");
            }

            var smokeCatalog = new CsdkCatalog(
                PinnedCsdkGeneration,
                new Uri(CsdkPinnedPage),
                new Uri("https://example.invalid/csdk.zip"),
                Array.Empty<DepotManifest>(),
                null);
            WriteCsdkArchiveCacheMarker(
                cacheSmokeRoot,
                archive,
                smokeCatalog,
                ComputeFileSha256(archive));

            if (!IsTrustedCsdkArchiveCache(cacheSmokeRoot, archive, smokeCatalog))
            {
                return 6;
            }

            File.AppendAllText(archive, "corrupt");
            if (IsTrustedCsdkArchiveCache(cacheSmokeRoot, archive, smokeCatalog))
            {
                return 7;
            }
        }
        finally
        {
            TryDeleteDirectory(cacheSmokeRoot);
        }

        return 0;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
                if (string.Equals(installedRelease, latestRelease.TagName, StringComparison.OrdinalIgnoreCase)
                    && IsTrustedManagedDeadlockTools(root))
                {
                    return new(
                        ToolchainStatusKind.UpToDate,
                        $"Installed DeadlockTools reviewed release: {installedRelease}.",
                        true,
                        InstalledVersion: installedRelease,
                        AvailableVersion: latestRelease.TagName);
                }

                return new(
                    ToolchainStatusKind.UpdateAvailable,
                    string.Equals(installedRelease, latestRelease.TagName, StringComparison.OrdinalIgnoreCase)
                        ? $"DeadlockTools {installedRelease} is installed, but its integrity marker is missing or does not match. Reinstall the reviewed release."
                        : $"Installed DeadlockTools {installedRelease}; reviewed release {latestRelease.TagName} is required.",
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
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
        using var operation = ToolchainOperationHub.Begin(
            ToolchainOperationTarget.Csdk,
            cancellationToken,
            ProgressText("Preparing Reduced CSDK installation…", "Подготовка установки Reduced CSDK…"));
        var installRoot = ResolveCsdkInstallRoot(destinationRoot);
        var installRootExisted = Directory.Exists(installRoot);
        try
        {
            EnsureEmptyDestination(installRoot, "Reduced CSDK");
            Directory.CreateDirectory(installRoot);
            Report(operation, progress, ProgressText("Checking current CSDK release…", "Проверка актуального релиза CSDK…"), 3);
            var catalog = await GetLatestCsdkCatalogAsync(operation.Token).ConfigureAwait(false);
            Report(operation, progress, ProgressText($"Downloading CSDK {catalog.Generation}…", $"Загрузка CSDK {catalog.Generation}…"), 7);
            await InstallCsdkArchiveAsync(catalog, installRoot, false, operation, progress, 7, 96).ConfigureAwait(false);
            WriteCsdkMarker(installRoot, catalog, setup: false);
            var complete = ProgressText($"CSDK {catalog.Generation} installed.", $"CSDK {catalog.Generation} установлен.");
            ToolchainOperationHub.Complete(operation, complete);
            return new(
                Path.GetFullPath(installRoot),
                new(ToolchainStatusKind.UpToDate, $"Installed CSDK generation: {catalog.Generation}.", true, catalog.Generation, catalog.Generation));
        }
        catch (OperationCanceledException)
        {
            if (!installRootExisted)
            {
                TryDeleteDirectory(installRoot);
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
            Report(operation, progress, ProgressText("Checking reviewed DeadlockTools release…", "Проверка проверенного релиза DeadlockTools…"), 3);
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

            Report(operation, progress, ProgressText("Checking reviewed DeadlockTools release…", "Проверка проверенного релиза DeadlockTools…"), 3);
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
        bool force = false,
        CancellationToken cancellationToken = default)
    {
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

            var depotKeys = catalog.Depots.Select(GetDepotKey).ToArray();
            if (!force && IsCsdkSetupCurrent(csdkRoot, catalog.Generation, depotKeys))
            {
                var alreadyComplete = ProgressText(
                    "CSDK fine-tuning is already complete for the current guide. No files were downloaded or changed.",
                    "Донастройка CSDK по текущей инструкции уже выполнена. Файлы не скачивались и не изменялись.");
                Report(operation, progress, alreadyComplete, 100);
                ToolchainOperationHub.Complete(operation, alreadyComplete);
                return;
            }

            Report(operation, progress, ProgressText("Preparing DepotDownloader…", "Подготовка DepotDownloader…"), 5);
            var depotDownloader = await EnsureDepotDownloaderAsync(operation, progress).ConfigureAwait(false);
            var stagingRoot = CreateTempFolder("csdk-setup");
            try
            {
                var fallbackApplied = false;
                for (var depotIndex = 0; depotIndex < catalog.Depots.Count; depotIndex++)
                {
                    var depot = catalog.Depots[depotIndex];
                    Report(
                        operation,
                        progress,
                        ProgressText(
                            $"Downloading Deadlock depot {depot.DepotId} ({depotIndex + 1}/{catalog.Depots.Count}) to staging…",
                            $"Загрузка депо Deadlock {depot.DepotId} ({depotIndex + 1}/{catalog.Depots.Count}) во временную папку…"),
                        null);
                    try
                    {
                        await RunInteractiveAsync(
                            depotDownloader,
                            DepotArguments(depot, stagingRoot),
                            Path.GetDirectoryName(depotDownloader)!,
                            operation.Token).ConfigureAwait(false);
                    }
                    catch (InvalidOperationException) when (!fallbackApplied && catalog.ManifestArchiveUri is not null)
                    {
                        Report(operation, progress, ProgressText("Applying manifest fallback…", "Применение fallback-манифестов…"), 38);
                        await ApplyManifestFallbackAsync(catalog.ManifestArchiveUri, stagingRoot, operation, progress).ConfigureAwait(false);
                        fallbackApplied = true;
                        await RunInteractiveAsync(
                            depotDownloader,
                            DepotArguments(depot, stagingRoot),
                            Path.GetDirectoryName(depotDownloader)!,
                            operation.Token).ConfigureAwait(false);
                    }
                }

                var stagedCitadelRoot = Path.Combine(stagingRoot, "game", "citadel");
                var citadelVpk = Path.Combine(stagedCitadelRoot, "pak01_dir.vpk");
                if (!File.Exists(citadelVpk))
                {
                    throw new FileNotFoundException("DepotDownloader completed, but staged game\\citadel\\pak01_dir.vpk was not found.", citadelVpk);
                }

                Report(operation, progress, ProgressText("Extracting full game files from staged VPK…", "Извлечение полных файлов игры из временного VPK…"), 52);
                ExtractVpkAsIs(citadelVpk, stagedCitadelRoot, operation, progress, operation.Token, 52, 76);
                DeletePak01Vpks(stagedCitadelRoot);
                DeletePak01Vpks(Path.Combine(stagingRoot, "game", "core"));

                var stagedGameRoot = Path.Combine(stagingRoot, "game");
                if (!Directory.Exists(stagedGameRoot))
                {
                    throw new DirectoryNotFoundException($"Staged Deadlock game folder was not found: {stagedGameRoot}");
                }

                var stagedCsdkRoot = Path.Combine(stagingRoot, "csdk-overlay");
                Report(operation, progress, ProgressText("Staging the current Reduced CSDK overlay…", "Подготовка актуального оверлея Reduced CSDK во временной папке…"), 77);
                await InstallCsdkArchiveAsync(catalog, stagedCsdkRoot, false, operation, progress, 77, 88).ConfigureAwait(false);
                ValidateCsdkRoot(stagedCsdkRoot);

                Report(operation, progress, ProgressText("Applying validated staged game files…", "Применение проверенных временных файлов игры…"), 89);
                CopyDirectory(stagedGameRoot, Path.Combine(csdkRoot, "game"), true, operation.Token, operation, progress, 89, 94);

                Report(operation, progress, ProgressText("Applying validated Reduced CSDK files…", "Применение проверенных файлов Reduced CSDK…"), 95);
                CopyDirectory(stagedCsdkRoot, csdkRoot, true, operation.Token, operation, progress, 95, 98);
            }
            finally
            {
                TryDeleteDirectory(stagingRoot);
            }

            WriteCsdkMarker(csdkRoot, catalog, setup: true);
            var complete = ProgressText("CSDK fine-tuning complete.", "Донастройка CSDK завершена.");
            progress?.Report(complete);
            ToolchainOperationHub.Complete(operation, complete);
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

    private static Task<CsdkCatalog> GetLatestCsdkCatalogAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var downloadUri = new Uri(
            $"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(CsdkPinnedDriveId)}");
        var depots = new[]
        {
            new DepotManifest("1422450", "1422451", "2639812037154209539"),
            new DepotManifest("1422450", "1422456", "6378769520310560496"),
        };
        return Task.FromResult(new CsdkCatalog(
            PinnedCsdkGeneration,
            new Uri(CsdkPinnedPage),
            downloadUri,
            depots,
            new Uri(CsdkPinnedManifestArchiveUrl)));
    }

    private async Task<DeadlockToolsRelease> GetLatestDeadlockToolsReleaseAsync(CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(DeadlockToolsReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var tagName = root.GetProperty("tag_name").GetString()
            ?? throw new InvalidDataException("DeadlockTools release response did not contain tag_name.");
        if (!string.Equals(tagName, DeadlockToolsPinnedTag, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"DeadlockTools release identity mismatch. Expected {DeadlockToolsPinnedTag}, received {tagName}.");
        }
        var htmlUrl = root.GetProperty("html_url").GetString()
            ?? throw new InvalidDataException("DeadlockTools release response did not contain html_url.");
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
        var extract = Path.Combine(workRoot, "extract");
        Directory.CreateDirectory(extract);
        try
        {
            var downloadEnd = Math.Max(startPercent + 1, endPercent - 18);
            var archive = await EnsureCsdkArchiveAsync(
                catalog,
                operation,
                progress,
                startPercent,
                downloadEnd).ConfigureAwait(false);
            Report(operation, progress, ProgressText("Extracting CSDK archive…", "Распаковка архива CSDK…"), downloadEnd + 1);
            await ExtractZipAsync(archive, extract, true, operation.Token, operation, progress, downloadEnd + 1, endPercent - 8).ConfigureAwait(false);
            var launcher = Directory.EnumerateFiles(extract, "csdkcfg.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The cached CSDK archive does not contain csdkcfg.exe.");
            Report(operation, progress, ProgressText("Applying CSDK files…", "Применение файлов CSDK…"), endPercent - 7);
            if (overwrite)
            {
                ApplyOverlayTransaction(
                    Path.GetDirectoryName(launcher)!,
                    destinationRoot,
                    operation.Token,
                    operation,
                    progress,
                    endPercent - 7,
                    endPercent);
            }
            else
            {
                CopyDirectory(
                    Path.GetDirectoryName(launcher)!,
                    destinationRoot,
                    overwrite: false,
                    operation.Token,
                    operation,
                    progress,
                    endPercent - 7,
                    endPercent);
            }
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private async Task<string> EnsureCsdkArchiveAsync(
        CsdkCatalog catalog,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        var cacheRoot = GetCsdkArchiveCacheRoot(catalog);
        var archive = Path.Combine(cacheRoot, CsdkArchiveCacheFileName);
        if (IsTrustedCsdkArchiveCache(cacheRoot, archive, catalog))
        {
            Report(
                operation,
                progress,
                ProgressText(
                    $"Using cached CSDK {catalog.Generation} archive.",
                    $"Используется кэшированный архив CSDK {catalog.Generation}."),
                endPercent);
            return archive;
        }

        if (Directory.Exists(cacheRoot))
        {
            TryDeleteDirectory(cacheRoot);
        }
        Directory.CreateDirectory(cacheRoot);

        var temporaryArchive = Path.Combine(cacheRoot, $"csdk-{Guid.NewGuid():N}.download");
        try
        {
            await DownloadFileAsync(
                catalog.DownloadUri,
                temporaryArchive,
                operation,
                progress,
                ProgressText($"Downloading CSDK {catalog.Generation}", $"Загрузка CSDK {catalog.Generation}"),
                startPercent,
                endPercent).ConfigureAwait(false);

            ValidateCsdkArchive(temporaryArchive);
            var archiveSha256 = ComputeFileSha256(temporaryArchive);
            File.Move(temporaryArchive, archive, overwrite: true);
            WriteCsdkArchiveCacheMarker(cacheRoot, archive, catalog, archiveSha256);
            return archive;
        }
        finally
        {
            if (File.Exists(temporaryArchive))
            {
                File.Delete(temporaryArchive);
            }
        }
    }

    private static string GetCsdkArchiveCacheRoot(CsdkCatalog catalog) =>
        UserDataPaths.Combine(
            "cache",
            "csdk",
            catalog.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void ValidateCsdkArchive(string archive)
    {
        using var zip = ZipFile.OpenRead(archive);
        if (!zip.Entries.Any(entry =>
                string.Equals(entry.Name, "csdkcfg.exe", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("The downloaded CSDK archive does not contain csdkcfg.exe.");
        }
    }

    private static void WriteCsdkArchiveCacheMarker(
        string cacheRoot,
        string archive,
        CsdkCatalog catalog,
        string archiveSha256)
    {
        var marker = JsonSerializer.Serialize(new
        {
            generation = catalog.Generation,
            source = catalog.DownloadUri.ToString(),
            archiveSha256,
            archiveSize = new FileInfo(archive).Length,
            cachedUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(Path.Combine(cacheRoot, CsdkArchiveCacheMarkerFileName), marker);
    }

    private static bool IsTrustedCsdkArchiveCache(
        string cacheRoot,
        string archive,
        CsdkCatalog catalog)
    {
        var markerPath = Path.Combine(cacheRoot, CsdkArchiveCacheMarkerFileName);
        if (!File.Exists(markerPath) || !File.Exists(archive))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var marker = document.RootElement;
            if (!marker.TryGetProperty("generation", out var generation)
                || !generation.TryGetInt32(out var parsedGeneration)
                || parsedGeneration != catalog.Generation
                || !marker.TryGetProperty("source", out var source)
                || !string.Equals(source.GetString(), catalog.DownloadUri.ToString(), StringComparison.Ordinal)
                || !marker.TryGetProperty("archiveSize", out var archiveSize)
                || !archiveSize.TryGetInt64(out var expectedSize)
                || new FileInfo(archive).Length != expectedSize
                || !marker.TryGetProperty("archiveSha256", out var archiveHash))
            {
                return false;
            }

            return string.Equals(
                archiveHash.GetString(),
                ComputeFileSha256(archive),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
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
                downloadEnd,
                DeadlockToolsWindowsSha256).ConfigureAwait(false);
            Report(operation, progress, ProgressText("Extracting DeadlockTools…", "Распаковка DeadlockTools…"), downloadEnd + 1);
            await ExtractZipAsync(archive, extract, true, operation.Token, operation, progress, downloadEnd + 1, endPercent - 8).ConfigureAwait(false);
            var executable = Directory.EnumerateFiles(extract, "DeadlockTools.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The downloaded DeadlockTools release does not contain DeadlockTools.exe.");
            Report(operation, progress, ProgressText("Installing DeadlockTools files…", "Установка файлов DeadlockTools…"), endPercent - 7);
            if (overwrite)
            {
                ApplyOverlayTransaction(
                    Path.GetDirectoryName(executable)!,
                    destinationRoot,
                    operation.Token,
                    operation,
                    progress,
                    endPercent - 7,
                    endPercent);
            }
            else
            {
                CopyDirectory(
                    Path.GetDirectoryName(executable)!,
                    destinationRoot,
                    overwrite: false,
                    operation.Token,
                    operation,
                    progress,
                    endPercent - 7,
                    endPercent);
            }
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
        var cacheRoot = UserDataPaths.Combine("tools", "DepotDownloader");
        var executable = Path.Combine(cacheRoot, "DepotDownloader.exe");
        if (File.Exists(executable) && IsTrustedDepotDownloaderCache(cacheRoot, executable))
        {
            return executable;
        }

        if (Directory.Exists(cacheRoot))
        {
            TryDeleteDirectory(cacheRoot);
            if (Directory.Exists(cacheRoot))
            {
                throw new IOException(
                    $"The unverified DepotDownloader cache could not be removed: {cacheRoot}");
            }
        }

        using var response = await _http.GetAsync(DepotDownloaderReleaseApiUrl, operation.Token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: operation.Token).ConfigureAwait(false);
        var tagName = document.RootElement.GetProperty("tag_name").GetString();
        if (!string.Equals(tagName, DepotDownloaderPinnedTag, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"DepotDownloader release identity mismatch. Expected {DepotDownloaderPinnedTag}, received {tagName ?? "<missing>"}.");
        }
        var assetUrl = document.RootElement.GetProperty("assets")
            .EnumerateArray()
            .Where(asset => string.Equals(asset.GetProperty("name").GetString(), "DepotDownloader-windows-x64.zip", StringComparison.OrdinalIgnoreCase))
            .Select(asset => asset.GetProperty("browser_download_url").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?? throw new InvalidOperationException("The reviewed DepotDownloader release does not contain a Windows x64 archive.");

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
                12,
                DepotDownloaderWindowsSha256).ConfigureAwait(false);
            Directory.CreateDirectory(cacheRoot);
            await ExtractZipAsync(archive, cacheRoot, true, operation.Token, operation, progress, 12, 15).ConfigureAwait(false);
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("DepotDownloader.exe was not found after extraction.", executable);
            }
            WriteDepotDownloaderMarker(cacheRoot, executable);
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
        return File.Exists(executable) && IsTrustedDepotDownloaderCache(cacheRoot, executable)
            ? executable
            : throw new InvalidDataException(
                "DepotDownloader extraction completed, but integrity verification metadata is not valid.");
    }

    private async Task<HttpResponseMessage> OpenDownloadResponseAsync(
        Uri uri,
        CancellationToken cancellationToken)
    {
        var currentUri = uri;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await _http.GetAsync(
                currentUri,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            try
            {
                response.EnsureSuccessStatusCode();
                if (!IsGoogleDriveUri(currentUri)
                    || !IsHtmlResponse(response)
                    || response.Content.Headers.ContentDisposition is not null)
                {
                    return response;
                }

                var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var responseUri = response.RequestMessage?.RequestUri ?? currentUri;
                var confirmationUri = TryGetGoogleDriveConfirmationUri(html, responseUri);
                if (confirmationUri is null)
                {
                    var providerError = TryGetGoogleDriveError(html)
                        ?? "Google Drive did not provide a downloadable file. The file may be unavailable, no longer public, or temporarily rate-limited.";
                    throw new GoogleDriveDownloadUnavailableException(
                        providerError,
                        new Uri(CsdkPinnedDrivePage));
                }

                currentUri = confirmationUri;
            }
            catch
            {
                response.Dispose();
                throw;
            }

            response.Dispose();
        }

        throw new GoogleDriveDownloadUnavailableException(
            "Google Drive returned too many confirmation pages while resolving the CSDK archive.",
            new Uri(CsdkPinnedDrivePage));
    }

    private static bool IsGoogleDriveUri(Uri uri) =>
        uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase)
        || uri.Host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsHtmlResponse(HttpResponseMessage response) =>
        string.Equals(
            response.Content.Headers.ContentType?.MediaType,
            "text/html",
            StringComparison.OrdinalIgnoreCase);

    private static Uri? TryGetGoogleDriveConfirmationUri(string html, Uri responseUri)
    {
        foreach (Match formMatch in Regex.Matches(
                     html,
                     @"<form\b(?<attrs>[^>]*)>(?<body>.*?)</form>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
        {
            var attributes = formMatch.Groups["attrs"].Value;
            if (!string.Equals(
                    TryGetHtmlAttribute(attributes, "id"),
                    "download-form",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var action = TryGetHtmlAttribute(attributes, "action");
            if (string.IsNullOrWhiteSpace(action))
            {
                continue;
            }

            var actionUri = Uri.TryCreate(action, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(responseUri, action);
            var parameters = ParseQuery(actionUri.Query);
            foreach (Match inputMatch in Regex.Matches(
                         formMatch.Groups["body"].Value,
                         @"<input\b(?<attrs>[^>]*)>",
                         RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant))
            {
                var inputAttributes = inputMatch.Groups["attrs"].Value;
                var type = TryGetHtmlAttribute(inputAttributes, "type");
                if (!string.Equals(type, "hidden", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var name = TryGetHtmlAttribute(inputAttributes, "name");
                var value = TryGetHtmlAttribute(inputAttributes, "value");
                if (!string.IsNullOrWhiteSpace(name) && value is not null)
                {
                    parameters[name] = value;
                }
            }

            return BuildUriWithQuery(actionUri, parameters);
        }

        var hrefMatch = Regex.Match(
            html,
            @"href\s*=\s*[""'](?<url>/uc\?export=download[^""']+)[""']",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (hrefMatch.Success)
        {
            var relative = System.Net.WebUtility.HtmlDecode(hrefMatch.Groups["url"].Value);
            return new Uri(new Uri("https://docs.google.com"), relative);
        }

        var downloadUrlMatch = Regex.Match(
            html,
            @"""downloadUrl""\s*:\s*""(?<url>(?:\\.|[^""])*)""",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (downloadUrlMatch.Success)
        {
            var encoded = downloadUrlMatch.Groups["url"].Value;
            var decoded = encoded
                .Replace(@"\u003d", "=", StringComparison.OrdinalIgnoreCase)
                .Replace(@"\u0026", "&", StringComparison.OrdinalIgnoreCase)
                .Replace(@"\/", "/", StringComparison.Ordinal);
            if (Uri.TryCreate(decoded, UriKind.Absolute, out var downloadUri))
            {
                return downloadUri;
            }
        }

        return null;
    }

    private static string? TryGetGoogleDriveError(string html)
    {
        var match = Regex.Match(
            html,
            @"<p\b[^>]*class\s*=\s*[""'][^""']*uc-error-subcaption[^""']*[""'][^>]*>(?<message>.*?)</p>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var withoutTags = Regex.Replace(match.Groups["message"].Value, "<[^>]+>", string.Empty);
        var decoded = System.Net.WebUtility.HtmlDecode(withoutTags).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string? TryGetHtmlAttribute(string attributes, string attributeName)
    {
        var match = Regex.Match(
            attributes,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*(?:""(?<double>[^""]*)""|'(?<single>[^']*)'|(?<bare>[^\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["double"].Success
            ? match.Groups["double"].Value
            : match.Groups["single"].Success
                ? match.Groups["single"].Value
                : match.Groups["bare"].Value;
        return System.Net.WebUtility.HtmlDecode(value);
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            var key = separator >= 0 ? part[..separator] : part;
            var value = separator >= 0 ? part[(separator + 1)..] : string.Empty;
            parameters[DecodeQueryComponent(key)] = DecodeQueryComponent(value);
        }

        return parameters;
    }

    private static string DecodeQueryComponent(string value) =>
        Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));

    private static Uri BuildUriWithQuery(Uri uri, IReadOnlyDictionary<string, string> parameters)
    {
        var builder = new UriBuilder(uri)
        {
            Query = string.Join(
                "&",
                parameters.Select(pair =>
                    $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}")),
        };
        return builder.Uri;
    }

    private async Task DownloadFileAsync(
        Uri uri,
        string destination,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        string label,
        int startPercent,
        int endPercent,
        string? expectedSha256 = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        using var response = await OpenDownloadResponseAsync(uri, operation.Token).ConfigureAwait(false);
        if (IsHtmlResponse(response) && response.Content.Headers.ContentDisposition is null)
        {
            throw new InvalidOperationException("The download provider returned an HTML page instead of an archive.");
        }

        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(operation.Token).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        var buffer = new byte[256 * 1024];
        long transferred = 0;
        using var hasher = expectedSha256 is null
            ? null
            : IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var read = await source.ReadAsync(buffer, operation.Token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            await target.WriteAsync(buffer.AsMemory(0, read), operation.Token).ConfigureAwait(false);
            hasher?.AppendData(buffer, 0, read);
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

        if (hasher is not null)
        {
            var actualSha256 = Convert.ToHexString(hasher.GetHashAndReset());
            if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Downloaded archive SHA-256 mismatch. Expected {expectedSha256}, received {actualSha256}.");
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

    private static void ApplyOverlayTransaction(
        string sourceRoot,
        string destinationRoot,
        CancellationToken cancellationToken,
        ToolchainOperationHub.OperationScope operation,
        IProgress<string>? progress,
        int startPercent,
        int endPercent)
    {
        Directory.CreateDirectory(destinationRoot);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToArray();
        var rollbackRoot = CreateTempFolder("toolchain-overlay-rollback");
        var createdTargets = new List<string>();
        var replacedTargets = new List<(string Target, string Backup)>();
        var rollbackErrors = new List<Exception>();

        try
        {
            for (var index = 0; index < files.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = files[index];
                var relative = Path.GetRelativePath(sourceRoot, source);
                var destination = SafePath.ResolveUnderRoot(
                    destinationRoot,
                    relative,
                    "toolchain overlay destination");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

                if (File.Exists(destination))
                {
                    var backup = SafePath.ResolveUnderRoot(
                        rollbackRoot,
                        relative,
                        "toolchain overlay rollback file");
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(destination, backup, overwrite: true);
                    replacedTargets.Add((destination, backup));
                }
                else
                {
                    createdTargets.Add(destination);
                }

                var temporary = destination + $".deadlimit-update-{Guid.NewGuid():N}.tmp";
                try
                {
                    File.Copy(source, temporary, overwrite: true);
                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }

                if (index == 0 || (index + 1) % 50 == 0 || index == files.Length - 1)
                {
                    var fraction = files.Length == 0 ? 1d : (double)(index + 1) / files.Length;
                    var percent = startPercent + (int)Math.Round((endPercent - startPercent) * fraction);
                    Report(operation, progress, ProgressText("Applying files…", "Применение файлов…"), percent);
                }
            }

            TryDeleteDirectory(rollbackRoot);
        }
        catch (Exception applyError)
        {
            foreach (var target in createdTargets.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(target))
                    {
                        File.Delete(target);
                    }
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            foreach (var item in replacedTargets.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(item.Backup))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(item.Target)!);
                        File.Copy(item.Backup, item.Target, overwrite: true);
                    }
                }
                catch (Exception rollbackError) when (rollbackError is IOException or UnauthorizedAccessException)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count == 0)
            {
                TryDeleteDirectory(rollbackRoot);
                throw;
            }

            throw new AggregateException(
                $"Toolchain update failed and rollback was incomplete. Recovery files were preserved at: {rollbackRoot}",
                new[] { applyError }.Concat(rollbackErrors));
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
        AtomicFile.WriteAllText(Path.Combine(root, CsdkMarkerFileName), marker);
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
        AtomicFile.WriteAllText(Path.Combine(root, CsdkSetupMarkerFileName), setupMarker);
    }

    internal static bool IsCsdkSetupCurrent(
        string root,
        int generation,
        IReadOnlyCollection<string> expectedDepotKeys)
    {
        if (string.IsNullOrWhiteSpace(root)
            || !File.Exists(Path.Combine(root, "csdkcfg.exe"))
            || !File.Exists(Path.Combine(root, "game", "citadel", "gameinfo.gi")))
        {
            return false;
        }

        var markerPath = Path.Combine(root, CsdkSetupMarkerFileName);
        if (!File.Exists(markerPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var marker = document.RootElement;
            if (!marker.TryGetProperty("generation", out var markerGeneration)
                || !markerGeneration.TryGetInt32(out var parsedGeneration)
                || parsedGeneration != generation
                || !marker.TryGetProperty("depots", out var markerDepots)
                || markerDepots.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var actualDepotKeys = markerDepots
                .EnumerateArray()
                .Select(GetDepotKey)
                .ToHashSet(StringComparer.Ordinal);
            return actualDepotKeys.SetEquals(expectedDepotKeys);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string GetDepotKey(DepotManifest depot) =>
        $"{depot.AppId}:{depot.DepotId}:{depot.ManifestId}";

    private static string GetDepotKey(JsonElement depot)
    {
        if (!depot.TryGetProperty("AppId", out var appId)
            || !depot.TryGetProperty("DepotId", out var depotId)
            || !depot.TryGetProperty("ManifestId", out var manifestId))
        {
            return string.Empty;
        }

        return $"{appId.GetString()}:{depotId.GetString()}:{manifestId.GetString()}";
    }

    private static void WriteDeadlockToolsMarker(string root, DeadlockToolsRelease release)
    {
        var executable = GetDeadlockToolsExecutable(root);
        var marker = JsonSerializer.Serialize(new
        {
            tag = release.TagName,
            source = release.PageUri.ToString(),
            asset = DeadlockToolsWindowsAssetName,
            archiveSha256 = DeadlockToolsWindowsSha256,
            executableSha256 = ComputeFileSha256(executable),
            updatedUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(Path.Combine(root, DeadlockToolsMarkerFileName), marker);
    }

    private static bool IsTrustedManagedDeadlockTools(string root)
    {
        var markerPath = Path.Combine(root, DeadlockToolsMarkerFileName);
        var executable = GetDeadlockToolsExecutable(root);
        if (!File.Exists(markerPath) || !File.Exists(executable))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var marker = document.RootElement;
            return marker.TryGetProperty("tag", out var tag)
                && string.Equals(tag.GetString(), DeadlockToolsPinnedTag, StringComparison.Ordinal)
                && marker.TryGetProperty("archiveSha256", out var archiveHash)
                && string.Equals(archiveHash.GetString(), DeadlockToolsWindowsSha256, StringComparison.OrdinalIgnoreCase)
                && marker.TryGetProperty("executableSha256", out var executableHash)
                && string.Equals(
                    executableHash.GetString(),
                    ComputeFileSha256(executable),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static void WriteDepotDownloaderMarker(string cacheRoot, string executable)
    {
        var marker = JsonSerializer.Serialize(new
        {
            tag = DepotDownloaderPinnedTag,
            archiveSha256 = DepotDownloaderWindowsSha256,
            executableSha256 = ComputeFileSha256(executable),
            updatedUtc = DateTimeOffset.UtcNow,
        }, new JsonSerializerOptions { WriteIndented = true });
        AtomicFile.WriteAllText(Path.Combine(cacheRoot, DepotDownloaderMarkerFileName), marker);
    }

    private static bool IsTrustedDepotDownloaderCache(string cacheRoot, string executable)
    {
        var markerPath = Path.Combine(cacheRoot, DepotDownloaderMarkerFileName);
        if (!File.Exists(markerPath) || !File.Exists(executable))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(markerPath));
            var marker = document.RootElement;
            return marker.TryGetProperty("tag", out var tag)
                && string.Equals(tag.GetString(), DepotDownloaderPinnedTag, StringComparison.Ordinal)
                && marker.TryGetProperty("archiveSha256", out var archiveHash)
                && string.Equals(archiveHash.GetString(), DepotDownloaderWindowsSha256, StringComparison.OrdinalIgnoreCase)
                && marker.TryGetProperty("executableSha256", out var executableHash)
                && string.Equals(
                    executableHash.GetString(),
                    ComputeFileSha256(executable),
                    StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1024 * 128,
            FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string ResolveCsdkInstallRoot(string selectedParent)
    {
        if (string.IsNullOrWhiteSpace(selectedParent))
        {
            throw new ArgumentException("Reduced CSDK installation location is empty.", nameof(selectedParent));
        }

        var parent = Path.GetFullPath(selectedParent.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(parent, CsdkInstallFolderName);
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
