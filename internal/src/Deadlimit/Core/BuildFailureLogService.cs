using System.Text;

namespace Deadlimit.Core;

internal static class BuildFailureLogService
{
    internal static string? EnsureCurrentFailureLog(
        ProjectManifest manifest,
        DateTimeOffset buildAttemptStartedUtc,
        Exception exception)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            var logFolder = Path.Combine(
                ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
                "logs");
            Directory.CreateDirectory(logFolder);

            var existingCurrentLog = Directory
                .EnumerateFiles(logFolder, "build-test-*.log", SearchOption.TopDirectoryOnly)
                .Where(path => new FileInfo(path).Length > 0)
                .Where(path => File.GetLastWriteTimeUtc(path) >= buildAttemptStartedUtc.UtcDateTime.AddSeconds(-1))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (existingCurrentLog is not null)
            {
                return existingCurrentLog;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var logPath = Path.Combine(logFolder, $"build-test-{timestamp}-preflight.log");
            if (File.Exists(logPath))
            {
                logPath = Path.Combine(
                    logFolder,
                    $"build-test-{timestamp}-preflight-{Guid.NewGuid():N}.log");
            }

            var log = new StringBuilder();
            log.AppendLine($"Deadlimit Build & Test preflight — {DateTimeOffset.Now:O}");
            log.AppendLine($"Project: {manifest.ProjectName}");
            log.AppendLine($"Hero: {manifest.Hero}");
            log.AppendLine($"Release ID: {manifest.ReleaseTarget}");
            log.AppendLine();
            log.AppendLine($"RESULT: FAILED BEFORE COMPILATION — {exception}");
            File.WriteAllText(logPath, log.ToString());
            return logPath;
        }
        catch (Exception logException) when (logException is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            // Preserve the original build failure if diagnostics cannot be written.
            return null;
        }
    }
}
