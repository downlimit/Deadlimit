using System.Reflection;
using System.Text;

namespace Deadlimit.Core;

public static class VertexColorMaxScriptService
{
    private const string ResourceName = "Deadlimit.VertexColorSidecar.ms";
    private const string ProcessorPathToken = "__DEADLIMIT_PROCESSOR_PATH__";
    private const string ScriptFolderName = "wallworm";
    private const string ScriptFileName = "DeadlimitVertexColorFBX.ms";

    public static string WriteProjectScript(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.ProjectFolder);

        var projectFolder = Path.GetFullPath(manifest.ProjectFolder.Trim());
        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException(projectFolder);
        }

        var scriptFolder = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), ScriptFolderName);
        var processorPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Deadlimit executable path is unavailable.");
        return WriteScript(scriptFolder, processorPath);
    }

    public static string WriteScript(string scriptFolder, string processorPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorPath);

        var template = ReadTemplate();
        if (!template.Contains(ProcessorPathToken, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Embedded Vertex Color helper is missing token '{ProcessorPathToken}'.");
        }

        var script = template.Replace(
            ProcessorPathToken,
            EscapeMaxScriptVerbatimString(Path.GetFullPath(processorPath)),
            StringComparison.Ordinal);

        scriptFolder = Path.GetFullPath(scriptFolder.Trim());
        Directory.CreateDirectory(scriptFolder);
        var scriptPath = Path.Combine(scriptFolder, ScriptFileName);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
                $"Embedded Vertex Color helper '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string EscapeMaxScriptVerbatimString(string value) =>
        value.Replace("\"", "\"\"", StringComparison.Ordinal);
}
