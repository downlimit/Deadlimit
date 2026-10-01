namespace Deadlimit.Core;

internal static class ProjectIdentityAndReleaseSlotSmoke
{
    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-project-identity-smoke-{Guid.NewGuid():N}");
        try
        {
            var projectsRoot = Path.Combine(root, "projects");
            var retailRoot = Path.Combine(root, "retail");
            var csdkRoot = Path.Combine(root, "csdk");
            Directory.CreateDirectory(projectsRoot);
            Directory.CreateDirectory(Path.Combine(retailRoot, "game", "citadel", "addons"));

            var paths = new DeadlimitPaths(new ToolPathSettings
            {
                ProjectsRoot = projectsRoot,
                RetailDeadlockRoot = retailRoot,
                CsdkRoot = csdkRoot,
                DeadlockToolsRoot = Path.Combine(root, "tools"),
            });

            File.WriteAllBytes(
                Path.Combine(retailRoot, "game", "citadel", "addons", "pak01_dir.vpk"),
                [1]);
            SaveProject(Path.Combine(projectsRoot, "Second"), "Second", "02");
            SaveProject(Path.Combine(projectsRoot, "Third"), "Third", "03");
            if (!string.Equals(
                    ReleaseSlotAllocationService.AllocateFirstFree(projectsRoot, paths),
                    "04",
                    StringComparison.Ordinal))
            {
                return 1;
            }

            var projectFolder = Path.Combine(projectsRoot, "IvyBridge");
            var original = SaveProject(projectFolder, "IvyBridge", "04");
            var identityService = new AddonIdentityService(paths);
            _ = identityService.ResolveAndClaim(original);
            var originalProjectId = original.ProjectId;

            var recreated = SaveProject(projectFolder, "IvyBridge", "04");
            recreated.AddonId = original.AddonId;
            ProjectStore.Save(recreated);
            if (string.Equals(recreated.ProjectId, originalProjectId, StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            _ = identityService.ResolveAndClaim(recreated);
            if (!string.Equals(recreated.ProjectId, originalProjectId, StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            var copied = SaveProject(Path.Combine(projectsRoot, "IvyBridge Copy"), "IvyBridge", "05");
            copied.AddonId = recreated.AddonId;
            try
            {
                _ = identityService.ResolveAndClaim(copied);
                return 4;
            }
            catch (InvalidOperationException)
            {
                // Expected: another project path cannot take over the addon.
            }

            return 0;
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Temp cleanup is not part of the assertion.
            }
        }
    }

    private static ProjectManifest SaveProject(string folder, string name, string releaseTarget)
    {
        Directory.CreateDirectory(folder);
        var manifest = new ProjectManifest
        {
            ProjectId = AddonIdentityService.CreateProjectId(),
            ProjectName = name,
            ProjectFolder = folder,
            Hero = "ivy",
            ReleaseTarget = releaseTarget,
        };
        ProjectStore.Save(manifest);
        return manifest;
    }
}
