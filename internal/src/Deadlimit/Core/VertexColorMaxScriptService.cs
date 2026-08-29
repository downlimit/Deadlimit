using System.Reflection;
using System.Text;

namespace Deadlimit.Core;

public static class VertexColorMaxScriptService
{
    private const string ResourceName = "Deadlimit.PipelineScripts.ms";
    private const string RepositoryFolderName = "maxscript-vertcolor-trans";
    private const string ScriptFileName = "DeadlimitPipelineScripts.ms";
    private const string ReadmeFileName = "README.md";

    public static string GetBundledScriptFolder()
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
            if (File.Exists(Path.Combine(candidate, ScriptFileName))
                && File.Exists(Path.Combine(candidate, ReadmeFileName)))
            {
                // Keep walking: a build-output copy can be closer than the repository copy.
                repositoryCandidate = candidate;
            }
        }

        if (repositoryCandidate is not null)
        {
            HideMetadataFolder(Path.GetDirectoryName(repositoryCandidate)!);
            return repositoryCandidate;
        }

        throw new DirectoryNotFoundException(
            $"Bundled MaxScript folder '.deadlimit\\{RepositoryFolderName}' was not found beside Deadlimit.");
    }

    public static string GetBundledScriptPath() =>
        Path.Combine(GetBundledScriptFolder(), ScriptFileName);

    public static string WriteScript(string scriptFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFolder);

        var template = ReadTemplate();
        scriptFolder = Path.GetFullPath(scriptFolder.Trim());
        Directory.CreateDirectory(scriptFolder);
        var scriptPath = Path.Combine(scriptFolder, ScriptFileName);
        if (!File.Exists(scriptPath)
            || !string.Equals(File.ReadAllText(scriptPath), template, StringComparison.Ordinal))
        {
            AtomicFile.WriteAllText(
                scriptPath,
                template,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }

        return scriptPath;
    }

    public static string CreateFileInCommand(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var fullPath = Path.GetFullPath(scriptPath.Trim());
        return $"fileIn @\"{EscapeMaxScriptVerbatimString(fullPath)}\"";
    }

    private static string ReadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Deadlimit Pipeline Scripts resource '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string EscapeMaxScriptVerbatimString(string value) =>
        value.Replace("\"", "\"\"", StringComparison.Ordinal);

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
