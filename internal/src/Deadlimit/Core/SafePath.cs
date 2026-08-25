namespace Deadlimit.Core;

internal static class SafePath
{
    public static string ResolveUnderRoot(string root, string relativePath, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"{description} must be relative: '{relativePath}'.");
        }

        var fullRoot = NormalizeRoot(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
        EnsureUnderRoot(fullRoot, candidate, description);
        return candidate;
    }

    public static string EnsureUnderRoot(string root, string candidate, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);

        var fullRoot = NormalizeRoot(root);
        var fullCandidate = Path.GetFullPath(candidate);
        var rootWithSeparator = fullRoot + Path.DirectorySeparatorChar;
        if (!string.Equals(fullCandidate, fullRoot, StringComparison.OrdinalIgnoreCase)
            && !fullCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"{description} escapes its allowed folder.\nAllowed: {fullRoot}\nResolved: {fullCandidate}");
        }

        return fullCandidate;
    }

    public static string NormalizeRelative(string value, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Replace('\\', '/');
        if (normalized.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(value))
        {
            throw new InvalidDataException($"{description} must be relative: '{value}'.");
        }
        _ = ResolveUnderRoot(Path.GetTempPath(), normalized.Replace('/', Path.DirectorySeparatorChar), description);
        return normalized;
    }

    private static string NormalizeRoot(string root)
    {
        var full = Path.GetFullPath(root);
        var pathRoot = Path.GetPathRoot(full);
        return string.Equals(full, pathRoot, StringComparison.OrdinalIgnoreCase)
            ? full
            : full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
