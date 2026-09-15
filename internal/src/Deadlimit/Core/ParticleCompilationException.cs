namespace Deadlimit.Core;

public sealed class ParticleCompilationException : InvalidOperationException
{
    public ParticleCompilationException(
        string message,
        int exitCode,
        IReadOnlyList<string> sourcePaths,
        IReadOnlyList<int> formatVersions,
        string? logPath = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ExitCode = exitCode;
        SourcePaths = sourcePaths;
        FormatVersions = formatVersions;
        LogPath = logPath;
    }

    public int ExitCode { get; }

    public IReadOnlyList<string> SourcePaths { get; }

    public IReadOnlyList<int> FormatVersions { get; }

    public string? LogPath { get; internal set; }
}
