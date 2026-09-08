namespace Deadlimit.App;

/// <summary>
/// Marks child processes started by Deadlimit Manager so the updater can tell an
/// in-app update from a manually launched updater shortcut. The marker is process-
/// local: Explorer-launched updater shortcuts do not inherit it.
/// </summary>
internal static class UpdaterLaunchOriginFeature
{
    internal const string RelaunchEnvironmentVariable = "DEADLIMIT_UPDATE_RELAUNCH";

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void MarkManagerProcess()
    {
        Environment.SetEnvironmentVariable(
            RelaunchEnvironmentVariable,
            "1",
            EnvironmentVariableTarget.Process);
    }
}
