$ErrorActionPreference = 'Stop'

function Replace-Exact([string]$Path, [string]$Old, [string]$New) {
    $text = Get-Content -LiteralPath $Path -Raw
    if (-not $text.Contains($Old)) {
        throw "Expected generated block was not found in $Path"
    }
    Set-Content -LiteralPath $Path -Value ($text.Replace($Old, $New)) -Encoding utf8NoBOM -NoNewline
}

$path = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'

Replace-Exact $path @'
        launchGameButton.Click += async (_, _) =>
'@ @'
        ToolTip? toolTip = null;

        launchGameButton.Click += async (_, _) =>
'@

Replace-Exact $path @'
        var toolTip = new ToolTip
'@ @'
        toolTip = new ToolTip
'@

Replace-Exact $path @'
            gameLaunchPendingUntilUtc = DateTime.UtcNow.AddSeconds(15);
            gameButtonUsesActivePalette = true;
            gameStateTimer.Interval = 250;
            launchGameButton.Invalidate();
'@ @'
            gameLaunchPendingUntilUtc = DateTime.UtcNow.AddSeconds(15);
            gameButtonUsesActivePalette = true;
            gameStateTimer.Interval = 250;
            RefreshGameButtonState();
'@

Replace-Exact $path @'
            launchGameButton.Text = running
                ? UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")
                : UiText.T("▶  LAUNCH GAME", "▶  ЗАПУСК ИГРЫ");
'@ @'
            launchGameButton.Text = running
                ? UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")
                : launchPending
                    ? UiText.T("GAME IS LAUNCHING", "ИГРА ЗАПУСКАЕТСЯ")
                    : UiText.T("▶  LAUNCH GAME", "▶  ЗАПУСК ИГРЫ");
'@

$smokePath = 'internal/tests/launch-game-fastpath-smoke.ps1'
Replace-Exact $smokePath @'
    'UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")',
'@ @'
    'UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")',
    'UiText.T("GAME IS LAUNCHING", "ИГРА ЗАПУСКАЕТСЯ")',
'@
