using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public static class DeadlockInstallLocator
{
    private static readonly Regex SteamLibraryPathRegex = new(
        "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string? FindInstallation()
    {
        foreach (var candidate in EnumerateCandidates())
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(Path.Combine(fullPath, "game", "citadel")))
                {
                    return fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException
                                               or IOException
                                               or UnauthorizedAccessException)
            {
                // Ignore inaccessible or malformed candidates and continue searching.
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateCandidates()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var steamRoot in EnumerateSteamRoots())
        {
            foreach (var libraryRoot in EnumerateSteamLibraries(steamRoot))
            {
                var candidate = Path.Combine(libraryRoot, "steamapps", "common", "Project8Staging");
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }

        foreach (var drive in EnumerateFixedDriveRoots())
        {
            foreach (var relativeSteamRoot in CommonSteamRoots)
            {
                var candidate = Path.Combine(drive, relativeSteamRoot, "steamapps", "common", "Project8Staging");
                if (seen.Add(candidate))
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var registryRoot in EnumerateRegistrySteamRoots())
        {
            if (seen.Add(registryRoot))
            {
                yield return registryRoot;
            }
        }

        foreach (var specialFolder in new[]
                 {
                     Environment.SpecialFolder.ProgramFilesX86,
                     Environment.SpecialFolder.ProgramFiles,
                 })
        {
            var programFiles = Environment.GetFolderPath(specialFolder);
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                continue;
            }

            var steamRoot = Path.Combine(programFiles, "Steam");
            if (seen.Add(steamRoot))
            {
                yield return steamRoot;
            }
        }
    }

    private static IEnumerable<string> EnumerateRegistrySteamRoots()
    {
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                RegistryKey? baseKey = null;
                RegistryKey? steamKey = null;
                try
                {
                    baseKey = RegistryKey.OpenBaseKey(hive, view);
                    steamKey = baseKey.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    var path = steamKey?.GetValue("SteamPath") as string
                               ?? steamKey?.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        yield return path.Trim();
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException
                                                   or IOException
                                                   or System.Security.SecurityException)
                {
                    // Registry access is optional; fall back to common locations.
                }
                finally
                {
                    steamKey?.Dispose();
                    baseKey?.Dispose();
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSteamLibraries(string steamRoot)
    {
        yield return steamRoot;

        var vdfPath = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            yield break;
        }

        string text;
        try
        {
            text = File.ReadAllText(vdfPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (Match match in SteamLibraryPathRegex.Matches(text))
        {
            var raw = match.Groups["path"].Value;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            yield return raw.Replace("\\\\", "\\", StringComparison.Ordinal).Trim();
        }
    }

    private static IEnumerable<string> EnumerateFixedDriveRoots()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            bool usable;
            try
            {
                usable = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                usable = false;
            }

            if (usable)
            {
                yield return drive.RootDirectory.FullName;
            }
        }
    }

    private static readonly string[] CommonSteamRoots =
    [
        @"Program Files (x86)\Steam",
        @"Program Files\Steam",
        @"SteamLibrary",
        @"Steam",
        @"Games\SteamLibrary",
        @"Games\Steam",
    ];
}
