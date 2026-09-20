$ErrorActionPreference = 'Stop'

$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
$onlinePath = 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs'
$buildPath = 'internal/src/Deadlimit/App/BuildFeature.cs'
$interlockPath = 'internal/src/Deadlimit/App/GameLaunchInterlockFeature.cs'
$header = Get-Content -LiteralPath $headerPath -Raw
$online = Get-Content -LiteralPath $onlinePath -Raw
$build = Get-Content -LiteralPath $buildPath -Raw

$requiredHeader = @(
    'if (await LaunchDeadlockAsync(form))',
    'OnlinePreparationFeature.StopForGameLaunch();',
    'private static async Task<bool> LaunchDeadlockAsync(MainForm form)',
    'private static string? _cachedSteamExecutable;',
    '_ = Task.Run(FindSteamExecutable);',
    'Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", writable: false)',
    'Arguments = $"-applaunch {DeadlockSteamAppId}"',
    'GameActiveGradientStart = Color.FromArgb(0x39, 0x9A, 0xED)',
    'GameActiveGradientEnd = Color.FromArgb(0x24, 0x5E, 0xCF)',
    'await DeadlockProcessService.IsRunningAsync()',
    'gameStateProbeActive',
    'ApplyGameButtonState();',
    'Task.Run(TryLaunchDeadlockExecutable)',
    'ProjectStore.GetToolPathSettings().RetailDeadlockRoot',
    'DeadlockInstallLocator.FindInstallation()',
    'Arguments = "-steam -console -console"',
    'Task.Run(TryLaunchDeadlockThroughSteamExecutable)',
    'await DeadlockProcessService.CloseAsync()',
    'UiText.T("✕  CLOSE", "✕  ЗАКРЫТЬ")',
    'UiText.T("GAME IS LAUNCHING", "ИГРА ЗАПУСКАЕТСЯ")',
    'GameLaunchPendingTimeout = TimeSpan.FromMinutes(2)',
    'DateTime.UtcNow + GameLaunchPendingTimeout',
    '? 1000',
    '? 250',
    ': 2000'
)
foreach ($pattern in $requiredHeader) {
    if (-not $header.Contains($pattern)) {
        throw "Missing launch-game fastpath contract: $pattern"
    }
}
if ($header.Contains('DeadlockProcessService.IsRunning()')) {
    throw 'ProjectHeaderFeature must not enumerate Deadlock processes on the UI thread.'
}

$directLaunchIndex = $header.IndexOf('Task.Run(TryLaunchDeadlockExecutable)', [StringComparison]::Ordinal)
$steamLaunchIndex = $header.IndexOf('Task.Run(TryLaunchDeadlockThroughSteamExecutable)', [StringComparison]::Ordinal)
if ($directLaunchIndex -lt 0 -or $steamLaunchIndex -lt 0 -or $directLaunchIndex -ge $steamLaunchIndex) {
    throw 'Direct retail launch must run before Steam app launch so stale AppID tracking cannot swallow the request.'
}

$requiredBuildInterlock = @(
    'internal static event Action<MainForm, bool>? BuildForTestStateChanged;',
    'internal static bool IsBuildForTestRunning(MainForm form)',
    'GameLaunchInterlockFeature.Attach(form);',
    'SetBuildForTestRunning(form, true);',
    'SetBuildForTestRunning(form, false);',
    'UiText.T("CANCEL PREPARATION", "ОТМЕНИТЬ ПОДГОТОВКУ")',
    'UiText.T("CANCEL BUILD", "ОТМЕНИТЬ СБОРКУ")',
    'service.BuildAsync(manifest, progress, cancellationToken)',
    'cancellationToken),',
    'RequestCancellation('
)
foreach ($pattern in $requiredBuildInterlock) {
    if (-not $build.Contains($pattern)) {
        throw "Missing BUILD FOR TEST lifecycle contract: $pattern"
    }
}

if (-not (Test-Path -LiteralPath $interlockPath)) {
    throw 'Game launch/build interlock feature is missing.'
}
$interlock = Get-Content -LiteralPath $interlockPath -Raw
$requiredInterlock = @(
    'BuildFeature.BuildForTestStateChanged += OnBuildForTestStateChanged;',
    '_desiredLaunchEnabled = launchButton.Enabled;',
    'launchButton.EnabledChanged += OnLaunchButtonEnabledChanged;',
    '_desiredLaunchEnabled = _launchButton.Enabled;',
    'SetLaunchEnabled(_buildRunning ? false : _desiredLaunchEnabled);',
    'ProjectHeaderFeature.SetBuildForTestState(_form, running);'
)
foreach ($pattern in $requiredInterlock) {
    if (-not $interlock.Contains($pattern)) {
        throw "Missing game launch/build interlock contract: $pattern"
    }
}

$requiredBuildVisualState = @(
    'gameButtonUsesActivePalette = buildForTestRunning',
    'UiText.T("BUILDING...", "ИДЁТ СБОРКА")',
    'internal static void SetBuildForTestState(MainForm form, bool running)'
)
foreach ($pattern in $requiredBuildVisualState) {
    if (-not $header.Contains($pattern)) {
        throw "Missing BUILD FOR TEST game-button visual contract: $pattern"
    }
}

$requiredOnline = @(
    'internal static bool StopForGameLaunch()',
    '_session = null;',
    'DisposeSessionAfterGameLaunchAsync(session)',
    'await Task.Run(session.Dispose);',
    'return true;'
)
foreach ($pattern in $requiredOnline) {
    if (-not $online.Contains($pattern)) {
        throw "Missing online-stop contract: $pattern"
    }
}

$processServicePath = 'internal/src/Deadlimit/App/DeadlockProcessService.cs'
$processService = Get-Content -LiteralPath $processServicePath -Raw
$requiredProcessService = @(
    'public static Task<bool> IsRunningAsync',
    'Task.Run(IsRunning, cancellationToken)',
    'await Task.Run(GetRunningProcesses, cancellationToken)',
    'await IsRunningAsync(cancellationToken)'
)
foreach ($pattern in $requiredProcessService) {
    if (-not $processService.Contains($pattern)) {
        throw "Missing asynchronous process-state contract: $pattern"
    }
}

$accessDeniedCatch = 'catch (System.ComponentModel.Win32Exception)'
$accessDeniedCatchCount = ([regex]::Matches(
    $processService,
    [regex]::Escape($accessDeniedCatch))).Count
if ($accessDeniedCatchCount -lt 3) {
    throw 'Deadlock process probing, graceful close, and forced close must tolerate inaccessible crash-reporting process clones.'
}
if (-not $processService.Contains('return true;') -or
    -not $processService.Contains('process.Kill(entireProcessTree: true);')) {
    throw 'Inaccessible Deadlock process clones must remain running-state evidence while accessible parents are force-closed by process tree.'
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
