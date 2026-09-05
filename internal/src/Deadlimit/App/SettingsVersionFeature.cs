using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsVersionFeature
{
    private const string VersionStatusName = "DeadlimitManagerVersionStatus";
    private const string ManagerPathName = "DeadlimitManagerPath";
    private const string OpenButtonName = "DeadlimitManagerOpenButton";
    private const string BrowseButtonName = "DeadlimitManagerBrowseButton";
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
            Text = $"↻ {UiText.T("Checking…", "Проверка…")} [main]",
            AutoSize = false,
            Width = 199,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 6, 4),
        };
        var managerPath = new TextBox
        {
            Name = ManagerPathName,
            Text = DeadlimitPaths.DefaultDeadlimitRoot,
            ReadOnly = true,
            TabStop = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(0, 4, 6, 4),
        };
        var openButton = new Button
        {
            Name = OpenButtonName,
            Text = "📂",
            AutoSize = false,
            Width = 28,
            Height = 24,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 4, 4),
            Padding = Padding.Empty,
            TabStop = false,
            Font = new Font("Segoe UI Emoji", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };
        var browseButton = new Button
        {
            Name = BrowseButtonName,
            Text = UiText.T("BROWSE…", "ОБЗОР…"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 4, 0, 4),
        };
        var action = new Button
        {
            Name = UpdateButtonName,
            Text = UiText.T("CHECKING…", "ПРОВЕРКА…"),
            AutoSize = false,
            Width = 94,
            Height = 26,
            Enabled = true,
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
        grid.Controls.Add(managerPath, 2, row);
        grid.Controls.Add(openButton, 3, row);
        grid.Controls.Add(browseButton, 4, row);
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
        var managerPath = FindDescendants<TextBox>(toolGrid).FirstOrDefault(textBox =>
            string.Equals(textBox.Name, ManagerPathName, StringComparison.Ordinal));
        var openButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, OpenButtonName, StringComparison.Ordinal));
        var browseButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, BrowseButtonName, StringComparison.Ordinal));
        var updateButton = FindDescendants<Button>(toolGrid).FirstOrDefault(button =>
            string.Equals(button.Name, UpdateButtonName, StringComparison.Ordinal));
        if (versionStatus is null || managerPath is null || openButton is null || browseButton is null || updateButton is null)
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

        var managerState = ManagerVersionState.Checking(CurrentDisplayIdentity());
        CancellationTokenSource? checkCancellation = null;

        void RenderManagerState()
        {
            var stateText = managerState.Kind switch
            {
                ManagerVersionStateKind.UpToDate => $"✓ {UiText.T("Up to date", "Актуально")}",
                ManagerVersionStateKind.UpdateAvailable => $"↑ {UiText.T("Update available", "Есть обновление")}",
                ManagerVersionStateKind.NetworkIssue => $"! {UiText.T("Network issue", "Ошибка сети")}",
                ManagerVersionStateKind.Cancelled => $"○ {UiText.T("Check cancelled", "Проверка отменена")}",
                _ => $"↻ {UiText.T("Checking…", "Проверка…")}",
            };
            versionStatus.Text = $"{stateText} [{managerState.Identity}]";
            versionStatus.ForeColor = ManagerStatusColor(managerState.Kind, SelectedTheme());
            if (!versionStatus.Font.Bold)
            {
                versionStatus.Font = new Font(versionStatus.Font, FontStyle.Bold);
            }

            managerPath.Text = DeadlimitPaths.DefaultDeadlimitRoot;
            updateButton.Text = managerState.Kind switch
            {
                ManagerVersionStateKind.UpdateAvailable => UiText.T("UPDATE…", "ОБНОВИТЬ…"),
                ManagerVersionStateKind.Checking => UiText.T("CHECKING…", "ПРОВЕРКА…"),
                _ => UiText.T("CHECK", "ПРОВЕРИТЬ"),
            };
            updateButton.Enabled = true;
            updateButton.AccessibleDescription = managerState.Kind == ManagerVersionStateKind.Checking
                ? UiText.T("Checking for updates. Click again to cancel the check.", "Идёт проверка обновлений. Нажмите ещё раз, чтобы отменить проверку.")
                : managerState.Detail;
            versionStatus.AccessibleDescription = managerState.Detail;
            openButton.AccessibleDescription = UiText.T("Open the Deadlimit installation folder.", "Открыть папку установки Deadlimit.");
            browseButton.AccessibleDescription = UiText.T("Move Deadlimit to another folder.", "Переместить Deadlimit в другую папку.");
        }

        async Task RefreshManagerStateAsync()
        {
            checkCancellation?.Cancel();
            checkCancellation?.Dispose();
            var cancellation = new CancellationTokenSource();
            checkCancellation = cancellation;
            managerState = ManagerVersionState.Checking(managerState.Identity);
            RenderManagerState();
            try
            {
                var state = await CheckManagerVersionAsync(cancellation.Token);
                if (ReferenceEquals(checkCancellation, cancellation) && !cancellation.IsCancellationRequested)
                {
                    managerState = state;
                }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    managerState = ManagerVersionState.Cancelled(managerState.Identity);
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    managerState = ManagerVersionState.NetworkIssue(managerState.Identity, exception.Message);
                }
            }
            finally
            {
                if (ReferenceEquals(checkCancellation, cancellation))
                {
                    checkCancellation = null;
                    cancellation.Dispose();
                }

                if (!form.IsDisposed)
                {
                    RenderManagerState();
                }
            }
        }

        PreparedForms.Add(form, new object());
        themeCombo.SelectedIndexChanged += (_, _) => RenderManagerState();
        openButton.Click += (_, _) => OpenFolder(form, managerPath.Text);
        browseButton.Click += async (_, _) =>
        {
            var selected = ChooseRelocationFolder(form, managerPath.Text);
            if (selected is null || string.Equals(
                    Path.GetFullPath(selected).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    Path.GetFullPath(managerPath.Text).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var answer = MessageBox.Show(
                form,
                UiText.T(
                    $"Move Deadlimit to:\n{selected}\n\nDeadlimit Manager will restart after the move.",
                    $"Переместить Deadlimit в:\n{selected}\n\nПосле перемещения Deadlimit Manager перезапустится."),
                UiText.T("Move Deadlimit", "Перемещение Deadlimit"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            try
            {
                form.UseWaitCursor = true;
                browseButton.Enabled = false;
                await DeadlimitRelocationService.PrepareRelocationAsync(selected);
                Application.Exit();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                MessageBox.Show(
                    form,
                    exception.Message,
                    UiText.T("Could not move Deadlimit", "Не удалось переместить Deadlimit"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                if (!form.IsDisposed)
                {
                    form.UseWaitCursor = false;
                    browseButton.Enabled = true;
                }
            }
        };
        updateButton.Click += async (_, _) =>
        {
            if (managerState.Kind == ManagerVersionStateKind.Checking)
            {
                checkCancellation?.Cancel();
                managerState = ManagerVersionState.Cancelled(managerState.Identity);
                RenderManagerState();
                return;
            }

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

    private static string? ChooseRelocationFolder(IWin32Window owner, string currentRoot)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = UiText.T("Choose the new Deadlimit folder", "Выберите новую папку Deadlimit"),
            ShowNewFolderButton = true,
            UseDescriptionForTitle = true,
            SelectedPath = Directory.GetParent(currentRoot)?.FullName ?? currentRoot,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK
            ? dialog.SelectedPath
            : null;
    }

    private static void OpenFolder(IWin32Window owner, string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true,
            });
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                owner,
                exception.Message,
                UiText.T("Could not open Deadlimit folder", "Не удалось открыть папку Deadlimit"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
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

    private static async Task<ManagerVersionState> CheckManagerVersionAsync(CancellationToken cancellationToken)
    {
        if (ReleaseChannelPolicy.IsPortableRelease)
        {
            var current = NormalizeReleaseTag(GetDisplayVersion());
            var latest = NormalizeReleaseTag(await GetLatestReleaseVersionAsync(cancellationToken).ConfigureAwait(false));
                return string.Equals(current, latest, StringComparison.OrdinalIgnoreCase)
                ? ManagerVersionState.UpToDate(
                    $"v{current}",
                    UiText.T($"Deadlimit Manager {current} is the latest successful build.", $"Deadlimit Manager {current} — последняя успешная сборка."))
                : ManagerVersionState.UpdateAvailable(
                    $"v{current}",
                    UiText.T($"Deadlimit Manager {current} is installed; {latest} is available.", $"Установлен Deadlimit Manager {current}; доступна версия {latest}."));
        }

        var repositoryRoot = DeadlimitPaths.DefaultDeadlimitRoot;
        var localSha = await ReadGitHeadAsync(repositoryRoot, cancellationToken).ConfigureAwait(false);
        var remoteSha = await GetMainCommitShaAsync(cancellationToken).ConfigureAwait(false);
        return string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase)
            ? ManagerVersionState.UpToDate(
                $"main-{ShortSha(localSha)}",
                UiText.T("This developer checkout matches origin/main.", "Эта рабочая копия соответствует origin/main."))
            : ManagerVersionState.UpdateAvailable(
                $"main-{ShortSha(localSha)}",
                UiText.T("A newer origin/main revision is available.", "Доступна более новая версия origin/main."));
    }

    private static async Task<string> GetLatestReleaseVersionAsync(CancellationToken cancellationToken)
    {
        using var response = await VersionHttpClient.GetAsync(LatestReleaseApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
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

        using var metadataResponse = await VersionHttpClient.GetAsync(metadataUri, cancellationToken).ConfigureAwait(false);
        metadataResponse.EnsureSuccessStatusCode();
        await using var metadataStream = await metadataResponse.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var metadata = await JsonDocument.ParseAsync(metadataStream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!metadata.RootElement.TryGetProperty("version", out var version)
            || string.IsNullOrWhiteSpace(version.GetString()))
        {
            throw new InvalidOperationException("The latest Deadlimit build metadata has no version.");
        }

        return version.GetString()!;
    }

    private static async Task<string> GetMainCommitShaAsync(CancellationToken cancellationToken)
    {
        using var response = await VersionHttpClient.GetAsync(MainCommitApiUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!document.RootElement.TryGetProperty("sha", out var shaElement)
            || string.IsNullOrWhiteSpace(shaElement.GetString()))
        {
            throw new InvalidOperationException("The origin/main response has no commit identity.");
        }

        return shaElement.GetString()!;
    }

    private static async Task<string> ReadGitHeadAsync(string repositoryRoot, CancellationToken cancellationToken)
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
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = (await outputTask.ConfigureAwait(false)).Trim();
        var error = (await errorTask.ConfigureAwait(false)).Trim();
        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            throw new InvalidOperationException(
                string.IsNullOrWhiteSpace(error) ? "The current Git revision could not be read." : error);
        }

        return output;
    }

    private static string CurrentDisplayIdentity() => ReleaseChannelPolicy.IsPortableRelease
        ? $"v{NormalizeReleaseTag(GetDisplayVersion())}"
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
            ManagerVersionStateKind.Checking => dark ? Color.FromArgb(117, 190, 255) : Color.FromArgb(30, 105, 175),
            _ => dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(85, 85, 85),
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
        Cancelled,
        UpToDate,
        UpdateAvailable,
        NetworkIssue,
    }

    private sealed record ManagerVersionState(
        ManagerVersionStateKind Kind,
        string Identity,
        string Detail)
    {
        public static ManagerVersionState Checking(string identity) =>
            new(ManagerVersionStateKind.Checking, identity, UiText.T("Checking for Deadlimit Manager updates…", "Проверка обновлений Deadlimit Manager…"));

        public static ManagerVersionState Cancelled(string identity) =>
            new(ManagerVersionStateKind.Cancelled, identity, UiText.T("Deadlimit Manager update check cancelled.", "Проверка обновлений Deadlimit Manager отменена."));

        public static ManagerVersionState UpToDate(string identity, string detail) =>
            new(ManagerVersionStateKind.UpToDate, identity, detail);

        public static ManagerVersionState UpdateAvailable(string identity, string detail) =>
            new(ManagerVersionStateKind.UpdateAvailable, identity, detail);

        public static ManagerVersionState NetworkIssue(string identity, string detail) =>
            new(ManagerVersionStateKind.NetworkIssue, identity, detail);
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
