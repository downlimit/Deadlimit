using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record PrepareAuthoringProgress(string Message);

public sealed record PrepareAuthoringResult(
    string AddonName,
    string AddonContentRoot,
    string SourceVmdlPath,
    int DmxCount,
    int MaterialRemapCount,
    int RetailSourceFilesCopied,
    string LogPath);

public sealed class PrepareAuthoringService
{
    private static readonly Regex InvalidMaterialRegex = new(
        @"materials/models/[A-Za-z0-9_./\\-]+\.vmat",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public PrepareAuthoringService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<PrepareAuthoringResult> PrepareAsync(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(manifest, progress, cancellationToken), cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateEnvironment(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var rootDmxFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.dmx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rootDmxFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "No .dmx files were found in the project root. Export the current artist model to the project root first.");
        }

        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException(
                "Retail main model is unknown. Run EXTRACT HERO SOURCE once before PREPARE FOR CSDK.");
        }

        var addonName = MakeAddonName(manifest.ProjectName);
        var sourceResourcePath = ToSourceVmdlResourcePath(manifest.RetailMainModel);
        var sourceRelativePath = ToWindowsPath(sourceResourcePath);
        var addonContentRoot = Path.Combine(_paths.CsdkContentRoot, "citadel_addons", addonName);
        var sourceVmdlPath = Path.Combine(addonContentRoot, sourceRelativePath);
        var sourceModelDirectory = Path.GetDirectoryName(sourceVmdlPath)!;
        var generatedDmxDirectory = Path.Combine(sourceModelDirectory, "deadlimit_mesh");

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"prepare-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit authoring prepare — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Addon: {addonName}");
        log.AppendLine($"Retail model: {manifest.RetailMainModel}");
        log.AppendLine($"CSDK content root: {addonContentRoot}");
        log.AppendLine($"Source VMDL: {sourceVmdlPath}");
        log.AppendLine("CSDK game output: untouched by Deadlimit during prepare.");
        log.AppendLine();

        try
        {
            progress?.Report(new PrepareAuthoringProgress("Refreshing authoring source in CSDK content..."));
            Directory.CreateDirectory(addonContentRoot);

            var retailSourceFilesCopied = RetailVmdlInheritance.CopyRetailModelSourceTree(manifest, addonContentRoot);
            log.AppendLine($"Retail source files copied into addon content: {retailSourceFilesCopied}");

            Directory.CreateDirectory(sourceModelDirectory);
            if (Directory.Exists(generatedDmxDirectory))
            {
                Directory.Delete(generatedDmxDirectory, recursive: true);
            }
            Directory.CreateDirectory(generatedDmxDirectory);

            foreach (var dmx in rootDmxFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Copy(dmx, Path.Combine(generatedDmxDirectory, Path.GetFileName(dmx)), overwrite: true);
            }

            var remaps = DiscoverMaterialRemaps(rootDmxFiles);
            var renderMeshResourcePaths = rootDmxFiles
                .Select(path => NormalizeResourcePath(
                    Path.Combine(
                            Path.GetDirectoryName(sourceResourcePath) ?? string.Empty,
                            "deadlimit_mesh",
                            Path.GetFileName(path))
                        .Replace('\\', '/')))
                .ToArray();

            progress?.Report(new PrepareAuthoringProgress("Generating CSDK-compatible authoring VMDL..."));
            var retailInheritance = RetailVmdlInheritance.Load(manifest);
            var vmdlText = BuildVmdl(renderMeshResourcePaths, remaps, retailInheritance);
            File.WriteAllText(sourceVmdlPath, vmdlText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            log.AppendLine($"Artist DMX files copied: {rootDmxFiles.Length}");
            foreach (var dmx in rootDmxFiles)
            {
                log.AppendLine($"  {Path.GetFileName(dmx)}");
            }

            log.AppendLine($"Material remaps generated: {remaps.Count}");
            foreach (var remap in remaps)
            {
                log.AppendLine($"  {remap.From} -> {remap.To}");
            }

            if (retailInheritance.SourceVmdlPath is null)
            {
                log.AppendLine("Retail VMDL inheritance: source VMDL not found; using generated BoneMarkup fallback only.");
            }
            else
            {
                log.AppendLine($"Retail VMDL inheritance source: {retailInheritance.SourceVmdlPath}");
                log.AppendLine($"Compatible retail root nodes preserved: {retailInheritance.Nodes.Count}");
                foreach (var node in retailInheritance.Nodes)
                {
                    log.AppendLine($"  keep {node.ClassName}");
                }
                foreach (var removedClass in retailInheritance.RemovedClasses)
                {
                    log.AppendLine($"  strip unsupported source node {removedClass}");
                }
            }

            manifest.SourceVmdl = sourceVmdlPath;
            // Prepare owns only content. Any existing runtime output may be stale until CSDK compiles it.
            manifest.CompiledVmdl = null;
            ProjectStore.Save(manifest);

            log.AppendLine();
            log.AppendLine("RESULT: AUTHORING CONTENT PREPARED");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareAuthoringProgress("Authoring content prepared. Launch CSDK to compile/preview it."));

            return new PrepareAuthoringResult(
                addonName,
                addonContentRoot,
                sourceVmdlPath,
                rootDmxFiles.Length,
                remaps.Count,
                retailSourceFilesCopied,
                logPath);
        }
        catch (Exception ex)
        {
            log.AppendLine();
            log.AppendLine($"RESULT: FAILED — {ex}");
            File.WriteAllText(logPath, log.ToString());
            throw;
        }
    }

    private void ValidateEnvironment(ProjectManifest manifest)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        if (!Directory.Exists(_paths.CsdkContentRoot))
        {
            throw new DirectoryNotFoundException($"CSDK content root was not found: {_paths.CsdkContentRoot}");
        }
    }

    private static List<MaterialRemap> DiscoverMaterialRemaps(IEnumerable<string> dmxFiles)
    {
        var remaps = new Dictionary<string, MaterialRemap>(StringComparer.OrdinalIgnoreCase);

        foreach (var dmxPath in dmxFiles)
        {
            var raw = File.ReadAllBytes(dmxPath);
            var text = Encoding.Latin1.GetString(raw).Replace('\\', '/');

            foreach (Match match in InvalidMaterialRegex.Matches(text))
            {
                var from = match.Value;
                var to = from["materials/".Length..];
                remaps.TryAdd(from, new MaterialRemap(from, to));
            }
        }

        return remaps.Values
            .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string BuildVmdl(
        IReadOnlyList<string> renderMeshResourcePaths,
        IReadOnlyList<MaterialRemap> materialRemaps,
        RetailVmdlInheritanceResult retailInheritance)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!-- kv3 encoding:text:version{e21c7f3c-8a33-41c5-9977-a76d3a32aa0d} format:modeldoc29:version{3cec427c-1b0e-4d48-a90a-0436f33a6041} -->");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("rootNode =");
        sb.AppendLine("{");
        sb.AppendLine("_class = \"RootNode\"");
        sb.AppendLine("children =");
        sb.AppendLine("[");

        foreach (var inheritedNode in retailInheritance.Nodes)
        {
            sb.AppendLine(inheritedNode.Text.TrimEnd());
            sb.AppendLine(",");
        }

        if (!retailInheritance.Contains("BoneMarkupList"))
        {
            sb.AppendLine("{");
            sb.AppendLine("_class = \"BoneMarkupList\"");
            sb.AppendLine("children = [ ]");
            sb.AppendLine("bone_cull_type = \"None\"");
            sb.AppendLine("},");
        }

        if (materialRemaps.Count > 0)
        {
            sb.AppendLine("{");
            sb.AppendLine("_class = \"MaterialGroupList\"");
            sb.AppendLine("children =");
            sb.AppendLine("[");
            sb.AppendLine("{");
            sb.AppendLine("_class = \"DefaultMaterialGroup\"");
            sb.AppendLine("remaps =");
            sb.AppendLine("[");
            foreach (var remap in materialRemaps)
            {
                sb.AppendLine("{");
                sb.AppendLine($"from = \"{EscapeKv3(remap.From)}\"");
                sb.AppendLine($"to = \"{EscapeKv3(remap.To)}\"");
                sb.AppendLine("},");
            }
            sb.AppendLine("]");
            sb.AppendLine("use_global_default = false");
            sb.AppendLine("global_default_material = \"\"");
            sb.AppendLine("},");
            sb.AppendLine("]");
            sb.AppendLine("},");
        }

        sb.AppendLine("{");
        sb.AppendLine("_class = \"RenderMeshList\"");
        sb.AppendLine("children =");
        sb.AppendLine("[");

        foreach (var path in renderMeshResourcePaths)
        {
            var name = SanitizeNodeName(Path.GetFileNameWithoutExtension(path));
            sb.AppendLine("{");
            sb.AppendLine("_class = \"RenderMeshFile\"");
            sb.AppendLine($"name = \"{EscapeKv3(name)}\"");
            sb.AppendLine($"filename = \"{EscapeKv3(path)}\"");
            sb.AppendLine("import_translation = [ 0.0, 0.0, 0.0 ]");
            sb.AppendLine("import_rotation = [ 0.0, 0.0, 0.0 ]");
            sb.AppendLine("import_scale = 1.0");
            sb.AppendLine("align_origin_x_type = \"None\"");
            sb.AppendLine("align_origin_y_type = \"None\"");
            sb.AppendLine("align_origin_z_type = \"None\"");
            sb.AppendLine("parent_bone = \"\"");
            sb.AppendLine("import_filter =");
            sb.AppendLine("{");
            sb.AppendLine("exclude_by_default = false");
            sb.AppendLine("exception_list = [ ]");
            sb.AppendLine("}");
            sb.AppendLine("},");
        }

        sb.AppendLine("]");
        sb.AppendLine("},");
        sb.AppendLine("]");
        sb.AppendLine("model_archetype = \"\"");
        sb.AppendLine("primary_associated_entity = \"\"");
        sb.AppendLine("anim_graph_name = \"\"");
        sb.AppendLine("base_model_name = \"\"");
        sb.AppendLine("}");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static string MakeAddonName(string projectName)
    {
        var sb = new StringBuilder();
        var previousUnderscore = false;

        foreach (var ch in projectName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                previousUnderscore = true;
            }
        }

        var value = sb.ToString().Trim('_');
        if (value.Length == 0)
        {
            value = "deadlimit_project";
        }
        if (char.IsDigit(value[0]))
        {
            value = $"deadlimit_{value}";
        }
        return value;
    }

    private static string SanitizeNodeName(string value) =>
        new(value.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());

    private static string ToSourceVmdlResourcePath(string compiledResourcePath)
    {
        var normalized = NormalizeResourcePath(compiledResourcePath);
        if (!normalized.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Retail main model is not a .vmdl_c resource: {compiledResourcePath}");
        }
        return normalized[..^2];
    }

    private static string NormalizeResourcePath(string value) => value.Replace('\\', '/').TrimStart('/');

    private static string ToWindowsPath(string resourcePath) =>
        resourcePath.Replace('/', Path.DirectorySeparatorChar);

    private static string EscapeKv3(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    private sealed record MaterialRemap(string From, string To);
}
