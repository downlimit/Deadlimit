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
                "Prepare the selected project's working files for Reduced CSDK12 / ModelDoc / Material Editor.\n\nA normal click preserves manual VMAT tuning while synchronizing matching project textures. Hold SHIFT to regenerate Deadlimit Manager custom materials; the confirmation dialog lets you choose whether to create a backup first.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы Deadlimit Manager; в окне подтверждения можно выбрать, создавать ли резервную копию."));
        toolTip.SetToolTip(
            buildAndTestButton,
            UiText.T(
                "Compile the current project and deploy its VPK into Deadlock game client so it is ready for testing.\n\nThis action does not launch the game. If Deadlock is already running, it must be closed because the loaded VPK is locked. Hold SHIFT while clicking to force a full clean rebuild.",
                "Скомпилировать текущий проект и установить его VPK в игровой клиент Deadlock, чтобы мод был готов к тесту.\n\nЭта кнопка не запускает игру. Если Deadlock уже запущен, его нужно закрыть: загруженный VPK заблокирован игрой. Удерживайте SHIFT при клике для полной чистой пересборки."));
        toolTip.SetToolTip(
            launchCsdkButton,
            UiText.T(
                "Launch the configured Reduced CSDK12 environment.\n\nHold SHIFT while clicking to prepare once, enable ONLINE PREPARATION and launch CSDK. Repeat SHIFT+click to stop online synchronization without launching another CSDK instance.",
                "Запустить настроенное окружение Reduced CSDK12.\n\nУдерживайте SHIFT при клике, чтобы выполнить подготовку, включить ОНЛАЙН-ПОДГОТОВКУ и запустить CSDK. Повторный SHIFT+клик остановит онлайн-синхронизацию без запуска ещё одного CSDK."));

        var buildProgressBar = AddBuildProgressBar(form);
        var actionButtons = new[] { prepareButton, buildAndTestButton, launchCsdkButton };

        prepareButton.Click += async (_, _) =>
            await RunPrepareAsync(form, actionButtons, buildProgressBar);
        buildAndTestButton.Click += async (_, _) =>
            await RunBuildAndTestAsync(form, actionButtons, buildProgressBar);
        launchCsdkButton.Click += async (_, _) =>
        {
            if ((Control.ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                LaunchCsdk(form);
                return;
            }

            if (await OnlinePreparationFeature.ToggleFromLaunchButtonAsync())
            {
                LaunchCsdk(form);
            }
        };

        topBar.Controls.Add(prepareButton);
        topBar.Controls.Add(buildAndTestButton);
        topBar.Controls.Add(launchCsdkButton);
    }

    private static async Task RunPrepareAsync(
        MainForm form,
        IReadOnlyList<Button> actionButtons,
        ToolStripProgressBar? progressBar)
    {
        var regenerateCustomMaterials = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        var backupCustomMaterials = true;
        var manifest = ProjectStore.TryLoadLastProject();
        if (manifest is null || !Directory.Exists(manifest.ProjectFolder))
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "Save the current Deadlimit Manager project before running PREPARE FOR CSDK.",
                    "Сохраните текущий проект Deadlimit Manager перед запуском ПОДГОТОВИТЬ ДЛЯ CSDK."),
                "Deadlimit Manager",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (regenerateCustomMaterials)
        {
            var choice = MessageBox.ShowCustom(
                form,
                UiText.T(
                    "SHIFT+PREPARE will regenerate every custom VMAT currently referenced by this project. Manual Material Editor tuning in those VMAT files will be replaced by the current Deadlimit Manager templates and project textures.\n\nYES creates a backup first. YES, NO BACKUP regenerates immediately without creating a backup.\n\nContinue?",
                    "SHIFT+ПОДГОТОВИТЬ пересоздаст все custom-VMAT, на которые сейчас ссылается проект. Ручные настройки этих VMAT из Material Editor будут заменены текущими шаблонами Deadlimit Manager и текстурами проекта.\n\nДА сначала создаст резервную копию. ДА, БЕЗ БЭКАПА пересоздаст материалы сразу, без резервной копии.\n\nПродолжить?"),
                UiText.T("Clean material preparation", "Чистая подготовка материалов"),
                new DeadlimitDialogButton(
                    UiText.T("YES", "ДА"),
                    DeadlimitDialogChoice.Yes,
                    IsDefault: true),
                new DeadlimitDialogButton(
                    UiText.T("YES, NO BACKUP", "ДА, БЕЗ БЭКАПА"),
                    DeadlimitDialogChoice.YesWithoutBackup),
                new DeadlimitDialogButton(
                    UiText.T("NO", "НЕТ"),
                    DeadlimitDialogChoice.No,
                    IsCancel: true));
            if (choice is not DeadlimitDialogChoice.Yes and not DeadlimitDialogChoice.YesWithoutBackup)
            {
                return;
            }

            backupCustomMaterials = choice != DeadlimitDialogChoice.YesWithoutBackup;
        }

        SetButtonsEnabled(actionButtons, false);
        var originalTitle = form.Text;
        using var animator = new BuildProgressAnimator(
            form,
            progressBar,
            originalTitle,
            UiText.T("Starting preparation for CSDK...", "Запуск подготовки для CSDK..."));

        try
        {
            animator.Start();
            var progress = new Progress<PrepareAuthoringProgress>(update =>
                animator.Update(new BuildAndTestProgress(
                    update.Message,
                    MapStandalonePrepareProgress(update.Message))));

            var service = new PrepareAuthoringService(new DeadlimitPaths());
            var result = await service.PrepareAsync(
                manifest,
                progress,
                regenerateCustomMaterials: regenerateCustomMaterials,
                backupCustomMaterials: backupCustomMaterials);
            animator.Update(new BuildAndTestProgress(
                UiText.T("Preparation for CSDK complete.", "Подготовка для CSDK готова."),
                100));

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
                $"Project working files prepared.\n\n" +
                $"Addon: {result.AddonName}\n" +
                $"DMX overlays: {result.DmxCount}\n" +
                $"Vertex Color sidecars applied: {result.VertexColorAppliedDmxCount}\n" +
                $"Vertex Color sidecars missing: {result.VertexColorMissingDmxCount}\n" +
                $"Vertex Color sidecars skipped: {result.VertexColorSkippedDmxCount}\n" +
                $"DMX material references detected: {result.DmxMaterialReferenceCount}\n" +
                $"VMDL remaps preserved: {result.ExistingMaterialRemapCount}\n" +
                $"Compatibility remaps generated: {result.CompatibilityRemapCount}\n" +
                $"VMDL remaps added: {result.AddedMaterialRemapCount}\n" +
                $"Total VMDL remaps: {result.ExistingMaterialRemapCount + result.AddedMaterialRemapCount}\n" +
                customMaterialSummary +
                $"Game-client source files copied: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Model source:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit Manager did not compile it; use LAUNCH CSDK while working on the model and materials, or BUILD FOR TEST when you want to compile and deploy the game-client VPK. Launch the game separately when you are ready.\n\n" +
                $"Log: {result.LogPath}",
                $"Рабочие файлы проекта подготовлены.\n\n" +
                $"Аддон: {result.AddonName}\n" +
                $"DMX overlays: {result.DmxCount}\n" +
                $"Vertex Color sidecars применено: {result.VertexColorAppliedDmxCount}\n" +
                $"Vertex Color sidecars отсутствует: {result.VertexColorMissingDmxCount}\n" +
                $"Vertex Color sidecars пропущено: {result.VertexColorSkippedDmxCount}\n" +
                $"Материалов в DMX найдено: {result.DmxMaterialReferenceCount}\n" +
                $"VMDL remaps сохранено: {result.ExistingMaterialRemapCount}\n" +
                $"Compatibility remaps создано: {result.CompatibilityRemapCount}\n" +
                $"VMDL remaps добавлено: {result.AddedMaterialRemapCount}\n" +
                $"Всего VMDL remaps: {result.ExistingMaterialRemapCount + result.AddedMaterialRemapCount}\n" +
                customMaterialSummary +
                $"Файлов из игрового клиента Deadlock скопировано: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Исходник модели:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit Manager его не компилировал; для работы с моделью и материалами используйте ЗАПУСК CSDK, а для компиляции и установки VPK игрового клиента Deadlock — СОБРАТЬ ДЛЯ ТЕСТА. Игру запускайте отдельно, когда будете готовы.\n\n" +
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
                    "Save the current Deadlimit Manager project before running BUILD FOR TEST.",
                    "Сохраните текущий проект Deadlimit Manager перед запуском СОБРАТЬ ДЛЯ ТЕСТА."),
                "Deadlimit Manager",
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
                    "Deadlock is running and has the loaded VPK locked, so Deadlimit Manager cannot replace the current mod archive while the game is open.\n\nClose Deadlock automatically and continue BUILD FOR TEST?",
                    "Deadlock сейчас запущен и блокирует загруженный VPK, поэтому Deadlimit Manager не может заменить текущий архив мода, пока игра открыта.\n\nАвтоматически закрыть Deadlock и продолжить СОБРАТЬ ДЛЯ ТЕСТА?"),
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
                UiText.T("Checking Deadlock game-client mod loading...", "Проверка загрузки модов в игровом клиенте Deadlock..."),
                1));
            var modLoading = await Task.Run(() =>
                new RetailModLoadingService(paths).EnsureEnabled(manifest));

            animator.Update(new BuildAndTestProgress(
                UiText.T("Checking Deadlock game-client VPK release slot...", "Проверка слота VPK игрового клиента Deadlock..."),
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
                    "\nDeadlock game-client mod loading: repaired automatically. The next Deadlock launch will use the repaired resource search path.",
                    "\nЗагрузка модов в игровом клиенте Deadlock автоматически восстановлена. Следующий запуск Deadlock будет использовать исправленный путь поиска ресурсов.")
                : string.Empty;
            var legacySlotSummary = slotCheck.LegacyOwnershipAdopted
                ? UiText.T(
                    "\nVPK slot ownership: adopted from the previous legacy Deadlimit build state.",
                    "\nVPK-слот: владение принято из предыдущего legacy-состояния сборки Deadlimit.")
                : string.Empty;
            var forceSummary = forceFullRebuild
                ? UiText.T("\nForced full rebuild: yes (SHIFT).", "\nПринудительная полная пересборка: да (SHIFT).")
                : string.Empty;
            var closedGameSummary = deadlockWasRunning
                ? UiText.T("\nDeadlock was closed automatically to unlock the VPK.", "\nDeadlock был автоматически закрыт для разблокировки VPK.")
                : string.Empty;
            var warningSummary = result.Warnings.Count == 0
                ? string.Empty
                : UiText.T(
                    "\n\n⚠ Vertex Color warning:\n" + string.Join("\n", result.Warnings.Select(warning => $"• {warning}")),
                    "\n\n⚠ Предупреждение Vertex Color:\n" + string.Join("\n", result.Warnings.Select(warning => $"• {warning}")));

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
                + closedGameSummary
                + warningSummary;

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

    private static int MapStandalonePrepareProgress(string message)
    {
        if (message.StartsWith("Cleaning stale", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Очистка устаревшего", StringComparison.OrdinalIgnoreCase))
        {
            return 10;
        }
        if (message.StartsWith("Refreshing retail", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Обновление retail", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }
        if (message.StartsWith("Overlaying artist", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Наложение пользовательских", StringComparison.OrdinalIgnoreCase))
        {
            return 45;
        }
        if (message.StartsWith("Preparing addon-owned", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Подготовка custom", StringComparison.OrdinalIgnoreCase))
        {
            return 65;
        }
        if (message.StartsWith("Applying narrow", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Применение необходимых", StringComparison.OrdinalIgnoreCase))
        {
            return 85;
        }
        if (message.StartsWith("Project working files prepared", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Authoring content подготовлен", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        return 5;
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
        private string _message;
        private bool _disposed;

        public BuildProgressAnimator(
            Form form,
            ToolStripProgressBar? progressBar,
            string baseTitle,
            string? initialMessage = null)
        {
            _form = form;
            _progressBar = progressBar;
            _baseTitle = baseTitle;
            _message = initialMessage
                ?? UiText.T("Starting build for test...", "Запуск сборки для теста...");
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
