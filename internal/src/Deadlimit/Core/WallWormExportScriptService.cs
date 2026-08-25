using System.Reflection;
using System.Text;

namespace Deadlimit.Core;

public static class WallWormExportScriptService
{
    private const string ResourceName = "Deadlimit.WallWormExport.ms";
    private const string ProjectRootToken = "__DEADLIMIT_PROJECT_ROOT__";
    private const string ScriptFolderName = "wallworm";
    private const string ScriptFileName = "DeadlimitWallWormExport.ms";

    public static string WriteProjectScript(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            throw new ArgumentException("Project folder is required.", nameof(projectFolder));
        }

        var fullProjectFolder = Path.GetFullPath(projectFolder.Trim());
        if (!Directory.Exists(fullProjectFolder))
        {
            throw new DirectoryNotFoundException(fullProjectFolder);
        }

        var template = ReadTemplate();
        if (!template.Contains(ProjectRootToken, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Embedded Wall Worm export template is missing token '{ProjectRootToken}'.");
        }

        var script = template.Replace(
            ProjectRootToken,
            EscapeMaxScriptVerbatimString(fullProjectFolder),
            StringComparison.Ordinal);

        var scriptFolder = Path.Combine(ProjectStore.GetMetadataFolder(fullProjectFolder), ScriptFolderName);
        Directory.CreateDirectory(scriptFolder);

        var scriptPath = Path.Combine(scriptFolder, ScriptFileName);
        File.WriteAllText(scriptPath, script, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return scriptPath;
    }

    public static string CreateFileInCommand(string scriptPath)
    {
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            throw new ArgumentException("Script path is required.", nameof(scriptPath));
        }

        var fullPath = Path.GetFullPath(scriptPath.Trim());
        return $"fileIn @\"{EscapeMaxScriptVerbatimString(fullPath)}\"";
    }

    private static string ReadTemplate()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded Wall Worm export helper '{ResourceName}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static string EscapeMaxScriptVerbatimString(string value) =>
        value.Replace("\"", "\"\"", StringComparison.Ordinal);
}
