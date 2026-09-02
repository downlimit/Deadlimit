$ErrorActionPreference = 'Stop'

$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
$onlinePath = 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs'
$header = Get-Content -LiteralPath $headerPath -Raw
$online = Get-Content -LiteralPath $onlinePath -Raw

$requiredHeader = @(
    'if (LaunchDeadlock(form))',
    'OnlinePreparationFeature.StopForGameLaunch();',
    'private static bool LaunchDeadlock(MainForm form)',
    'private static string? _cachedSteamExecutable;',
    '_ = Task.Run(FindSteamExecutable);',
    'Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false)',
    'Arguments = $"-applaunch {DeadlockSteamAppId}"',
    'GameActiveGradientStart = Color.FromArgb(0x39, 0x9A, 0xED)',
    'GameActiveGradientEnd = Color.FromArgb(0x24, 0x5E, 0xCF)',
    'DeadlockProcessService.IsRunning()',
    'await DeadlockProcessService.CloseAsync()',
    'UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")',
    'UiText.T("GAME IS LAUNCHING", "ИГРА ЗАПУСКАЕТСЯ")',
    'DateTime.UtcNow.AddSeconds(15)',
    '? 1000',
    '? 250',
    ': 2000'
)
foreach ($pattern in $requiredHeader) {
    if (-not $header.Contains($pattern)) {
        throw "Missing launch-game fastpath contract: $pattern"
    }
}

$requiredOnline = @(
    'internal static bool StopForGameLaunch()',
    'StopSession();',
    'return true;'
)
foreach ($pattern in $requiredOnline) {
    if (-not $online.Contains($pattern)) {
        throw "Missing online-stop contract: $pattern"
    }
}

$resolutionIndex = $header.IndexOf('var resolved = FindSteamExecutableFromRegistry()', [StringComparison]::Ordinal)
$registryIndex = $header.IndexOf('FindSteamExecutableFromRegistry()', $resolutionIndex, [StringComparison]::Ordinal)
$knownIndex = $header.IndexOf('FindSteamExecutableFromKnownLocations()', $resolutionIndex, [StringComparison]::Ordinal)
$processIndex = $header.IndexOf('FindSteamExecutableFromRunningProcess()', $resolutionIndex, [StringComparison]::Ordinal)
if ($resolutionIndex -lt 0 -or $registryIndex -lt 0 -or $knownIndex -lt 0 -or $processIndex -lt 0) {
    throw 'Steam resolution stages were not found.'
}
if (-not ($registryIndex -lt $knownIndex -and $knownIndex -lt $processIndex)) {
    throw 'Steam path lookup must prefer registry and known locations before running-process MainModule scan.'
}

Write-Host 'Launch game fastpath smoke passed.'