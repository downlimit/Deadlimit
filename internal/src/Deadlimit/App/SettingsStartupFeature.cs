using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsStartupFeature
{
    internal static bool RequiresSetup(ToolPathSettings settings) =>
        string.IsNullOrWhiteSpace(settings.ProjectsRoot)
        || string.IsNullOrWhiteSpace(settings.CsdkRoot)
        || string.IsNullOrWhiteSpace(settings.DeadlockToolsRoot)
        || string.IsNullOrWhiteSpace(settings.RetailDeadlockRoot);

    internal static int RunSmoke()
    {
        var complete = new ToolPathSettings
        {
            ProjectsRoot = @"C:\DeadlimitProjects",
            CsdkRoot = @"C:\Reduced_CSDK_12",
            DeadlockToolsRoot = @"C:\DeadlockTools",
            RetailDeadlockRoot = @"D:\Project8Staging",
        };
        if (RequiresSetup(complete))
        {
            return 1;
        }

        var cases = new Action<ToolPathSettings>[]
        {
            settings => settings.ProjectsRoot = string.Empty,
            settings => settings.CsdkRoot = "   ",
            settings => settings.DeadlockToolsRoot = string.Empty,
            settings => settings.RetailDeadlockRoot = string.Empty,
        };

        foreach (var makeIncomplete in cases)
        {
            var candidate = new ToolPathSettings
            {
                ProjectsRoot = complete.ProjectsRoot,
                CsdkRoot = complete.CsdkRoot,
                DeadlockToolsRoot = complete.DeadlockToolsRoot,
                RetailDeadlockRoot = complete.RetailDeadlockRoot,
            };
            makeIncomplete(candidate);
            if (!RequiresSetup(candidate))
            {
                return 2;
            }
        }

        return 0;
    }
}
