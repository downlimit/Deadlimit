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
    string LogPath)
{
    public bool ImportedCompiledPayload { get; init; }
    public int InspectedCompiledModelCount { get; init; }
    public int RepairedCompiledModelCount { get; init; }
}

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
        if (manifest.Mode == ProjectMode.ImportedVpk)
        {
            return new ImportedVpkBuildAndTestService(_paths)
                .Build(manifest, progress, cancellationToken);
        }

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
            Report(progress, 2, LocalizedText.T("Starting Build & Test...", "Запуск сборки для теста..."));

            if (canIncrement && Directory.Exists(addonGameRoot))
            {
                preservedGameBackup = addonGameRoot + $".deadlimit-backup-{Guid.NewGuid():N}";
                Report(progress, 4, LocalizedText.T("Preserving previous compiled output for incremental prepare...", "Сохранение предыдущего compiled output для инкрементальной подготовки..."));
                Directory.Move(addonGameRoot, preservedGameBackup);
                log.AppendLine($"Preserved previous game output: {preservedGameBackup}");
            }

            Report(progress, 6, LocalizedText.T("Preparing current DMX, materials and textures...", "Подготовка текущих DMX, материалов и текстур..."));
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

            Report(progress, 30, LocalizedText.T("Authoring content synchronized.", "Authoring content синхронизирован."));
            log.AppendLine($"Prepare log: {prepare.LogPath}");
            log.AppendLine($"Prepared content root: {prepare.AddonContentRoot}");

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, 33, LocalizedText.T("Comparing prepared content with the previous successful build...", "Сравнение подготовленного content с предыдущей успешной сборкой..."));

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
                Report(progress, 40, LocalizedText.T($"Compiling {compileTargets.Count} changed asset(s)...", $"Компиляция изменённых ресурсов: {compileTargets.Count}..."));
                await CompileInBatchesAsync(compileTargets, log, progress, cancellationToken);
                Report(progress, 79, LocalizedText.T("Verifying compiled outputs...", "Проверка скомпилированных файлов..."));
                VerifyCompiledOutputs(prepare.AddonContentRoot, addonGameRoot, compileTargets);
            }
            else
            {
                Report(progress, 79, LocalizedText.T("No Source 2 compile inputs changed; reusing verified compiled output...", "Исходники Source 2 для компиляции не изменились; используется проверенный compiled output..."));
                log.AppendLine("No compile inputs changed and all expected direct outputs exist; ResourceCompiler skipped.");
            }

            var compiledMainModel = GetCompiledMainModelPath(manifest, addonGameRoot);
            var mainModelWasCompiled = compileTargets.Contains(prepare.SourceVmdlPath);
            var ag2Applied = false;

            if (mainModelWasCompiled)
            {
                Report(progress, 83, LocalizedText.T("Restoring AnimGraph2 / NmSkeleton on the compiled character model...", "Восстановление AnimGraph2 / NmSkeleton в скомпилированной модели персонажа..."));
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
            Report(progress, 90, LocalizedText.T("Packing VPK directly into retail Deadlock addons...", "Упаковка VPK непосредственно в retail addons Deadlock..."));

            var retailAddonsRoot = Path.Combine(_paths.RetailDeadlockRoot, "game", "citadel", "addons");
            Directory.CreateDirectory(retailAddonsRoot);
            var vpkPath = Path.Combine(retailAddonsRoot, $"pak{releaseSlot:D2}_dir.vpk");

            PackVpk(addonGameRoot, vpkPath, log, progress, cancellationToken);

            SaveState(statePath, new BuildTestState
            {
                ContentHashes = currentHashes,
            });

            Report(progress, 100, LocalizedText.T("Build & Test complete.", "Сборка для теста завершена."));
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
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        for (var offset = 0; offset < ordered.Length; offset += CompileBatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = ordered.Skip(offset).Take(CompileBatchSize).ToArray();
            var args = new List<string>
            {
                "-f",
                "-nop4",
                "-game",
                _paths.CsdkGameRoot,
            };
            args.AddRange(batch);

            var batchNumber = offset / CompileBatchSize + 1;
            var batchTotal = (int)Math.Ceiling(ordered.Length / (double)CompileBatchSize);
            var compilePercent = 40 + (int)Math.Round(
                36d * Math.Min(offset + batch.Length, ordered.Length) / Math.Max(ordered.Length, 1));
            Report(
                progress,
                Math.Min(76, compilePercent),
                LocalizedText.T(
                    $"ResourceCompiler batch {batchNumber}/{batchTotal}...",
                    $"ResourceCompiler пакет {batchNumber}/{batchTotal}..."));

            log.AppendLine($"ResourceCompiler batch {batchNumber}/{batchTotal}:");
            foreach (var source in batch)
            {
                log.AppendLine($"  {source}");
            }

            var result = await ProcessRunner.RunAsync(
                _paths.ResourceCompilerPath,
                args,
                workingDirectory: _paths.CsdkBinRoot,
                cancellationToken: cancellationToken);

            log.AppendLine(result.StandardOutput);
            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                log.AppendLine(result.StandardError);
            }

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"ResourceCompiler failed with exit code {result.ExitCode}.\n\n{result.StandardError}\n\nSee log for full output.");
            }
        }
    }

    private static bool IsDirectCompileSource(string path)
    {
        var extension = Path.GetExtension(path);
        return DirectCompileExtensions.Contains(extension);
    }

    private static void VerifyCompiledOutputs(
        string contentRoot,
        string gameRoot,
        IEnumerable<string> sources)
    {
        var missing = new List<string>();
        foreach (var source in sources)
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
                "Compiled output verification");
            if (!File.Exists(output))
            {
                missing.Add(compiledRelative);
            }
        }

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "ResourceCompiler completed but expected compiled outputs are missing:\n" +
                string.Join("\n", missing.Select(path => $"- {path}")));
        }
    }

    private static string? GetCompiledRelativePath(string sourceRelative)
    {
        var extension = Path.GetExtension(sourceRelative);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        if (string.Equals(extension, ".vtex", StringComparison.OrdinalIgnoreCase))
        {
            return sourceRelative + "_c";
        }

        if (DirectCompileExtensions.Contains(extension))
        {
            return sourceRelative + "_c";
        }

        return null;
    }

    private static string GetCompiledMainModelPath(ProjectManifest manifest, string addonGameRoot)
    {
        if (!string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            return SafePath.ResolveUnderRoot(
                addonGameRoot,
                ToWindowsPath(manifest.RetailMainModel),
                "Compiled main model");
        }

        var hero = manifest.Hero?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(hero))
        {
            throw new InvalidOperationException("Hero is not selected for BUILD & TEST.");
        }

        return Path.Combine(addonGameRoot, "models", "heroes", hero, $"{hero}.vmdl_c");
    }

    private void ApplyAg2(
        ProjectManifest manifest,
        string compiledMainModel,
        StringBuilder log,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(compiledMainModel))
        {
            throw new FileNotFoundException(
                "Compiled main VMDL_C was not found for AnimGraph2 / NmSkeleton patching.",
                compiledMainModel);
        }

        var deadlockTools = _paths.DeadlockToolsExePath;
        if (!File.Exists(deadlockTools))
        {
            throw new FileNotFoundException(
                "DeadlockTools.exe is required to restore AnimGraph2 / NmSkeleton references after compile.",
                deadlockTools);
        }

        var sourceVmdl = RetailVmdlInheritance.FindRetailVmdl(manifest)
            ?? throw new InvalidOperationException(
                "Retail source VMDL is missing; extract hero source before Build & Test so the NmSkeleton reference can be recovered.");
        var nmSkeletonRef = FindNmSkeletonReference(sourceVmdl)
            ?? throw new InvalidOperationException(
                "Retail source VMDL does not expose an NmSkeleton reference required by current Deadlock.");

        var family = InferRetailFamily(manifest.RetailMainModel)
            ?? throw new InvalidOperationException(
                $"Could not infer the retail hero family from: {manifest.RetailMainModel}");
        var heroToken = NormalizeHeroToken(manifest.Hero)
            ?? throw new InvalidOperationException("Hero is not selected for Build & Test.");

        var args = new List<string>
        {
            "add",
            "ag2",
            compiledMainModel,
            "-h",
            heroToken,
            "-f",
            family,
            "--override-skeleton",
            nmSkeletonRef,
        };

        log.AppendLine("DeadlockTools add ag2:");
        log.AppendLine($"  model={compiledMainModel}");
        log.AppendLine($"  hero={heroToken}");
        log.AppendLine($"  family={family}");
        log.AppendLine($"  skeleton={nmSkeletonRef}");

        var result = ProcessRunner.RunAsync(
                deadlockTools,
                args,
                workingDirectory: Path.GetDirectoryName(deadlockTools),
                cancellationToken: cancellationToken)
            .GetAwaiter()
            .GetResult();

        log.AppendLine(result.StandardOutput);
        if (!string.IsNullOrWhiteSpace(result.StandardError))
        {
            log.AppendLine(result.StandardError);
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"DeadlockTools add ag2 failed with exit code {result.ExitCode}.\n\n{result.StandardError}");
        }

        manifest.NmSkeletonRef = nmSkeletonRef;
    }

    private static string? FindNmSkeletonReference(string sourceVmdl)
    {
        var text = File.ReadAllText(sourceVmdl);
        var match = NmSkeletonRegex.Match(text);
        return match.Success
            ? NormalizeRelativePath(match.Value)
            : null;
    }

    private static string? InferRetailFamily(string? retailMainModel)
    {
        if (string.IsNullOrWhiteSpace(retailMainModel))
        {
            return null;
        }

        var normalized = NormalizeRelativePath(retailMainModel);
        foreach (var family in new[] { "heroes_wip", "heroes_staging", "heroes" })
        {
            if (normalized.StartsWith($"models/{family}/", StringComparison.OrdinalIgnoreCase))
            {
                return family;
            }
        }
        return null;
    }

    private static string? NormalizeHeroToken(string? hero)
    {
        if (string.IsNullOrWhiteSpace(hero))
        {
            return null;
        }

        return Regex.Replace(hero.Trim().ToLowerInvariant(), "[^a-z0-9_]+", "_")
            .Trim('_');
    }

    private static int MapPrepareProgress(string message)
    {
        if (message.StartsWith("Cleaning stale", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Очистка устаревшего", StringComparison.OrdinalIgnoreCase))
        {
            return 8;
        }
        if (message.StartsWith("Refreshing retail", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Обновление retail", StringComparison.OrdinalIgnoreCase))
        {
            return 14;
        }
        if (message.StartsWith("Overlaying artist", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Наложение пользовательских", StringComparison.OrdinalIgnoreCase))
        {
            return 20;
        }
        if (message.StartsWith("Preparing addon-owned", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Подготовка custom", StringComparison.OrdinalIgnoreCase))
        {
            return 25;
        }
        if (message.StartsWith("Applying narrow", StringComparison.OrdinalIgnoreCase) || message.StartsWith("Применение необходимых", StringComparison.OrdinalIgnoreCase))
        {
            return 28;
        }
        return 10;
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static string ToWindowsPath(string path) =>
        NormalizeRelativePath(path).Replace('/', Path.DirectorySeparatorChar);

    private static void Report(
        IProgress<BuildAndTestProgress>? progress,
        int percent,
        string message) => progress?.Report(new BuildAndTestProgress(message, percent));

    private static BuildTestState? TryLoadState(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<BuildTestState>(File.ReadAllText(path));
            if (state?.ContentHashes is null)
            {
                return null;
            }

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
        AtomicFile.WriteJson(
            path,
            state,
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static void PackVpk(
        string addonGameRoot,
        string vpkPath,
        StringBuilder log,
        IProgress<BuildAndTestProgress>? progress,
        CancellationToken cancellationToken)
    {
        var files = Directory.EnumerateFiles(addonGameRoot, "*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                $"Compiled addon game folder is empty: {addonGameRoot}");
        }

        var retailAddonsRoot = Path.GetDirectoryName(vpkPath)
            ?? throw new InvalidOperationException("Retail VPK destination has no parent folder.");
        Directory.CreateDirectory(retailAddonsRoot);

        var finalBaseName = GetVpkBaseName(vpkPath);
        var temporaryBaseName = $"{finalBaseName}_deadlimit_tmp_{Guid.NewGuid():N}";
        var temporaryVpk = Path.Combine(retailAddonsRoot, $"{temporaryBaseName}_dir.vpk");

        try
        {
            using (var package = new Package { Version = 2 })
            {
                var added = 0;
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relative = NormalizeRelativePath(Path.GetRelativePath(addonGameRoot, file));
                    package.AddFile(relative, File.ReadAllBytes(file));
                    added++;

                    if (added % 25 == 0 || added == files.Length)
                    {
                        var percent = 90 + (int)Math.Round(4d * added / files.Length);
                        Report(
                            progress,
                            Math.Min(94, percent),
                            LocalizedText.T(
                                $"Packing VPK files {added}/{files.Length}...",
                                $"Упаковка файлов VPK {added}/{files.Length}..."));
                    }
                }

                package.Write(temporaryVpk);
            }

            if (!File.Exists(temporaryVpk))
            {
                throw new InvalidOperationException(
                    $"ValvePak completed without creating the expected VPK: {temporaryVpk}");
            }

            Report(progress, 95, LocalizedText.T("Verifying generated VPK...", "Проверка созданного VPK..."));
            VerifyVpk(temporaryVpk);
            DeployVerifiedVpkFamily(temporaryVpk, vpkPath, cancellationToken);
            VerifyVpk(vpkPath);

            log.AppendLine($"Packed VPK from: {addonGameRoot}");
            log.AppendLine($"Packed VPK to: {vpkPath}");
            log.AppendLine($"Packed file count: {files.Length}");
            log.AppendLine("VPK checksums: verified before and after deployment");
        }
        catch (IOException ex) when (File.Exists(vpkPath))
        {
            throw new InvalidOperationException(
                $"Could not replace the retail VPK:\n\n{vpkPath}\n\n" +
                "The archive is probably locked by the running Deadlock client or another process. " +
                "Close Deadlock and retry BUILD & TEST.",
                ex);
        }
        finally
        {
            DeleteVpkFamilyBestEffort(temporaryVpk);
        }
    }

    private static void DeployVerifiedVpkFamily(
        string temporaryVpk,
        string finalVpk,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(finalVpk)
            ?? throw new InvalidOperationException("Retail VPK destination has no parent folder.");
        var temporaryBaseName = GetVpkBaseName(temporaryVpk);
        var finalBaseName = GetVpkBaseName(finalVpk);

        var temporaryFamily = EnumerateVpkFamily(temporaryVpk);
        if (!temporaryFamily.Any(path =>
                string.Equals(path, temporaryVpk, StringComparison.OrdinalIgnoreCase)))
        {
            throw new FileNotFoundException(
                "Verified temporary VPK disappeared before deployment.",
                temporaryVpk);
        }

        var finalFamily = EnumerateVpkFamily(finalVpk);
        var backupDirectory = Path.Combine(
            directory,
            $".deadlimit-vpk-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);

        var backedUp = new List<(string Original, string Backup)>();
        var deployed = new List<string>();

        try
        {
            foreach (var existing in finalFamily)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var backup = Path.Combine(backupDirectory, Path.GetFileName(existing));
                File.Move(existing, backup);
                backedUp.Add((existing, backup));
            }

            var mappings = temporaryFamily
                .Select(path =>
                {
                    var fileName = Path.GetFileName(path);
                    var finalName = string.Equals(
                            path,
                            temporaryVpk,
                            StringComparison.OrdinalIgnoreCase)
                        ? $"{finalBaseName}_dir.vpk"
                        : finalBaseName + fileName[temporaryBaseName.Length..];
                    return (Source: path, Target: Path.Combine(directory, finalName));
                })
                .OrderBy(mapping =>
                    string.Equals(mapping.Source, temporaryVpk, StringComparison.OrdinalIgnoreCase)
                        ? 1
                        : 0)
                .ToArray();

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();
                File.Move(mapping.Source, mapping.Target);
                deployed.Add(mapping.Target);
            }
        }
        catch
        {
            foreach (var deployedPath in deployed)
            {
                try
                {
                    if (File.Exists(deployedPath))
                    {
                        File.Delete(deployedPath);
                    }
                }
                catch (IOException)
                {
                    // Best-effort rollback; preserve the original deployment error.
                }
            }

            foreach (var backup in backedUp)
            {
                try
                {
                    if (File.Exists(backup.Backup))
                    {
                        File.Move(backup.Backup, backup.Original);
                    }
                }
                catch (IOException)
                {
                    // Best-effort rollback; preserve the original deployment error.
                }
            }

            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(backupDirectory))
                {
                    Directory.Delete(backupDirectory, recursive: true);
                }
            }
            catch (IOException)
            {
                // Deployment result remains authoritative; stale backup cleanup is non-fatal.
            }
        }
    }

    private static void VerifyVpk(string path)
    {
        using var package = new Package();
        package.Read(path);
        package.VerifyHashes();
        package.VerifyFileChecksums();
    }

    private static string GetVpkBaseName(string dirVpkPath)
    {
        var fileName = Path.GetFileName(dirVpkPath);
        const string suffix = "_dir.vpk";
        return fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? fileName[..^suffix.Length]
            : Path.GetFileNameWithoutExtension(fileName);
    }

    private static IReadOnlyList<string> EnumerateVpkFamily(string dirVpkPath)
    {
        var fullDirVpkPath = Path.GetFullPath(dirVpkPath);
        var directory = Path.GetDirectoryName(fullDirVpkPath)!;
        if (!Directory.Exists(directory))
        {
            return Array.Empty<string>();
        }

        var baseName = GetVpkBaseName(fullDirVpkPath);
        var chunkRegex = new Regex(
            $"^{Regex.Escape(baseName)}_\\d{{3}}\\.vpk$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
        var result = Directory.EnumerateFiles(directory, $"{baseName}_*.vpk", SearchOption.TopDirectoryOnly)
            .Where(path => chunkRegex.IsMatch(Path.GetFileName(path)))
            .Select(Path.GetFullPath)
            .ToList();
        if (File.Exists(fullDirVpkPath))
        {
            result.Add(fullDirVpkPath);
        }

        return result
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void DeleteVpkFamilyBestEffort(string dirVpkPath)
    {
        foreach (var path in EnumerateVpkFamily(dirVpkPath))
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // Preserve the primary build/deploy result.
            }
        }
    }

    private sealed class BuildTestState
    {
        public Dictionary<string, string> ContentHashes { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }
}
