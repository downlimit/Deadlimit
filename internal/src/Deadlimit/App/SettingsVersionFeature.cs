using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsVersionFeature
{
    private const string VersionStatusName = "DeadlimitManagerVersionStatus";
    private const string VersionValueName = "DeadlimitManagerVersionValue";
    private const string UpdateButtonName = "DeadlimitUpdateButton";
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/downlimit/Deadlimit/releases/tags/latest-main";
    private const string LatestReleaseMetadataAssetName = "Deadlimit-release.json";
    private const string MainCommitApiUrl = "https://api.github.com/repos/downlimit/Deadlimit/commits/main";

    private static readonly HttpClient VersionHttpClient = CreateVersionHttpClient();
    private static readonly ConditionalWeakTable<SettingsForm, object> PreparedForms = new();

    public static void Attach()
    {
        // Normal preparation happens before first activation in UiRenderingStabilityFeature.
        // Keep Idle only as a defensive fallback if the native pre-show hook is unavailable.
        Application.Idle += OnApplicationIdle;
    }

    internal static void Prepare(SettingsForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        EnsureManagerStatus(form);
    }

    internal static void AddManagerRow(TableLayoutPanel grid, int row)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var status = new Label
        {
            Name = VersionStatusName,
            Text = UiText.T("↻ Checking…", "↻ Проверка…"),
            AutoSize = false,
            Width = 137,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 6, 4),
        };
        var version = new TextBox
        {
            Name = VersionValueName,
            Text = $"{UiText.T("Version", "Версия")} {GetDisplayVersion()}",
            ReadOnly = true,
            TabStop = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 6, 4),
        };
        var action = new Button
        {
            Name = UpdateButtonName,
            Text = UiText.T("CHECKING…", "ПРОВЕРКА…"),
            AutoSize = false,
            Width = 94,
            Height = 26,
            Enabled = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 5, 3),
        };

        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.Controls.Add(new Label
        {
            Text = "Deadlimit Manager:",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 8, 10, 8),
        }, 0, row);
        grid.Controls.Add(status, 1, row);
        grid.Controls.Add(version, 2, row);
        grid.Controls.Add(CreateSpacer(), 3, row);
        grid.Controls.Add(CreateSpacer(), 4, row);
        grid.Controls.Add(action, 5, row);
        grid.Controls.Add(CreateSpacer(), 6, row);
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var settingsForm in Application.OpenForms.OfType<SettingsForm>())
        {
            Prepare(settingsForm);
        }
    }

    private static void EnsureManagerStatus(SettingsForm form)
    {
        if (PreparedForms.TryGetValue(form, out _))
        {
            return;
        }

        var toolGrid = FindDescendants<TableLayoutPanel>(form)
            .FirstOrDefault(panel => panel.ColumnCount == 7 && panel.RowCount >= 5);
        if (toolGrid is null)
        {
            return;
        }

        var versionStatus = FindDescendants<Label>(toolGrid).FirstOrDefault(label =>
            string.Equals(label.Name, VersionStatusName, StringComparison.Ordinal));
        var versionValue = FindDescendants<TextBox>(toolGrid).FirstOrDefault(textBox =>
            string.Equals(textBox.Name, VersionValueName, StringComparison.Ordinal));
        var updateButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, UpdateButtonName, StringComparison.Ordinal));
        if (versionStatus is null || versionValue is null || updateButton is null)
        {
            return;
        }

        var comboBoxes = FindDescendants<ComboBox>(form).ToArray();
        var languageCombo = comboBoxes.FirstOrDefault(combo =>
            combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "English", StringComparison.Ordinal))
            && combo.Items.Cast<object>().Any(item => string.Equals(item.ToString(), "Русский", StringComparison.Ordinal)));
        if (languageCombo is null)
        {
            return;
        }

        var themeCombo = comboBoxes.FirstOrDefault(combo => !ReferenceEquals(combo, languageCombo));
        if (themeCombo is null)
        {
            return;
        }

        string SelectedTheme()
        {
            var label = themeCombo.SelectedItem?.ToString() ?? string.Empty;
            return label switch
            {
                "Light" or "Светлая" => "light",
                "Gray" or "Серая" => "gray",
                "Dark" or "Тёмная" => "dark",
                _ => "system",
            };
        }

        var managerState = ManagerVersionState.Checking(CurrentVersionIdentity());

        void RenderManagerState()
        {
            versionValue.Text = managerState.DisplayVersion;
            versionStatus.Text = managerState.Kind switch
            {
                ManagerVersionStateKind.UpToDate => $"✓ {UiText.T("Up to date", "Актуально")}",
                ManagerVersionStateKind.UpdateAvailable => $"↑ {UiText.T("Update available", "Есть обновление")}",
                ManagerVersionStateKind.NetworkIssue => $"! {UiText.T("Network issue", "Ошибка сети")}",
                _ => $"↻ {UiText.T("Checking…", "Проверка…")}",
            };
            versionStatus.ForeColor = ManagerStatusColor(managerState.Kind, SelectedTheme());
            if (!versionStatus.Font.Bold)
            {
                versionStatus.Font = new Font(versionStatus.Font, FontStyle.Bold);
            }

            updateButton.Text = managerState.Kind switch
            {
                ManagerVersionStateKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
                ManagerVersionStateKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
                _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
            };
            updateButton.Enabled = managerState.Kind != ManagerVersionStateKind.Checking;
            updateButton.AccessibleDescription = managerState.Detail;
            versionStatus.AccessibleDescription = managerState.Detail;
        }

        async Task RefreshManagerStateAsync()
        {
            managerState = ManagerVersionState.Checking(managerState.DisplayVersion);
            RenderManagerState();
            try
            {
                managerState = await CheckManagerVersionAsync();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                managerState = ManagerVersionState.NetworkIssue(CurrentVersionIdentity(), exception.Message);
            }

            if (!form.IsDisposed)
            {
                RenderManagerState();
            }
        }

        PreparedForms.Add(form, new object());
        themeCombo.SelectedIndexChanged += (_, _) => RenderManagerState();
        updateButton.Click += async (_, _) =>
        {
            if (managerState.Kind == ManagerVersionStateKind.UpdateAvailable)
            {
                LaunchUpdater(form);
                return;
            }

            await RefreshManagerStateAsync();
        };

        RenderManagerState();
        if (form.Visible)
        {
            _ = RefreshManagerStateAsync();
        }
        else
        {
            form.Shown += async (_, _) => await RefreshManagerStateAsync();
        }
    }

    private static void LaunchUpdater(IWin32Window owner)
    {
        var updateRoot = ReleaseChannelPolicy.IsPortableRelease
            ? AppContext.BaseDirectory
            : DeadlimitPaths.DefaultDeadlimitRoot;
        var updater = Path.Combine(updateRoot, "Update Deadlimit.cmd");
        if (!File.Exists(updater))
        {
            MessageBox.Show(
                owner,
                UiText.T(
                    "The updater entry point was not found for the current release channel.",
                    "Файл обновления не найден для текущего канала программы."),
                UiText.T("Updater unavailable", "Обновление недоступно"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = updater,
                WorkingDirectory = Path.GetDirectoryName(updater) ?? AppContext.BaseDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                exception.Message,
                UiText.T("Could not start updater", "Не удалось запустить обновление"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static async Task<ManagerVersionState> CheckManagerVersionAsync()
    {
        if (ReleaseChannelPolicy.IsPortableRelease)
        {
            var current = NormalizeReleaseTag(GetDisplayVersion());
            var latest = NormalizeReleaseTag(await GetLatestReleaseVersionAsync().ConfigureAwait(false));
            var display = $"{UiText.T("Version", "Версия")} {current}";
            return string.Equals(current, latest, StringComparison.OrdinalIgnoreCase)
                ? ManagerVersionState.UpToDate(
                    display,
                    UiText.T($"Deadlimit Manager {current} is the latest successful build.", $"Deadlimit Manager {current} — последняя успешная сборка."))
                : ManagerVersionState.UpdateAvailable(
                    display,
                    UiText.T($"Deadlimit Manager {current} is installed; {latest} is available.", $"Установлен Deadlimit Manager {current}; доступна версия {latest}."));
        }

        var repositoryRoot = DeadlimitPaths.DefaultDeadlimitRoot;
        var localSha = await ReadGitHeadAsync(repositoryRoot).ConfigureAwait(false);
        var remoteSha = await GetMainCommitShaAsync().ConfigureAwait(false);
        var displayVersion = $"main · {ShortSha(localSha)}";
        return string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase)
            ? ManagerVersionState.UpToDate(
                displayVersion,
                UiText.T("This developer checkout matches origin/main.", "Эта рабочая копия соответствует origin/main."))
            : ManagerVersionState.UpdateAvailable(
                displayVersion,
                UiText.T("A newer origin/main revision is available.", "Доступна более новая версия origin/main."));
    }

    private static async Task<string> GetLatestReleaseVersionAsync()
    {
        using var response = await VersionHttpClient.GetAsync(LatestReleaseApiUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty("assets", out var assets)
            || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("The Deadlimit release response is malformed.");
        }

        string? metadataUrl = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!asset.TryGetProperty("name", out var name)
                || !string.Equals(name.GetString(), LatestReleaseMetadataAssetName, StringComparison.Ordinal)
                || !asset.TryGetProperty("browser_download_url", out var downloadUrl))
            {
                continue;
            }

            metadataUrl = downloadUrl.GetString();
            break;
        }

        if (!Uri.TryCreate(metadataUrl, UriKind.Absolute, out var metadataUri)
            || metadataUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(metadataUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The latest Deadlimit build has no trusted metadata asset.");
        }

        using var metadataResponse = await VersionHttpClient.GetAsync(metadataUri).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();
        await using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var metadata = await JsonDocument.ParseAsync(metadataStream).ConfigureAwait(false);
        if (!metadata.RootElement.TryGetProperty("version", out var version)
            || string.IsNullOrWhiteSpace(version.GetString()))
        {
            throw new InvalidOperationException("The latest Deadlimit build metadata has no version.");
        }

        return version.GetString()!;
    }

    private static async Task<string> GetMainCommitShaAsync()
    {
        using var response = await VersionHttpClient.GetAsync(MainCommitApiUrl).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("sha", out var shaElement)
            || string.IsNullOrWhiteSpace(shaElement.GetString()))
        {
            throw new InvalidOperationException("The origin/main response has no commit identity.");
        }

        return shaElement.GetString()!;
    }

    private static async Task<string> ReadGitHeadAsync(string repositoryRoot)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repositoryRoot);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "The current Git revision could not be read." : error);
        }

        return output;
    }

    private static string CurrentVersionIdentity() => ReleaseChannelPolicy.IsPortableRelease
        ? $"{UiText.T("Version", "Версия")} {NormalizeReleaseTag(GetDisplayVersion())}"
        : "main";

    private static string NormalizeReleaseTag(string version) =>
        version.Trim().TrimStart('v', 'V');

    private static string ShortSha(string sha) => sha.Length <= 8 ? sha : sha[..8];

    private static Color ManagerStatusColor(ManagerVersionStateKind kind, string theme)
    {
        var dark = string.Equals(theme, "dark", StringComparison.OrdinalIgnoreCase)
            || (string.Equals(theme, "system", StringComparison.OrdinalIgnoreCase) && Application.IsDarkModeEnabled);
        return kind switch
        {
            ManagerVersionStateKind.UpToDate => dark ? Color.FromArgb(113, 214, 137) : Color.FromArgb(25, 125, 55),
            ManagerVersionStateKind.UpdateAvailable => dark ? Color.FromArgb(255, 194, 92) : Color.FromArgb(173, 103, 0),
            ManagerVersionStateKind.NetworkIssue => dark ? Color.FromArgb(255, 118, 118) : Color.FromArgb(184, 40, 40),
            _ => dark ? Color.FromArgb(117, 190, 255) : Color.FromArgb(30, 105, 175),
        };
    }

    private static Control CreateSpacer() => new Panel
    {
        Width = 1,
        Height = 26,
        Margin = Padding.Empty,
    };

    private static HttpClient CreateVersionHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("DeadlimitManager/1.0");
        return client;
    }

    private static string GetDisplayVersion()
    {
        var informationalVersion = typeof(SettingsVersionFeature).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var metadataSeparator = informationalVersion.IndexOf('+');
            return metadataSeparator >= 0
                ? informationalVersion[..metadataSeparator]
                : informationalVersion;
        }

        return Application.ProductVersion;
    }

    private enum ManagerVersionStateKind
    {
        Checking,
        UpToDate,
        UpdateAvailable,
        NetworkIssue,
    }

    private sealed record ManagerVersionState(
        ManagerVersionStateKind Kind,
        string DisplayVersion,
        string Detail)
    {
        public static ManagerVersionState Checking(string displayVersion) =>
            new(ManagerVersionStateKind.Checking, displayVersion, UiText.T("Checking for Deadlimit Manager updates…", "Проверка обновлений Deadlimit Manager…"));

        public static ManagerVersionState UpToDate(string displayVersion, string detail) =>
            new(ManagerVersionStateKind.UpToDate, displayVersion, detail);

        public static ManagerVersionState UpdateAvailable(string displayVersion, string detail) =>
            new(ManagerVersionStateKind.UpdateAvailable, displayVersion, detail);

        public static ManagerVersionState NetworkIssue(string displayVersion, string detail) =>
            new(ManagerVersionStateKind.NetworkIssue, displayVersion, detail);
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
