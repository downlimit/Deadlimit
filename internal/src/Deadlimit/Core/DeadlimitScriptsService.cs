using System.Reflection;
using System.Text;

namespace Deadlimit.Core;

public static class DeadlimitScriptsService
{
    private const string BaseResourceName = "Deadlimit.Scripts.PipelineScripts.ms";
    private const string ExperimentalResourceName = "Deadlimit.Scripts.PipelineScripts.Experimental.ms";
    private const string RepositoryFolderName = "scripts";
    private const string ScriptFileName = "DeadlimitPipelineScripts.ms";
    private const string ExperimentalScriptFileName = "DeadlimitPipelineScripts.Experimental.ms";
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
                && File.Exists(Path.Combine(candidate, ExperimentalScriptFileName))
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
            $"Bundled Deadlimit Scripts folder '.deadlimit\\{RepositoryFolderName}' was not found beside the Deadlimit repository.");
    }

    public static string GetBundledScriptPath() =>
        Path.Combine(GetBundledScriptFolder(), ExperimentalScriptFileName);

    public static string WriteScript(string scriptFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFolder);

        var baseTemplate = ReadTemplate(BaseResourceName);
        var experimentalTemplate = ReadTemplate(ExperimentalResourceName);
        scriptFolder = Path.GetFullPath(scriptFolder.Trim());
        Directory.CreateDirectory(scriptFolder);

        WriteTemplateIfChanged(
            Path.Combine(scriptFolder, ScriptFileName),
            baseTemplate);
        var experimentalScriptPath = Path.Combine(scriptFolder, ExperimentalScriptFileName);
        WriteTemplateIfChanged(experimentalScriptPath, experimentalTemplate);

        return experimentalScriptPath;
    }

    public static string CreateFileInCommand(string scriptPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        var fullPath = Path.GetFullPath(scriptPath.Trim());
        return $"fileIn @\"{EscapeMaxScriptVerbatimString(fullPath)}\"";
    }

    private static void WriteTemplateIfChanged(string path, string template)
    {
        if (!File.Exists(path)
            || !string.Equals(File.ReadAllText(path), template, StringComparison.Ordinal))
        {
            AtomicFile.WriteAllText(
                path,
                template,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }

    private static string ReadTemplate(string resourceName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Deadlimit Scripts resource '{resourceName}' was not found.");
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
