using Deadlimit.Core;

namespace Deadlimit.App;

internal static class BuildFeature
{
    private static readonly HashSet<MainForm> ActiveBuildForms = [];

    internal static event Action<MainForm, bool>? BuildForTestStateChanged;

    internal static bool IsBuildForTestRunning(MainForm form) => ActiveBuildForms.Contains(form);

    public static void Attach(MainForm form)
    {
        var topBar = FindDescendants<FlowLayoutPanel>(form)
            .FirstOrDefault(panel => panel.Controls.OfType<Button>()
                .Any(button => string.Equals(
                    button.Name,
                    UiControlNames.ExtractHeroSourceButton,
                    StringComparison.Ordinal)));

        if (topBar is null)
        {
            return;
        }

        var prepareButton = new Button
        {
            Name = UiControlNames.PrepareButton,
            Text = UiText.T("PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK"),
            AutoSize = true,
        };

        var buildAndTestButton = new Button
        {
            Name = UiControlNames.BuildForTestButton,
            Text = UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА"),
            AutoSize = true,
        };

        var launchCsdkButton = new Button
        {
            Name = UiControlNames.LaunchCsdkButton,
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
                "Prepare the selected project's working files for Reduced CSDK12 / ModelDoc / Material Editor.\n\nA normal click preserves artist edits. Hold SHIFT to choose reset sections or create an editable hero-select VMAP.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nОбычный клик сохраняет правки автора. Удерживайте SHIFT, чтобы выбрать разделы для восстановления или создать редактируемый VMAP сцены выбора героя."));
        toolTip.SetToolTip(
            buildAndTestButton,
            UiText.T(
                "Compile the current project and deploy its VPK into Deadlock game client so it is ready for testing.\n\nThis action does not launch the game. If Deadlock is already running, it must be closed because the loaded VPK is locked. Hold SHIFT while clicking to force a full clean rebuild.",
                "Скомпилировать текущий проект и установить его VPK в игровой клиент Deadlock, чтобы мод был готов к тесту.\n\nЭта кнопка не запускает игру. Если Deadlock уже запущен, его нужно закрыть: загруженный VPK заблокирован игрой. Удерживайте SHIFT при клике для полной чистой пересборки."));
        toolTip.SetToolTip(
            launchCsdkButton,
            UiText.T(
                "Launch the configured Reduced CSDK12 environment.\n\nHold SHIFT while clicking to prepare once, enable LIVE SYNC and launch CSDK. Repeat SHIFT+click to stop LIVE SYNC without launching another CSDK instance.",
                "Запустить настроенное окружение Reduced CSDK12.\n\nУдерживайте SHIFT при клике, чтобы выполнить подготовку, включить LIVE SYNC и запустить CSDK. Повторный SHIFT+клик отключит LIVE SYNC без запуска ещё одного CSDK."));

        var buildProgressBar = AddBuildProgressBar(form);
        var actionButtons = new[] { prepareButton, buildAndTestButton, launchCsdkButton };
        var csdkStateTimer = new System.Windows.Forms.Timer
        {
            Interval = 2000,
        };
        var csdkStateProbeActive = false;
        var csdkIsRunning = false;
        CancellationTokenSource? prepareCancellation = null;
        CancellationTokenSource? buildCancellation = null;
        var closeRequested = false;

        void CloseAfterActiveOperationIfRequested()
        {
            if (!closeRequested
                || prepareCancellation is not null
                || buildCancellation is not null
                || form.IsDisposed
                || !form.IsHandleCreated)
            {
                return;
            }

            form.BeginInvoke((Action)form.Close);
        }

        bool IsOnlineCsdkState() =>
            launchCsdkButton.Text.Contains("LIVE SYNC", StringComparison.OrdinalIgnoreCase)
            || (launchCsdkButton.Text.Contains("CSDK", StringComparison.OrdinalIgnoreCase)
                && (launchCsdkButton.Text.Contains("ONLINE", StringComparison.OrdinalIgnoreCase)
                    || launchCsdkButton.Text.Contains("ОНЛАЙН", StringComparison.OrdinalIgnoreCase)));

        void ApplyCsdkButtonState()
        {
            if (launchCsdkButton.IsDisposed || IsOnlineCsdkState())
            {
                return;
            }

            var desiredText = csdkIsRunning
                ? UiText.T("CSDK RUNNING", "CSDK ЗАПУЩЕН")
                : UiText.T("▶  LAUNCH CSDK", "▶  ЗАПУСК CSDK");
            var textChanged = !string.Equals(
                launchCsdkButton.Text,
                desiredText,
                StringComparison.Ordinal);
            if (textChanged)
            {
                launchCsdkButton.Text = desiredText;
            }

            toolTip.SetToolTip(
                launchCsdkButton,
                csdkIsRunning
                    ? UiText.T(
                        "Reduced CSDK12 is already running. Click to bring its most recently active visible window to the foreground.\n\nHold SHIFT to keep using the LIVE SYNC shortcut.",
                        "Reduced CSDK12 уже запущен. Нажмите, чтобы вывести его последнее активное видимое окно на передний план.\n\nУдерживайте SHIFT, чтобы использовать LIVE SYNC.")
                    : UiText.T(
                        "Launch the configured Reduced CSDK12 environment.\n\nHold SHIFT while clicking to prepare once, enable LIVE SYNC and launch CSDK. Repeat SHIFT+click to stop LIVE SYNC without launching another CSDK instance.",
                        "Запустить настроенное окружение Reduced CSDK12.\n\nУдерживайте SHIFT при клике, чтобы выполнить подготовку, включить LIVE SYNC и запустить CSDK. Повторный SHIFT+клик отключит LIVE SYNC без запуска ещё одного CSDK."));

            if (textChanged)
            {
                launchCsdkButton.Invalidate();
            }
        }

        async Task RefreshCsdkButtonStateAsync()
        {
            if (csdkStateProbeActive || launchCsdkButton.IsDisposed || form.IsDisposed)
            {
                return;
            }

            csdkStateProbeActive = true;
            try
            {
                var running = await Task.Run(() => CsdkProcessService.IsRunning(new DeadlimitPaths()));
                if (launchCsdkButton.IsDisposed || form.IsDisposed)
                {
                    return;
                }

                csdkIsRunning = running;
                ApplyCsdkButtonState();
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException
                or ArgumentException)
            {
                // Process observation is best-effort. Keep the last known button state.
            }
            finally
            {
                csdkStateProbeActive = false;
            }
        }

        async Task RefreshCsdkAfterLaunchAsync()
        {
            await Task.Delay(600);
            await RefreshCsdkButtonStateAsync();
        }

        bool ActivateRunningCsdk(DeadlimitPaths paths)
        {
            try
            {
                return CsdkProcessService.TryActivateRunningWindow(paths);
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or InvalidOperationException
                or System.ComponentModel.Win32Exception
                or NotSupportedException
                or ArgumentException)
            {
                return false;
            }
        }

        prepareButton.Click += async (_, _) =>
        {
            if (prepareCancellation is not null)
            {
                RequestCancellation(
                    prepareButton,
                    prepareCancellation,
                    UiText.T("CANCELLING PREPARATION...", "ОТМЕНА ПОДГОТОВКИ..."));
                return;
            }

            using var cancellation = new CancellationTokenSource();
            prepareCancellation = cancellation;
            try
            {
                await RunPrepareAsync(
                    form,
                    actionButtons,
                    prepareButton,
                    buildProgressBar,
                    cancellation.Token);
            }
            finally
            {
                prepareCancellation = null;
                CloseAfterActiveOperationIfRequested();
            }
        };
        buildAndTestButton.Click += async (_, _) =>
        {
            if (buildCancellation is not null)
            {
                RequestCancellation(
                    buildAndTestButton,
                    buildCancellation,
                    UiText.T("CANCELLING BUILD...", "ОТМЕНА СБОРКИ..."));
                return;
            }

            using var cancellation = new CancellationTokenSource();
            buildCancellation = cancellation;
            try
            {
                await RunBuildAndTestAsync(
                    form,
                    actionButtons,
                    buildAndTestButton,
                    buildProgressBar,
                    cancellation.Token);
            }
            finally
            {
                buildCancellation = null;
                CloseAfterActiveOperationIfRequested();
            }
        };
        launchCsdkButton.Click += async (_, _) =>
        {
            var paths = new DeadlimitPaths();
            if ((Control.ModifierKeys & Keys.Shift) != Keys.Shift)
            {
                var running = csdkIsRunning || await Task.Run(() => CsdkProcessService.IsRunning(paths));
                if (running)
                {
                    csdkIsRunning = true;
                    ApplyCsdkButtonState();
                    if (!ActivateRunningCsdk(paths))
                    {
                        MessageBox.Show(
                            form,
                            UiText.T(
                                "CSDK is running, but Deadlimit Manager could not find a visible CSDK window to activate yet.",
                                "CSDK запущен, но Deadlimit Manager пока не смог найти видимое окно CSDK для переключения."),
                            UiText.T("CSDK window not found", "Окно CSDK не найдено"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                    return;
                }

                LaunchCsdk(form);
                _ = RefreshCsdkAfterLaunchAsync();
                return;
            }

            if (await OnlinePreparationFeature.ToggleFromLaunchButtonAsync())
            {
                var running = csdkIsRunning || await Task.Run(() => CsdkProcessService.IsRunning(paths));
                if (running)
                {
                    csdkIsRunning = true;
                    _ = ActivateRunningCsdk(paths);
                }
                else
                {
                    LaunchCsdk(form);
                    _ = RefreshCsdkAfterLaunchAsync();
                }
            }
            else
            {
                _ = RefreshCsdkButtonStateAsync();
            }
        };

        csdkStateTimer.Tick += (_, _) => _ = RefreshCsdkButtonStateAsync();
        form.Shown += (_, _) =>
        {
            csdkStateTimer.Start();
            _ = RefreshCsdkButtonStateAsync();
        };
        form.Activated += (_, _) => _ = RefreshCsdkButtonStateAsync();
        form.FormClosing += (_, args) =>
        {
            if (prepareCancellation is null && buildCancellation is null)
            {
                return;
            }

            closeRequested = true;
            args.Cancel = true;
            if (prepareCancellation is not null)
            {
                RequestCancellation(
                    prepareButton,
                    prepareCancellation,
                    UiText.T("CANCELLING PREPARATION...", "ОТМЕНА ПОДГОТОВКИ..."));
            }
            if (buildCancellation is not null)
            {
                RequestCancellation(
                    buildAndTestButton,
                    buildCancellation,
                    UiText.T("CANCELLING BUILD...", "ОТМЕНА СБОРКИ..."));
            }
        };
        form.FormClosed += (_, _) =>
        {
            csdkStateTimer.Stop();
            csdkStateTimer.Dispose();
            toolTip.Dispose();
        };

        topBar.Controls.Add(prepareButton);
        topBar.Controls.Add(buildAndTestButton);
        topBar.Controls.Add(launchCsdkButton);
        GameLaunchInterlockFeature.Attach(form);
    }

    private static async Task RunPrepareAsync(
        MainForm form,
        IReadOnlyList<Button> actionButtons,
        Button prepareButton,
        ToolStripProgressBar? progressBar,
        CancellationToken cancellationToken)
    {
        var cleanPrepare = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
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

        using var mutation = TryBeginMutation(form, "PREPARE FOR CSDK");
        if (mutation is null)
        {
            return;
        }

        var options = PrepareAuthoringOptions.PreserveArtistWork;
        if (cleanPrepare)
        {
            var selected = CleanPrepareDialog.Choose(form);
            if (selected is null)
            {
                return;
            }
            options = selected;
        }

        BeginCancelableOperation(
            actionButtons,
            prepareButton,
            UiText.T("CANCEL PREPARATION", "ОТМЕНИТЬ ПОДГОТОВКУ"));
        using var animator = new BuildProgressAnimator(
            form,
            progressBar,
            UiText.T("Starting preparation for CSDK...", "Запуск подготовки для CSDK..."));

        try
        {
            animator.Start();
            var progress = new Progress<PrepareAuthoringProgress>(update =>
                animator.Update(new BuildAndTestProgress(
                    update.Message,
                    MapStandalonePrepareProgress(update.Message))));

            var service = new PrepareAuthoringService(new DeadlimitPaths());
            var result = await RunWithTextureTargetPromptsAsync(
                form,
                manifest,
                () => service.PrepareAsync(
                    manifest,
                    progress,
                    cancellationToken,
                    options: options));
            if (result is null)
            {
                return;
            }
            MarkOperationCompleting(
                prepareButton,
                UiText.T("PREPARATION COMPLETE", "ПОДГОТОВКА ЗАВЕРШЕНА"));
            animator.Update(new BuildAndTestProgress(
                UiText.T("Preparation for CSDK complete.", "Подготовка для CSDK готова."),
                100));

            var gameState = result.GameOutputCleaned
                ? UiText.T("Existing compiled output for this addon was removed.", "Старый compiled output этого аддона удалён.")
                : UiText.T("Existing compiled output was preserved for incremental builds.", "Существующий compiled output сохранён для инкрементальных сборок.");

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

            var heroSelectSceneSummary = result.HeroSelectScene is null
                ? string.Empty
                : UiText.T(
                    $"Hero-select prefab: {result.HeroSelectScene.HeroPrefabId}\n" +
                    $"Hero-select VMAP created: {result.HeroSelectScene.CreatedCount}\n" +
                    $"Hero-select VMAP preserved: {result.HeroSelectScene.PreservedCount}\n" +
                    $"Hero-select authoring resources created: {result.HeroSelectScene.AuthoringResourceCreatedCount}\n" +
                    $"Hero-select authoring resources preserved: {result.HeroSelectScene.AuthoringResourcePreservedCount}\n" +
                    $"Hero-select runtime resources created: {result.HeroSelectScene.RuntimeCreatedCount}\n" +
                    $"Hero-select runtime resources preserved: {result.HeroSelectScene.RuntimePreservedCount}\n" +
                    $"Hero-select scene:\n{string.Join("\n", result.HeroSelectScene.ScenePaths)}\n\n",
                    $"Префаб сцены выбора героя: {result.HeroSelectScene.HeroPrefabId}\n" +
                    $"Создано VMAP сцены выбора: {result.HeroSelectScene.CreatedCount}\n" +
                    $"Сохранено существующих VMAP: {result.HeroSelectScene.PreservedCount}\n" +
                    $"Создано authoring-ресурсов сцены: {result.HeroSelectScene.AuthoringResourceCreatedCount}\n" +
                    $"Сохранено существующих authoring-ресурсов: {result.HeroSelectScene.AuthoringResourcePreservedCount}\n" +
                    $"Создано runtime-ресурсов сцены: {result.HeroSelectScene.RuntimeCreatedCount}\n" +
                    $"Сохранено существующих runtime-ресурсов: {result.HeroSelectScene.RuntimePreservedCount}\n" +
                    $"Сцена выбора героя:\n{string.Join("\n", result.HeroSelectScene.ScenePaths)}\n\n");

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
                heroSelectSceneSummary +
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
                heroSelectSceneSummary +
                $"Файлов из игрового клиента Deadlock скопировано: {result.RetailSourceFilesCopied}\n\n" +
                $"CSDK content:\n{result.AddonContentRoot}\n\n" +
                $"Исходник модели:\n{result.SourceVmdlPath}\n\n" +
                $"CSDK game output: CLEAN. {gameState}\n" +
                $"Deadlimit Manager его не компилировал; для работы с моделью и материалами используйте ЗАПУСК CSDK, а для компиляции и установки VPK игрового клиента Deadlock — СОБРАТЬ ДЛЯ ТЕСТА. Игру запускайте отдельно, когда будете готовы.\n\n" +
                $"Лог: {result.LogPath}");

            using var dialog = BuildTestSuccessDialog.CreatePrepareSummary(message);
            dialog.ShowDialog(form);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            animator.Update(new BuildAndTestProgress(
                UiText.T("Preparation cancelled.", "Подготовка отменена."),
                0));
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
            EndCancelableOperation(
                actionButtons,
                prepareButton,
                UiText.T("PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK"));
        }
    }

    private static async Task RunBuildAndTestAsync(
        MainForm form,
        IReadOnlyList<Button> actionButtons,
        Button buildAndTestButton,
        ToolStripProgressBar? progressBar,
        CancellationToken cancellationToken)
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

        using var mutation = TryBeginMutation(form, "BUILD FOR TEST");
        if (mutation is null)
        {
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
        var buildAttemptStartedUtc = DateTimeOffset.UtcNow;
        using var animator = new BuildProgressAnimator(form, progressBar);

        string? forceStatePath = null;
        string? forceStateBackupPath = null;

        SetBuildForTestRunning(form, true);
        try
        {
            BeginCancelableOperation(
                actionButtons,
                buildAndTestButton,
                UiText.T("CANCEL BUILD", "ОТМЕНИТЬ СБОРКУ"));
            animator.Start();
            var paths = new DeadlimitPaths();

            if (deadlockWasRunning)
            {
                animator.Update(new BuildAndTestProgress(
                    UiText.T("Closing Deadlock to unlock the current VPK...", "Закрытие Deadlock для разблокировки текущего VPK..."),
                    0));

                var stopped = await DeadlockProcessService.CloseAsync(cancellationToken);
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
            var modLoading = await Task.Run(
                () => new RetailModLoadingService(paths).EnsureEnabled(manifest),
                cancellationToken);

            animator.Update(new BuildAndTestProgress(
                UiText.T("Checking Deadlock game-client VPK release slot...", "Проверка слота VPK игрового клиента Deadlock..."),
                1));
            var slotGuard = new VpkSlotOwnershipService(paths);
            VpkSlotOwnershipCheck slotCheck;
            try
            {
                slotCheck = await Task.Run(
                    () => slotGuard.EnsureSlotAvailable(manifest),
                    cancellationToken);
            }
            catch (LegacyVpkOwnershipException legacyOwnership)
            {
                var adopt = MessageBox.Show(
                    form,
                    UiText.T(
                        $"This project has legacy BUILD FOR TEST state, but that old state cannot prove ownership of the existing retail VPK.\n\nVPK:\n{legacyOwnership.VpkPath}\n\nAdopt the current VPK as belonging to this project? Deadlimit will record its cryptographic identity before continuing and will stop if the file changes later.",
                        $"У проекта есть старое состояние BUILD FOR TEST, но оно не может доказать владение существующим retail VPK.\n\nVPK:\n{legacyOwnership.VpkPath}\n\nПринять текущий VPK как принадлежащий этому проекту? Deadlimit сначала запишет его криптографическую идентичность и остановит сборку, если файл затем изменится."),
                    UiText.T("Adopt legacy VPK slot", "Принять старый VPK-слот"),
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (adopt != DialogResult.Yes)
                {
                    return;
                }

                slotCheck = await Task.Run(
                    () => slotGuard.AdoptLegacySlot(manifest),
                    cancellationToken);
            }

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
            IReadOnlyList<string> skippedParticleSources = [];
            try
            {
                var progress = new Progress<BuildAndTestProgress>(animator.Update);
                var service = new BuildAndTestService(paths);
                try
                {
                    result = (await RunWithTextureTargetPromptsAsync(
                        form,
                        manifest,
                        () => Task.Run(
                            () => service.BuildAsync(manifest, progress, cancellationToken),
                            cancellationToken)))!;
                    if (result is null)
                    {
                        RestoreForceBuildState(forceStatePath, forceStateBackupPath);
                        return;
                    }
                }
                catch (ParticleCompilationException particleError)
                {
                    var choice = ShowParticleCompilationFallback(form, particleError);
                    if (choice != DeadlimitDialogChoice.Continue)
                    {
                        RestoreForceBuildState(forceStatePath, forceStateBackupPath);
                        return;
                    }

                    skippedParticleSources = particleError.SourcePaths;
                    animator.Update(new BuildAndTestProgress(
                        UiText.T(
                            $"Continuing build without {skippedParticleSources.Count} failed VPCF particle definition(s)...",
                            $"Продолжение сборки без проблемных VPCF-эффектов: {skippedParticleSources.Count}..."),
                        39));
                    result = await Task.Run(() =>
                        service.BuildWithoutFailedParticlesAsync(
                            manifest,
                            skippedParticleSources,
                            progress,
                            cancellationToken),
                        cancellationToken);
                }
            }
            catch
            {
                RestoreForceBuildState(forceStatePath, forceStateBackupPath);
                throw;
            }

            MarkOperationCompleting(
                buildAndTestButton,
                UiText.T("FINALIZING BUILD...", "ЗАВЕРШЕНИЕ СБОРКИ..."));
            if (forceStateBackupPath is not null && File.Exists(forceStateBackupPath))
            {
                try
                {
                    File.Delete(forceStateBackupPath);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                {
                    // The new build state and deployed VPK are already committed.
                    // A stale force-backup is harmless and must never roll state back.
                }
                finally
                {
                    forceStateBackupPath = null;
                }
            }

            if (manifest.Mode == ProjectMode.ImportedVpk)
            {
                await Task.Run(() => slotGuard.RecordSuccessfulDeployment(manifest, result.VpkPath));
            }
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
                    "\nVPK slot ownership: explicitly adopted from legacy Deadlimit build state and fingerprinted before this build.",
                    "\nVPK-слот: старое владение явно подтверждено пользователем и зафиксировано криптографически перед этой сборкой.")
                : string.Empty;
            var forceSummary = forceFullRebuild
                ? UiText.T("\nForced full rebuild: yes (SHIFT).", "\nПринудительная полная пересборка: да (SHIFT).")
                : string.Empty;
            var closedGameSummary = deadlockWasRunning
                ? UiText.T("\nDeadlock was closed automatically to unlock the VPK.", "\nDeadlock был автоматически закрыт для разблокировки VPK.")
                : string.Empty;
            var particleFallbackSummary = skippedParticleSources.Count > 0
                ? UiText.T(
                    $"\nFailed VPCF particle definitions skipped: {string.Join(", ", skippedParticleSources.Select(Path.GetFileName))}. Successful edited VPCF resources are included.",
                    $"\nПропущены проблемные VPCF-эффекты: {string.Join(", ", skippedParticleSources.Select(Path.GetFileName))}. Успешно собранные изменённые VPCF включены в сборку.")
                : string.Empty;
            var warningSummary = result.Warnings.Count == 0
                ? string.Empty
                : UiText.T(
                    "\n\n⚠ Vertex Color warning:\n" + string.Join("\n", result.Warnings.Select(warning => $"• {warning}")),
                    "\n\n⚠ Предупреждение Vertex Color:\n" + string.Join("\n", result.Warnings.Select(warning => $"• {warning}")));

            var buildMode = result.FullRebuild
                ? UiText.T("clean/full", "полная")
                : UiText.T("incremental", "инкрементальная");
            var ag2Status = result.Ag2Applied
                ? UiText.T("yes", "да")
                : UiText.T("not needed", "не требовалось");
            var summary = UiText.T(
                $"Addon: {result.AddonName}\n" +
                $"Mode: {buildMode}\n" +
                $"Compiled sources: {result.CompiledSourceCount}\n" +
                $"Stale compiled outputs removed: {result.RemovedCompiledOutputCount}\n" +
                $"AG2 restored this run: {ag2Status}",
                $"Аддон: {result.AddonName}\n" +
                $"Режим: {buildMode}\n" +
                $"Скомпилировано source-файлов: {result.CompiledSourceCount}\n" +
                $"Удалено устаревших compiled outputs: {result.RemovedCompiledOutputCount}\n" +
                $"AG2 восстановлен: {ag2Status}")
                + forceSummary
                + modLoadingSummary
                + legacySlotSummary
                + closedGameSummary
                + particleFallbackSummary
                + warningSummary;

            using var dialog = new BuildTestSuccessDialog(result.VpkPath, summary);
            dialog.ShowDialog(form);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            RestoreForceBuildState(forceStatePath, forceStateBackupPath);
            animator.Update(new BuildAndTestProgress(
                UiText.T("Build cancelled.", "Сборка отменена."),
                0));
        }
        catch (Exception ex)
        {
            RestoreForceBuildState(forceStatePath, forceStateBackupPath);
            var failureLogPath = BuildFailureLogService.EnsureCurrentFailureLog(
                manifest,
                buildAttemptStartedUtc,
                ex);
            var failureLogSummary = string.IsNullOrWhiteSpace(failureLogPath)
                ? string.Empty
                : UiText.T($"\n\nLog: {failureLogPath}", $"\n\nЛог: {failureLogPath}");
            MessageBox.Show(
                form,
                ex.Message + failureLogSummary,
                UiText.T("Build for test failed", "Ошибка сборки для теста"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            EndCancelableOperation(
                actionButtons,
                buildAndTestButton,
                UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА"));
            SetBuildForTestRunning(form, false);
        }
    }

    private static async Task<T?> RunWithTextureTargetPromptsAsync<T>(
        MainForm form,
        ProjectManifest manifest,
        Func<Task<T>> operation)
        where T : class
    {
        while (true)
        {
            try
            {
                return await operation();
            }
            catch (AmbiguousTextureTargetException ambiguity)
            {
                var choice = TextureTargetSelectionDialog.Show(form, ambiguity);
                if (!choice.Accepted || choice.ResourcePaths.Count == 0)
                {
                    return null;
                }

                if (choice.Remember)
                {
                    manifest.TextureTargetBindings[ambiguity.AuthoringIdentity] = [.. choice.ResourcePaths];
                    ProjectStore.Save(manifest);
                }
                else
                {
                    manifest.TransientTextureTargetBindings[ambiguity.AuthoringIdentity] = [.. choice.ResourcePaths];
                }
            }
        }
    }

    private static DeadlimitDialogChoice ShowParticleCompilationFallback(
        MainForm form,
        ParticleCompilationException error)
    {
        var failedFiles = error.SourcePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var fileSummary = failedFiles.Length == 0
            ? string.Empty
            : string.Join(", ", failedFiles.Take(3));
        var formatSummary = error.FormatVersions.Count == 0
            ? string.Empty
            : string.Join(", ", error.FormatVersions.Select(version => $"vpcf{version}"));

        while (true)
        {
            var details = new List<string>();
            if (fileSummary.Length > 0)
            {
                details.Add(UiText.T($"Failed VPCF: {fileSummary}", $"Не удалось собрать VPCF: {fileSummary}"));
            }
            if (formatSummary.Length > 0)
            {
                details.Add(UiText.T($"Format: {formatSummary}", $"Формат: {formatSummary}"));
            }

            var detailText = details.Count == 0
                ? string.Empty
                : "\n\n" + string.Join("\n", details);
            var choice = MessageBox.ShowCustom(
                form,
                UiText.T(
                    "The current ResourceCompiler could not compile one or more VPCF particle effects.\n\nYou can continue BUILD FOR TEST while skipping only the failed VPCF files. Successfully compiled edited VPCF resources and other project resources will be packaged. The game will reuse original Deadlock definitions only for the failed paths. Edited materials, textures and models referenced by those effects can still override the game resources." +
                    detailText +
                    "\n\nCONTINUE WITHOUT FAILED VPCF finishes the build. OPEN LOG shows the compiler output. CANCEL stops the build.",
                    "Текущий ResourceCompiler не смог скомпилировать один или несколько VPCF-эффектов.\n\nМожно продолжить СОБРАТЬ ДЛЯ ТЕСТА, пропустив только проблемные VPCF. Успешно скомпилированные изменённые VPCF и остальные ресурсы проекта попадут в VPK. Оригинальные определения Deadlock будут использоваться только для проблемных путей. Изменённые материалы, текстуры и модели, на которые ссылаются эти эффекты, всё равно смогут подменять игровые ресурсы." +
                    detailText +
                    "\n\nПРОПУСТИТЬ ПРОБЛЕМНЫЕ VPCF завершит сборку. ОТКРЫТЬ ЛОГ покажет вывод компилятора. ОТМЕНА остановит сборку."),
                UiText.T("VPCF compilation failed", "Не удалось скомпилировать VPCF"),
                new DeadlimitDialogButton(
                    UiText.T("CANCEL", "ОТМЕНА"),
                    DeadlimitDialogChoice.Cancel,
                    IsCancel: true),
                new DeadlimitDialogButton(
                    UiText.T("OPEN LOG", "ОТКРЫТЬ ЛОГ"),
                    DeadlimitDialogChoice.Retry),
                new DeadlimitDialogButton(
                    UiText.T("SKIP FAILED VPCF", "ПРОПУСТИТЬ ПРОБЛЕМНЫЕ VPCF"),
                    DeadlimitDialogChoice.Continue,
                    IsDefault: true));

            if (choice != DeadlimitDialogChoice.Retry)
            {
                return choice;
            }

            OpenParticleCompilationLog(form, error.LogPath);
        }
    }

    private static void OpenParticleCompilationLog(MainForm form, string? logPath)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
        {
            MessageBox.Show(
                form,
                UiText.T(
                    "The Build & Test log is not available yet.",
                    "Лог сборки пока недоступен."),
                UiText.T("Log not found", "Лог не найден"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = logPath,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or IOException
            or UnauthorizedAccessException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not open log", "Не удалось открыть лог"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static void SetBuildForTestRunning(MainForm form, bool running)
    {
        var changed = running
            ? ActiveBuildForms.Add(form)
            : ActiveBuildForms.Remove(form);
        if (changed)
        {
            BuildForTestStateChanged?.Invoke(form, running);
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
        if (message.StartsWith("Overlaying", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("Подготовка моделей", StringComparison.OrdinalIgnoreCase))
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
            var manifest = ProjectStore.TryLoadLastProject();
            if (manifest is not null)
            {
                HeroSelectScenePreparationService.RemoveLegacyLooseCompiledMaps(manifest, paths);
            }

            CsdkAssetWatcherCompatibility.EnsureLuaUnlockerContentMirror(paths.CsdkRoot);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = paths.CsdkLauncherPath,
                WorkingDirectory = paths.CsdkRoot,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or IOException
                                   or UnauthorizedAccessException)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not launch CSDK", "CSDK не удалось запустить"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static IDisposable? TryBeginMutation(MainForm form, string operation)
    {
        try
        {
            return ApplicationMutationCoordinator.Begin(operation);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Operation in progress", "Операция выполняется"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return null;
        }
    }

    private static void SetButtonsEnabled(IEnumerable<Button> buttons, bool enabled)
    {
        foreach (var button in buttons)
        {
            button.Enabled = enabled;
        }
    }

    private static void BeginCancelableOperation(
        IEnumerable<Button> actionButtons,
        Button activeButton,
        string cancelText)
    {
        SetButtonsEnabled(actionButtons, false);
        activeButton.Text = cancelText;
        activeButton.Enabled = true;
    }

    private static void EndCancelableOperation(
        IEnumerable<Button> actionButtons,
        Button activeButton,
        string idleText)
    {
        activeButton.Text = idleText;
        SetButtonsEnabled(actionButtons, true);
    }

    private static void RequestCancellation(
        Button activeButton,
        CancellationTokenSource cancellation,
        string cancellingText)
    {
        if (cancellation.IsCancellationRequested)
        {
            return;
        }

        activeButton.Text = cancellingText;
        activeButton.Enabled = false;
        cancellation.Cancel();
    }

    private static void MarkOperationCompleting(Button activeButton, string completingText)
    {
        activeButton.Text = completingText;
        activeButton.Enabled = false;
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
        private readonly MainForm _form;
        private readonly ToolStripProgressBar? _progressBar;

        private string _message;
        private bool _disposed;

        public BuildProgressAnimator(
            MainForm form,
            ToolStripProgressBar? progressBar,
            string? initialMessage = null)
        {
            _form = form;
            _progressBar = progressBar;
            _message = initialMessage
                ?? UiText.T("Starting build for test...", "Запуск сборки для теста...");
        }

        public void Start()
        {
            if (_progressBar is not null)
            {
                _progressBar.Value = 0;
                _progressBar.Visible = true;
            }

            Render();
        }

        public void Update(BuildAndTestProgress update)
        {
            if (_disposed)
            {
                return;
            }

            _message = update.Message;

            if (_progressBar is not null)
            {
                _progressBar.Value = Math.Clamp(update.Percent, 0, 100);
            }

            Render();
        }

        private void Render()
        {
            if (_disposed)
            {
                return;
            }

            WindowProgressFeature.ReportStatus(_form, _message);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_progressBar is not null)
            {
                _progressBar.Visible = false;
                _progressBar.Value = 0;
            }
        }
    }
}
