using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed record VpkImportCandidate(
    string SourceVpkPath,
    string SourceVpkFileName,
    string SourceVpkSha256,
    string? ReleaseTarget,
    int EntryCount);

public static class VpkImportSourceValidator
{
    private static readonly Regex RetailAddonVpkRegex = new(
        @"^pak(?<slot>\d{2})_dir\.vpk$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static VpkImportCandidate Validate(string sourceVpkPath)
    {
        if (string.IsNullOrWhiteSpace(sourceVpkPath))
        {
            throw new ArgumentException("VPK path is required.", nameof(sourceVpkPath));
        }

        var fullPath = Path.GetFullPath(sourceVpkPath.Trim());
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The selected VPK file does not exist.", fullPath);
        }

        var fileName = Path.GetFileName(fullPath);
        if (!fileName.EndsWith("_dir.vpk", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Select a VPK directory archive whose filename ends with _dir.vpk, not a numbered VPK chunk.");
        }

        int entryCount;
        try
        {
            using var package = new Package();
            package.Read(fullPath);
            var entries = package.Entries
                ?? throw new InvalidDataException("The selected VPK did not expose an entry table.");
            entryCount = entries.Sum(group => group.Value.Count);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException(
                $"The selected VPK could not be read as a supported Valve VPK directory archive: {fileName}",
                exception);
        }

        if (entryCount <= 0)
        {
            throw new InvalidDataException("The selected VPK is readable but contains no archive entries.");
        }

        var releaseTarget = TryDeriveReleaseTarget(fileName);
        var sha256 = ComputeSha256(fullPath);

        return new VpkImportCandidate(
            fullPath,
            fileName,
            sha256,
            releaseTarget,
            entryCount);
    }

    public static string? TryDeriveReleaseTarget(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var match = RetailAddonVpkRegex.Match(Path.GetFileName(fileName.Trim()));
        if (!match.Success
            || !int.TryParse(match.Groups["slot"].Value, out var slot)
            || slot is < 1 or > 99)
        {
            return null;
        }

        return slot.ToString("00");
    }

    private static string ComputeSha256(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 128,
            options: FileOptions.SequentialScan);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
