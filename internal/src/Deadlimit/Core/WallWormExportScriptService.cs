using System.Reflection;
using System.Text;

namespace Deadlimit.Core;

public static class WallWormExportScriptService
{
    private const string ResourceName = "Deadlimit.WallWormExport.ms";
    private const string ProjectRootToken = "__DEADLIMIT_PROJECT_ROOT__";
    private const string OutputMapToken = "__DEADLIMIT_OUTPUT_MAP__";
    private const string ScriptFolderName = "wallworm";
    private const string ScriptFileName = "DeadlimitWallWormExport.ms";

    public static string WriteProjectScript(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (string.IsNullOrWhiteSpace(manifest.ProjectFolder))
        {
            throw new ArgumentException("Project folder is required.", nameof(manifest));
        }

        var fullProjectFolder = Path.GetFullPath(manifest.ProjectFolder.Trim());
        if (!Directory.Exists(fullProjectFolder))
        {
            throw new DirectoryNotFoundException(fullProjectFolder);
        }

        var template = ReadTemplate();
        foreach (var token in new[] { ProjectRootToken, OutputMapToken })
        {
            if (!template.Contains(token, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Embedded Wall Worm export template is missing token '{token}'.");
            }
        }

        var outputMap = BuildOutputMap(manifest, fullProjectFolder);
        var script = template
            .Replace(
                ProjectRootToken,
                EscapeMaxScriptVerbatimString(fullProjectFolder),
                StringComparison.Ordinal)
            .Replace(
                OutputMapToken,
                FormatMaxScriptOutputMap(outputMap),
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

    private static IReadOnlyList<(string NodeName, string FileName)> BuildOutputMap(
        ProjectManifest manifest,
        string projectFolder)
    {
        var mappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var ambiguous = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string nodeName, string fileName)
        {
            nodeName = nodeName.Trim();
            fileName = Path.GetFileName(fileName.Trim());
            if (nodeName.Length == 0 || fileName.Length == 0 || ambiguous.Contains(nodeName))
            {
                return;
            }

            if (mappings.TryGetValue(nodeName, out var existing)
                && !string.Equals(existing, fileName, StringComparison.OrdinalIgnoreCase))
            {
                mappings.Remove(nodeName);
                ambiguous.Add(nodeName);
                return;
            }

            mappings[nodeName] = fileName;
        }

        foreach (var dmxPath in Directory.EnumerateFiles(projectFolder, "*.dmx", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(dmxPath);
            Add(Path.GetFileNameWithoutExtension(fileName), fileName);
        }

        var vmdlPath = !string.IsNullOrWhiteSpace(manifest.SourceVmdl) && File.Exists(manifest.SourceVmdl)
            ? manifest.SourceVmdl
            : RetailVmdlInheritance.FindRetailVmdl(manifest);

        if (!string.IsNullOrWhiteSpace(vmdlPath) && File.Exists(vmdlPath))
        {
            foreach (var entry in RetailVmdlInheritance.ReadRenderMeshes(vmdlPath))
            {
                var fileName = Path.GetFileName(entry.Filename.Replace('/', Path.DirectorySeparatorChar));
                Add(entry.Name, fileName);
                Add(Path.GetFileNameWithoutExtension(fileName), fileName);
            }
        }

        return mappings
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .Select(pair => (pair.Key, pair.Value))
            .ToArray();
    }

    private static string FormatMaxScriptOutputMap(
        IReadOnlyList<(string NodeName, string FileName)> outputMap)
    {
        if (outputMap.Count == 0)
        {
            return "#()";
        }

        var entries = outputMap.Select(entry =>
            $"#(@\"{EscapeMaxScriptVerbatimString(entry.NodeName)}\", @\"{EscapeMaxScriptVerbatimString(entry.FileName)}\")");
        return $"#({string.Join(", ", entries)})";
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
