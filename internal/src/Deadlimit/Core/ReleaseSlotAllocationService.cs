namespace Deadlimit.Core;

internal static class ReleaseSlotAllocationService
{
    public static string AllocateFirstFree(string projectsRoot, DeadlimitPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (string.IsNullOrWhiteSpace(projectsRoot) || !Directory.Exists(projectsRoot))
        {
            throw new DirectoryNotFoundException(
                string.IsNullOrWhiteSpace(projectsRoot)
                    ? "Projects folder is not configured."
                    : projectsRoot);
        }

        var used = new HashSet<int>();
        foreach (var projectFolder in Directory.EnumerateDirectories(projectsRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var manifest = ProjectStore.TryLoad(projectFolder);
            if (int.TryParse(manifest?.ReleaseTarget?.Trim(), out var slot) && slot is >= 1 and <= 99)
            {
                used.Add(slot);
            }
        }

        if (!string.IsNullOrWhiteSpace(paths.RetailDeadlockRoot))
        {
            var addonsRoot = Path.Combine(paths.RetailDeadlockRoot, "game", "citadel", "addons");
            for (var slot = 1; slot <= 99; slot++)
            {
                if (File.Exists(Path.Combine(addonsRoot, $"pak{slot:D2}_dir.vpk")))
                {
                    used.Add(slot);
                }
            }
        }

        for (var slot = 1; slot <= 99; slot++)
        {
            if (!used.Contains(slot))
            {
                return slot.ToString("D2");
            }
        }

        throw new InvalidOperationException(
            "All Release IDs from 01 through 99 are already used by projects or installed VPK files.");
    }
}
