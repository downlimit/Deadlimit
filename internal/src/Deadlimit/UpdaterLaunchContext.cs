using System.Runtime.CompilerServices;

namespace Deadlimit;

internal static class UpdaterLaunchContext
{
    internal const string RelaunchEnvironmentVariable = "DEADLIMIT_UPDATER_RELAUNCH_MANAGER";

    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(
            RelaunchEnvironmentVariable,
            "1",
            EnvironmentVariableTarget.Process);
    }
}
