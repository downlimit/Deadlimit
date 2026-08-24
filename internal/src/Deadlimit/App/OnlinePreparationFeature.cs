using Deadlimit.Core;

namespace Deadlimit.App;

internal static class OnlinePreparationFeature
{
    private const string OnlineButtonText = "ONLINE PREARAION";
    private const int LeftMouseButtonDown = 0x0201;
    private const int LeftMouseButtonUp = 0x0202;

    private static OnlinePreparationSession? _session;
    private static ShiftPrepareMessageFilter? _messageFilter;
    private static ToolTip? _toolTip;
    private static Button? _prepareButton;
    private static MainForm? _form;
    private static string? _normalButtonText;
    private static bool _toggleBusy;

    public static void Attach(MainForm form)
    {
        var prepareButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "PREPARE FOR CSDK", StringComparison.Ordinal)
                || string.Equals(button.Text, "ПОДГОТОВИТЬ ДЛЯ CSDK", StringComparison.Ordinal));
        if (prepareButton is null)
        {
            return;
        }

        _form = form;
        _prepareButton = prepareButton;
        _normalButtonText = prepareButton.Text;
        _toolTip = new ToolTip
        {
            AutoPopDelay = 16000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true,
        };

        _messageFilter = new ShiftPrepareMessageFilter(
            prepareButton,
            ToggleOnlinePreparationAsync);
        Application.AddMessageFilter(_messageFilter);

        prepareButton.Click += (_, _) =>
        {
            if (_session is not null)
            {
                _ = RefreshBaselineAfterManualPrepareAsync();
            }
        };

        form.FormClosed += (_, _) => Detach();
    }

    private static async Task ToggleOnlinePreparationAsync()
    {
        if (_toggleBusy || _form is null || _prepareButton is null)
        {
            return;
        }

        if (_session is not null)
        {
            StopSession();
            return;
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
            return;
        }

        _toggleBusy = true;
        var originalTitle = _form.Text;
        var buttons = FindActionButtons(_prepareButton).ToArray();
        var enabledStates = buttons.ToDictionary(button => button, button => button.Enabled);

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
            _prepareButton.Text = OnlineButtonText;
            UpdateToolTip(
                "ONLINE PREPARATION is active. Existing root DMX and texture files are hash-checked after a short debounce; only files whose bytes actually changed are copied into CSDK content. Shift-click this button again to stop. A normal click still runs full PREPARE FOR CSDK and refreshes the live-sync baseline.");
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
            _prepareButton.Text = OnlineButtonText;
            UpdateToolTip(
                "ONLINE PREPARATION baseline refreshed after PREPARE FOR CSDK. Existing root DMX and texture files are hash-checked and only byte-level changes are copied into CSDK content. Shift-click to stop.");
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
            if (_prepareButton is null || _prepareButton.IsDisposed || _session is null)
            {
                return;
            }

            _prepareButton.Text = OnlineButtonText;
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

        if (_prepareButton is not null && !_prepareButton.IsDisposed && _normalButtonText is not null)
        {
            _prepareButton.Text = _normalButtonText;
        }

        UpdateToolTip(
            "ONLINE PREPARATION is off. Shift-click PREPARE FOR CSDK to prepare once and start live hash-based DMX/texture synchronization.");
    }

    private static void Detach()
    {
        StopSession();

        if (_messageFilter is not null)
        {
            Application.RemoveMessageFilter(_messageFilter);
            _messageFilter = null;
        }

        _toolTip?.Dispose();
        _toolTip = null;
        _prepareButton = null;
        _form = null;
        _normalButtonText = null;
    }

    private static void UpdateToolTip(string text)
    {
        if (_toolTip is not null && _prepareButton is not null && !_prepareButton.IsDisposed)
        {
            _toolTip.SetToolTip(_prepareButton, text);
        }
    }

    private static IEnumerable<Button> FindActionButtons(Button prepareButton)
    {
        if (prepareButton.Parent is not FlowLayoutPanel topBar)
        {
            return [prepareButton];
        }

        return topBar.Controls.OfType<Button>();
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

    private sealed class ShiftPrepareMessageFilter(
        Button button,
        Func<Task> toggleAsync) : IMessageFilter
    {
        private bool _capturingShiftClick;

        public bool PreFilterMessage(ref Message m)
        {
            if (m.HWnd != button.Handle)
            {
                return false;
            }

            if (m.Msg == LeftMouseButtonDown
                && (Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                _capturingShiftClick = true;
                _ = toggleAsync();
                return true;
            }

            if (m.Msg == LeftMouseButtonUp && _capturingShiftClick)
            {
                _capturingShiftClick = false;
                return true;
            }

            return false;
        }
    }
}
