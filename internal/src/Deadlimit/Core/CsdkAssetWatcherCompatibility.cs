namespace Deadlimit.Core;

public static class CsdkAssetWatcherCompatibility
{
    private static readonly string[] LuaUnlockerSegments =
    [
        "citadel",
        "addons",
        "luaunlocker",
    ];

    public static string? EnsureLuaUnlockerContentMirror(string csdkRoot)
    {
        if (string.IsNullOrWhiteSpace(csdkRoot))
        {
            return null;
        }

        var root = Path.GetFullPath(csdkRoot.Trim());
        var gameAddon = Combine(root, "game", LuaUnlockerSegments);
        if (!Directory.Exists(gameAddon))
        {
            return null;
        }

        var contentAddon = Combine(root, "content", LuaUnlockerSegments);
        Directory.CreateDirectory(contentAddon);
        return contentAddon;
    }

    internal static int RunSmoke()
    {
        var root = Path.Combine(Path.GetTempPath(), "Deadlimit-CsdlLuaUnlocker-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "game", "citadel", "addons", "luaunlocker"));
            var created = EnsureLuaUnlockerContentMirror(root);
            var expected = Path.Combine(root, "content", "citadel", "addons", "luaunlocker");
            if (!string.Equals(created, expected, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(expected))
            {
                return 1;
            }

            var noRuntimeRoot = Path.Combine(root, "without-runtime");
            Directory.CreateDirectory(noRuntimeRoot);
            if (EnsureLuaUnlockerContentMirror(noRuntimeRoot) is not null
                || Directory.Exists(Path.Combine(noRuntimeRoot, "content", "citadel", "addons", "luaunlocker")))
            {
                return 2;
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
            catch
            {
            }
        }
    }

    private static string Combine(string root, string first, IReadOnlyList<string> remaining)
    {
        var path = Path.Combine(root, first);
        foreach (var segment in remaining)
        {
            path = Path.Combine(path, segment);
        }

        return path;
    }
}
