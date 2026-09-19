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
    private const int Csdk12MaximumParticleFormatVersion = 63;

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

    private static readonly Regex ParticleFormatRegex = new(
        @"\bformat:vpcf(?<version>\d+)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly DeadlimitPaths _paths;

    public BuildAndTestService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<BuildAndTestResult> BuildAsync(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildInternalAsync(
            manifest,
            progress,
            cancellationToken,
            skippedParticleSources: null,
            skipAllParticleSources: false);

    public Task<BuildAndTestResult> BuildWithoutParticlesAsync(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        BuildInternalAsync(
            manifest,
            progress,
            cancellationToken,
            skippedParticleSources: null,
            skipAllParticleSources: true);

    public Task<BuildAndTestResult> BuildWithoutFailedParticlesAsync(
        ProjectManifest manifest,
        IReadOnlyCollection<string> failedParticleSources,
        IProgress<BuildAndTestProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(failedParticleSources);
        return BuildInternalAsync(
            manifest,
            progress,
            cancellationToken,
            failedParticleSources,
            skipAllParticleSources: false);
    }

    private async Task<BuildAndTestResult> BuildInternalAsync(
        ProjectManifest manifest,
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken,
        IReadOnlyCollection<string>? skippedParticleSources,
        bool skipAllParticleSources)
    {
        ValidateEnvironment(manifest);
        var slotOwnership = new VpkSlotOwnershipService(_paths);
        slotOwnership.EnsureSlotAvailable(manifest);

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
        var requestedSkippedParticles = skippedParticleSources?
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        log.AppendLine($"VPCF mode: {(skipAllParticleSources ? "skip all and reuse retail particle definitions" : requestedSkippedParticles.Count > 0 ? $"skip {requestedSkippedParticles.Count} failed particle definition(s)" : "attempt compile")}");
        log.AppendLine();

        var totalTimer = Stopwatch.StartNew();
        var stageTimer = Stopwatch.StartNew();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 2, LocalizedText.T("Starting Build & Test...", "Запуск сборки для теста..."));

            Report(progress, 6, LocalizedText.T("Preparing current DMX, materials and textures...", "Подготовка текущих DMX, материалов и текстур..."));
            var prepareProgress = new InlineProgress<PrepareAuthoringProgress>(update =>
                Report(progress, MapPrepareProgress(update.Message), update.Message));

            stageTimer.Restart();
            var prepare = await new PrepareAuthoringService(_paths)
                .PrepareAsync(manifest, prepareProgress, cancellationToken);
            AppendStageTiming(log, "Authoring PREPARE", stageTimer.Elapsed);

            Report(progress, 30, LocalizedText.T("Authoring content synchronized.", "Authoring content синхронизирован."));
            log.AppendLine($"Prepare log: {prepare.LogPath}");
            log.AppendLine($"Prepared content root: {prepare.AddonContentRoot}");

            var sourceRoot = SafePath.ResolveUnderRoot(
                manifest.ProjectFolder,
                manifest.SourceDumpFolderName,
                "Project source-dump folder");

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 33, LocalizedText.T("Comparing prepared content with the previous successful build...", "Сравнение подготовленного content с предыдущей успешной сборкой..."));
            stageTimer.Restart();

            var contentHashCachePath = Path.Combine(metadataFolder, "prepared-content-hashes.json");
            var currentHashResult = HashContentTreeCached(
                prepare.AddonContentRoot,
                contentHashCachePath,
                cancellationToken);
            var currentHashes = currentHashResult.Hashes;
            log.AppendLine(
                $"Prepared content hash cache: reused={currentHashResult.ReusedCount}, hashed={currentHashResult.HashedCount}.");
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

            var baselineCachePath = Path.Combine(metadataFolder, "source-baseline-hashes.json");
            var baselineIdentity = BuildSourceBaselineIdentity(manifest, sourceRoot);
            var baselineHashes = LoadOrUpdateSourceBaselineHashes(
                sourceRoot,
                currentHashes.Keys,
                baselineIdentity,
                baselineCachePath,
                log,
                cancellationToken);
            var projectOwnedSources = ResolveProjectOwnedSources(currentHashes, baselineHashes);
            var explicitProjectRootOverrides = ResolveExplicitProjectRootTextureOverrides(
                manifest,
                sourceRoot);
            projectOwnedSources.UnionWith(explicitProjectRootOverrides);
            if (explicitProjectRootOverrides.Count > 0)
            {
                log.AppendLine(
                    $"Explicit project-root texture overrides forced into authored compilation: {explicitProjectRootOverrides.Count}");
            }
            var projectChanged = changed
                .Where(projectOwnedSources.Contains)
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
                Report(progress, 36, LocalizedText.T("Preparing clean compiled output for the first/full build...", "Подготовка чистого compiled output для первой/полной сборки..."));
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

            var particleSources = allDirectSources
                .Where(IsParticleSource)
                .ToArray();

            var compileTargets = fullRebuild
                ? ResolveFullCompileTargets(
                    prepare.AddonContentRoot,
                    allDirectSources,
                    projectOwnedSources)
                : ResolveIncrementalCompileTargets(
                    prepare.AddonContentRoot,
                    addonGameRoot,
                    allDirectSources,
                    projectChanged,
                    removed,
                    projectOwnedSources);

            var particlesToSkip = SelectParticleSourcesToSkip(
                particleSources,
                requestedSkippedParticles,
                skipAllParticleSources);

            if (particlesToSkip.Length > 0)
            {
                foreach (var particleSource in particlesToSkip)
                {
                    compileTargets.Remove(particleSource);
                }

                removedCompiledOutputs += RemoveParticleCompiledOutputs(
                    prepare.AddonContentRoot,
                    addonGameRoot,
                    particlesToSkip,
                    log);
                log.AppendLine(
                    $"VPCF fallback active: skipped {particlesToSkip.Length} failed particle source(s); original Deadlock definitions will be reused for those paths.");
            }

            var newerParticleSources = FindUnsupportedParticleSourcesFromPaths(
                compileTargets.Where(IsParticleSource),
                Csdk12MaximumParticleFormatVersion,
                cancellationToken);
            if (newerParticleSources.Count > 0)
            {
                log.AppendLine(
                    $"Selected particle sources newer than the known Reduced CSDK 12 vpcf{Csdk12MaximumParticleFormatVersion} baseline: {newerParticleSources.Count}");
                foreach (var path in newerParticleSources)
                {
                    log.AppendLine($"  attempt {Path.GetRelativePath(prepare.AddonContentRoot, path)}");
                }
            }

            log.AppendLine($"Prepared content files tracked: {currentHashes.Count}");
            log.AppendLine($"Changed/new source files: {changed.Count}");
            log.AppendLine($"Project-owned changed/new source files: {projectChanged.Count}");
            log.AppendLine($"Retail-identical changed/new source files reused: {changed.Count - projectChanged.Count}");
            log.AppendLine($"Removed source files: {removed.Count}");
            log.AppendLine($"Direct compile targets: {compileTargets.Count}");
            var compileTargetBreakdown = compileTargets
                .GroupBy(path => Path.GetExtension(path).ToLowerInvariant())
                .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => $"{group.Key}={group.Count()}")
                .ToArray();
            log.AppendLine($"Compile target breakdown: {(compileTargetBreakdown.Length == 0 ? "none" : string.Join(", ", compileTargetBreakdown))}");
            log.AppendLine($"Known stale compiled outputs removed: {removedCompiledOutputs}");
            AppendStageTiming(log, "Content hashing and compile-target selection", stageTimer.Elapsed);

            stageTimer.Restart();
            if (compileTargets.Count > 0)
            {
                Report(progress, 40, LocalizedText.T($"Compiling {compileTargets.Count} changed asset(s)...", $"Компиляция изменённых ресурсов: {compileTargets.Count}..."));
                using var invalidatedOutputs = CompileOutputInvalidation.Begin(
                    prepare.AddonContentRoot,
                    addonGameRoot,
                    metadataFolder,
                    compileTargets,
                    projectChanged,
                    log);
                try
                {
                    var compilationFailures = await CompileInBatchesAsync(compileTargets, log, progress, cancellationToken);
                    Report(progress, 79, LocalizedText.T("Verifying compiled outputs...", "Проверка скомпилированных файлов..."));
                    var processFailedSources = compilationFailures.SourcePaths
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var verificationFailures = VerifyCompiledOutputs(
                        prepare.AddonContentRoot,
                        addonGameRoot,
                        compileTargets.Where(path => !processFailedSources.Contains(path)));
                    var particleFailures = MergeParticleFailures(compilationFailures, verificationFailures);
                    if (particleFailures.SourcePaths.Count > 0)
                    {
                        throw new ParticleCompilationException(
                            $"ResourceCompiler could not produce {particleFailures.SourcePaths.Count} VPCF particle source(s).",
                            particleFailures.ExitCode,
                            particleFailures.SourcePaths,
                            particleFailures.FormatVersions);
                    }

                    invalidatedOutputs.Commit();
                }
                catch
                {
                    invalidatedOutputs.Restore(log);
                    throw;
                }
            }
            else
            {
                Report(progress, 79, LocalizedText.T("No Source 2 compile inputs changed; reusing verified compiled output...", "Исходники Source 2 для компиляции не изменились; используется проверенный compiled output..."));
                log.AppendLine("No compile inputs changed and all expected direct outputs exist; ResourceCompiler skipped.");
            }
            AppendStageTiming(log, "ResourceCompiler and output verification", stageTimer.Elapsed);

            stageTimer.Restart();
            var compiledMainModel = GetCompiledMainModelPath(manifest, addonGameRoot);
            if (!File.Exists(compiledMainModel))
            {
                throw new InvalidOperationException(
                    $"The compiled character model is missing after incremental compilation: {compiledMainModel}");
            }

            // ResourceCompiler can rebuild the main VMDL transitively while compiling a
            // material, particle, or another model. Always run the idempotent binding
            // repair after compilation so a transitive rebuild cannot ship an A-pose model.
            Report(progress, 83, LocalizedText.T("Verifying AnimGraph2 / NmSkeleton on the compiled character model...", "Проверка AnimGraph2 / NmSkeleton в скомпилированной модели персонажа..."));
            ApplyAg2(manifest, compiledMainModel, log, cancellationToken);
            var ag2Applied = true;

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 86, LocalizedText.T(
                "Finalizing retail rigid bodies and authored secondary motion...",
                "Финализация retail rigidbody и авторской вторичной физики..."));
            var inheritedPhysics = CompiledModelPhysicsInheritance.Apply(manifest, compiledMainModel);
            log.AppendLine(
                $"Retail rigid-body physics restored: {inheritedPhysics.RetailRigidBodyCount} body part(s). " +
                (inheritedPhysics.PreservedAuthoredCloth
                    ? $"Authored cloth FE preserved: {inheritedPhysics.MergedNodeCount} node(s); retail secondary-motion FE was not applied."
                    : $"Retail secondary-motion FE restored: {inheritedPhysics.RetailNodeCount} retail node(s), " +
                      $"{inheritedPhysics.CustomJiggleCount} custom jiggle node(s), " +
                      $"{inheritedPhysics.MergedNodeCount} total."));

            manifest.CompiledVmdl = compiledMainModel;
            ProjectStore.Save(manifest);
            AppendStageTiming(log, "Compiled model finalization", stageTimer.Elapsed);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 88, LocalizedText.T("Preparing retail Deadlock package...", "Подготовка пакета для retail Deadlock..."));

            var retailAddonsRoot = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "addons");
            Directory.CreateDirectory(retailAddonsRoot);
            var vpkPath = Path.Combine(retailAddonsRoot, $"pak{releaseSlot:D2}_dir.vpk");

            stageTimer.Restart();
            var packagingPlan = RetailResourcePackagingPolicy.Resolve(
                manifest,
                _paths.RetailDeadlockRoot,
                sourceRoot,
                prepare.AddonContentRoot,
                addonGameRoot,
                compiledMainModel,
                cancellationToken,
                message => log.AppendLine(message));
            log.AppendLine($"Compiled output paths also available from retail: {packagingPlan.RetailResourceCount}");
            log.AppendLine($"Project-owned compiled roots: {packagingPlan.ProjectRootCount}");
            log.AppendLine($"Retail/redundant compiled outputs omitted from VPK: {packagingPlan.ExcludedRelativePaths.Count}");
            foreach (var reusableResource in packagingPlan.ExcludedRelativePaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                log.AppendLine($"  reuse {reusableResource}");
            }

            Report(progress, 89, LocalizedText.T(
                "Building authored hero-select scene packages...",
                "Сборка авторских пакетов сцены выбора героя..."));
            var heroSelectPackages = await BuildHeroSelectScenePackagesAsync(
                prepare.AddonContentRoot,
                addonGameRoot,
                metadataFolder,
                log,
                cancellationToken);
            var packagingExclusions = packagingPlan.ExcludedRelativePaths
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var packagePath in heroSelectPackages)
            {
                packagingExclusions.Remove(packagePath);
            }
            var looseHeroSelectResources = Directory.EnumerateFiles(
                    addonGameRoot,
                    "*",
                    SearchOption.AllDirectories)
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(addonGameRoot, path)))
                .Where(path => IsLooseHeroSelectResource(path, heroSelectPackages))
                .ToArray();
            packagingExclusions.UnionWith(looseHeroSelectResources);
            log.AppendLine(
                $"Loose hero-select resources omitted from outer VPK: {looseHeroSelectResources.Length}");
            log.AppendLine($"Authored hero-select packages built: {heroSelectPackages.Count}");

            PackVpk(
                addonGameRoot,
                vpkPath,
                packagingExclusions,
                log,
                progress,
                cancellationToken);
            slotOwnership.RecordSuccessfulDeployment(manifest, vpkPath);
            log.AppendLine("VPK slot ownership updated by the deployment transaction.");
            AppendStageTiming(log, "Retail reuse analysis and VPK packaging", stageTimer.Elapsed);

            var skippedRelativePaths = particlesToSkip
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(prepare.AddonContentRoot, path)))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var successfulHashes = skippedRelativePaths.Count > 0
                ? currentHashes
                    .Where(pair => !skippedRelativePaths.Contains(pair.Key))
                    .ToDictionary(
                        pair => pair.Key,
                        pair => pair.Value,
                        StringComparer.OrdinalIgnoreCase)
                : currentHashes;

            SaveState(statePath, new BuildTestState
            {
                ContentHashes = successfulHashes,
            });

            Report(progress, 100, LocalizedText.T("Build & Test complete.", "Сборка для теста завершена."));
            AppendStageTiming(log, "Total BUILD & TEST", totalTimer.Elapsed);
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
            if (ex is ParticleCompilationException particleCompilationException)
            {
                particleCompilationException.LogPath = logPath;
            }

            AppendStageTiming(log, "Total BUILD & TEST before failure", totalTimer.Elapsed);
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

    private static ContentHashResult HashContentTreeCached(
        string root,
        string cachePath,
        CancellationToken cancellationToken)
    {
        var rootIdentity = Path.GetFullPath(root);
        var loadedCache = TryLoadPreparedContentHashCache(cachePath, rootIdentity);
        var cache = loadedCache
            ?? new PreparedContentHashCache { RootIdentity = rootIdentity };
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refreshedEntries = new Dictionary<string, PreparedContentHashEntry>(StringComparer.OrdinalIgnoreCase);
        var reusedCount = 0;
        var hashedCount = 0;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = NormalizeRelativePath(Path.GetRelativePath(root, file));
            var info = new FileInfo(file);
            string hash;
            if (cache.Entries.TryGetValue(relative, out var cached)
                && cached.Length == info.Length
                && cached.LastWriteTimeUtcTicks == info.LastWriteTimeUtc.Ticks
                && cached.Sha256.Length > 0)
            {
                hash = cached.Sha256;
                reusedCount++;
            }
            else
            {
                using var stream = File.OpenRead(file);
                hash = Convert.ToHexString(SHA256.HashData(stream));
                hashedCount++;
            }

            hashes[relative] = hash;
            refreshedEntries[relative] = new PreparedContentHashEntry
            {
                Length = info.Length,
                LastWriteTimeUtcTicks = info.LastWriteTimeUtc.Ticks,
                Sha256 = hash,
            };
        }

        if (loadedCache is null
            || hashedCount > 0
            || refreshedEntries.Count != cache.Entries.Count)
        {
            AtomicFile.WriteJson(
                cachePath,
                new PreparedContentHashCache
                {
                    RootIdentity = rootIdentity,
                    Entries = refreshedEntries,
                },
                new JsonSerializerOptions { WriteIndented = false });
        }
        return new ContentHashResult(hashes, reusedCount, hashedCount);
    }

    private static PreparedContentHashCache? TryLoadPreparedContentHashCache(
        string cachePath,
        string expectedRootIdentity)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            var cache = JsonSerializer.Deserialize<PreparedContentHashCache>(File.ReadAllText(cachePath));
            if (cache is null
                || cache.SchemaVersion != 1
                || !string.Equals(cache.RootIdentity, expectedRootIdentity, StringComparison.OrdinalIgnoreCase)
                || cache.Entries is null)
            {
                return null;
            }

            cache.Entries = cache.Entries.ToDictionary(
                pair => SafePath.NormalizeRelative(pair.Key, "Prepared content hash cache path"),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            return cache;
        }
        catch (Exception ex) when (ex is JsonException
                                   or IOException
                                   or InvalidDataException
                                   or ArgumentException)
        {
            return null;
        }
    }

    internal static IReadOnlyList<string> FindUnsupportedParticleSources(
        string contentRoot,
        int maximumSupportedVersion,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(contentRoot))
        {
            return [];
        }

        return FindUnsupportedParticleSourcesFromPaths(
            Directory.EnumerateFiles(contentRoot, "*.vpcf", SearchOption.AllDirectories),
            maximumSupportedVersion,
            cancellationToken);
    }

    private static IReadOnlyList<string> FindUnsupportedParticleSourcesFromPaths(
        IEnumerable<string> particleSources,
        int maximumSupportedVersion,
        CancellationToken cancellationToken)
    {
        var unsupported = new List<string>();
        foreach (var path in particleSources.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var version = ReadParticleFormatVersion(path);
            if (version is not null && version > maximumSupportedVersion)
            {
                unsupported.Add(path);
            }
        }

        return unsupported;
    }

    private static int? ReadParticleFormatVersion(string path)
    {
        var header = File.ReadLines(path).FirstOrDefault() ?? string.Empty;
        var match = ParticleFormatRegex.Match(header);
        return match.Success
               && int.TryParse(match.Groups["version"].Value, out var version)
            ? version
            : null;
    }

    private static string? GetDependencyCompiledRelativePath(string sourceRelativePath)
    {
        var extension = Path.GetExtension(sourceRelativePath);
        var compiledExtension = extension.ToLowerInvariant() switch
        {
            ".dmx" or ".fbx" => ".vmesh_c",
            ".png" or ".tga" or ".jpg" or ".jpeg" or ".tif" or ".tiff" => ".vtex_c",
            _ => null,
        };
        return compiledExtension is null
            ? null
            : NormalizeRelativePath(Path.ChangeExtension(sourceRelativePath, compiledExtension));
    }

    private static HashSet<string> ResolveIncrementalCompileTargets(
        string contentRoot,
        string gameRoot,
        IReadOnlyList<string> allDirectSources,
        IReadOnlySet<string> changed,
        IReadOnlySet<string> removed,
        IReadOnlySet<string> projectOwnedSources)
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

        var changedRenderMeshes = changed.Concat(removed)
            .Where(path =>
                string.Equals(Path.GetExtension(path), ".dmx", StringComparison.OrdinalIgnoreCase)
                || string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changedRenderMeshes.Count > 0)
        {
            foreach (var vmdl in FindDirectDependents(
                         allDirectSources,
                         changedRenderMeshes,
                         ".vmdl"))
            {
                targets.Add(vmdl);
            }
        }

        var changedImages = changed.Concat(removed)
            .Where(path => ImageSourceExtensions.Contains(Path.GetExtension(path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (changedImages.Count > 0)
        {
            foreach (var vmat in FindDirectDependents(
                         allDirectSources,
                         changedImages,
                         ".vmat"))
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
                if (projectOwnedSources.Contains(relative))
                {
                    targets.Add(source);
                }
            }
        }

        return targets;
    }

    private static IReadOnlyList<string> FindDirectDependents(
        IReadOnlyList<string> allDirectSources,
        IReadOnlySet<string> changedDependencies,
        string consumerExtension)
    {
        if (changedDependencies.Count == 0)
        {
            return Array.Empty<string>();
        }

        var normalizedDependencies = changedDependencies
            .Select(NormalizeRelativePath)
            .ToArray();
        var dependents = new List<string>();

        foreach (var source in allDirectSources.Where(path =>
                     string.Equals(Path.GetExtension(path), consumerExtension, StringComparison.OrdinalIgnoreCase)))
        {
            string text;
            try
            {
                text = File.ReadAllText(source).Replace('\\', '/');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // If a source is concurrently being saved, its own content hash will
                // select it on the next run. Avoid broad invalidation as a fallback.
                continue;
            }

            if (normalizedDependencies.Any(dependency =>
                    text.Contains(dependency, StringComparison.OrdinalIgnoreCase)))
            {
                dependents.Add(source);
            }
        }

        return dependents;
    }

    private static HashSet<string> ResolveFullCompileTargets(
        string contentRoot,
        IReadOnlyList<string> allDirectSources,
        IReadOnlySet<string> projectOwnedSources) =>
        allDirectSources
            .Where(source =>
            {
                var relative = NormalizeRelativePath(Path.GetRelativePath(contentRoot, source));
                return projectOwnedSources.Contains(relative);
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> ResolveProjectOwnedSources(
        IReadOnlyDictionary<string, string> currentHashes,
        IReadOnlyDictionary<string, string> baselineHashes)
    {
        var projectOwned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relativePath, currentHash) in currentHashes)
        {
            if (!baselineHashes.TryGetValue(relativePath, out var baselineHash)
                || !string.Equals(currentHash, baselineHash, StringComparison.Ordinal))
            {
                projectOwned.Add(relativePath);
            }
        }

        return projectOwned;
    }

    private static bool IsLooseHeroSelectResource(
        string relativePath,
        IReadOnlyCollection<string> heroSelectPackages)
    {
        var normalizedPath = NormalizeRelativePath(relativePath);
        foreach (var packagePath in heroSelectPackages)
        {
            var normalizedPackagePath = NormalizeRelativePath(packagePath);
            if (!normalizedPackagePath.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var sceneStem = normalizedPackagePath[..^".vpk".Length];
            if (string.Equals(normalizedPath, sceneStem + ".vmap_c", StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(sceneStem + "/", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HashSet<string> ResolveExplicitProjectRootTextureOverrides(
        ProjectManifest manifest,
        string extractedSourceRoot) =>
        RetailTextureOverrideService.ResolveProjectRootOverrides(
                manifest,
                RetailTextureOverrideService.BuildTargetIndex(extractedSourceRoot))
            .Select(textureOverride => NormalizeRelativePath(textureOverride.StagedSourceResourcePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string BuildSourceBaselineIdentity(ProjectManifest manifest, string sourceRoot) =>
        $"{Path.GetFullPath(sourceRoot)}|{manifest.LastSourceExtractionUtc?.UtcTicks ?? 0}|{manifest.ExtractedSourceFileCount ?? -1}";

    private static Dictionary<string, string> LoadOrUpdateSourceBaselineHashes(
        string sourceRoot,
        IEnumerable<string> requiredRelativePaths,
        string identity,
        string cachePath,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var cache = TryLoadSourceBaselineHashCache(cachePath, identity)
            ?? new SourceBaselineHashCache { Identity = identity };
        var cacheWasCurrent = cache.Hashes.Count > 0;
        var addedCount = 0;

        foreach (var relativePath in requiredRelativePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache.Hashes.ContainsKey(relativePath))
            {
                continue;
            }

            var extractedPath = SafePath.ResolveUnderRoot(
                sourceRoot,
                ToWindowsPath(relativePath),
                "Extracted source baseline path");
            if (!File.Exists(extractedPath))
            {
                continue;
            }

            using var stream = File.OpenRead(extractedPath);
            cache.Hashes[relativePath] = Convert.ToHexString(SHA256.HashData(stream));
            addedCount++;
        }

        if (addedCount > 0 || !cacheWasCurrent || !File.Exists(cachePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
            AtomicFile.WriteJson(
                cachePath,
                cache,
                new JsonSerializerOptions { WriteIndented = true });
        }

        log.AppendLine(
            $"Source baseline hash cache: {(cacheWasCurrent ? "reused" : "created")}; cached={cache.Hashes.Count}, newly hashed={addedCount}.");
        return cache.Hashes;
    }

    private static SourceBaselineHashCache? TryLoadSourceBaselineHashCache(
        string cachePath,
        string expectedIdentity)
    {
        if (!File.Exists(cachePath))
        {
            return null;
        }

        try
        {
            var cache = JsonSerializer.Deserialize<SourceBaselineHashCache>(File.ReadAllText(cachePath));
            if (cache is null
                || cache.SchemaVersion != 1
                || !string.Equals(cache.Identity, expectedIdentity, StringComparison.Ordinal)
                || cache.Hashes is null)
            {
                return null;
            }

            cache.Hashes = cache.Hashes.ToDictionary(
                pair => SafePath.NormalizeRelative(pair.Key, "Source baseline cache path"),
                pair => pair.Value,
                StringComparer.OrdinalIgnoreCase);
            return cache;
        }
        catch (Exception ex) when (ex is JsonException
                                   or IOException
                                   or InvalidDataException
                                   or ArgumentException)
        {
            return null;
        }
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

    private static int RemoveParticleCompiledOutputs(
        string contentRoot,
        string gameRoot,
        IEnumerable<string> particleSources,
        StringBuilder log)
    {
        var removedCount = 0;
        foreach (var particleSource in particleSources)
        {
            var relative = NormalizeRelativePath(Path.GetRelativePath(contentRoot, particleSource));
            var compiledRelative = GetCompiledRelativePath(relative);
            if (compiledRelative is null)
            {
                continue;
            }

            var output = SafePath.ResolveUnderRoot(
                gameRoot,
                ToWindowsPath(compiledRelative),
                "Particle output removed for VPCF fallback");
            if (!File.Exists(output))
            {
                continue;
            }

            File.Delete(output);
            removedCount++;
            log.AppendLine($"Removed VPCF compiled output for fallback: {compiledRelative}");
        }

        return removedCount;
    }

    private static string[] SelectParticleSourcesToSkip(
        IReadOnlyCollection<string> particleSources,
        IReadOnlySet<string> requestedSkippedParticles,
        bool skipAllParticleSources) =>
        skipAllParticleSources
            ? particleSources.ToArray()
            : particleSources
                .Where(requestedSkippedParticles.Contains)
                .ToArray();

    private async Task<ParticleCompilationFailures> CompileInBatchesAsync(
        IReadOnlyCollection<string> sources,
        StringBuilder log,
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var nonParticleSources = sources
            .Where(path => !IsParticleSource(path))
            .OrderBy(path => Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var particleSources = sources
            .Where(IsParticleSource)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var batches = nonParticleSources
            .Chunk(CompileBatchSize)
            .Select(batch => new CompileBatch(batch, IsParticle: false))
            .Concat(particleSources
                .Chunk(CompileBatchSize)
                .Select(batch => new CompileBatch(batch, IsParticle: true)))
            .ToArray();
        var failedParticleSources = new List<string>();
        var failedParticleVersions = new HashSet<int>();
        var firstParticleExitCode = 0;

        for (var batchIndex = 0; batchIndex < batches.Length; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = batches[batchIndex];
            var beforePercent = 40 + (int)Math.Floor(36.0 * batchIndex / Math.Max(1, batches.Length));
            var kind = batch.IsParticle ? "VPCF particle" : "Source 2 asset";
            Report(progress, beforePercent, LocalizedText.T(
                $"Compiling {kind} — batch {batchIndex + 1}/{batches.Length}...",
                batch.IsParticle
                    ? $"Компиляция VPCF — пакет {batchIndex + 1}/{batches.Length}..."
                    : $"Компиляция ресурсов Source 2 — пакет {batchIndex + 1}/{batches.Length}..."));

            var arguments = new List<string>(batch.Sources.Length * 2 + 1);
            foreach (var source in batch.Sources)
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

            AppendProcessLog(
                log,
                batch.IsParticle
                    ? $"ResourceCompiler VPCF {batchIndex + 1}"
                    : $"ResourceCompiler batch {batchIndex + 1}",
                result);
            if (!result.Success)
            {
                if (batch.IsParticle)
                {
                    log.AppendLine(
                        $"VPCF batch failed; probing {batch.Sources.Length} source(s) individually to identify only the incompatible definitions.");
                    var probeFailures = await ProbeParticleBatchFailuresAsync(
                        batch.Sources,
                        log,
                        cancellationToken);
                    failedParticleSources.AddRange(probeFailures.SourcePaths);
                    foreach (var version in probeFailures.FormatVersions)
                    {
                        failedParticleVersions.Add(version);
                    }
                    if (firstParticleExitCode == 0 && probeFailures.ExitCode != 0)
                    {
                        firstParticleExitCode = probeFailures.ExitCode;
                    }
                    continue;
                }

                throw new InvalidOperationException(
                    $"ResourceCompiler failed with exit code {result.ExitCode}. See the Build & Test log.");
            }

            var afterPercent = 40 + (int)Math.Ceiling(36.0 * (batchIndex + 1) / Math.Max(1, batches.Length));
            Report(progress, afterPercent, LocalizedText.T(
                batch.IsParticle
                    ? $"Compiled VPCF particle — batch {batchIndex + 1}/{batches.Length}."
                    : $"Compiled Source 2 assets — batch {batchIndex + 1}/{batches.Length}.",
                batch.IsParticle
                    ? $"VPCF скомпилирован — пакет {batchIndex + 1}/{batches.Length}."
                    : $"Ресурсы Source 2 скомпилированы — пакет {batchIndex + 1}/{batches.Length}."));
        }

        return new ParticleCompilationFailures(
            firstParticleExitCode,
            failedParticleSources,
            failedParticleVersions.Order().ToArray());
    }

    private async Task<ParticleCompilationFailures> ProbeParticleBatchFailuresAsync(
        IReadOnlyList<string> sources,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var failedSources = new List<string>();
        var failedVersions = new HashSet<int>();
        var firstExitCode = 0;

        for (var index = 0; index < sources.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = sources[index];
            var result = await RunProcessAsync(
                _paths.ResourceCompilerPath,
                ["-i", source, "-nop4"],
                Path.GetDirectoryName(_paths.ResourceCompilerPath)!,
                cancellationToken);
            AppendProcessLog(log, $"ResourceCompiler VPCF probe {index + 1}/{sources.Count}", result);
            if (result.Success)
            {
                continue;
            }

            failedSources.Add(source);
            var version = ReadParticleFormatVersion(source);
            if (version is not null)
            {
                failedVersions.Add(version.Value);
            }
            if (firstExitCode == 0)
            {
                firstExitCode = result.ExitCode;
            }
        }

        return new ParticleCompilationFailures(
            firstExitCode,
            failedSources,
            failedVersions.Order().ToArray());
    }

    private static ParticleCompilationFailures VerifyCompiledOutputs(
        string contentRoot,
        string gameRoot,
        IEnumerable<string> compileTargets)
    {
        var failedParticleSources = new List<string>();
        var failedParticleVersions = new HashSet<int>();
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
            if (File.Exists(output))
            {
                continue;
            }

            if (IsParticleSource(source))
            {
                var version = ReadParticleFormatVersion(source);
                failedParticleSources.Add(source);
                if (version is not null)
                {
                    failedParticleVersions.Add(version.Value);
                }
                continue;
            }

            throw new InvalidOperationException(
                $"ResourceCompiler exited successfully, but expected output was not found: {output}");
        }

        return new ParticleCompilationFailures(
            ExitCode: 0,
            failedParticleSources,
            failedParticleVersions.Order().ToArray());
    }

    private static ParticleCompilationFailures MergeParticleFailures(
        ParticleCompilationFailures first,
        ParticleCompilationFailures second) =>
        new(
            first.ExitCode != 0 ? first.ExitCode : second.ExitCode,
            first.SourcePaths
                .Concat(second.SourcePaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            first.FormatVersions
                .Concat(second.FormatVersions)
                .Distinct()
                .Order()
                .ToArray());

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
    }

    private static void PackVpk(
        string addonGameRoot,
        string outputVpk,
        IReadOnlySet<string> excludedRelativePaths,
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
            .Select(path => new
            {
                Path = path,
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(addonGameRoot, path)),
            })
            .Where(item => !excludedRelativePaths.Contains(item.RelativePath))
            .OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"Cannot create VPK because the compiled addon game folder is empty after retail-resource reuse filtering: {addonGameRoot}");
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
                    package.AddFile(file.RelativePath, File.ReadAllBytes(file.Path));

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
            log.AppendLine($"Retail/redundant compiled outputs omitted: {excludedRelativePaths.Count}");
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

    private async Task<IReadOnlyList<string>> BuildHeroSelectScenePackagesAsync(
        string addonContentRoot,
        string addonGameRoot,
        string metadataFolder,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var sceneFolder = Path.Combine(addonContentRoot, "maps", "ui", "hero_prefabs");
        if (!Directory.Exists(sceneFolder))
        {
            return Array.Empty<string>();
        }

        var scenes = Directory.EnumerateFiles(sceneFolder, "*.vmap", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scenes.Length == 0)
        {
            return Array.Empty<string>();
        }

        var builtPackages = new List<string>(scenes.Length);
        foreach (var scenePath in scenes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sceneId = Path.GetFileNameWithoutExtension(scenePath);
            var originalPackagePath = Path.Combine(
                _paths.CsdkGameRoot,
                "citadel",
                "maps",
                "ui",
                "hero_prefabs",
                sceneId + ".vpk");
            if (!File.Exists(originalPackagePath))
            {
                throw new FileNotFoundException(
                    $"Original hero-select package was not found for authored scene '{sceneId}'.",
                    originalPackagePath);
            }

            var sceneAuthoringFolder = Path.Combine(sceneFolder, sceneId);
            var sceneAuthoringModels = Directory.Exists(sceneAuthoringFolder)
                ? Directory.EnumerateFiles(sceneAuthoringFolder, "*.vmdl", SearchOption.AllDirectories)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
            if (sceneAuthoringModels.Length > 0)
            {
                using var invalidatedSceneModels = CompileOutputInvalidation.Begin(
                    addonContentRoot,
                    addonGameRoot,
                    metadataFolder,
                    sceneAuthoringModels,
                    Array.Empty<string>(),
                    log);
                try
                {
                    await CompileInBatchesAsync(
                        sceneAuthoringModels,
                        log,
                        progress: null,
                        cancellationToken);
                    var missingSceneModels = VerifyCompiledOutputs(
                        addonContentRoot,
                        addonGameRoot,
                        sceneAuthoringModels);
                    if (missingSceneModels.SourcePaths.Count > 0)
                    {
                        throw new InvalidOperationException(
                            $"ResourceCompiler did not produce {missingSceneModels.SourcePaths.Count} hero-select scene model(s) for '{sceneId}'.");
                    }

                    invalidatedSceneModels.Commit();
                    log.AppendLine($"Hero-select authoring models rebuilt: {sceneAuthoringModels.Length}");
                }
                catch
                {
                    invalidatedSceneModels.Restore(log);
                    throw;
                }
            }

            var temporaryOutputRoot = Path.Combine(
                Path.GetTempPath(),
                $"deadlimit-hero-select-{Guid.NewGuid():N}");
            Directory.CreateDirectory(temporaryOutputRoot);
            try
            {
                var compileResult = await RunProcessAsync(
                    _paths.ResourceCompilerPath,
                    [
                        "-threads", Math.Max(1, Environment.ProcessorCount - 1).ToString(),
                        "-fshallow",
                        "-maxtextureres", "256",
                        "-dxlevel", "110",
                        "-quiet",
                        "-unbufferedio",
                        "-i", scenePath,
                        "-noassert",
                        "-world",
                        "-phys",
                        "-vis",
                        "-retail",
                        "-breakpad",
                        "-nop4",
                        "-outroot", temporaryOutputRoot,
                    ],
                    Path.GetDirectoryName(_paths.ResourceCompilerPath)!,
                    cancellationToken);
                AppendProcessLog(log, $"Hero-select map compile: {sceneId}", compileResult);
                if (!compileResult.Success)
                {
                    throw new InvalidOperationException(
                        $"Hero-select map compilation failed for '{sceneId}' with exit code {compileResult.ExitCode}. See the Build & Test log.");
                }

                var compiledMapPackages = Directory.EnumerateFiles(
                        temporaryOutputRoot,
                        sceneId + ".vpk",
                        SearchOption.AllDirectories)
                    .ToArray();
                if (compiledMapPackages.Length != 1)
                {
                    throw new InvalidOperationException(
                        $"Hero-select map compiler produced {compiledMapPackages.Length} matching VPK files for '{sceneId}'; expected exactly one.");
                }

                var targetRelativePath = NormalizeRelativePath(
                    Path.Combine("maps", "ui", "hero_prefabs", sceneId + ".vpk"));
                var targetPath = SafePath.ResolveUnderRoot(
                    addonGameRoot,
                    ToWindowsPath(targetRelativePath),
                    "Authored hero-select VPK");
                CreateHeroSelectPackage(
                    originalPackagePath,
                    compiledMapPackages[0],
                    targetRelativePath,
                    targetPath,
                    log,
                    cancellationToken);
                builtPackages.Add(targetRelativePath);
            }
            finally
            {
                if (Directory.Exists(temporaryOutputRoot))
                {
                    Directory.Delete(temporaryOutputRoot, recursive: true);
                }
            }
        }

        return builtPackages;
    }

    private static void CreateHeroSelectPackage(
        string originalPackagePath,
        string compiledMapPackagePath,
        string targetRelativePath,
        string targetPath,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        OverlayPackageEntries(originalPackagePath, entries, cancellationToken);

        // Project-owned models, materials, textures, and effects remain at the
        // top level of the outer mod VPK. The nested hero-select VPK carries
        // only the original scene support files plus the freshly compiled map,
        // avoiding a second copy of the whole authored mod payload.
        OverlayPackageEntries(compiledMapPackagePath, entries, cancellationToken);
        log.AppendLine($"Hero-select nested payload restricted to scene resources: {entries.Count} entries.");

        var targetFolder = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException($"Hero-select VPK has no parent folder: {targetPath}");
        Directory.CreateDirectory(targetFolder);
        var temporaryPath = Path.Combine(
            targetFolder,
            $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp.vpk");
        try
        {
            using (var outputPackage = new Package { Version = 2 })
            {
                foreach (var (relativePath, data) in entries.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outputPackage.AddFile(relativePath, data);
                }
                outputPackage.Write(temporaryPath);
            }

            VerifyVpk(temporaryPath);
            File.Move(temporaryPath, targetPath, overwrite: true);
            log.AppendLine(
                $"Hero-select VPK built: {targetRelativePath} | entries={entries.Count} | output={targetPath}");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void OverlayPackageEntries(
        string packagePath,
        IDictionary<string, byte[]> destination,
        CancellationToken cancellationToken)
    {
        using var package = new Package();
        package.Read(packagePath);
        var packageEntries = package.Entries
            ?? throw new InvalidDataException($"VPK contains no entries: {packagePath}");
        foreach (var entry in packageEntries.SelectMany(group => group.Value))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = NormalizeRelativePath(entry.GetFullPath());
            package.ReadEntry(entry, out byte[] data);
            destination[relativePath] = data;
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
        if (!ExtractedSourceLayout.GetOrderedRoots(manifest).Any(Directory.Exists))
        {
            return null;
        }

        var desiredVmdlName = string.IsNullOrWhiteSpace(manifest.RetailMainModel)
            ? null
            : Path.GetFileName(ToSourceVmdlResourcePath(manifest.RetailMainModel));

        var candidates = ExtractedSourceLayout.EnumerateFilesByPriority(manifest, "*.vmdl")
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

    private static bool IsParticleSource(string path) =>
        string.Equals(Path.GetExtension(path), ".vpcf", StringComparison.OrdinalIgnoreCase);

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
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException
                                       or System.ComponentModel.Win32Exception
                                       or NotSupportedException)
            {
                // The process may have exited between the cancellation signal and kill.
            }

            try
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // The process already exited or never reached a waitable state.
            }

            throw;
        }

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

    private static void AppendStageTiming(StringBuilder log, string stage, TimeSpan elapsed) =>
        log.AppendLine($"TIMING {stage}: {elapsed.TotalSeconds:F3}s");

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
        if (message.StartsWith("Overlaying", StringComparison.OrdinalIgnoreCase))
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

    private sealed record CompileBatch(string[] Sources, bool IsParticle);

    private sealed record ParticleCompilationFailures(
        int ExitCode,
        IReadOnlyList<string> SourcePaths,
        IReadOnlyList<int> FormatVersions);

    private sealed record ContentHashResult(
        Dictionary<string, string> Hashes,
        int ReusedCount,
        int HashedCount);

    private sealed class BuildTestState
    {
        public int SchemaVersion { get; set; } = 1;
        public Dictionary<string, string> ContentHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SourceBaselineHashCache
    {
        public int SchemaVersion { get; set; } = 1;
        public string Identity { get; set; } = string.Empty;
        public Dictionary<string, string> Hashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PreparedContentHashCache
    {
        public int SchemaVersion { get; set; } = 1;
        public string RootIdentity { get; set; } = string.Empty;
        public Dictionary<string, PreparedContentHashEntry> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PreparedContentHashEntry
    {
        public long Length { get; set; }
        public long LastWriteTimeUtcTicks { get; set; }
        public string Sha256 { get; set; } = string.Empty;
    }

    private sealed class CompileOutputInvalidation : IDisposable
    {
        private readonly string _gameRoot;
        private readonly string _backupRoot;
        private readonly IReadOnlyList<InvalidatedOutput> _outputs;
        private bool _completed;

        private CompileOutputInvalidation(
            string gameRoot,
            string backupRoot,
            IReadOnlyList<InvalidatedOutput> outputs)
        {
            _gameRoot = gameRoot;
            _backupRoot = backupRoot;
            _outputs = outputs;
        }

        internal static CompileOutputInvalidation Begin(
            string contentRoot,
            string gameRoot,
            string metadataFolder,
            IReadOnlyCollection<string> compileTargets,
            IReadOnlyCollection<string> changedRelativePaths,
            StringBuilder log)
        {
            var relativeOutputs = compileTargets
                .Select(path => NormalizeRelativePath(Path.GetRelativePath(contentRoot, path)))
                .Select(GetCompiledRelativePath)
                .Where(path => path is not null)
                .Select(path => path!)
                .Concat(changedRelativePaths
                    .Select(GetDependencyCompiledRelativePath)
                    .Where(path => path is not null)
                    .Select(path => path!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var backupRoot = Path.Combine(
                metadataFolder,
                $"compile-output-backup-{Guid.NewGuid():N}");
            var outputs = new List<InvalidatedOutput>(relativeOutputs.Length);
            var transaction = new CompileOutputInvalidation(gameRoot, backupRoot, outputs);

            try
            {
                foreach (var relative in relativeOutputs)
                {
                    var output = SafePath.ResolveUnderRoot(
                        gameRoot,
                        ToWindowsPath(relative),
                        "Compiled output selected for rebuild");
                    var existed = File.Exists(output);
                    if (existed)
                    {
                        var backup = SafePath.ResolveUnderRoot(
                            backupRoot,
                            ToWindowsPath(relative),
                            "Compiled output rebuild backup");
                        Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                        File.Copy(output, backup, overwrite: true);
                        outputs.Add(new InvalidatedOutput(relative, Existed: true));
                        File.Delete(output);
                    }
                    else
                    {
                        outputs.Add(new InvalidatedOutput(relative, Existed: false));
                    }
                }

                log.AppendLine(
                    $"Invalidated compiled outputs before forced rebuild: {outputs.Count(output => output.Existed)} existing, " +
                    $"{outputs.Count(output => !output.Existed)} already missing.");
                return transaction;
            }
            catch
            {
                transaction.Restore(log);
                throw;
            }
        }

        internal void Commit()
        {
            if (_completed)
            {
                return;
            }

            DeleteBackupDirectory();
            _completed = true;
        }

        internal void Restore(StringBuilder? log)
        {
            if (_completed)
            {
                return;
            }

            foreach (var entry in _outputs.Reverse())
            {
                var output = SafePath.ResolveUnderRoot(
                    _gameRoot,
                    ToWindowsPath(entry.RelativePath),
                    "Compiled output restored after failed rebuild");
                if (File.Exists(output))
                {
                    File.Delete(output);
                }

                if (!entry.Existed)
                {
                    continue;
                }

                var backup = SafePath.ResolveUnderRoot(
                    _backupRoot,
                    ToWindowsPath(entry.RelativePath),
                    "Compiled output rebuild backup restore");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.Copy(backup, output, overwrite: true);
            }

            _completed = true;
            DeleteBackupDirectory();
            log?.AppendLine("Restored previous compiled outputs after failed forced rebuild.");
        }

        public void Dispose()
        {
            Restore(log: null);
        }

        private void DeleteBackupDirectory()
        {
            if (Directory.Exists(_backupRoot))
            {
                try
                {
                    Directory.Delete(_backupRoot, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Cleanup is best-effort. A stale private backup is safer than
                    // turning a successful compile into a failed build.
                }
            }
        }

        private sealed record InvalidatedOutput(string RelativePath, bool Existed);
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
