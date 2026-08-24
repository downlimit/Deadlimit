using Deadlimit.Core;

namespace Deadlimit.App;

internal static class BuildFeature
{
    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => panel.Controls.OfType<Button>()
                .Any(button => string.Equals(button.Text, "EXTRACT HERO SOURCE", StringComparison.Ordinal)
                    || string.Equals(button.Text, "ИЗВЛЕЧЬ ИСХОДНИКИ ГЕРОЯ", StringComparison.Ordinal)));

        if (topBar is null)
        {
            return;
        }

        var prepareButton = new Button
        {
            Text = UiText.T("PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK"),
            AutoSize = true,
        };

        var buildAndTestButton = new Button
        {
            Text = UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА"),
            AutoSize = true,
        };

        var launchCsdkButton = new Button
        {
            Text = UiText.T("LAUNCH CSDK", "ЗАПУСТИТЬ CSDK"),
            AutoSize = true,
        };

        var toolTip = new ToolTip
        {
            AutoPopDelay = 12000,
            InitialDelay = 450,
            ReshowDelay = 100,
            ShowAlways = true,
        };
        toolTip.SetToolTip(
            prepareButton,
            UiText.T(
                "Prepare the selected project's authoring content for Reduced CSDK12 / ModelDoc / Material Editor.\n\nDeadlimit refreshes the CSDK content and clears compiled output for this addon so CSDK can rebuild it cleanly. This action does not launch CSDK.",
                "Подготовить authoring-контент выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nDeadlimit обновляет CSDK content и очищает compiled output этого аддона, чтобы CSDK пересобрал его чисто. Эта кнопка не запускает CSDK."));
        toolTip.SetToolTip(
            buildAndTestButton,
            UiText.T(
                "Compile the current project and deploy its VPK into retail Deadlock so it is ready for testing.\n\nThis action does not launch the game. If Deadlock is already running, it must be closed because the loaded VPK is locked. Hold SHIFT while clicking to force a full clean rebuild.",
                "Скомпилировать текущий проект и установить его VPK в retail Deadlock, чтобы мод был готов к тесту.\n\nЭта кнопка не запускает игру. Если Deadlock уже запущен, его нужно закрыть: загруженный VPK заблокирован игрой. Удерживайте SHIFT при клике для полной чистой пересборки."));
        toolTip.SetToolTip(
            launchCsdkButton,
            UiText.T(
                "Launch the configured Reduced CSDK12 environment.\n\nUse it for ModelDoc, Material Editor and other Source 2 authoring tools. It does not build or deploy the retail VPK.",
                "Запустить настроенное окружение Reduced CSDK12.\n\nИспользуйте его для ModelDoc, Material Editor и других Source 2 authoring-инструментов. Эта кнопка не собирает и не устанавливает retail VPK."));

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
                UiText.T(
                    "Save the current Deadlimit project before running PREPARE FOR CSDK.",
                    "Сохраните текущий проект Deadlimit перед запуском ПОДГОТОВИТЬ ДЛЯ CSDK."),
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
                ? UiText.T("Existing compiled output for this addon was removed.", "Старый compiled output этого аддона удалён.")
                : UiText.T("No previous compiled output for this addon existed.", "Предыдущего compiled output для этого аддона не было.");

            var customMaterialSummary = result.CustomMaterialCount == 0
                ? UiText.T("Custom materials detected: 0\n", "Новых custom-материалов: 0\n")
                : UiText.T(
                    $"Custom materials detected: {result.CustomMaterialCount}\n" +
                    $"Custom VMAT created: {result.CustomVmatCreatedCount}\n" +
                    $"Custom VMAT preserved: {result.CustomVmatPreservedCount}\n" +
                    $"Texture PNG sources refreshed: {result.TextureSourceCount}\n" +
                    $"Custom material folder:\n{result.CustomMaterialContentFolder}\n",
                    $"Custom-материалов найдено: {result.CustomMaterialCount}\n" +
                    $"Создано VMAT: {result.CustomVmatCreatedCount}\n" +
                    $"Сохранено существующих VMAT: {result.CustomVmatPreservedCount}\n" +
                    $"Обновлено PNG-текстур: {result.TextureSourceCount}\n" +
                    $"Папка custom-материалов:\n{result.CustomMaterialContentFolder}\n");

            var message = UiText.T(
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
                $"Deadlimit did not compile it; use LAUNCH CSDK while authoring, or BUILD FOR TEST when you want to compile and deploy the retail VPK. Launch the game separately when you are ready.\n\n" +
                $"Log: {result.LogPath}",
                $"Authoring-контент подготовлен.\n\n" +
                $"Аддон: {result.AddonName}\n" +
                $"DMX overlays: {result.DmxCount}\n" +
                $"Материалов в DMX найдено: {result.DmxMaterialReferenceCount}\n" +
                $"VMDL remaps сохранено: {result.ExistingMaterialRemapCount}\n" +
                $"Compatibility remaps создано: {result.CompatibilityRemapCount}\n" +
                $"VMDL remaps добавлено: {result.AddedMaterialRemapCount}\n" +
                $"Всего VMDL remaps: {result.ExistingMaterialRemapCount + result.AddedMaterialRemapCount}\n" +
                customMaterialSummary +
                $"Retail source файлов скопировано: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Исходник модели:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit его не компилировал; для authoring используйте ЗАПУСК CSDK, а для компиляции и установки retail VPK — СОБРАТЬ ДЛЯ ТЕСТА. Игру запускайте отдельно, когда будете готовы.\n\n" +
                $"Лог: {result.LogPath}");

            using var dialog = BuildTestSuccessDialog.CreatePrepareSummary(message);
            dialog.ShowDialog(form);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Prepare failed", "Ошибка подготовки"),
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
                UiText.T(
                    "Save the current Deadlimit project before running BUILD FOR TEST.",
                    "Сохраните текущий проект Deadlimit перед запуском СОБРАТЬ ДЛЯ ТЕСТА."),
                "Deadlimit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        var deadlockWasRunning = DeadlockProcessService.IsRunning();
        if (deadlockWasRunning)
        {
            var closeAnswer = MessageBox.Show(
                form,
                UiText.T(
                    "Deadlock is running and has the loaded VPK locked, so Deadlimit cannot replace the current mod archive while the game is open.\n\nClose Deadlock automatically and continue BUILD FOR TEST?",
                    "Deadlock сейчас запущен и блокирует загруженный VPK, поэтому Deadlimit не может заменить текущий архив мода, пока игра открыта.\n\nАвтоматически закрыть Deadlock и продолжить СОБРАТЬ ДЛЯ ТЕСТА?"),
                UiText.T("Deadlock must be closed", "Нужно закрыть Deadlock"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (closeAnswer != DialogResult.Yes)
            {
                return;
            }
        }

        var forceFullRebuild = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        SetButtonsEnabled(actionButtons, false);
        var originalTitle = form.Text;
        using var animator = new BuildProgressAnimator(form, progressBar, originalTitle);

        string? forceStatePath = null;
        string? forceStateBackupPath = null;

        try
        {
            animator.Start();
            var paths = new DeadlimitPaths();

            if (deadlockWasRunning)
            {
                animator.Update(new BuildAndTestProgress(
                    UiText.T("Closing Deadlock to unlock the current VPK...", "Закрытие Deadlock для разблокировки текущего VPK..."),
                    0));

                var stopped = await DeadlockProcessService.CloseAsync();
                if (!stopped)
                {
                    throw new InvalidOperationException(UiText.T(
                        "Deadlock did not close, so the current VPK may still be locked. Close the game manually and run BUILD FOR TEST again.",
                        "Deadlock не удалось закрыть, поэтому текущий VPK всё ещё может быть заблокирован. Закройте игру вручную и снова запустите СОБРАТЬ ДЛЯ ТЕСТА."));
                }
            }

            animator.Update(new BuildAndTestProgress(
                UiText.T("Checking retail Deadlock mod loading...", "Проверка загрузки модов в retail Deadlock..."),
                1));
            var modLoading = await Task.Run(() =>
                new RetailModLoadingService(paths).EnsureEnabled(manifest));

            animator.Update(new BuildAndTestProgress(
                UiText.T("Checking retail VPK release slot...", "Проверка retail VPK-слота..."),
                1));
            var slotGuard = new VpkSlotOwnershipService(paths);
            var slotCheck = await Task.Run(() => slotGuard.EnsureSlotAvailable(manifest));

            if (forceFullRebuild)
            {
                animator.Update(new BuildAndTestProgress(
                    UiText.T(
                        "SHIFT detected — forcing a clean/full rebuild...",
                        "SHIFT — принудительная полная чистая пересборка..."),
                    2));
                forceStatePath = Path.Combine(
                    ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
                    "build-test-state.json");

                if (File.Exists(forceStatePath))
                {
                    forceStateBackupPath = forceStatePath + $".force-backup-{Guid.NewGuid():N}";
                    File.Move(forceStatePath, forceStateBackupPath);
                }
            }

            BuildAndTestResult result;
            try
            {
                var progress = new Progress<BuildAndTestProgress>(animator.Update);
                var service = new BuildAndTestService(paths);
                result = await Task.Run(() => service.BuildAsync(manifest, progress));
            }
            catch
            {
                RestoreForceBuildState(forceStatePath, forceStateBackupPath);
                throw;
            }

            if (forceStateBackupPath is not null && File.Exists(forceStateBackupPath))
            {
                File.Delete(forceStateBackupPath);
                forceStateBackupPath = null;
            }

            await Task.Run(() => slotGuard.RecordSuccessfulDeployment(manifest, result.VpkPath));
            animator.Update(new BuildAndTestProgress(
                UiText.T("Build for test complete.", "Сборка для теста готова."),
                100));

            var modLoadingSummary = modLoading.Patched
                ? UiText.T(
                    "\nRetail mod loading: repaired automatically. The next Deadlock launch will use the repaired search path.",
                    "\nЗагрузка retail-модов: автоматически восстановлена. Следующий запуск Deadlock будет использовать исправленный search path.")
                : string.Empty;
            var legacySlotSummary = slotCheck.LegacyOwnershipAdopted
                ? UiText.T(
                    "\nVPK slot ownership: adopted from the previous Deadlimit build state.",
                    "\nVPK-слот: владение принято из предыдущего состояния сборки Deadlimit.")
                : string.Empty;
            var forceSummary = forceFullRebuild
                ? UiText.T("\nForced full rebuild: yes (SHIFT).", "\nПринудительная полная пересборка: да (SHIFT).")
                : string.Empty;
            var closedGameSummary = deadlockWasRunning
                ? UiText.T("\nDeadlock was closed automatically to unlock the VPK.", "\nDeadlock был автоматически закрыт для разблокировки VPK.")
                : string.Empty;

            var summary = UiText.T(
                $"Addon: {result.AddonName}\n" +
                $"Mode: {(result.FullRebuild ? "clean/full" : "incremental")}\n" +
                $"Compiled sources: {result.CompiledSourceCount}\n" +
                $"Stale compiled outputs removed: {result.RemovedCompiledOutputCount}\n" +
                $"AG2 restored this run: {(result.Ag2Applied ? "yes" : "not needed")}",
                $"Аддон: {result.AddonName}\n" +
                $"Режим: {(result.FullRebuild ? "clean/full" : "incremental")}\n" +
                $"Скомпилировано source-файлов: {result.CompiledSourceCount}\n" +
                $"Удалено устаревших compiled outputs: {result.RemovedCompiledOutputCount}\n" +
                $"AG2 восстановлен: {(result.Ag2Applied ? "да" : "не требовалось")}")
                + forceSummary
                + modLoadingSummary
                + legacySlotSummary
                + closedGameSummary;

            using var dialog = new BuildTestSuccessDialog(result.VpkPath, summary);
            dialog.ShowDialog(form);
        }
        catch (Exception ex)
        {
            RestoreForceBuildState(forceStatePath, forceStateBackupPath);
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Build for test failed", "Ошибка сборки для теста"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            form.Text = originalTitle;
            SetButtonsEnabled(actionButtons, true);
        }
    }

    private static void RestoreForceBuildState(string? statePath, string? backupPath)
    {
        if (string.IsNullOrWhiteSpace(statePath)
            || string.IsNullOrWhiteSpace(backupPath)
            || !File.Exists(backupPath))
        {
            return;
        }

        if (File.Exists(statePath))
        {
            File.Delete(statePath);
        }
        File.Move(backupPath, statePath);
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
                UiText.T(
                    $"CSDK launcher was not found:\n{paths.CsdkLauncherPath}\n\nOpen SETTINGS and select the Reduced_CSDK_12 root.",
                    $"CSDK launcher не найден:\n{paths.CsdkLauncherPath}\n\nОткройте НАСТРОЙКИ и выберите корень Reduced_CSDK_12."),
                UiText.T("CSDK launcher not found", "CSDK launcher не найден"),
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
                UiText.T("Could not launch CSDK", "Не удалось запустить CSDK"),
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
        private string _message = UiText.T("Starting build for test...", "Запуск сборки для теста...");
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
