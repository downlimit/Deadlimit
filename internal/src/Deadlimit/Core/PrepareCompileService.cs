using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record PrepareCompileProgress(string Message);

public sealed record PrepareCompileResult(
    string AddonName,
    string SourceVmdlPath,
    string CompiledVmdlPath,
    int DmxCount,
    int MaterialRemapCount,
    bool Ag2Applied,
    string? NmSkeletonRef,
    string LogPath);

public sealed class PrepareCompileService
{
    private static readonly Regex InvalidMaterialRegex = new(
        @"materials/models/[A-Za-z0-9_./\\-]+\.vmat",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NmSkeletonRegex = new(
        @"models/[A-Za-z0-9_./\\-]+\.vnmskel",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public PrepareCompileService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public async Task<PrepareCompileResult> PrepareAndCompileAsync(
        ProjectManifest manifest,
        IProgress<PrepareCompileProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(manifest);

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
                "Retail main model is unknown. Run EXTRACT HERO SOURCE once before PREPARE + COMPILE.");
        }

        var addonName = MakeAddonName(manifest.ProjectName);
        var sourceResourcePath = ToSourceVmdlResourcePath(manifest.RetailMainModel);
        var sourceRelativePath = ToWindowsPath(sourceResourcePath);
        var compiledRelativePath = ToWindowsPath(NormalizeResourcePath(manifest.RetailMainModel));

        var addonContentRoot = Path.Combine(_paths.CsdkContentRoot, "citadel_addons", addonName);
        var addonGameRoot = Path.Combine(_paths.CsdkGameRoot, "citadel_addons", addonName);
        var sourceVmdlPath = Path.Combine(addonContentRoot, sourceRelativePath);
        var compiledVmdlPath = Path.Combine(addonGameRoot, compiledRelativePath);
        var sourceModelDirectory = Path.GetDirectoryName(sourceVmdlPath)!;
        var generatedDmxDirectory = Path.Combine(sourceModelDirectory, "deadlimit_mesh");

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"build-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit Stage 1B build — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Addon: {addonName}");
        log.AppendLine($"Retail model: {manifest.RetailMainModel}");
        log.AppendLine($"Source VMDL: {sourceVmdlPath}");
        log.AppendLine($"Compiled VMDL: {compiledVmdlPath}");
        log.AppendLine();

        try
        {
            progress?.Report(new PrepareCompileProgress("Cleaning addon runtime output..."));
            if (Directory.Exists(addonGameRoot))
            {
                Directory.Delete(addonGameRoot, recursive: true);
            }

            log.AppendLine($"Runtime output cleaned before rebuild: {addonGameRoot}");

            progress?.Report(new PrepareCompileProgress("Refreshing retail authoring source context..."));
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

            progress?.Report(new PrepareCompileProgress("Preserving compatible retail ModelDoc context..."));
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

            log.AppendLine("Character skeleton retention: compatible retail ModelDoc nodes are preserved; artist RenderMeshList and project MaterialGroupList remain Deadlimit-owned.");

            progress?.Report(new PrepareCompileProgress("Compiling fresh runtime model with CSDK12 ResourceCompiler..."));
            var compileResult = await RunProcessAsync(
                _paths.ResourceCompilerPath,
                ["-i", sourceVmdlPath, "-nop4"],
                Path.GetDirectoryName(_paths.ResourceCompilerPath)!,
                cancellationToken);

            AppendProcessLog(log, "ResourceCompiler", compileResult);

            if (!compileResult.Success)
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler failed with exit code {compileResult.ExitCode}. See {logPath}");
            }

            if (!File.Exists(compiledVmdlPath))
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler exited successfully, but the expected compiled model was not found: {compiledVmdlPath}");
            }

            manifest.SourceVmdl = sourceVmdlPath;
            manifest.CompiledVmdl = compiledVmdlPath;
            ProjectStore.Save(manifest);

            progress?.Report(new PrepareCompileProgress("Discovering original NmSkeleton reference..."));
            var nmSkeletonRef = FindNmSkeletonReference(manifest);
            var ag2Applied = false;

            if (nmSkeletonRef is not null && File.Exists(_paths.DeadlockToolsExePath))
            {
                var family = InferFamily(nmSkeletonRef, manifest.RetailMainModel);
                var heroToken = NormalizeCliToken(manifest.Hero);

                if (!string.IsNullOrWhiteSpace(family) && heroToken.Length > 0)
                {
                    progress?.Report(new PrepareCompileProgress("Restoring AnimGraph2 / NmSkeleton runtime references..."));
                    var ag2Result = await RunProcessAsync(
                        _paths.DeadlockToolsExePath,
                        [
                            "add", "ag2", compiledVmdlPath,
                            "-h", heroToken,
                            "-f", family,
                            "--override-skeleton", nmSkeletonRef,
                        ],
                        Path.GetDirectoryName(_paths.DeadlockToolsExePath)!,
                        cancellationToken);

                    AppendProcessLog(log, "DeadlockTools add ag2", ag2Result);
                    if (!ag2Result.Success)
                    {
                        throw new InvalidOperationException(
                            $"Model compiled, but DeadlockTools add ag2 failed with exit code {ag2Result.ExitCode}. See {logPath}");
                    }

                    ag2Applied = true;
                    manifest.NmSkeletonRef = nmSkeletonRef;
                    ProjectStore.Save(manifest);
                }
            }
            else
            {
                log.AppendLine();
                log.AppendLine(nmSkeletonRef is null
                    ? "AG2 post-process skipped: no .vnmskel reference was discovered in 0source."
                    : $"AG2 post-process skipped: DeadlockTools was not found at {_paths.DeadlockToolsExePath}");
            }

            log.AppendLine();
            log.AppendLine("RESULT: SUCCESS");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareCompileProgress(
                ag2Applied ? "Prepare + compile complete." : "Compile complete; AG2 post-process was skipped."));

            return new PrepareCompileResult(
                addonName,
                sourceVmdlPath,
                compiledVmdlPath,
                rootDmxFiles.Length,
                remaps.Count,
                ag2Applied,
                nmSkeletonRef,
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

        if (!Directory.Exists(_paths.CsdkGameRoot))
        {
            throw new DirectoryNotFoundException($"CSDK game root was not found: {_paths.CsdkGameRoot}");
        }

        if (!File.Exists(_paths.ResourceCompilerPath))
        {
            throw new FileNotFoundException(
                "Validated bin_cs2 ResourceCompiler was not found.",
                _paths.ResourceCompilerPath);
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

    private static string? FindNmSkeletonReference(ProjectManifest manifest)
    {
        var sourceRoot = Path.Combine(manifest.ProjectFolder, manifest.SourceDumpFolderName);
        if (!Directory.Exists(sourceRoot))
        {
            return null;
        }

        var desiredVmdlName = string.IsNullOrWhiteSpace(manifest.RetailMainModel)
            ? null
            : Path.GetFileName(ToSourceVmdlResourcePath(manifest.RetailMainModel));

        var candidates = Directory.EnumerateFiles(sourceRoot, "*.vmdl", SearchOption.AllDirectories)
            .OrderByDescending(path => desiredVmdlName is not null
                && string.Equals(Path.GetFileName(path), desiredVmdlName, StringComparison.OrdinalIgnoreCase))
            .ThenBy(path => path.Length)
            .ToArray();

        foreach (var file in candidates)
        {
            try
            {
                var text = File.ReadAllText(file).Replace('\\', '/');
                var match = NmSkeletonRegex.Match(text);
                if (match.Success)
                {
                    return NormalizeResourcePath(match.Value);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return null;
    }

    private static string InferFamily(string nmSkeletonRef, string? retailMainModel)
    {
        foreach (var value in new[] { nmSkeletonRef, retailMainModel })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var parts = NormalizeResourcePath(value).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && string.Equals(parts[0], "models", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }

        return string.Empty;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            WorkingDirectory = workingDirectory,
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

        return new ProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            $"{fileName} {string.Join(' ', arguments.Select(QuoteForLog))}");
    }

    private static void AppendProcessLog(StringBuilder log, string heading, ProcessResult result)
    {
        log.AppendLine();
        log.AppendLine($"=== {heading} ===");
        log.AppendLine(result.CommandLine);
        log.AppendLine($"Exit code: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            log.AppendLine("--- stdout ---");
            log.AppendLine(result.StandardOutput.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            log.AppendLine("--- stderr ---");
            log.AppendLine(result.StandardError.TrimEnd());
        }
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

    private static string NormalizeCliToken(string value) =>
        new(value.Trim().ToLowerInvariant().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray());

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

    private static string QuoteForLog(string value) =>
        value.Contains(' ') ? $"\"{value}\"" : value;

    private sealed record MaterialRemap(string From, string To);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError,
        string CommandLine)
    {
        public bool Success => ExitCode == 0;
    }
}
