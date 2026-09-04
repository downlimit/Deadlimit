namespace Deadlimit.Core;

public static class ReleaseChannelPolicy
{
    private const string ReleaseMetadataFileName = "release.json";

    public static bool IsPortableRelease => IsPortableReleaseRoot(AppContext.BaseDirectory);

    public static bool AllowsUnverifiedToolchainAutomation => !IsPortableRelease;

    internal static bool IsPortableReleaseRoot(string root) =>
        !string.IsNullOrWhiteSpace(root)
        && File.Exists(Path.Combine(Path.GetFullPath(root), ReleaseMetadataFileName));

    public static void RequireUnverifiedToolchainAutomation()
    {
        if (AllowsUnverifiedToolchainAutomation)
        {
            return;
        }

        throw new InvalidOperationException(
            "Automatic CSDK, DepotDownloader, and DeadlockTools installation/update is disabled in portable releases " +
            "until the upstream archives have release-pinned trusted checksums. Select an existing installation in Settings.");
    }
}
