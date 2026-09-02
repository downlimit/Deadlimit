$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = Get-Content -LiteralPath $Path -Raw
    if (-not $text.Contains($Old)) {
        throw "Expected source block was not found in $Path"
    }
    Set-Content -LiteralPath $Path -Value ($text.Replace($Old, $New)) -Encoding utf8NoBOM -NoNewline
}

$path = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'

Replace-Exact $path @'
    private static readonly Color GameGradientStart = Color.FromArgb(0x4C, 0xC7, 0x31);
    private static readonly Color GameGradientEnd = Color.FromArgb(0x13, 0xA5, 0x44);
'@ @'
    private static readonly Color GameGradientStart = Color.FromArgb(0x4C, 0xC7, 0x31);
    private static readonly Color GameGradientEnd = Color.FromArgb(0x13, 0xA5, 0x44);
    private static readonly Color GameActiveGradientStart = Color.FromArgb(0x39, 0x9A, 0xED);
    private static readonly Color GameActiveGradientEnd = Color.FromArgb(0x24, 0x5E, 0xCF);
'@

Replace-Exact $path @'
        launchGameButton.Click += (_, _) =>
        {
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                TryCopyCameraLockCommand(form);
                return;
            }

            if (LaunchDeadlock(form))
            {
                OnlinePreparationFeature.StopForGameLaunch();
            }
        };
'@ @'
        var gameLaunchPendingUntilUtc = DateTime.MinValue;
        var gameButtonUsesActivePalette = DeadlockProcessService.IsRunning();
        var gameStateTimer = new System.Windows.Forms.Timer();

        launchGameButton.Click += async (_, _) =>
        {
            if ((Control.ModifierKeys & Keys.Shift) == Keys.Shift)
            {
                TryCopyCameraLockCommand(form);
                return;
            }

            if (DeadlockProcessService.IsRunning())
            {
                launchGameButton.Enabled = false;
                try
                {
                    var closed = await DeadlockProcessService.CloseAsync();
                    if (!closed)
                    {
                        MessageBox.Show(
                            form,
                            UiText.T(
                                "Deadlock did not close within the expected time.",
                                "Deadlock не закрылся за ожидаемое время."),
                            UiText.T("Could not close Deadlock", "Не удалось закрыть Deadlock"),
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
                finally
                {
                    gameLaunchPendingUntilUtc = DateTime.MinValue;
                    launchGameButton.Enabled = true;
                    RefreshGameButtonState();
                }
                return;
            }

            gameLaunchPendingUntilUtc = DateTime.UtcNow.AddSeconds(15);
            gameButtonUsesActivePalette = true;
            gameStateTimer.Interval = 250;
            launchGameButton.Invalidate();

            if (LaunchDeadlock(form))
            {
                OnlinePreparationFeature.StopForGameLaunch();
            }
            else
            {
                gameLaunchPendingUntilUtc = DateTime.MinValue;
                RefreshGameButtonState();
            }
        };
'@

Replace-Exact $path @'
        toolTip.SetToolTip(
            launchGameButton,
            UiText.T(
                $"Launch Deadlock game client through Steam.\n\nHold SHIFT while clicking to copy '{CameraLockCommand}' to the clipboard without launching the game.",
                $"Запустить Deadlock через Steam.\n\nУдерживайте SHIFT при клике, чтобы скопировать '{CameraLockCommand}' в буфер обмена без запуска игры."));

        void PositionControls()
'@ @'
        toolTip.SetToolTip(
            launchGameButton,
            UiText.T(
                $"Launch Deadlock game client through Steam.\n\nHold SHIFT while clicking to copy '{CameraLockCommand}' to the clipboard without launching the game.",
                $"Запустить Deadlock через Steam.\n\nУдерживайте SHIFT при клике, чтобы скопировать '{CameraLockCommand}' в буфер обмена без запуска игры."));

        void RefreshGameButtonState()
        {
            if (launchGameButton.IsDisposed)
            {
                return;
            }

            var running = DeadlockProcessService.IsRunning();
            var launchPending = !running && DateTime.UtcNow < gameLaunchPendingUntilUtc;
            if (!launchPending)
            {
                gameLaunchPendingUntilUtc = DateTime.MinValue;
            }

            gameButtonUsesActivePalette = running || launchPending;
            launchGameButton.Text = running
                ? UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")
                : UiText.T("▶  LAUNCH GAME", "▶  ЗАПУСК ИГРЫ");

            gameStateTimer.Interval = running
                ? 1000
                : launchPending
                    ? 250
                    : 2000;

            toolTip.SetToolTip(
                launchGameButton,
                running
                    ? UiText.T(
                        "Deadlock is running. Click to close the game.\n\nHold SHIFT while clicking to copy the camera-lock command instead.",
                        "Deadlock запущен. Нажмите, чтобы закрыть игру.\n\nУдерживайте SHIFT при клике, чтобы вместо этого скопировать команду блокировки камеры.")
                    : launchPending
                        ? UiText.T(
                            "The launch request was sent to Steam. Deadlimit is waiting for the Deadlock process to appear.",
                            "Запрос на запуск отправлен Steam. Deadlimit ждёт появления процесса Deadlock.")
                        : UiText.T(
                            $"Launch Deadlock game client through Steam.\n\nHold SHIFT while clicking to copy '{CameraLockCommand}' to the clipboard without launching the game.",
                            $"Запустить Deadlock через Steam.\n\nУдерживайте SHIFT при клике, чтобы скопировать '{CameraLockCommand}' в буфер обмена без запуска игры."));

            launchGameButton.Invalidate();
        }

        gameStateTimer.Tick += (_, _) => RefreshGameButtonState();
        form.FormClosed += (_, _) =>
        {
            gameStateTimer.Stop();
            gameStateTimer.Dispose();
        };

        void PositionControls()
'@

Replace-Exact $path @'
        form.Shown += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
            ConfigureGradientButton(launchCsdkButton, CsdkGradientStart, CsdkGradientEnd);
            ConfigureGradientButton(launchGameButton, GameGradientStart, GameGradientEnd);
        };
'@ @'
        form.Shown += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
            ConfigureGradientButton(launchCsdkButton, CsdkGradientStart, CsdkGradientEnd);
            ConfigureGradientButton(
                launchGameButton,
                () => gameButtonUsesActivePalette
                    ? (GameActiveGradientStart, GameActiveGradientEnd)
                    : (GameGradientStart, GameGradientEnd));
            RefreshGameButtonState();
            gameStateTimer.Start();
        };
'@

$oldGradient = @'
    private static void ConfigureGradientButton(Button button, Color gradientStart, Color gradientEnd)
    {
        var hovered = false;
        var pressed = false;

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.ForeColor = Color.White;
        button.BackColor = gradientStart;
        button.TabStop = false;

        button.Paint += (_, e) =>
        {
            button.FlatAppearance.BorderSize = 0;

            var start = gradientStart;
            var end = gradientEnd;
            if (hovered)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.05, valueFactor: 1.10);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.05, valueFactor: 1.10);
            }
            if (pressed)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.0, valueFactor: 0.90);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.0, valueFactor: 0.90);
            }

            using var brush = new LinearGradientBrush(button.ClientRectangle, start, end, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, button.ClientRectangle);
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        button.MouseEnter += (_, _) =>
        {
            hovered = true;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
        button.MouseLeave += (_, _) =>
        {
            hovered = false;
            pressed = false;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                button.FlatAppearance.BorderSize = 0;
                button.Invalidate();
            }
        };
        button.MouseUp += (_, _) =>
        {
            pressed = false;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
    }
'@
$newGradient = @'
    private static void ConfigureGradientButton(Button button, Color gradientStart, Color gradientEnd) =>
        ConfigureGradientButton(button, () => (gradientStart, gradientEnd));

    private static void ConfigureGradientButton(
        Button button,
        Func<(Color Start, Color End)> gradientProvider)
    {
        var hovered = false;
        var pressed = false;
        var initialGradient = gradientProvider();

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.ForeColor = Color.White;
        button.BackColor = initialGradient.Start;
        button.TabStop = false;

        button.Paint += (_, e) =>
        {
            button.FlatAppearance.BorderSize = 0;

            var (start, end) = gradientProvider();
            if (hovered)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.05, valueFactor: 1.10);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.05, valueFactor: 1.10);
            }
            if (pressed)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.0, valueFactor: 0.90);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.0, valueFactor: 0.90);
            }

            using var brush = new LinearGradientBrush(button.ClientRectangle, start, end, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, button.ClientRectangle);
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        button.MouseEnter += (_, _) =>
        {
            hovered = true;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
        button.MouseLeave += (_, _) =>
        {
            hovered = false;
            pressed = false;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                button.FlatAppearance.BorderSize = 0;
                button.Invalidate();
            }
        };
        button.MouseUp += (_, _) =>
        {
            pressed = false;
            button.FlatAppearance.BorderSize = 0;
            button.Invalidate();
        };
    }
'@
Replace-Exact $path $oldGradient $newGradient

$smokePath = 'internal/tests/launch-game-fastpath-smoke.ps1'
$smoke = Get-Content -LiteralPath $smokePath -Raw
$needle = @'
    'Arguments = $"-applaunch {DeadlockSteamAppId}"'
'@
$replacement = @'
    'Arguments = $"-applaunch {DeadlockSteamAppId}"',
    'GameActiveGradientStart = Color.FromArgb(0x39, 0x9A, 0xED)',
    'GameActiveGradientEnd = Color.FromArgb(0x24, 0x5E, 0xCF)',
    'DeadlockProcessService.IsRunning()',
    'await DeadlockProcessService.CloseAsync()',
    'UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")',
    'DateTime.UtcNow.AddSeconds(15)',
    '? 1000',
    '? 250',
    ': 2000'
'@
if (-not $smoke.Contains($needle)) {
    throw 'Launch-game smoke insertion point was not found.'
}
Set-Content -LiteralPath $smokePath -Value ($smoke.Replace($needle, $replacement)) -Encoding utf8NoBOM -NoNewline
