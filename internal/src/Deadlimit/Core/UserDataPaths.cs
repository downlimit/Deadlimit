namespace Deadlimit.Core;

public static class UserDataPaths
{
    private const string ProductFolderName = "Deadlimit";

    public static string Root => ResolveRoot();

    internal static string ResolveRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ProductFolderName);

    public static string Combine(params string[] paths)
    {
        var segments = new string[paths.Length + 1];
        segments[0] = Root;
        Array.Copy(paths, 0, segments, 1, paths.Length);
        return Path.Combine(segments);
    }
}
