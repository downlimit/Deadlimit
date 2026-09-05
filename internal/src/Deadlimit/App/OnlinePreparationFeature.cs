using System.Security.Cryptography;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class OnlinePreparationFeature
{
    private static string OnlineButtonText => UiText.T("▶  ONLINE CSDK", "▶  CSDK ОНЛАЙН");

    private static OnlinePreparationSession? _session;
    private static ToolTip? _toolTip;
    private static Button? _prepareButton;
    private static Button? _buildButton;
    private static Button? _launchButton;
    private static MainForm? _form;
    private static bool _toggleBusy;
    private static bool _autoPrepareBusy;
    private static bool _autoPrepareRequested;

    public static void Attach(MainForm form)
    {
        var prepareButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "PREPARE FOR CSDK", StringComparison.Ordinal)
                || string.Equals(button.Text, "ПОДГОТОВИТЬ ДЛЯ CSDK", StringComparison.Ordinal));
        var buildButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "BUILD FOR TEST", StringComparison.Ordinal)
                || string.Equals(button.Text, "СОБРАТЬ ДЛЯ ТЕСТА", StringComparison.Ordinal));
        var launchButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "LAUNCH CSDK", StringComparison.Ordinal)
                || string.Equals(button.Text, "ЗАПУСТИТЬ CSDK", StringComparison.Ordinal));
        if (prepareButton is null || launchButton is null)
        {
            return;
        }

        _form = form;
        _prepareButton = prepareButton;
        _buildButton = buildButton;
        _launchButton = launchButton;
        _toolTip = new ToolTip
        {
            AutoPopDelay = 16000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true,
        };

        prepareButton.Click += (_, _) =>
        {
            if (_session is not null)
            {
                _ = RefreshBaselineAfterSuccessfulPrepareActionAsync(
                    prepareButton,
                    UiText.T("PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK"));
            }
        };

        if (buildButton is not null)
        {
            buildButton.Click += (_, _) =>
            {
                if (_session is not null)
                {
                    _ = RefreshBaselineAfterSuccessfulPrepareActionAsync(
                        buildButton,
                        UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА"));
                }
            };
        }

        form.FormClosed += (_, _) => Detach();
    }

    internal static async Task<bool> ToggleFromLaunchButtonAsync()
    {
        if (_toggleBusy
            || _form is null
            || _prepareButton is null
            || _prepareButton.IsDisposed
            || _launchButton is null
            || _launchButton.IsDisposed)
        {
            return false;
        }

        return await ToggleOnlinePreparationAsync();
    }

    internal static bool StopForGameLaunch()
    {
        var session = _session;
        if (session is null)
        {
            return false;
        }

        _session = null;
        _autoPrepareRequested = false;
        session.Updated -= OnSessionUpdated;

        if (_launchButton is not null && !_launchButton.IsDisposed)
        {
            _launchButton.Text = UiText.T("▶  LAUNCH CSDK", "▶  ЗАПУСК CSDK");
        }

        UpdateToolTip(UiText.T(
            "ONLINE PREPARATION is off.\n\nShift-click LAUNCH CSDK to prepare once and enable live synchronization. CSDK launches only if no CSDK process is already running.",
            "ОНЛАЙН-ПОДГОТОВКА выключена.\n\nИспользуйте SHIFT+клик по ЗАПУСК CSDK, чтобы один раз выполнить подготовку и включить онлайн-синхронизацию. CSDK будет запущен только если другой процесс CSDK ещё не работает."));

        _ = DisposeSessionAfterGameLaunchAsync(session);
        return true;
    }

    private static async Task DisposeSessionAfterGameLaunchAsync(OnlinePreparationSession session)
    {
        await Task.Run(session.Dispose);
    }

    private static async Task<bool> ToggleOnlinePreparationAsync()
    {
        if (_toggleBusy
            || _form is null
            || _prepareButton is null
            || _prepareButton.IsDisposed
            || _launchButton is null
            || _launchButton.IsDisposed)
        {
            return false;
        }

        if (_session is not null)
        {
            StopSession();
            return false;
        }

        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                _form,
                UiText.T(
                    "Save the current Deadlimit Manager project before enabling ONLINE PREPARATION.",
                    "Сохраните текущий проект Deadlimit Manager перед включением ОНЛАЙН-ПОДГОТОВКИ."),
                "Deadlimit Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        _toggleBusy = true;
        var originalTitle = _form.Text;
        var buttons = FindActionButtons().ToArray();
        var enabledStates = buttons.ToDictionary(button => button, button => button.Enabled);
        var shouldLaunchCsdk = false;

        try
        {
            foreach (var button in buttons)
            {
                button.Enabled = false;
            }

            var progress = new Progress<PrepareAuthoringProgress>(update =>
            {
                if (_form is not null && !_form.IsDisposed)
                {
                    _form.Text = $"{UiText.T("Deadlimit Manager — ONLINE PREPARATION", "Deadlimit Manager — ОНЛАЙН-ПОДГОТОВКА")} — {update.Message}";
                }
            });

            var paths = new DeadlimitPaths();
            var prepareService = new PrepareAuthoringService(paths);
            await prepareService.PrepareAsync(manifest, progress);

            var refreshedManifest = ProjectStore.TryLoadLastProject()
                ?? throw new InvalidOperationException(
                    "ONLINE PREPARATION could not reload the project after PREPARE FOR CSDK.");

            StartOrReplaceSession(refreshedManifest, paths);
            _launchButton.Text = OnlineButtonText;

            var csdkAlreadyRunning = CsdkProcessService.IsRunning(paths);
            UpdateToolTip(
                csdkAlreadyRunning
                    ? UiText.T(
                        "ONLINE PREPARATION is active.\n\nChanged DMX and texture files are synchronized automatically. The existing CSDK instance is kept; no second instance is launched.\n\nShift-click again to stop online synchronization.",
                        "ОНЛАЙН-ПОДГОТОВКА активна.\n\nИзменённые DMX и текстуры синхронизируются автоматически. Уже запущенный CSDK остаётся активным; второй экземпляр не запускается.\n\nПовторный SHIFT+клик отключит онлайн-синхронизацию.")
                    : UiText.T(
                        "ONLINE PREPARATION is active.\n\nChanged DMX and texture files are synchronized automatically. CSDK will launch now.\n\nShift-click again to stop online synchronization.",
                        "ОНЛАЙН-ПОДГОТОВКА активна.\n\nИзменённые DMX и текстуры синхронизируются автоматически. CSDK сейчас будет запущен.\n\nПовторный SHIFT+клик отключит онлайн-синхронизацию."));
            shouldLaunchCsdk = !csdkAlreadyRunning;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MessageBox.Show(
                _form,
                ex.Message,
                UiText.T("Online preparation failed", "Ошибка ONLINE PREPARATION"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            if (_form is not null && !_form.IsDisposed)
            {
                _form.Text = originalTitle;
            }

            foreach (var pair in enabledStates)
            {
                if (!pair.Key.IsDisposed)
                {
                    pair.Key.Enabled = pair.Value;
                }
            }

            _toggleBusy = false;
        }

        return shouldLaunchCsdk;
    }

    private static async Task RefreshBaselineAfterSuccessfulPrepareActionAsync(
        Button actionButton,
        string actionName)
    {
        var manifestBefore = ProjectStore.TryLoadLastProject();
        if (manifestBefore is null || _session is null)
        {
            return;
        }

        var beforeLog = ReadLatestPrepareLogSnapshot(manifestBefore.ProjectFolder);
        var sawBusyState = !actionButton.Enabled;

        while (!actionButton.IsDisposed)
        {
            if (_form is null || _form.IsDisposed || _session is null)
            {
                return;
            }

            if (!actionButton.Enabled)
            {
                sawBusyState = true;
            }
            else if (sawBusyState)
            {
                break;
            }

            await Task.Delay(100);
        }

        if (!sawBusyState || _session is null || actionButton.IsDisposed)
        {
            return;
        }

        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null)
        {
            return;
        }

        var afterLog = ReadLatestPrepareLogSnapshot(manifest.ProjectFolder);
        var producedNewPrepareResult = afterLog is not null
            && !string.Equals(beforeLog?.Token, afterLog.Token, StringComparison.Ordinal);
        if (!producedNewPrepareResult || !afterLog!.Succeeded)
        {
            UpdateToolTip(UiText.T(
                $"ONLINE PREPARATION kept its previous live-sync baseline because {actionName} did not finish a successful PREPARE transaction.\n\nThe last good prepared DMX remains protected.",
                $"ОНЛАЙН-ПОДГОТОВКА сохранила предыдущую базовую версию, потому что {actionName} не завершилась успешной транзакцией PREPARE.\n\nПоследний корректно подготовленный DMX сохранён."));
            return;
        }

        try
        {
            StartOrReplaceSession(manifest, new DeadlimitPaths());
            if (_launchButton is not null && !_launchButton.IsDisposed)
            {
                _launchButton.Text = OnlineButtonText;
            }
            UpdateToolTip(UiText.T(
                $"ONLINE PREPARATION baseline refreshed after {actionName}.\n\nChanged DMX and texture files will continue to synchronize automatically. Shift-click LAUNCH CSDK to stop.",
                $"Базовая версия ОНЛАЙН-ПОДГОТОВКИ обновлена после {actionName}.\n\nИзменённые DMX и текстуры продолжат синхронизироваться автоматически. Для остановки используйте SHIFT+клик по ЗАПУСК CSDK."));
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            UpdateToolTip(UiText.T(
                $"ONLINE PREPARATION could not refresh its baseline after {actionName}: {ex.Message}",
                $"Не удалось обновить базовую версию ОНЛАЙН-ПОДГОТОВКИ после {actionName}."));
        }
    }

    private static PrepareLogSnapshot? ReadLatestPrepareLogSnapshot(string projectFolder)
    {
        try
        {
            var logFolder = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), "logs");
            if (!Directory.Exists(logFolder))
            {
                return null;
            }

            var latest = Directory.EnumerateFiles(logFolder, "prepare-*.log", SearchOption.TopDirectoryOnly)
                .Select(path => new FileInfo(path))
                .OrderByDescending(info => info.LastWriteTimeUtc)
                .ThenByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
            if (latest is null || !latest.Exists)
            {
                return null;
            }

            var bytes = File.ReadAllBytes(latest.FullName);
            var text = System.Text.Encoding.UTF8.GetString(bytes);
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            var succeeded = text.Contains(
                "RESULT: AUTHORING CONTENT PREPARED; ADDON GAME OUTPUT CLEAN",
                StringComparison.Ordinal);
            return new PrepareLogSnapshot(
                latest.FullName + "|" + hash,
                succeeded);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
    }

    private static void StartOrReplaceSession(ProjectManifest manifest, DeadlimitPaths paths)
    {
        var replacement = OnlinePreparationSession.Start(manifest, paths);
        replacement.Updated += OnSessionUpdated;

        var previous = _session;
        _session = replacement;

        if (previous is not null)
        {
            previous.Updated -= OnSessionUpdated;
            previous.Dispose();
        }
    }

    private static void OnSessionUpdated(object? sender, OnlinePreparationUpdate update)
    {
        var form = _form;
        if (form is null || form.IsDisposed)
        {
            return;
        }

        form.BeginInvoke((Action)(() =>
        {
            var session = _session;
            if (_launchButton is null
                || _launchButton.IsDisposed
                || session is null
                || !ReferenceEquals(sender, session))
            {
                return;
            }

            _launchButton.Text = OnlineButtonText;
            var suffix = update.PrepareRequired
                ? UiText.T(
                    "\n\nA structural change was detected. ONLINE PREPARATION is rebuilding the full PREPARE baseline automatically.",
                    "\n\nОбнаружено структурное изменение. ОНЛАЙН-ПОДГОТОВКА автоматически перестраивает полную базовую версию PREPARE.")
                : string.Empty;
            UpdateToolTip(update.Message + suffix);

            if (update.PrepareRequired)
            {
                _autoPrepareRequested = true;
                if (!_autoPrepareBusy)
                {
                    _ = RefreshBaselineAutomaticallyAsync(session);
                }
            }
        }));
    }

    private static async Task RefreshBaselineAutomaticallyAsync(OnlinePreparationSession requestedSession)
    {
        if (_autoPrepareBusy
            || _session is null
            || !ReferenceEquals(_session, requestedSession)
            || _form is null
            || _form.IsDisposed)
        {
            return;
        }

        _autoPrepareBusy = true;
        _toggleBusy = true;
        var form = _form;
        var originalTitle = form.Text;
        var buttons = FindActionButtons().ToArray();
        var enabledStates = buttons.ToDictionary(button => button, button => button.Enabled);

        try
        {
            foreach (var button in buttons)
            {
                button.Enabled = false;
            }

            while (_autoPrepareRequested)
            {
                _autoPrepareRequested = false;
                var activeSession = _session;
                if (activeSession is null || form.IsDisposed)
                {
                    return;
                }

                var manifest = ProjectStore.TryLoadLastProject()
                    ?? throw new InvalidOperationException(
                        "ONLINE PREPARATION could not reload the project for automatic PREPARE.");
                var paths = new DeadlimitPaths();
                var sourceSnapshot = CaptureOnlineSourceSnapshot(manifest.ProjectFolder);
                var progress = new Progress<PrepareAuthoringProgress>(update =>
                {
                    if (!form.IsDisposed && ReferenceEquals(_session, activeSession))
                    {
                        form.Text = $"{UiText.T("Deadlimit Manager — ONLINE PREPARATION", "Deadlimit Manager — ОНЛАЙН-ПОДГОТОВКА")} — {update.Message}";
                    }
                });

                UpdateToolTip(UiText.T(
                    "ONLINE PREPARATION detected a structural project change and is running PREPARE FOR CSDK automatically. CSDK remains open and will receive the refreshed authoring content.",
                    "ОНЛАЙН-ПОДГОТОВКА обнаружила структурное изменение проекта и автоматически выполняет ПОДГОТОВИТЬ ДЛЯ CSDK. CSDK остаётся открытым и получит обновлённый authoring content."));

                await new PrepareAuthoringService(paths).PrepareAsync(manifest, progress);

                if (form.IsDisposed
                    || _session is null
                    || !ReferenceEquals(_session, activeSession))
                {
                    return;
                }

                var refreshedManifest = ProjectStore.TryLoadLastProject()
                    ?? throw new InvalidOperationException(
                        "ONLINE PREPARATION could not reload the project after automatic PREPARE.");
                StartOrReplaceSession(refreshedManifest, paths);
                if (!string.Equals(
                        sourceSnapshot,
                        CaptureOnlineSourceSnapshot(refreshedManifest.ProjectFolder),
                        StringComparison.Ordinal))
                {
                    _autoPrepareRequested = true;
                }
                if (_launchButton is not null && !_launchButton.IsDisposed)
                {
                    _launchButton.Text = OnlineButtonText;
                }

                UpdateToolTip(UiText.T(
                    "ONLINE PREPARATION refreshed the full PREPARE baseline automatically. Changed DMX and texture files continue to synchronize without another click.",
                    "ОНЛАЙН-ПОДГОТОВКА автоматически обновила полную базовую версию PREPARE. Изменённые DMX и текстуры продолжают синхронизироваться без дополнительного клика."));
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            UpdateToolTip(UiText.T(
                $"ONLINE PREPARATION could not refresh the full PREPARE baseline automatically: {ex.Message}. The last good prepared content was kept; run PREPARE FOR CSDK manually to recover.",
                $"ОНЛАЙН-ПОДГОТОВКА не смогла автоматически обновить полную базовую версию PREPARE: {ex.Message}. Последний корректный подготовленный content сохранён; для восстановления выполните ПОДГОТОВИТЬ ДЛЯ CSDK вручную."));
        }
        finally
        {
            if (!form.IsDisposed)
            {
                form.Text = originalTitle;
            }

            foreach (var pair in enabledStates)
            {
                if (!pair.Key.IsDisposed)
                {
                    pair.Key.Enabled = pair.Value;
                }
            }

            _autoPrepareBusy = false;
            _toggleBusy = false;

            if (_autoPrepareRequested
                && _session is { } session
                && !form.IsDisposed)
            {
                _ = RefreshBaselineAutomaticallyAsync(session);
            }
        }
    }

    private static string CaptureOnlineSourceSnapshot(string projectFolder)
    {
        return string.Join(
            '\n',
            Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
                .Where(path =>
                {
                    var extension = Path.GetExtension(path);
                    return extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
                        || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase)
                        || (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
                            && VertexColorSidecarService.IsSidecarPath(path));
                })
                .Select(path => new FileInfo(path))
                .OrderBy(info => info.FullName, StringComparer.OrdinalIgnoreCase)
                .Select(info => $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"));
    }

    private static void StopSession()
    {
        _autoPrepareRequested = false;
        if (_session is not null)
        {
            _session.Updated -= OnSessionUpdated;
            _session.Dispose();
            _session = null;
        }

        if (_launchButton is not null && !_launchButton.IsDisposed)
        {
            _launchButton.Text = UiText.T("▶  LAUNCH CSDK", "▶  ЗАПУСК CSDK");
        }

        UpdateToolTip(UiText.T(
            "ONLINE PREPARATION is off.\n\nShift-click LAUNCH CSDK to prepare once and enable live synchronization. CSDK launches only if no CSDK process is already running.",
            "ОНЛАЙН-ПОДГОТОВКА выключена.\n\nИспользуйте SHIFT+клик по ЗАПУСК CSDK, чтобы один раз выполнить подготовку и включить онлайн-синхронизацию. CSDK будет запущен только если другой процесс CSDK ещё не работает."));
    }

    private static void Detach()
    {
        StopSession();

        _toolTip?.Dispose();
        _toolTip = null;
        _prepareButton = null;
        _buildButton = null;
        _launchButton = null;
        _form = null;
    }

    private static void UpdateToolTip(string text)
    {
        if (_toolTip is not null && _launchButton is not null && !_launchButton.IsDisposed)
        {
            _toolTip.SetToolTip(_launchButton, text);
        }
    }

    private static IEnumerable<Button> FindActionButtons()
    {
        if (_form is null)
        {
            return [];
        }

        return FindDescendants<Button>(_form)
            .Where(button => button.Text.Contains("PREPARE", StringComparison.OrdinalIgnoreCase)
                             || button.Text.Contains("ПОДГОТОВ", StringComparison.OrdinalIgnoreCase)
                             || button.Text.Contains("BUILD", StringComparison.OrdinalIgnoreCase)
                             || button.Text.Contains("СОБРАТЬ", StringComparison.OrdinalIgnoreCase)
                             || button.Text.Contains("CSDK", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<T> FindDescendants<T>(Control root)
        where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private sealed record PrepareLogSnapshot(string Token, bool Succeeded);
}
