using System.Diagnostics;

namespace Deadlimit.Core;

public sealed record ExternalToolResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Success => ExitCode == 0;
}

public sealed class Source2ViewerAdapter
{
    private readonly string _cliPath;

    public Source2ViewerAdapter(string cliPath)
    {
        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException("Source2Viewer-CLI.exe was not found.", cliPath);
        }

        _cliPath = cliPath;
    }

    public Task<ExternalToolResult> GetVersionAsync(CancellationToken cancellationToken = default) =>
        RunAsync(["--version"], cancellationToken);

    public Task<ExternalToolResult> ListVpkResourcesAsync(
        string vpkPath,
        string resourcePathFilter,
        CancellationToken cancellationToken = default) =>
        RunAsync(
        [
            "-i", vpkPath,
            "--vpk_list",
            "--vpk_filepath", resourcePathFilter,
        ],
        cancellationToken);

    public Task<ExternalToolResult> DecompileVpkFolderAsync(
        string vpkPath,
        string resourcePathFilter,
        string outputFolder,
        CancellationToken cancellationToken = default) =>
        RunAsync(
        [
            "-i", vpkPath,
            "--output", outputFolder,
            "--vpk_filepath", resourcePathFilter,
            "--vpk_decompile",
            "--threads", Math.Max(1, Math.Min(Environment.ProcessorCount, 8)).ToString(),
        ],
        cancellationToken);

    private async Task<ExternalToolResult> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _cliPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        var stdout = await stdoutTask;
        var stderr = await stderrTask;

        return new ExternalToolResult(process.ExitCode, stdout, stderr);
    }
}
