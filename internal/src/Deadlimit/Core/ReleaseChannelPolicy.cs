namespace Deadlimit.Core;

public static class ReleaseChannelPolicy
{
    private const string ReleaseMetadataFileName = "release.json";

    public static bool IsPortableRelease => IsPortableReleaseRoot(AppContext.BaseDirectory);

    public static bool AllowsUnverifiedToolchainAutomation => true;

    internal static bool IsPortableReleaseRoot(string root) =>
        !string.IsNullOrWhiteSpace(root)
        && File.Exists(Path.Combine(Path.GetFullPath(root), ReleaseMetadataFileName));

    public static void RequireUnverifiedToolchainAutomation()
    {
        // Git checkouts, installed copies, and manually extracted copies expose
        // the same explicitly initiated toolchain actions.
    }
}
