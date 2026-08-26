namespace Deadlimit.Core;

public static class CsdkAssetCacheToolService
{
    private const string RepositoryFolderName = "csdk-fast-startup-cache";
    private const string CommandFileName = "Fix CSDK Fast Startup.cmd";
    private const string PowerShellFileName = "Fix-CsdkFastStartup.ps1";
    private const string ReadmeFileName = "README.md";

    public static string GetBundledToolFolder()
    {
        string? repositoryCandidate = null;
        for (var current = new DirectoryInfo(AppContext.BaseDirectory);
             current is not null;
             current = current.Parent)
        {
            var candidate = Path.Combine(
                current.FullName,
                ".deadlimit",
                RepositoryFolderName);
            if (File.Exists(Path.Combine(candidate, CommandFileName))
                && File.Exists(Path.Combine(candidate, PowerShellFileName))
                && File.Exists(Path.Combine(candidate, ReadmeFileName)))
            {
                repositoryCandidate = candidate;
            }
        }

        if (repositoryCandidate is not null)
        {
            HideMetadataFolder(Path.GetDirectoryName(repositoryCandidate)!);
            return repositoryCandidate;
        }

        throw new DirectoryNotFoundException(
            $"Bundled CSDK cache tool folder '.deadlimit\\{RepositoryFolderName}' was not found beside Deadlimit.");
    }

    public static string GetBundledCommandPath() =>
        Path.Combine(GetBundledToolFolder(), CommandFileName);

    private static void HideMetadataFolder(string metadataFolder)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(metadataFolder))
        {
            return;
        }

        var attributes = File.GetAttributes(metadataFolder);
        File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
    }
}
