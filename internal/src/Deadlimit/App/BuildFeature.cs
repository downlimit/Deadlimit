using Deadlimit.Core;

namespace Deadlimit.App;

internal static class BuildFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => panel.Controls.OfType<Button>()
                .Any(button => string.Equals(button.Text, "EXTRACT HERO SOURCE", StringComparison.Ordinal)));

        if (topBar is null)
        {
            return;
        }

        var prepareButton = new Button
        {
            Text = "PREPARE FOR CSDK",
            AutoSize = true,
        };

        var buildAndTestButton = new Button
        {
            Text = "BUILD & TEST",
            AutoSize = true,
        };

        var launchCsdkButton = new Button
        {
            Text = "LAUNCH CSDK",
            AutoSize = true,
        };

        var buildProgressBar = AddBuildProgressBar(form);
        var actionButtons = new[] { prepareButton, buildAndTestButton, launchCsdkButton };

        prepareButton.Click += async (_, _) => await RunPrepareAsync(form, actionButtons);
        buildAndTestButton.Click += async (_, _) =>
            await RunBuildAndTestAsync(form, actionButtons, buildProgressBar);
        launchCsdkButton.Click += (_, _) => LaunchCsdk(form);

        topBar.Controls.Add(prepareButton);
        topBar.Controls.Add(buildAndTestButton);
        topBar.Controls.Add(launchCsdkButton);
    }

    private static async Task RunPrepareAsync(MainForm form, IReadOnlyList<Button> actionButtons)
    {
        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                form,
                "Save the current Deadlimit project before running PREPARE FOR CSDK.",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SetButtonsEnabled(actionButtons, false);
        var originalTitle = form.Text;

        try
        {
            var progress = new Progress<PrepareAuthoringProgress>(update =>
            {
                form.Text = $"Deadlimit — {update.Message}";
            });

            var service = new PrepareAuthoringService(new DeadlimitPaths());
            var result = await service.PrepareAsync(manifest, progress);

            var gameState = result.GameOutputCleaned
                ? "Existing compiled output for this addon was removed."
                : "No previous compiled output for this addon existed.";

            var customMaterialSummary = result.CustomMaterialCount == 0
                ? "Custom materials detected: 0\n"
                : $"Custom materials detected: {result.CustomMaterialCount}\n" +
                  $"Custom VMAT created: {result.CustomVmatCreatedCount}\n" +
                  $"Custom VMAT preserved: {result.CustomVmatPreservedCount}\n" +
                  $"Texture PNG sources refreshed: {result.TextureSourceCount}\n" +
                  $"Custom material folder:\n{result.CustomMaterialContentFolder}\n";

            MessageBox.Show(
                form,
                $"Authoring content prepared.\n\n" +
                $"Addon: {result.AddonName}\n" +
                $"DMX overlays: {result.DmxCount}\n" +
                $"DMX material references detected: {result.DmxMaterialReferenceCount}\n" +
                $"VMDL remaps preserved: {result.ExistingMaterialRemapCount}\n" +
                $"Compatibility remaps generated: {result.CompatibilityRemapCount}\n" +
                $"VMDL remaps added: {result.AddedMaterialRemapCount}\n" +
                $"Total VMDL remaps: {result.ExistingMaterialRemapCount + result.AddedMaterialRemapCount}\n" +
                customMaterialSummary +
                $"Retail source files copied: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Model source:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit did not compile it; use LAUNCH CSDK while authoring, or BUILD & TEST for the normal in-game iteration loop.\n\n" +
                $"Log: {result.LogPath}",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                "Prepare failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            form.Text = originalTitle;
            SetButtonsEnabled(actionButtons, true);
        }
    }

    private static async Task RunBuildAndTestAsync(
        MainForm form,
        IReadOnlyList<Button> actionButtons,
        ToolStripProgressBar? progressBar)
    {
        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                form,
                "Save the current Deadlimit project before running BUILD & TEST.",
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SetButtonsEnabled(actionButtons, false);
        var originalTitle = form.Text;
        using var animator = new BuildProgressAnimator(form, progressBar, originalTitle);

        try
        {
            animator.Start();
            var paths = new DeadlimitPaths();

            animator.Update(new BuildAndTestProgress("Checking retail Deadlock mod loading...", 1));
            var modLoading = await Task.Run(() =>
                new RetailModLoadingService(paths).EnsureEnabled(manifest));

            var progress = new Progress<BuildAndTestProgress>(animator.Update);
            var service = new BuildAndTestService(paths);
            var result = await Task.Run(() => service.BuildAsync(manifest, progress));
            animator.Update(new BuildAndTestProgress("Build & Test complete.", 100));

            var modLoadingSummary = modLoading.Patched
                ? "\nRetail mod loading: repaired automatically. Restart Deadlock once if it was already running."
                : string.Empty;

            var summary =
                $"Addon: {result.AddonName}\n" +
                $"Mode: {(result.FullRebuild ? "clean/full" : "incremental")}\n" +
                $"Compiled sources: {result.CompiledSourceCount}\n" +
                $"Stale compiled outputs removed: {result.RemovedCompiledOutputCount}\n" +
                $"AG2 restored this run: {(result.Ag2Applied ? "yes" : "not needed")}" +
                modLoadingSummary;

            using var dialog = new BuildTestSuccessDialog(result.VpkPath, summary);
            dialog.ShowDialog(form);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                form,
                ex.Message,
                "Build & Test failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            form.Text = originalTitle;
            SetButtonsEnabled(actionButtons, true);
        }
    }

    private static ToolStripProgressBar? AddBuildProgressBar(MainForm form)
    {
        var statusStrip = FindDescendants<StatusStrip>(form).FirstOrDefault();
        if (statusStrip is null)
        {
            return null;
        }

        var spacer = new ToolStripStatusLabel
        {
            Spring = true,
        };

        var progressBar = new ToolStripProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Width = 180,
            Visible = false,
            Style = ProgressBarStyle.Blocks,
        };

        statusStrip.Items.Add(spacer);
        statusStrip.Items.Add(progressBar);
        return progressBar;
    }

    private static void LaunchCsdk(MainForm form)
    {
        var paths = new DeadlimitPaths();
        if (!File.Exists(paths.CsdkLauncherPath))
        {
            MessageBox.Show(
                form,
                $"CSDK launcher was not found:\n{paths.CsdkLauncherPath}\n\nOpen SETTINGS and select the Reduced_CSDK_12 root.",
                "CSDK launcher not found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = paths.CsdkLauncherPath,
                WorkingDirectory = paths.CsdkRoot,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                form,
                ex.Message,
                "Could not launch CSDK",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void SetButtonsEnabled(IEnumerable<Button> buttons, bool enabled)
    {
        foreach (var button in buttons)
        {
            button.Enabled = enabled;
        }
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

    private sealed class BuildProgressAnimator : IDisposable
    {
        private static readonly string[] SpinnerFrames = ["|", "/", "—", "\\"];

        private readonly Form _form;
        private readonly ToolStripProgressBar? _progressBar;
        private readonly string _baseTitle;
        private readonly System.Windows.Forms.Timer _timer;

        private int _percent;
        private int _frameIndex;
        private string _message = "Starting Build & Test...";
        private bool _disposed;

        public BuildProgressAnimator(
            Form form,
            ToolStripProgressBar? progressBar,
            string baseTitle)
        {
            _form = form;
            _progressBar = progressBar;
            _baseTitle = baseTitle;
            _timer = new System.Windows.Forms.Timer
            {
                Interval = 120,
            };
            _timer.Tick += (_, _) =>
            {
                _frameIndex = (_frameIndex + 1) % SpinnerFrames.Length;
                Render();
            };
        }

        public void Start()
        {
            if (_progressBar is not null)
            {
                _progressBar.Value = 0;
                _progressBar.Visible = true;
            }

            _timer.Start();
            Render();
        }

        public void Update(BuildAndTestProgress update)
        {
            if (_disposed)
            {
                return;
            }

            _percent = Math.Clamp(update.Percent, 0, 100);
            _message = update.Message;

            if (_progressBar is not null)
            {
                _progressBar.Value = _percent;
            }

            Render();
        }

        private void Render()
        {
            if (_disposed || _form.IsDisposed)
            {
                return;
            }

            var spinner = _percent >= 100 ? "✓" : SpinnerFrames[_frameIndex];
            _form.Text = $"{_baseTitle} — [{_percent}% {spinner}] - {_message}";
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Dispose();

            if (_progressBar is not null)
            {
                _progressBar.Visible = false;
                _progressBar.Value = 0;
            }
        }
    }
}
