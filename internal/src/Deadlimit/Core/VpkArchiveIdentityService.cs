using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed record VpkArchiveComparison(bool Matches, string Reason);

public static class VpkArchiveIdentityService
{
    public static VpkArchiveComparison CompareToSnapshot(
        string dirVpkPath,
        OriginalVpkSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!File.Exists(dirVpkPath))
        {
            return new VpkArchiveComparison(false, $"VPK does not exist: {dirVpkPath}");
        }

        try
        {
            using var package = new Package();
            package.Read(dirVpkPath);
            var packageEntries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {dirVpkPath}");
            var entries = packageEntries
                .SelectMany(group => group.Value)
                .Select(entry => (Entry: entry, Path: NormalizeVpkPath(entry.GetFullPath())))
                .ToArray();

            if (entries.Length != snapshot.SourceEntryCount)
            {
                return new VpkArchiveComparison(
                    false,
                    $"Entry count differs: expected {snapshot.SourceEntryCount}, found {entries.Length}.");
            }

            var expectedByPath = snapshot.Entries
                .ToDictionary(entry => entry.InternalPath, StringComparer.Ordinal);
            var actualPaths = entries
                .Select(item => item.Path)
                .ToHashSet(StringComparer.Ordinal);
            var missing = expectedByPath.Keys.Where(path => !actualPaths.Contains(path)).Take(4).ToArray();
            var extra = actualPaths.Where(path => !expectedByPath.ContainsKey(path)).Take(4).ToArray();
            if (missing.Length > 0 || extra.Length > 0)
            {
                var details = new List<string>();
                if (missing.Length > 0)
                {
                    details.Add("missing: " + string.Join(", ", missing));
                }
                if (extra.Length > 0)
                {
                    details.Add("extra: " + string.Join(", ", extra));
                }
                return new VpkArchiveComparison(false, "Internal path set differs (" + string.Join("; ", details) + ").");
            }

            foreach (var item in entries)
            {
                package.ReadEntry(item.Entry, out byte[] rawData);
                var actualHash = Convert.ToHexString(SHA256.HashData(rawData)).ToLowerInvariant();
                var expected = expectedByPath[item.Path];
                if (!string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase)
                    || rawData.LongLength != expected.Size)
                {
                    return new VpkArchiveComparison(false, $"Entry bytes differ: {item.Path}");
                }
            }

            return new VpkArchiveComparison(true, "Archive entry paths and bytes match the imported source snapshot.");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return new VpkArchiveComparison(false, $"VPK could not be compared safely: {exception.Message}");
        }
    }

    public static string ComputeFamilySha256(string dirVpkPath)
    {
        var family = EnumerateFamily(dirVpkPath);
        if (!family.Contains(Path.GetFullPath(dirVpkPath), StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("VPK directory archive was not found.", dirVpkPath);
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in family)
        {
            var fileName = Path.GetFileName(file).ToLowerInvariant();
            hash.AppendData(Encoding.UTF8.GetBytes(fileName));
            hash.AppendData([0]);

            using var stream = new FileStream(
                file,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 1024 * 128,
                options: FileOptions.SequentialScan);
            var buffer = new byte[1024 * 128];
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                hash.AppendData(buffer, 0, read);
            }
            hash.AppendData([0]);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    public static IReadOnlyList<string> EnumerateFamily(string dirVpkPath)
    {
        var fullDirVpkPath = Path.GetFullPath(dirVpkPath);
        var directory = Path.GetDirectoryName(fullDirVpkPath)!;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var baseName = GetVpkBaseName(fullDirVpkPath);
        var chunkRegex = new Regex(
            $"^{Regex.Escape(baseName)}_\\d{{3}}\\.vpk$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var result = Directory.EnumerateFiles(directory, $"{baseName}_*.vpk", SearchOption.TopDirectoryOnly)
            .Where(path => chunkRegex.IsMatch(Path.GetFileName(path)))
            .Select(Path.GetFullPath)
            .ToList();
        if (File.Exists(fullDirVpkPath))
        {
            result.Add(fullDirVpkPath);
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetVpkBaseName(string dirVpkPath)
    {
        var fileName = Path.GetFileName(dirVpkPath);
        const string suffix = "_dir.vpk";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static string NormalizeVpkPath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "VPK internal path");
}
