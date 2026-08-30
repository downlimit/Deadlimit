namespace Deadlimit.Core;

internal static class DeadlockToolsLayoutMigration
{
    private const string MarkerFileName = ".deadlimit-deadlocktools.json";

    public static bool TryMigrateManagedRelease(string root)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            return false;
        }

        var fullRoot = Path.GetFullPath(root.Trim());
        var flatExecutable = Path.Combine(fullRoot, "DeadlockTools.exe");
        if (File.Exists(flatExecutable))
        {
            return false;
        }

        var marker = Path.Combine(fullRoot, MarkerFileName);
        var legacyTop = Path.Combine(fullRoot, "DeadlockTools");
        var legacyOutput = Path.Combine(legacyTop, "bin", "Release", "net10.0");
        var legacyExecutable = Path.Combine(legacyOutput, "DeadlockTools.exe");
        if (!File.Exists(marker)
            || Directory.Exists(Path.Combine(fullRoot, ".git"))
            || !File.Exists(legacyExecutable))
        {
            return false;
        }

        // The old Deadlimit-managed release layout owned only the marker plus the
        // nested DeadlockTools output tree. Do not flatten arbitrary user content.
        var unexpected = Directory.EnumerateFileSystemEntries(fullRoot)
            .Where(path => !string.Equals(path, marker, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(path, legacyTop, StringComparison.OrdinalIgnoreCase))
            .Any();
        if (unexpected)
        {
            return false;
        }

        try
        {
            foreach (var source in Directory.EnumerateFiles(legacyOutput, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(fullRoot, Path.GetRelativePath(legacyOutput, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination, overwrite: true);
            }

            if (!File.Exists(flatExecutable))
            {
                return false;
            }

            Directory.Delete(legacyTop, recursive: true);
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException
                                           or ArgumentException
                                           or NotSupportedException)
        {
            // Migration is best-effort. The legacy executable remains usable if
            // copying or cleanup cannot be completed on this machine.
            return false;
        }
    }
}
