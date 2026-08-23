using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record BuildAndTestProgress(string Message);

public sealed record BuildAndTestResult(
    string AddonName,
    int CompiledSourceCount,
    int RemovedCompiledOutputCount,
    bool FullRebuild,
    bool Ag2Applied,
    string VpkPath,
    string LogPath);

public sealed class BuildAndTestService
{
    private const int CompileBatchSize = 25;

    private static readonly HashSet<string> DirectCompileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".vmdl",
        ".vmat",
        ".vtex",
        ".vpcf",
        ".vsndevts",
        ".wav",
        ".xml",
        ".css",
        ".js",
        ".vsvg",
    };

    private static readonly HashSet<string> ImageSourceExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
    };

    private static readonly Regex NmSkeletonRegex = new(
        @"models/[A-Za-z0-9_./\\-]+\.vnmskel",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public BuildAndTestService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public async Task<BuildAndTestResult> BuildAsync(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateEnvironment(manifest);

        var releaseSlot = ParseReleaseSlot(manifest.ReleaseTarget);
        var addonName = MakeAddonName(manifest.ProjectName);
        var addonGameRoot = Path.Combine(_paths.CsdkGameRoot, "citadel_addons", addonName);
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var statePath = Path.Combine(metadataFolder, "build-test-state.json");
        var previousState = TryLoadState(statePath);
        var canIncrement = previousState is not null;

        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"build-test-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit Build & Test — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Addon: {addonName}");
        log.AppendLine($"Release slot: {releaseSlot:D2}");
        log.AppendLine($"Mode: {(canIncrement ? "incremental" : "first/clean build")}");
        log.AppendLine();

        string? preservedGameBackup = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (canIncrement && Directory.Exists(addonGameRoot))
            {
                preservedGameBackup = addonGameRoot + $".deadlimit-backup-{Guid.NewGuid():N}";
                progress?.Report(new BuildAndTestProgress("Preserving previous compiled output for incremental prepare..."));
                Directory.Move(addonGameRoot, preservedGameBackup);
                log.AppendLine($"Preserved previous game output: {preservedGameBackup}");
            }

            progress?.Report(new BuildAndTestProgress("Preparing current DMX, materials and textures..."));
            var prepareProgress = new Progress<PrepareAuthoringProgress>(update =>
                progress?.Report(new BuildAndTestProgress(update.Message)));

            PrepareAuthoringResult prepare;
            try
            {
                prepare = await new PrepareAuthoringService(_paths)
                    .PrepareAsync(manifest, prepareProgress, cancellationToken);
            }
            finally
            {
                if (preservedGameBackup is not null && Directory.Exists(preservedGameBackup))
                {
                    if (Directory.Exists(addonGameRoot))
                    {
                        Directory.Delete(addonGameRoot, recursive: true);
                    }

                    Directory.Move(preservedGameBackup, addonGameRoot);
                    log.AppendLine("Restored preserved game output after authoring prepare.");
                    preservedGameBackup = null;
                }
            }

            log.AppendLine($"Prepare log: {prepare.LogPath}");
            log.AppendLine($"Prepared content root: {prepare.AddonContentRoot}");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BuildAndTestProgress("Comparing prepared content with the previous successful build..."));

            var currentHashes = HashContentTree(prepare.AddonContentRoot, cancellationToken);
            var previousHashes = previousState?.ContentHashes
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var changed = currentHashes
                .Where(pair => !previousHashes.TryGetValue(pair.Key, out var previousHash)
                    || !string.Equals(previousHash, pair.Value, StringComparison.Ordinal))
                .Select(pair => pair.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var removed = previousHashes.Keys
                .Where(path => !currentHashes.ContainsKey(path))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var fullRebuild = previousState is null;
            if (!fullRebuild && removed.Any(path => GetCompiledRelativePath(path) is null))
            {
                fullRebuild = true;
                log.AppendLine("Falling back to a clean rebuild because a removed source has no proven one-to-one compiled-output mapping.");
            }

            var removedCompiledOutputs = 0;
            if (fullRebuild)
            {
                if (Directory.Exists(addonGameRoot))
                {
                    Directory.Delete(addonGameRoot, recursive: true);
                    log.AppendLine($"Removed addon game output for clean rebuild: {addonGameRoot}");
                }
            }
            else
            {
                removedCompiledOutputs = RemoveKnownDeletedOutputs(removed, addonGameRoot, log);
            }

            Directory.CreateDirectory(addonGameRoot);

            var allDirectSources = Directory.EnumerateFiles(prepare.AddonContentRoot, "*", SearchOption.AllDirectories)
                .Where(IsDirectCompileSource)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var compileTargets = fullRebuild
                ? allDirectSources.ToHashSet(StringComparer.OrdinalIgnoreCase)
                : ResolveIncrementalCompileTargets(
                    prepare.AddonContentRoot,
                    addonGameRoot,
                    allDirectSources,
                    changed,
                    removed);

            log.AppendLine($"Prepared content files tracked: {currentHashes.Count}");
            log.AppendLine($"Changed/new source files: {changed.Count}");
            log.AppendLine($"Removed source files: {removed.Count}");
            log.AppendLine($"Direct compile targets: {compileTargets.Count}");
            log.AppendLine($"Known stale compiled outputs removed: {removedCompiledOutputs}");

            if (compileTargets.Count > 0)
            {
                progress?.Report(new BuildAndTestProgress($"Compiling {compileTargets.Count} changed asset(s)..."));
                await CompileInBatchesAsync(compileTargets, log, cancellationToken);
                VerifyCompiledOutputs(prepare.AddonContentRoot, addonGameRoot, compileTargets);
            }
            else
            {
                log.AppendLine("No compile inputs changed and all expected direct outputs exist; ResourceCompiler skipped.");
            }

            var compiledMainModel = GetCompiledMainModelPath(manifest, addonGameRoot);
            var mainModelWasCompiled = compileTargets.Contains(prepare.SourceVmdlPath);
            var ag2Applied = false;

            if (mainModelWasCompiled)
            {
                progress?.Report(new BuildAndTestProgress("Restoring AnimGraph2 / NmSkeleton on the compiled character model..."));
                ApplyAg2(manifest, compiledMainModel, log, cancellationToken);
                ag2Applied = true;
            }
            else if (!File.Exists(compiledMainModel))
            {
                throw new InvalidOperationException(
                    $"The compiled character model is missing after incremental compilation: {compiledMainModel}");
            }

            manifest.CompiledVmdl = compiledMainModel;
            ProjectStore.Save(manifest);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new BuildAndTestProgress("Packing VPK directly into retail Deadlock addons..."));

            var retailAddonsRoot = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "addons");
            Directory.CreateDirectory(retailAddonsRoot);
            var vpkPath = Path.Combine(retailAddonsRoot, $"pak{releaseSlot:D2}_dir.vpk");

            await PackVpkAsync(addonGameRoot, vpkPath, log, cancellationToken);

            SaveState(statePath, new BuildTestState
            {
                ContentHashes = currentHashes,
            });

            log.AppendLine();
            log.AppendLine("RESULT: BUILD & TEST SUCCESS");
            log.AppendLine($"VPK deployed: {vpkPath}");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new BuildAndTestProgress("Build & Test complete. VPK is ready in retail Deadlock addons."));

            return new BuildAndTestResult(
                prepare.AddonName,
                compileTargets.Count,
                removedCompiledOutputs,
                fullRebuild,
                ag2Applied,
                vpkPath,
                logPath);
        }
        catch (Exception ex)
        {
            if (preservedGameBackup is not null && Directory.Exists(preservedGameBackup))
            {
                try
                {
                    if (Directory.Exists(addonGameRoot))
                    {
                        Directory.Delete(addonGameRoot, recursive: true);
                    }
                    Directory.Move(preservedGameBackup, addonGameRoot);
                    log.AppendLine("Restored previous game output after failed prepare transaction.");
                }
                catch (Exception restoreEx)
                {
                    log.AppendLine($"WARNING: failed to restore preserved game output: {restoreEx}");
                }
            }

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
            throw new FileNotFoundException("Validated bin_cs2 ResourceCompiler was not found.", _paths.ResourceCompilerPath);
        }
        if (!File.Exists(_paths.VpkPackerPath))
        {
            throw new FileNotFoundException("CSDKCfgVPK packer was not found.", _paths.VpkPackerPath);
        }
        if (!Directory.Exists(_paths.RetailDeadlockRoot))
        {
            throw new DirectoryNotFoundException($"Retail Deadlock root was not found: {_paths.RetailDeadlockRoot}");
        }
    }

    private static int ParseReleaseSlot(string? releaseTarget)
    {
        if (!int.TryParse(releaseTarget?.Trim(), out var slot) || slot is < 1 or > 99)
        {
            throw new InvalidOperationException(
                "BUILD & TEST needs Release ID 01-99. Set the project's Release ID first; it becomes pak##_dir.vpk in retail Deadlock addons.");
        }
        return slot;
    }

    private static Dictionary<string, string> HashContentTree(string root, CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, file));
            using var stream = File.OpenRead(file);
            hashes[relative] = Convert.ToHexString(SHA256.HashData(stream));
        }
        return hashes;
    }

    private static HashSet<string> ResolveIncrementalCompileTargets(
        string contentRoot,
        string gameRoot,
        IReadOnlyList<string> allDirectSources,
        IReadOnlySet<string> changed,
        IReadOnlySet<string> removed)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var relative in changed)
        {
            var absolute = Path.Combine(contentRoot, ToWindowsPath(relative));
            if (File.Exists(absolute) && IsDirectCompileSource(absolute))
            {
                targets.Add(absolute);
            }
        }

        var dmxDependencyChanged = changed.Concat(removed)
            .Any(path => string.Equals(Path.GetExtension(path), ".dmx", StringComparison.OrdinalIgnoreCase));
        if (dmxDependencyChanged)
        {
            foreach (var vmdl in allDirectSources.Where(path =>
                         string.Equals(Path.GetExtension(path), ".vmdl", StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(vmdl);
            }
        }

        var imageDependencyChanged = changed.Concat(removed)
            .Any(path => ImageSourceExtensions.Contains(Path.GetExtension(path)));
        if (imageDependencyChanged)
        {
            foreach (var vmat in allDirectSources.Where(path =>
                         string.Equals(Path.GetExtension(path), ".vmat", StringComparison.OrdinalIgnoreCase)))
            {
                targets.Add(vmat);
            }
        }

        foreach (var source in allDirectSources)
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(contentRoot, source));
            var compiledRelative = GetCompiledRelativePath(relative);
            if (compiledRelative is null)
            {
                continue;
            }

            var expectedOutput = Path.Combine(gameRoot, ToWindowsPath(compiledRelative));
            if (!File.Exists(expectedOutput))
            {
                targets.Add(source);
            }
        }

        return targets;
    }

    private static int RemoveKnownDeletedOutputs(
        IEnumerable<string> removedSources,
        string gameRoot,
        StringBuilder log)
    {
        var removedCount = 0;
        foreach (var source in removedSources)
        {
            var compiledRelative = GetCompiledRelativePath(source);
            if (compiledRelative is null)
            {
                continue;
            }

            var output = Path.Combine(gameRoot, ToWindowsPath(compiledRelative));
            if (!File.Exists(output))
            {
                continue;
            }

            File.Delete(output);
            removedCount++;
            log.AppendLine($"Removed stale compiled output: {compiledRelative}");
        }
        return removedCount;
    }

    private async Task CompileInBatchesAsync(
        IReadOnlyCollection<string> sources,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var ordered = sources
            .OrderBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var offset = 0; offset < ordered.Length; offset += CompileBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ordered.Skip(offset).Take(CompileBatchSize).ToArray();
            var arguments = new List<string>(batch.Length * 2 + 1);
            foreach (var source in batch)
            {
                arguments.Add("-i");
                arguments.Add(source);
            }
            arguments.Add("-nop4");

            var result = await RunProcessAsync(
                _paths.ResourceCompilerPath,
                arguments,
                Path.GetDirectoryName(_paths.ResourceCompilerPath)!,
                cancellationToken);

            AppendProcessLog(log, $"ResourceCompiler batch {offset / CompileBatchSize + 1}", result);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler failed with exit code {result.ExitCode}. See the Build & Test log.");
            }
        }
    }

    private static void VerifyCompiledOutputs(
        string contentRoot,
        string gameRoot,
        IEnumerable<string> compileTargets)
    {
        foreach (var source in compileTargets)
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(contentRoot, source));
            var compiledRelative = GetCompiledRelativePath(relative);
            if (compiledRelative is null)
            {
                continue;
            }

            var output = Path.Combine(gameRoot, ToWindowsPath(compiledRelative));
            if (!File.Exists(output))
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler exited successfully, but expected output was not found: {output}");
            }
        }
    }

    private void ApplyAg2(
        ProjectManifest manifest,
        string compiledMainModel,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(compiledMainModel))
        {
            throw new InvalidOperationException(
                $"Compiled character model was not found before AG2 restoration: {compiledMainModel}");
        }
        if (!File.Exists(_paths.DeadlockToolsExePath))
        {
            throw new FileNotFoundException(
                "DeadlockTools is required to restore AnimGraph2/NmSkeleton for a freshly compiled character model.",
                _paths.DeadlockToolsExePath);
        }

        var nmSkeletonRef = FindNmSkeletonReference(manifest);
        if (nmSkeletonRef is null)
        {
            throw new InvalidOperationException(
                "No NmSkeleton (.vnmskel) reference could be discovered in this project's 0source. Refresh hero source before BUILD & TEST.");
        }

        var family = InferFamily(nmSkeletonRef, manifest.RetailMainModel);
        var heroToken = NormalizeCliToken(manifest.Hero);
        if (family.Length == 0 || heroToken.Length == 0)
        {
            throw new InvalidOperationException("Could not derive generic DeadlockTools hero/family arguments from this project.");
        }

        var result = RunProcessAsync(
                _paths.DeadlockToolsExePath,
                [
                    "add", "ag2", compiledMainModel,
                    "-h", heroToken,
                    "-f", family,
                    "--override-skeleton", nmSkeletonRef,
                ],
                Path.GetDirectoryName(_paths.DeadlockToolsExePath)!,
                cancellationToken)
            .GetAwaiter()
            .GetResult();

        AppendProcessLog(log, "DeadlockTools add ag2", result);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"DeadlockTools add ag2 failed with exit code {result.ExitCode}. See the Build & Test log.");
        }

        manifest.NmSkeletonRef = nmSkeletonRef;
        ProjectStore.Save(manifest);
    }

    private async Task PackVpkAsync(
        string addonGameRoot,
        string outputVpk,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(addonGameRoot)
            || !Directory.EnumerateFiles(addonGameRoot, "*", SearchOption.AllDirectories).Any())
        {
            throw new InvalidOperationException(
                $"Cannot create VPK because the compiled addon game folder is empty: {addonGameRoot}");
        }

        DeletePreviousVpkFamily(outputVpk, log);

        var result = await RunProcessAsync(
            _paths.VpkPackerPath,
            [addonGameRoot, outputVpk],
            Path.GetDirectoryName(_paths.VpkPackerPath)!,
            cancellationToken);

        AppendProcessLog(log, "CSDKCfgVPK", result);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                $"CSDKCfgVPK failed with exit code {result.ExitCode}. See the Build & Test log.");
        }
        if (!File.Exists(outputVpk))
        {
            throw new InvalidOperationException(
                $"CSDKCfgVPK exited successfully, but the expected VPK was not created: {outputVpk}");
        }
    }

    private static void DeletePreviousVpkFamily(string outputVpk, StringBuilder log)
    {
        var directory = Path.GetDirectoryName(outputVpk)!;
        var fileName = Path.GetFileName(outputVpk);
        const string dirSuffix = "_dir.vpk";
        var baseName = fileName.EndsWith(dirSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^dirSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);

        if (File.Exists(outputVpk))
        {
            File.Delete(outputVpk);
            log.AppendLine($"Removed previous deployed VPK: {outputVpk}");
        }

        if (!Directory.Exists(directory))
        {
            return;
        }

        var chunkRegex = new Regex(
            $"^{Regex.Escape(baseName)}_\\d{{3}}\\.vpk$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        foreach (var chunk in Directory.EnumerateFiles(directory, $"{baseName}_*.vpk", SearchOption.TopDirectoryOnly))
        {
            if (!chunkRegex.IsMatch(Path.GetFileName(chunk)))
            {
                continue;
            }
            File.Delete(chunk);
            log.AppendLine($"Removed previous deployed VPK chunk: {chunk}");
        }
    }

    private static string GetCompiledMainModelPath(ProjectManifest manifest, string addonGameRoot)
    {
        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("Retail main model is unknown.");
        }
        return Path.Combine(addonGameRoot, ToWindowsPath(NormalizeResourcePath(manifest.RetailMainModel)));
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

            var parts = NormalizeResourcePath(value)
                .Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && string.Equals(parts[0], "models", StringComparison.OrdinalIgnoreCase))
            {
                return parts[1];
            }
        }
        return string.Empty;
    }

    private static string ToSourceVmdlResourcePath(string compiledResourcePath)
    {
        var normalized = NormalizeResourcePath(compiledResourcePath);
        return normalized.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
            ? normalized[..^2]
            : normalized;
    }

    private static string NormalizeCliToken(string value) =>
        new(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static bool IsDirectCompileSource(string path) =>
        DirectCompileExtensions.Contains(Path.GetExtension(path));

    private static string? GetCompiledRelativePath(string sourceRelativePath)
    {
        var extension = Path.GetExtension(sourceRelativePath);
        var compiledExtension = extension.ToLowerInvariant() switch
        {
            ".vmdl" => ".vmdl_c",
            ".vmat" => ".vmat_c",
            ".vtex" => ".vtex_c",
            ".vpcf" => ".vpcf_c",
            ".vsndevts" => ".vsndevts_c",
            ".wav" => ".vsnd_c",
            ".xml" => ".vxml_c",
            ".css" => ".vcss_c",
            ".js" => ".vjs_c",
            ".vsvg" => ".vsvg_c",
            ".png" or ".tga" or ".jpg" or ".jpeg" or ".tif" or ".tiff" => ".vtex_c",
            _ => null,
        };

        return compiledExtension is null
            ? null
            : NormalizeRelativePath(Path.ChangeExtension(sourceRelativePath, compiledExtension));
    }

    private static BuildTestState? TryLoadState(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<BuildTestState>(File.ReadAllText(path));
            if (state is null || state.ContentHashes is null)
            {
                return null;
            }

            state.ContentHashes = state.ContentHashes
                .ToDictionary(pair => NormalizeRelativePath(pair.Key), pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static void SaveState(string path, BuildTestState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
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

    private static void AppendProcessLog(StringBuilder log, string label, ProcessResult result)
    {
        log.AppendLine();
        log.AppendLine($"[{label}]");
        log.AppendLine(result.CommandLine);
        log.AppendLine($"ExitCode: {result.ExitCode}");
        if (!string.IsNullOrWhiteSpace(result.StdOut))
        {
            log.AppendLine("STDOUT:");
            log.AppendLine(result.StdOut.TrimEnd());
        }
        if (!string.IsNullOrWhiteSpace(result.StdErr))
        {
            log.AppendLine("STDERR:");
            log.AppendLine(result.StdErr.TrimEnd());
        }
    }

    private static string QuoteForLog(string value) =>
        value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;

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

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string NormalizeRelativePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');

    private static string ToWindowsPath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar);

    private sealed class BuildTestState
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> ContentHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StdOut,
        string StdErr,
        string CommandLine)
    {
        public bool Success => ExitCode == 0;
    }
}
