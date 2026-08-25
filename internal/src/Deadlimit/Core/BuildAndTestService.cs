using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SteamDatabase.ValvePak;

namespace Deadlimit.Core;

public sealed record BuildAndTestProgress(string Message, int Percent);

public sealed record BuildAndTestResult(
    string AddonName,
    int CompiledSourceCount,
    int RemovedCompiledOutputCount,
    bool FullRebuild,
    bool Ag2Applied,
    IReadOnlyList<string> Warnings,
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
        var addonIdentity = new AddonIdentityService(_paths).ResolveAndClaim(manifest);
        var addonName = addonIdentity.AddonId;
        var addonGameRoot = addonIdentity.GameRoot;
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
            Report(progress, 2, "Starting Build & Test...");

            if (canIncrement && Directory.Exists(addonGameRoot))
            {
                preservedGameBackup = addonGameRoot + $".deadlimit-backup-{Guid.NewGuid():N}";
                Report(progress, 4, "Preserving previous compiled output for incremental prepare...");
                Directory.Move(addonGameRoot, preservedGameBackup);
                log.AppendLine($"Preserved previous game output: {preservedGameBackup}");
            }

            Report(progress, 6, "Preparing current DMX, materials and textures...");
            var prepareProgress = new InlineProgress<PrepareAuthoringProgress>(update =>
                Report(progress, MapPrepareProgress(update.Message), update.Message));

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

            Report(progress, 30, "Authoring content synchronized.");
            log.AppendLine($"Prepare log: {prepare.LogPath}");
            log.AppendLine($"Prepared content root: {prepare.AddonContentRoot}");

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 33, "Comparing prepared content with the previous successful build...");

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
                Report(progress, 36, "Preparing clean compiled output for the first/full build...");
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
                Report(progress, 40, $"Compiling {compileTargets.Count} changed asset(s)...");
                await CompileInBatchesAsync(compileTargets, log, progress, cancellationToken);
                Report(progress, 79, "Verifying compiled outputs...");
                VerifyCompiledOutputs(prepare.AddonContentRoot, addonGameRoot, compileTargets);
            }
            else
            {
                Report(progress, 79, "No Source 2 compile inputs changed; reusing verified compiled output...");
                log.AppendLine("No compile inputs changed and all expected direct outputs exist; ResourceCompiler skipped.");
            }

            var compiledMainModel = GetCompiledMainModelPath(manifest, addonGameRoot);
            var mainModelWasCompiled = compileTargets.Contains(prepare.SourceVmdlPath);
            var ag2Applied = false;

            if (mainModelWasCompiled)
            {
                Report(progress, 83, "Restoring AnimGraph2 / NmSkeleton on the compiled character model...");
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
            Report(progress, 90, "Packing VPK directly into retail Deadlock addons...");

            var retailAddonsRoot = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "addons");
            Directory.CreateDirectory(retailAddonsRoot);
            var vpkPath = Path.Combine(retailAddonsRoot, $"pak{releaseSlot:D2}_dir.vpk");

            PackVpk(addonGameRoot, vpkPath, log, progress, cancellationToken);

            SaveState(statePath, new BuildTestState
            {
                ContentHashes = currentHashes,
            });

            Report(progress, 100, "Build & Test complete.");
            log.AppendLine();
            log.AppendLine("RESULT: BUILD & TEST SUCCESS");
            log.AppendLine($"VPK deployed: {vpkPath}");
            File.WriteAllText(logPath, log.ToString());

            return new BuildAndTestResult(
                prepare.AddonName,
                compileTargets.Count,
                removedCompiledOutputs,
                fullRebuild,
                ag2Applied,
                prepare.VertexColorWarnings,
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
            var absolute = SafePath.ResolveUnderRoot(
                contentRoot,
                ToWindowsPath(relative),
                "Incremental source path from build state");
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

            var expectedOutput = SafePath.ResolveUnderRoot(
                gameRoot,
                ToWindowsPath(compiledRelative),
                "Expected compiled output");
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

            var output = SafePath.ResolveUnderRoot(
                gameRoot,
                ToWindowsPath(compiledRelative),
                "Stale compiled output from build state");
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
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var ordered = sources
            .OrderBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var batchCount = (ordered.Length + CompileBatchSize - 1) / CompileBatchSize;
        for (var offset = 0; offset < ordered.Length; offset += CompileBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchIndex = offset / CompileBatchSize;
            var batch = ordered.Skip(offset).Take(CompileBatchSize).ToArray();
            var beforePercent = 40 + (int)Math.Floor(36.0 * batchIndex / Math.Max(1, batchCount));
            Report(progress, beforePercent, $"Compiling Source 2 assets — batch {batchIndex + 1}/{batchCount}...");

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

            AppendProcessLog(log, $"ResourceCompiler batch {batchIndex + 1}", result);
            if (!result.Success)
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler failed with exit code {result.ExitCode}. See the Build & Test log.");
            }

            var afterPercent = 40 + (int)Math.Ceiling(36.0 * (batchIndex + 1) / Math.Max(1, batchCount));
            Report(progress, afterPercent, $"Compiled Source 2 assets — batch {batchIndex + 1}/{batchCount}.");
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

            var output = SafePath.ResolveUnderRoot(
                gameRoot,
                ToWindowsPath(compiledRelative),
                "Verified compiled output");
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

    private static void PackVpk(
        string addonGameRoot,
        string outputVpk,
        StringBuilder log,
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(addonGameRoot))
        {
            throw new InvalidOperationException(
                $"Cannot create VPK because the compiled addon game folder is missing: {addonGameRoot}");
        }

        var files = Directory.EnumerateFiles(addonGameRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot create VPK because the compiled addon game folder is empty: {addonGameRoot}");
        }

        var targetDirectory = Path.GetDirectoryName(outputVpk)!;
        Directory.CreateDirectory(targetDirectory);
        var targetBase = Path.GetFileName(outputVpk);
        const string suffix = "_dir.vpk";
        targetBase = targetBase.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? targetBase[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(targetBase);
        var temporaryVpk = Path.Combine(
            targetDirectory,
            $"{targetBase}_deadlimit_{Guid.NewGuid():N}_dir.vpk");

        try
        {
            using (var package = new Package { Version = 2 })
            {
                for (var index = 0; index < files.Length; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var file = files[index];
                    var relative = NormalizeRelativePath(Path.GetRelativePath(addonGameRoot, file));
                    package.AddFile(relative, File.ReadAllBytes(file));

                    var percent = 90 + (int)Math.Floor(6.0 * (index + 1) / files.Length);
                    Report(progress, percent, $"Packing VPK — {index + 1}/{files.Length} files...");
                }

                Report(progress, 97, "Writing VPK archive...");
                package.Write(temporaryVpk);
            }

            if (!File.Exists(temporaryVpk))
            {
                throw new InvalidOperationException(
                    $"ValvePak completed without an exception, but the temporary VPK was not created: {temporaryVpk}");
            }

            Report(progress, 98, "Verifying VPK checksums...");
            VerifyVpk(temporaryVpk);

            DeployVerifiedVpkFamily(temporaryVpk, outputVpk, log);
            log.AppendLine();
            log.AppendLine("[ValvePak in-process packaging]");
            log.AppendLine($"Packed files: {files.Length}");
            log.AppendLine("VPK version: 2");
            log.AppendLine($"Output: {outputVpk}");
            Report(progress, 99, "VPK deployed to retail Deadlock addons.");
        }
        finally
        {
            foreach (var temporaryFile in EnumerateVpkFamily(temporaryVpk))
            {
                try
                {
                    File.Delete(temporaryFile);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    log.AppendLine($"Could not remove temporary VPK staging file '{temporaryFile}': {ex.Message}");
                }
            }
        }
    }

    private static void DeployVerifiedVpkFamily(
        string stagedVpk,
        string outputVpk,
        StringBuilder log)
    {
        var stagedFamily = EnumerateVpkFamily(stagedVpk);
        if (!stagedFamily.Contains(stagedVpk, StringComparer.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "The verified staging VPK disappeared before retail deployment.",
                stagedVpk);
        }

        var stagedBaseName = GetVpkBaseName(stagedVpk);
        var outputBaseName = GetVpkBaseName(outputVpk);
        var outputDirectory = Path.GetDirectoryName(outputVpk)!;
        var mappings = stagedFamily
            .Select(path =>
            {
                var fileName = Path.GetFileName(path);
                var targetName = string.Equals(path, stagedVpk, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(outputVpk)
                    : outputBaseName + fileName[stagedBaseName.Length..];
                return new VpkDeploymentMapping(path, Path.Combine(outputDirectory, targetName));
            })
            .OrderBy(mapping => string.Equals(mapping.StagedPath, stagedVpk, StringComparison.OrdinalIgnoreCase)
                ? 1
                : 0)
            .ToArray();

        var targetPaths = mappings
            .Select(mapping => mapping.TargetPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var obsoletePreviousFiles = EnumerateVpkFamily(outputVpk)
            .Where(path => !targetPaths.Contains(path))
            .ToArray();
        var transactionId = Guid.NewGuid().ToString("N");
        var completed = new List<VpkDeploymentStep>();

        try
        {
            foreach (var mapping in mappings)
            {
                var backupPath = File.Exists(mapping.TargetPath)
                    ? mapping.TargetPath + $".deadlimit-backup-{transactionId}"
                    : null;

                if (backupPath is null)
                {
                    File.Move(mapping.StagedPath, mapping.TargetPath);
                }
                else
                {
                    File.Replace(
                        mapping.StagedPath,
                        mapping.TargetPath,
                        backupPath,
                        ignoreMetadataErrors: true);
                }

                completed.Add(new VpkDeploymentStep(mapping.TargetPath, backupPath));
            }

            VerifyVpk(outputVpk);

            foreach (var obsoletePath in obsoletePreviousFiles)
            {
                TryDeleteDeploymentArtifact(obsoletePath, "obsolete previous VPK chunk", log);
            }

            foreach (var step in completed)
            {
                if (step.BackupPath is not null)
                {
                    TryDeleteDeploymentArtifact(step.BackupPath, "verified VPK transaction backup", log);
                }
            }

            log.AppendLine(
                $"Retail VPK transaction committed: {mappings.Length} staged file(s), " +
                $"{completed.Count(step => step.BackupPath is not null)} previous file backup(s), " +
                $"{obsoletePreviousFiles.Length} obsolete chunk(s).");
        }
        catch (Exception deploymentError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var step in completed.AsEnumerable().Reverse())
            {
                try
                {
                    if (step.BackupPath is null)
                    {
                        if (File.Exists(step.TargetPath))
                        {
                            File.Delete(step.TargetPath);
                        }
                    }
                    else if (File.Exists(step.BackupPath))
                    {
                        if (File.Exists(step.TargetPath))
                        {
                            File.Replace(
                                step.BackupPath,
                                step.TargetPath,
                                destinationBackupFileName: null,
                                ignoreMetadataErrors: true);
                        }
                        else
                        {
                            File.Move(step.BackupPath, step.TargetPath);
                        }
                    }
                }
                catch (Exception rollbackError) when (rollbackError is IOException
                    or UnauthorizedAccessException)
                {
                    rollbackErrors.Add(rollbackError);
                }
            }

            if (rollbackErrors.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Retail VPK deployment failed; the previous VPK family was restored. {deploymentError.Message}",
                    deploymentError);
            }

            throw new AggregateException(
                "Retail VPK deployment failed and its rollback was incomplete. " +
                "Close programs that may lock the retail addon files and inspect the .deadlimit-backup files before retrying.",
                new[] { deploymentError }.Concat(rollbackErrors));
        }
    }

    private static void VerifyVpk(string path)
    {
        using var verificationPackage = new Package();
        verificationPackage.Read(path);
        verificationPackage.VerifyHashes();
        verificationPackage.VerifyFileChecksums();
    }

    private static IReadOnlyList<string> EnumerateVpkFamily(string dirVpkPath)
    {
        var directory = Path.GetDirectoryName(dirVpkPath)!;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var baseName = GetVpkBaseName(dirVpkPath);
        var chunkRegex = new Regex(
            $"^{Regex.Escape(baseName)}_\\d{{3}}\\.vpk$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var result = Directory.EnumerateFiles(directory, $"{baseName}_*.vpk", SearchOption.TopDirectoryOnly)
            .Where(path => chunkRegex.IsMatch(Path.GetFileName(path)))
            .ToList();
        if (File.Exists(dirVpkPath))
        {
            result.Add(dirVpkPath);
        }

        return result
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetVpkBaseName(string dirVpkPath)
    {
        var fileName = Path.GetFileName(dirVpkPath);
        const string dirSuffix = "_dir.vpk";
        return fileName.EndsWith(dirSuffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^dirSuffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static void TryDeleteDeploymentArtifact(
        string path,
        string description,
        StringBuilder log)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                log.AppendLine($"Removed {description}: {path}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.AppendLine($"Could not remove {description} '{path}': {ex.Message}");
        }
    }

    private sealed record VpkDeploymentMapping(string StagedPath, string TargetPath);

    private sealed record VpkDeploymentStep(string TargetPath, string? BackupPath);

    private static string GetCompiledMainModelPath(ProjectManifest manifest, string addonGameRoot)
    {
        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException("Retail main model is unknown.");
        }
        return SafePath.ResolveUnderRoot(
            addonGameRoot,
            ToWindowsPath(NormalizeResourcePath(manifest.RetailMainModel)),
            "Compiled retail main model");
    }

    private static string? FindNmSkeletonReference(ProjectManifest manifest)
    {
        var sourceRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            manifest.SourceDumpFolderName,
            "Project source-dump folder");
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
                .ToDictionary(
                    pair => SafePath.NormalizeRelative(pair.Key, "Build-state content path"),
                    pair => pair.Value,
                    StringComparer.OrdinalIgnoreCase);
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
        catch (InvalidDataException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static void SaveState(string path, BuildTestState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        AtomicFile.WriteJson(
            path,
            state,
            new JsonSerializerOptions { WriteIndented = true });
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

    private static int MapPrepareProgress(string message)
    {
        if (message.StartsWith("Cleaning stale", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }
        if (message.StartsWith("Refreshing retail", StringComparison.OrdinalIgnoreCase))
        {
            return 12;
        }
        if (message.StartsWith("Overlaying artist", StringComparison.OrdinalIgnoreCase))
        {
            return 17;
        }
        if (message.StartsWith("Preparing addon-owned", StringComparison.OrdinalIgnoreCase))
        {
            return 22;
        }
        if (message.StartsWith("Applying narrow", StringComparison.OrdinalIgnoreCase))
        {
            return 27;
        }
        return 10;
    }

    private static void Report(
        IProgress<BuildAndTestProgress>? progress,
        int percent,
        string message) =>
        progress?.Report(new BuildAndTestProgress(message, Math.Clamp(percent, 0, 100)));

    private static string NormalizeResourcePath(string value) =>
        SafePath.NormalizeRelative(value, "Source 2 resource path");

    private static string NormalizeRelativePath(string value) =>
        SafePath.NormalizeRelative(value, "Build relative path");

    private static string ToWindowsPath(string value) =>
        value.Replace('/', Path.DirectorySeparatorChar);

    private sealed class BuildTestState
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> ContentHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class InlineProgress<T> : IProgress<T>
    {
        private readonly Action<T> _report;

        public InlineProgress(Action<T> report)
        {
            _report = report;
        }

        public void Report(T value) => _report(value);
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
