using System.Runtime.CompilerServices;

namespace Deadlimit.App;

/// <summary>
/// Composes the launch button's process-driven enabled state with BUILD FOR TEST state.
/// ProjectHeaderFeature remains the owner of game-process state; this feature only prevents
/// that state from re-enabling LAUNCH GAME while a retail VPK build/deploy is still active.
/// </summary>
internal static class GameLaunchInterlockFeature
{
    private static readonly ConditionalWeakTable<MainForm, Session> Sessions = new();

    internal static void Attach(MainForm form)
    {
        if (Sessions.TryGetValue(form, out _))
        {
            return;
        }

        var session = new Session(form);
        Sessions.Add(form, session);
        session.Attach();
    }

    private sealed class Session : IDisposable
    {
        private readonly MainForm _form;
        private Button? _launchButton;
        private bool _buildRunning;
        private bool _desiredLaunchEnabled = true;
        private bool _settingLaunchEnabled;
        private bool _disposed;

        public Session(MainForm form)
        {
            _form = form;
        }

        public void Attach()
        {
            BuildFeature.BuildForTestStateChanged += OnBuildForTestStateChanged;
            _form.Shown += OnFormShown;
            _form.FormClosed += OnFormClosed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            BuildFeature.BuildForTestStateChanged -= OnBuildForTestStateChanged;
            _form.Shown -= OnFormShown;
            _form.FormClosed -= OnFormClosed;
            if (_launchButton is not null)
            {
                _launchButton.EnabledChanged -= OnLaunchButtonEnabledChanged;
                _launchButton = null;
            }
        }

        private void OnFormShown(object? sender, EventArgs e)
        {
            BindLaunchButton();
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            Dispose();
        }

        private void BindLaunchButton()
        {
            if (_disposed || _launchButton is not null)
            {
                return;
            }

            var launchButton = FindDescendants<Button>(_form)
                .FirstOrDefault(IsGameLaunchButton);
            if (launchButton is null)
            {
                return;
            }

            _launchButton = launchButton;
            _desiredLaunchEnabled = launchButton.Enabled;
            _buildRunning = BuildFeature.IsBuildForTestRunning(_form);
            launchButton.EnabledChanged += OnLaunchButtonEnabledChanged;
            ApplyInterlock();
        }

        private void OnBuildForTestStateChanged(MainForm form, bool running)
        {
            if (_disposed || !ReferenceEquals(form, _form))
            {
                return;
            }

            BindLaunchButton();
            _buildRunning = running;
            ApplyInterlock();
        }

        private void OnLaunchButtonEnabledChanged(object? sender, EventArgs e)
        {
            if (_disposed || _settingLaunchEnabled || _launchButton is null)
            {
                return;
            }

            // Remember what ProjectHeaderFeature wanted. If its asynchronous close/probe
            // finishes during a build and tries to unlock the button, keep that desired
            // state but continue presenting the button as disabled until the build ends.
            _desiredLaunchEnabled = _launchButton.Enabled;
            if (_buildRunning && _launchButton.Enabled)
            {
                SetLaunchEnabled(false);
            }
        }

        private void ApplyInterlock()
        {
            if (_launchButton is null || _launchButton.IsDisposed)
            {
                return;
            }

            SetLaunchEnabled(_buildRunning ? false : _desiredLaunchEnabled);
        }

        private void SetLaunchEnabled(bool enabled)
        {
            if (_launchButton is null || _launchButton.IsDisposed || _launchButton.Enabled == enabled)
            {
                return;
            }

            _settingLaunchEnabled = true;
            try
            {
                _launchButton.Enabled = enabled;
            }
            finally
            {
                _settingLaunchEnabled = false;
            }
        }

        private static bool IsGameLaunchButton(Button button) =>
            string.Equals(button.Text, "▶  LAUNCH GAME", StringComparison.Ordinal)
            || string.Equals(button.Text, "▶  ЗАПУСК ИГРЫ", StringComparison.Ordinal)
            || string.Equals(button.Text, "✕  CLOSE", StringComparison.Ordinal)
            || string.Equals(button.Text, "✕  ЗАКРЫТЬ", StringComparison.Ordinal)
            || string.Equals(button.Text, "GAME IS LAUNCHING", StringComparison.Ordinal)
            || string.Equals(button.Text, "ИГРА ЗАПУСКАЕТСЯ", StringComparison.Ordinal);

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
}
