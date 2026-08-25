using Deadlimit.Core;

namespace Deadlimit.App;

internal static class OnlinePreparationFeature
{
    private static string OnlineButtonText => UiText.T("▶  ONLINE CSDK", "▶  CSDK ONLINE");

    private static OnlinePreparationSession? _session;
    private static ToolTip? _toolTip;
    private static Button? _prepareButton;
    private static Button? _launchButton;
    private static MainForm? _form;
    private static bool _toggleBusy;

    public static void Attach(MainForm form)
    {
        var prepareButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "PREPARE FOR CSDK", StringComparison.Ordinal)
                || string.Equals(button.Text, "ПОДГОТОВИТЬ ДЛЯ CSDK", StringComparison.Ordinal));
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
                _ = RefreshBaselineAfterManualPrepareAsync();
            }
        };

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
                    "Save the current Deadlimit project before enabling ONLINE PREPARATION.",
                    "Сохраните текущий проект Deadlimit перед включением ONLINE PREPARATION."),
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        _toggleBusy = true;
        var originalTitle = _form.Text;
        var buttons = FindActionButtons().ToArray();
        var enabledStates = buttons.ToDictionary(button => button, button => button.Enabled);
        var started = false;

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
                    _form.Text = $"Deadlimit — ONLINE PREPARATION — {update.Message}";
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
            UpdateToolTip(
                "ONLINE PREPARATION is active. Existing root DMX and texture files are hash-checked after a short debounce; only files whose bytes actually changed are copied into CSDK content. Shift-click LAUNCH CSDK again to stop. A normal PREPARE still runs full preparation and refreshes the live-sync baseline.");
            started = true;
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

        return started;
    }

    private static async Task RefreshBaselineAfterManualPrepareAsync()
    {
        if (_prepareButton is null)
        {
            return;
        }

        var sawBusyState = !_prepareButton.Enabled;
        for (var attempt = 0; attempt < 120 && !_prepareButton.IsDisposed; attempt++)
        {
            if (!_prepareButton.Enabled)
            {
                sawBusyState = true;
            }
            else if (sawBusyState)
            {
                break;
            }

            await Task.Delay(100);
        }

        if (!sawBusyState || _session is null || _prepareButton.IsDisposed)
        {
            return;
        }

        try
        {
            var manifest = ProjectStore.TryLoadLastProject();
            if (manifest is null)
            {
                return;
            }

            StartOrReplaceSession(manifest, new DeadlimitPaths());
            if (_launchButton is not null && !_launchButton.IsDisposed)
            {
                _launchButton.Text = OnlineButtonText;
            }
            UpdateToolTip(
                "ONLINE PREPARATION baseline refreshed after PREPARE FOR CSDK. Existing root DMX and texture files are hash-checked and only byte-level changes are copied into CSDK content. Shift-click LAUNCH CSDK to stop.");
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            UpdateToolTip(
                $"ONLINE PREPARATION could not refresh its baseline after PREPARE FOR CSDK: {ex.Message}");
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
            if (_launchButton is null || _launchButton.IsDisposed || _session is null)
            {
                return;
            }

            _launchButton.Text = OnlineButtonText;
            var suffix = update.PrepareRequired
                ? "\n\nA structural change was detected. Normal-click this button once to run full PREPARE FOR CSDK and establish a new live-sync baseline."
                : string.Empty;
            UpdateToolTip(update.Message + suffix);
        }));
    }

    private static void StopSession()
    {
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

        UpdateToolTip(
            "ONLINE PREPARATION is off. Shift-click LAUNCH CSDK to prepare once, start live hash-based DMX/texture synchronization and launch CSDK.");
    }

    private static void Detach()
    {
        StopSession();

        _toolTip?.Dispose();
        _toolTip = null;
        _prepareButton = null;
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
}
