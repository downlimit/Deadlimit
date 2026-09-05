namespace Deadlimit.Core;

public static class UserDataPaths
{
    private const string ProductFolderName = "Deadlimit";
    private const string PortableFolderName = "UserData";

    public static string Root => ResolveRoot(AppContext.BaseDirectory);

    internal static string ResolveRoot(string applicationRoot)
    {
        var root = Path.GetFullPath(applicationRoot);
        if (ReleaseChannelPolicy.IsPortableReleaseRoot(root))
        {
            return Path.Combine(root, PortableFolderName);
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);
    }

    public static string Combine(params string[] paths)
    {
        var segments = new string[paths.Length + 1];
        segments[0] = Root;
        Array.Copy(paths, 0, segments, 1, paths.Length);
        return Path.Combine(segments);
    }
}
