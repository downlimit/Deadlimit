using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Deadlimit.Core;

public sealed record ImportedVpkAuthoringBuildEntry(
    string InternalPath,
    string? OriginalSha256,
    string BuiltSha256,
    long Size);

public sealed class ImportedVpkAuthoringBuildSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset BuiltUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<ImportedVpkAuthoringFileSnapshot> AuthoringFiles { get; set; } = [];
    public List<ImportedVpkAuthoringBuildEntry> Entries { get; set; } = [];
}

public sealed record ImportedVpkAuthoringBuildResult(
    string CompiledFolder,
    bool Recompiled,
    int SourceFileCount,
    int ChangedCompiledEntryCount,
    string ReportPath);

public sealed class ImportedVpkAuthoringBuildService
{
    public const string ReportFileName = "imported-authoring-build.json";
    private const string OwnerFileName = ".deadlimit-import-owner.json";
    private const int CompileBatchSize = 20;

    private static readonly HashSet<string> DirectCompileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vmdl",
        ".vmat",
        ".vtex",
        ".vpcf",
        ".vsndevts",
        ".vnmclip",
        ".vnmgraph",
        ".wav",
        ".xml",
        ".css",
        ".js",
        ".vsvg",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeadlimitPaths _paths;

    public ImportedVpkAuthoringBuildService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public ImportedVpkAuthoringBuildResult PrepareCompiledTree(
        ProjectManifest manifest,
        StringBuilder log,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(log);

        var authoringMap = ImportedVpkPayloadService.TryLoadAuthoringMap(manifest.ProjectFolder)
            ?? throw new InvalidOperationException(
                "The imported project's authoring map is missing. Re-import the VPK with the current Deadlimit version before building it.");
        var authoringRoot = ProjectAuthoringLayout.GetAuthoringRoot(manifest);
        if (!Directory.Exists(authoringRoot))
        {
            throw new DirectoryNotFoundException(authoringRoot);
        }

        var currentAuthoring = CaptureAuthoringFiles(authoringRoot, cancellationToken);
        var changedAuthoringPaths = GetChangedAuthoringPaths(authoringMap.AuthoringFiles, currentAuthoring);
        var removedAuthoringPaths = GetRemovedAuthoringPaths(authoringMap.AuthoringFiles, currentAuthoring);
        var reportPath = Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), ReportFileName);
        var buildCompiledFolder = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            ImportedVpkPayloadService.BuiltCompiledFolderName);
        if (AuthoringMatchesImport(authoringMap.AuthoringFiles, currentAuthoring))
        {
            TryDeleteDirectory(buildCompiledFolder);
            TryDeleteFile(reportPath);
            log.AppendLine("Reconstructed 1authoring matches the imported baseline; byte-exact compiled snapshot reused.");
            return new ImportedVpkAuthoringBuildResult(
                ImportedVpkPayloadService.ResolveOriginalCompiledFolder(manifest.ProjectFolder),
                Recompiled: false,
                currentAuthoring.Count,
                ChangedCompiledEntryCount: 0,
                reportPath);
        }

        ValidateToolchain();
        if (removedAuthoringPaths.Count > 0)
        {
            throw new InvalidOperationException(
                "Deleting reconstructed 1authoring files is not supported by imported-project BUILD yet. " +
                "Restore or replace these files: " + string.Join(", ", removedAuthoringPaths.Take(4)));
        }
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, 8, LocalizedText.T(
            "Synchronizing reconstructed 1authoring into the imported-project compiler workspace...",
            "Синхронизация восстановленного 1authoring с рабочей областью компилятора импортированного проекта..."));

        var addonName = BuildAddonName(manifest.ProjectId);
        var contentRoot = SafePath.ResolveUnderRoot(
            Path.Combine(_paths.CsdkContentRoot, "citadel_addons"),
            addonName,
            "Imported VPK compiler content root");
        var gameRoot = SafePath.ResolveUnderRoot(
            Path.Combine(_paths.CsdkGameRoot, "citadel_addons"),
            addonName,
            "Imported VPK compiler game root");
        ResetOwnedWorkspace(contentRoot, gameRoot, manifest);
        CopyDirectory(authoringRoot, contentRoot, cancellationToken);
        var stagedTextureDescriptors = StageChangedTextureDescriptors(
            contentRoot,
            authoringMap,
            changedAuthoringPaths,
            cancellationToken);
        WriteOwner(contentRoot, manifest);
        CopyDirectory(
            ImportedVpkPayloadService.ResolveOriginalCompiledFolder(manifest.ProjectFolder),
            gameRoot,
            cancellationToken);

        var compileTargets = Directory.EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
            .Where(path => DirectCompileExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (compileTargets.Length == 0)
        {
            throw new InvalidOperationException(
                "Reconstructed 1authoring changed, but it contains no Source 2 resources that ResourceCompiler can build.");
        }

        log.AppendLine($"Imported authoring compiler addon: {addonName}");
        log.AppendLine($"Imported authoring files: {currentAuthoring.Count}");
        log.AppendLine($"Changed imported authoring files: {changedAuthoringPaths.Count}");
        log.AppendLine($"Generated texture descriptors: {stagedTextureDescriptors}");
        log.AppendLine($"Direct ResourceCompiler inputs: {compileTargets.Length}");
        Compile(compileTargets, log, progress, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, 34, LocalizedText.T(
            "Publishing compiled imported-project resources...",
            "Публикация скомпилированных ресурсов импортированного проекта..."));
        var original = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidOperationException("The imported VPK snapshot is missing or invalid.");
        var originalByPath = original.Entries.ToDictionary(
            entry => Normalize(entry.InternalPath),
            StringComparer.Ordinal);
        var builtEntries = CaptureBuiltEntries(gameRoot, originalByPath, cancellationToken);

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var stagingFolder = Path.Combine(metadataFolder, $"imported-build-staging-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(gameRoot, stagingFolder, cancellationToken);
            if (Directory.Exists(buildCompiledFolder))
            {
                Directory.Delete(buildCompiledFolder, recursive: true);
            }
            Directory.Move(stagingFolder, buildCompiledFolder);

            AtomicFile.WriteJson(
                reportPath,
                new ImportedVpkAuthoringBuildSnapshot
                {
                    BuiltUtc = DateTimeOffset.UtcNow,
                    AuthoringFiles = currentAuthoring,
                    Entries = builtEntries,
                },
                JsonOptions);
            TryDeleteFile(Path.Combine(metadataFolder, ImportedVpkAnimationBindingRepairService.ReportFileName));
            TryDeleteFile(Path.Combine(metadataFolder, ImportedVpkRepairInspectionService.InspectionFileName));
        }
        catch
        {
            TryDeleteDirectory(stagingFolder);
            throw;
        }

        return new ImportedVpkAuthoringBuildResult(
            buildCompiledFolder,
            Recompiled: true,
            currentAuthoring.Count,
            builtEntries.Count,
            reportPath);
    }

    public static ImportedVpkAuthoringBuildSnapshot? TryLoadSnapshot(string projectFolder)
    {
        var path = Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), ReportFileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var snapshot = JsonSerializer.Deserialize<ImportedVpkAuthoringBuildSnapshot>(
                File.ReadAllText(path),
                JsonOptions);
            return snapshot is { SchemaVersion: 1 } ? snapshot : null;
        }
        catch (Exception exception) when (exception is JsonException
                                           or IOException
                                           or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void ValidateToolchain()
    {
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
            throw new FileNotFoundException("Validated ResourceCompiler was not found.", _paths.ResourceCompilerPath);
        }
    }

    private void Compile(
        IReadOnlyList<string> sources,
        StringBuilder log,
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var batches = sources.Chunk(CompileBatchSize).ToArray();
        for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var percent = 12 + (int)Math.Round(18d * batchIndex / Math.Max(batches.Length, 1));
            Report(progress, percent, LocalizedText.T(
                $"Compiling reconstructed VPK resources — batch {batchIndex + 1}/{batches.Length}...",
                $"Компиляция восстановленных ресурсов VPK — пакет {batchIndex + 1}/{batches.Length}..."));

            var arguments = new List<string>(batches[batchIndex].Length * 2 + 1);
            foreach (var source in batches[batchIndex])
            {
                arguments.Add("-i");
                arguments.Add(source);
            }
            arguments.Add("-nop4");

            var result = RunProcess(
                _paths.ResourceCompilerPath,
                arguments,
                Path.GetDirectoryName(_paths.ResourceCompilerPath)!,
                cancellationToken);
            log.AppendLine();
            log.AppendLine($"[Imported ResourceCompiler batch {batchIndex + 1}/{batches.Length}]");
            log.AppendLine(result.CommandLine);
            log.AppendLine($"ExitCode: {result.ExitCode}");
            if (!string.IsNullOrWhiteSpace(result.StdOut))
            {
                log.AppendLine(result.StdOut.TrimEnd());
            }
            if (!string.IsNullOrWhiteSpace(result.StdErr))
            {
                log.AppendLine(result.StdErr.TrimEnd());
            }
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler failed while rebuilding imported 1authoring (exit code {result.ExitCode}). See the Build & Test log.");
            }
        }
    }

    private static ProcessResult RunProcess(
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
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            stdout.GetAwaiter().GetResult(),
            stderr.GetAwaiter().GetResult(),
            $"{fileName} {string.Join(' ', arguments.Select(QuoteForLog))}");
    }

    private static List<ImportedVpkAuthoringFileSnapshot> CaptureAuthoringFiles(
        string authoringRoot,
        CancellationToken cancellationToken)
    {
        var result = new List<ImportedVpkAuthoringFileSnapshot>();
        foreach (var path in Directory.EnumerateFiles(authoringRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = File.OpenRead(path);
            result.Add(new ImportedVpkAuthoringFileSnapshot(
                Normalize(Path.GetRelativePath(authoringRoot, path)),
                Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant(),
                stream.Length));
        }
        return result;
    }

    private static bool AuthoringMatchesImport(
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> baseline,
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> current)
    {
        if (baseline.Count != current.Count)
        {
            return false;
        }

        var currentByPath = current.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        return baseline.All(item =>
            currentByPath.TryGetValue(item.RelativePath, out var candidate)
            && candidate.Size == item.Size
            && string.Equals(candidate.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static HashSet<string> GetChangedAuthoringPaths(
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> baseline,
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> current)
    {
        var baselineByPath = baseline.ToDictionary(item => item.RelativePath, StringComparer.OrdinalIgnoreCase);
        return current
            .Where(item => !baselineByPath.TryGetValue(item.RelativePath, out var imported)
                || imported.Size != item.Size
                || !string.Equals(imported.Sha256, item.Sha256, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.RelativePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetRemovedAuthoringPaths(
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> baseline,
        IReadOnlyList<ImportedVpkAuthoringFileSnapshot> current)
    {
        var currentPaths = current.Select(item => item.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return baseline
            .Select(item => item.RelativePath)
            .Where(path => !currentPaths.Contains(path))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int StageChangedTextureDescriptors(
        string contentRoot,
        ImportedVpkAuthoringMap authoringMap,
        IReadOnlySet<string> changedAuthoringPaths,
        CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var mapping in authoringMap.Entries
                     .Where(entry => entry.AuthoringPath is not null
                         && entry.CompiledPath.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase)
                         && changedAuthoringPaths.Contains(entry.AuthoringPath))
                     .OrderBy(entry => entry.CompiledPath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var imagePath = Normalize(mapping.AuthoringPath!);
            var descriptorRelativePath = Normalize(mapping.CompiledPath[..^2]);
            var descriptorPath = SafePath.ResolveUnderRoot(
                contentRoot,
                descriptorRelativePath.Replace('/', Path.DirectorySeparatorChar),
                "Imported texture descriptor");
            Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
            File.WriteAllText(descriptorPath, BuildTextureDescriptor(imagePath));
            count++;
        }
        return count;
    }

    private static string BuildTextureDescriptor(string imageResourcePath)
    {
        var stem = Path.GetFileNameWithoutExtension(imageResourcePath).ToLowerInvariant();
        var linear = stem.Contains("normal", StringComparison.Ordinal)
            || stem.Contains("rough", StringComparison.Ordinal)
            || stem.Contains("metal", StringComparison.Ordinal)
            || stem.Contains("ambientocclusion", StringComparison.Ordinal)
            || stem.Contains("_ao", StringComparison.Ordinal)
            || stem.Contains("mask", StringComparison.Ordinal);
        var colorSpace = linear ? "linear" : "srgb";
        return $$"""
            <!-- dmx encoding keyvalues2_noids 1 format vtex 1 -->
            "CDmeVtex"
            {
                "m_inputTextureArray" "element_array"
                [
                    "CDmeInputTexture"
                    {
                        "m_name" "string" "InputTexture0"
                        "m_fileName" "string" "{{imageResourcePath}}"
                        "m_colorSpace" "string" "{{colorSpace}}"
                        "m_typeString" "string" "2D"
                        "m_imageProcessorArray" "element_array"
                        [
                            "CDmeImageProcessor"
                            {
                                "m_algorithm" "string" "None"
                                "m_stringArg" "string" ""
                                "m_vFloat4Arg" "vector4" "0 0 0 0"
                            }
                        ]
                    }
                ]
                "m_outputTypeString" "string" "2D"
                "m_outputFormat" "string" "RGBA8888"
                "m_outputClearColor" "vector4" "0 0 0 0"
                "m_nOutputMinDimension" "int" "0"
                "m_nOutputMaxDimension" "int" "0"
                "m_textureOutputChannelArray" "element_array"
                [
                    "CDmeTextureOutputChannel"
                    {
                        "m_inputTextureArray" "string_array" [ "InputTexture0" ]
                        "m_srcChannels" "string" "rgba"
                        "m_dstChannels" "string" "rgba"
                        "m_mipAlgorithm" "CDmeImageProcessor"
                        {
                            "m_algorithm" "string" "None"
                            "m_stringArg" "string" ""
                            "m_vFloat4Arg" "vector4" "0 0 0 0"
                        }
                        "m_outputColorSpace" "string" "{{colorSpace}}"
                    }
                ]
                "m_vClamp" "vector3" "0 0 0"
                "m_bNoLod" "bool" "0"
            }
            """;
    }

    private static List<ImportedVpkAuthoringBuildEntry> CaptureBuiltEntries(
        string gameRoot,
        IReadOnlyDictionary<string, OriginalVpkEntrySnapshot> originalByPath,
        CancellationToken cancellationToken)
    {
        var result = new List<ImportedVpkAuthoringBuildEntry>();
        foreach (var path in Directory.EnumerateFiles(gameRoot, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Normalize(Path.GetRelativePath(gameRoot, path));
            using var stream = File.OpenRead(path);
            var sha = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (originalByPath.TryGetValue(relative, out var original)
                && original.Size == stream.Length
                && string.Equals(original.Sha256, sha, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            result.Add(new ImportedVpkAuthoringBuildEntry(
                relative,
                originalByPath.TryGetValue(relative, out original) ? original.Sha256 : null,
                sha,
                stream.Length));
        }
        return result;
    }

    private static void ResetOwnedWorkspace(
        string contentRoot,
        string gameRoot,
        ProjectManifest manifest)
    {
        if (Directory.Exists(contentRoot) || Directory.Exists(gameRoot))
        {
            var ownerPath = Path.Combine(contentRoot, OwnerFileName);
            var owner = TryLoadOwner(ownerPath);
            if (owner is null
                || !string.Equals(owner.ProjectId, manifest.ProjectId, StringComparison.OrdinalIgnoreCase)
                || !PathsEqual(owner.ProjectFolder, manifest.ProjectFolder))
            {
                throw new InvalidOperationException(
                    $"Imported-project compiler workspace is not owned by this project: {contentRoot}");
            }
        }
        if (Directory.Exists(contentRoot))
        {
            Directory.Delete(contentRoot, recursive: true);
        }
        if (Directory.Exists(gameRoot))
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    private static void WriteOwner(string contentRoot, ProjectManifest manifest) =>
        AtomicFile.WriteJson(
            Path.Combine(contentRoot, OwnerFileName),
            new WorkspaceOwner(manifest.ProjectId, Path.GetFullPath(manifest.ProjectFolder)),
            JsonOptions);

    private static WorkspaceOwner? TryLoadOwner(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<WorkspaceOwner>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void CopyDirectory(string source, string destination, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var target = SafePath.ResolveUnderRoot(
                destination,
                Path.GetRelativePath(source, file),
                "Imported authoring compiler file");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string BuildAddonName(string projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidDataException("Imported project ID is missing.");
        }
        var safe = new string(projectId.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (safe.Length == 0)
        {
            throw new InvalidDataException("Imported project ID is invalid.");
        }
        return "deadlimit_import_" + safe[..Math.Min(safe.Length, 20)];
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string QuoteForLog(string value) =>
        value.Contains(' ') ? $"\"{value.Replace("\"", "\\\"")}\"" : value;

    private static void Report(
        IProgress<BuildAndTestProgress>? progress,
        int percent,
        string message) => progress?.Report(new BuildAndTestProgress(message, percent));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not reset imported-project compiled output: {path}", exception);
        }
    }

    private static void TryDeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private sealed record WorkspaceOwner(string ProjectId, string ProjectFolder);
    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr, string CommandLine);
}
