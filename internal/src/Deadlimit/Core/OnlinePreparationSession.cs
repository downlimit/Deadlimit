using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record OnlinePreparationUpdate(
    string Message,
    string? SourcePath,
    bool PrepareRequired);

internal sealed class OnlinePreparationSession : IDisposable
{
    private const int DebounceMilliseconds = 900;
    private const int VertexColorPairDebounceMilliseconds = 6660;
    private const int StableReadAttempts = 6;
    private const int StableReadDelayMilliseconds = 180;

    private static readonly HashSet<string> TextureExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".tga",
        ".jpg",
        ".jpeg",
        ".tif",
        ".tiff",
    };

    private static readonly Regex DmxMaterialReferenceRegex = new(
        @"materials/(?:[^\0\r\n\t""]+?\.vmat|[A-Za-z0-9_./\\-]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly string _projectFolder;
    private readonly string _textureTargetFolder;
    private readonly Dictionary<string, string> _dmxTargets;
    private readonly Dictionary<string, string> _sourceHashes;
    private readonly Dictionary<string, string[]> _dmxMaterialReferences;
    private readonly HashSet<string> _knownRelevantFiles;
    private readonly HashSet<string> _pendingPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dmxAwaitingVertexColorSidecar = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly System.Threading.Timer _debounceTimer;

    private bool _processing;
    private bool _rerunRequested;
    private bool _prepareRequired;
    private bool _disposed;

    private OnlinePreparationSession(
        string projectFolder,
        string textureTargetFolder,
        Dictionary<string, string> dmxTargets,
        Dictionary<string, string> sourceHashes,
        Dictionary<string, string[]> dmxMaterialReferences,
        HashSet<string> knownRelevantFiles)
    {
        _projectFolder = projectFolder;
        _textureTargetFolder = textureTargetFolder;
        _dmxTargets = dmxTargets;
        _sourceHashes = sourceHashes;
        _dmxMaterialReferences = dmxMaterialReferences;
        _knownRelevantFiles = knownRelevantFiles;

        _debounceTimer = new System.Threading.Timer(
            _ => QueueProcessPending(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);

        _watcher = new FileSystemWatcher(projectFolder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName
                | NotifyFilters.LastWrite
                | NotifyFilters.Size
                | NotifyFilters.CreationTime,
            EnableRaisingEvents = false,
        };
        _watcher.Changed += OnFileSystemEvent;
        _watcher.Created += OnFileSystemEvent;
        _watcher.Deleted += OnFileSystemEvent;
        _watcher.Renamed += OnRenamed;
        _watcher.Error += OnWatcherError;
    }

    public event EventHandler<OnlinePreparationUpdate>? Updated;

    public bool PrepareRequired
    {
        get
        {
            lock (_gate)
            {
                return _prepareRequired;
            }
        }
    }

    public static OnlinePreparationSession Start(ProjectManifest manifest, DeadlimitPaths paths)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        if (string.IsNullOrWhiteSpace(manifest.SourceVmdl) || !File.Exists(manifest.SourceVmdl))
        {
            throw new InvalidOperationException(
                LocalizedText.T("ONLINE PREPARATION needs prepared CSDK content first. Run PREPARE FOR CSDK once and try again.", "Для ОНЛАЙН-ПОДГОТОВКИ сначала нужен подготовленный CSDK content. Один раз выполните ПОДГОТОВИТЬ ДЛЯ CSDK и повторите попытку."));
        }

        var sourceVmdlFullPath = SafePath.EnsureUnderRoot(
            paths.CsdkContentRoot,
            manifest.SourceVmdl,
            "Prepared VMDL");
        var relativeVmdl = Path.GetRelativePath(paths.CsdkContentRoot, sourceVmdlFullPath);
        var parts = relativeVmdl
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 3
            || !string.Equals(parts[0], "citadel_addons", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Prepared VMDL is not under the configured CSDK content/citadel_addons root: {sourceVmdlFullPath}");
        }

        var addonName = parts[1];
        var addonContentRoot = SafePath.ResolveUnderRoot(
            paths.CsdkContentRoot,
            Path.Combine("citadel_addons", addonName),
            "Online addon content root");
        var textureTargetFolder = Path.Combine(addonContentRoot, "materials", addonName, "textures");

        var rootDmxFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.dmx", SearchOption.TopDirectoryOnly)
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rootDmxFiles.Length == 0)
        {
            throw new InvalidOperationException(
                LocalizedText.T("ONLINE PREPARATION found no root-level DMX files in the current project.", "ОНЛАЙН-ПОДГОТОВКА не нашла DMX-файлы в корне текущего проекта."));
        }

        var dmxMappings = ArtistDmxTargetResolver.Resolve(
            sourceVmdlFullPath,
            manifest.Hero,
            rootDmxFiles);

        var dmxTargets = dmxMappings.ToDictionary(
            mapping => Path.GetFullPath(mapping.ArtistDmxPath),
            mapping => SafePath.ResolveUnderRoot(
                addonContentRoot,
                mapping.TargetResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Online DMX target from VMDL"),
            StringComparer.OrdinalIgnoreCase);

        var knownRelevantFiles = EnumerateRelevantFiles(manifest.ProjectFolder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var dmxMaterialReferences = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in knownRelevantFiles)
        {
            sourceHashes[path] = ComputeStableHash(path);
            if (Path.GetExtension(path).Equals(".dmx", StringComparison.OrdinalIgnoreCase))
            {
                dmxMaterialReferences[path] = ReadDmxMaterialReferences(path);
            }
        }

        Directory.CreateDirectory(textureTargetFolder);

        var session = new OnlinePreparationSession(
            Path.GetFullPath(manifest.ProjectFolder),
            textureTargetFolder,
            dmxTargets,
            sourceHashes,
            dmxMaterialReferences,
            knownRelevantFiles);
        session._watcher.EnableRaisingEvents = true;
        return session;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        QueuePath(e.FullPath);
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        QueuePath(e.OldFullPath);
        QueuePath(e.FullPath);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        MarkPrepareRequired(
            LocalizedText.T($"ONLINE PREPARATION watcher error: {e.GetException().Message}. Run a normal PREPARE FOR CSDK to re-establish the live-sync baseline.", "Ошибка наблюдения за файлами ОНЛАЙН-ПОДГОТОВКИ. Выполните обычный ПОДГОТОВИТЬ ДЛЯ CSDK, чтобы восстановить базовую версию онлайн-синхронизации."),
            null);
    }

    private void QueuePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!IsRelevantPath(fullPath))
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pendingPaths.Add(fullPath);
            ResetVertexColorPairWaitForChangedSource(fullPath);
            _debounceTimer.Change(
                _dmxAwaitingVertexColorSidecar.Count == 0
                    ? DebounceMilliseconds
                    : VertexColorPairDebounceMilliseconds - DebounceMilliseconds,
                Timeout.Infinite);
        }
    }

    private void ResetVertexColorPairWaitForChangedSource(string fullPath)
    {
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase))
        {
            _dmxAwaitingVertexColorSidecar.Remove(fullPath);
            return;
        }

        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
            && VertexColorSidecarService.IsSidecarPath(fullPath)
            && File.Exists(fullPath))
        {
            _dmxAwaitingVertexColorSidecar.Remove(
                Path.GetFullPath(VertexColorSidecarService.GetArtistDmxPath(fullPath)));
        }
    }

    private void QueueProcessPending()
    {
        if (TryScheduleVertexColorPairWait())
        {
            return;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _dmxAwaitingVertexColorSidecar.Clear();
            if (_processing)
            {
                _rerunRequested = true;
                return;
            }

            _processing = true;
        }

        _ = Task.Run(ProcessPendingLoop);
    }

    private bool TryScheduleVertexColorPairWait()
    {
        string[] candidates;
        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            candidates = _pendingPaths
                .Where(path => Path.GetExtension(path).Equals(".dmx", StringComparison.OrdinalIgnoreCase))
                .Where(path => !_dmxAwaitingVertexColorSidecar.Contains(path))
                .Where(File.Exists)
                .ToArray();
        }

        var needsPairWait = new List<string>();
        foreach (var path in candidates)
        {
            try
            {
                if (ShouldWaitForVertexColorPair(VertexColorSourceGuard.Inspect(path)))
                {
                    needsPairWait.Add(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The base debounce can still land on an exporter-held file. Give that
                // revision one bounded pair-wait window before the stable-read path retries.
                needsPairWait.Add(path);
            }
        }

        if (needsPairWait.Count == 0)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            foreach (var path in needsPairWait.Where(_pendingPaths.Contains))
            {
                _dmxAwaitingVertexColorSidecar.Add(path);
            }

            _debounceTimer.Change(
                VertexColorPairDebounceMilliseconds - DebounceMilliseconds,
                Timeout.Infinite);
            return true;
        }
    }

    private static bool ShouldWaitForVertexColorPair(VertexColorSourceState state) =>
        state.NeedsExternalSidecar && (!state.SidecarExists || !state.SidecarCurrent);

    private void ProcessPendingLoop()
    {
        try
        {
            while (true)
            {
                string[] pending;
                lock (_gate)
                {
                    pending = _pendingPaths.ToArray();
                    _pendingPaths.Clear();
                    _rerunRequested = false;
                }

                ProcessPending(pending);

                lock (_gate)
                {
                    if (_pendingPaths.Count == 0)
                    {
                        _processing = false;
                        return;
                    }

                    if (!_rerunRequested)
                    {
                        _processing = false;
                        return;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            MarkPrepareRequired(
                LocalizedText.T($"ONLINE PREPARATION sync failed: {ex.Message}. Run a normal PREPARE FOR CSDK before continuing live sync.", "Ошибка онлайн-синхронизации. Перед продолжением выполните обычный ПОДГОТОВИТЬ ДЛЯ CSDK."),
                null);

            lock (_gate)
            {
                _processing = false;
            }
        }
    }

    private void ProcessPending(IReadOnlyCollection<string> pending)
    {
        if (pending.Count == 0)
        {
            return;
        }

        var currentRelevantFiles = EnumerateRelevantFiles(_projectFolder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var membershipChanges = _knownRelevantFiles
            .Except(currentRelevantFiles, StringComparer.OrdinalIgnoreCase)
            .Concat(currentRelevantFiles.Except(_knownRelevantFiles, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (membershipChanges.Any(path => !VertexColorSidecarService.IsSidecarPath(path)))
        {
            MarkPrepareRequired(
                LocalizedText.T("ONLINE PREPARATION detected a new, deleted, or renamed root DMX/texture file. A normal PREPARE FOR CSDK is required to rebuild project structure and bindings.", "ОНЛАЙН-ПОДГОТОВКА обнаружила новый, удалённый или переименованный DMX/файл текстуры в корне проекта. Для перестроения структуры и привязок требуется обычный ПОДГОТОВИТЬ ДЛЯ CSDK."),
                null);
            return;
        }

        foreach (var sidecarPath in membershipChanges)
        {
            if (File.Exists(sidecarPath))
            {
                _knownRelevantFiles.Add(sidecarPath);
            }
            else
            {
                _knownRelevantFiles.Remove(sidecarPath);
                _sourceHashes.Remove(sidecarPath);
                ReportProtectedSidecarRemoval(sidecarPath);
            }
        }

        foreach (var sourcePath in pending
                     .Where(_knownRelevantFiles.Contains)
                     .Where(File.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var hash = ComputeStableHash(sourcePath);
            if (_sourceHashes.TryGetValue(sourcePath, out var previousHash)
                && string.Equals(hash, previousHash, StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(sourcePath);
            var isVertexColorSidecar = extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
                && VertexColorSidecarService.IsSidecarPath(sourcePath);
            var isDmx = extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase);

            if ((isDmx || isVertexColorSidecar) && PrepareRequired)
            {
                if (isVertexColorSidecar)
                {
                    _sourceHashes[sourcePath] = hash;
                }
                else
                {
                    _sourceHashes[sourcePath] = hash;
                    _dmxMaterialReferences[sourcePath] = ReadDmxMaterialReferences(sourcePath);
                }

                RaiseUpdated(
                    LocalizedText.T(
                    $"ONLINE PREPARATION kept the existing prepared DMX unchanged because a full PREPARE is already required. {Path.GetFileName(sourcePath)} was not synchronized.",
                    $"ОНЛАЙН-ПОДГОТОВКА сохранила текущий подготовленный DMX без изменений, потому что уже требуется полный PREPARE. {Path.GetFileName(sourcePath)} не синхронизирован."),
                    sourcePath,
                    prepareRequired: true);
                continue;
            }

            if (isVertexColorSidecar)
            {
                var artistDmx = VertexColorSidecarService.GetArtistDmxPath(sourcePath);
                if (!File.Exists(artistDmx) || !_dmxTargets.TryGetValue(artistDmx, out var preparedDmx))
                {
                    MarkPrepareRequired(
                        LocalizedText.T($"ONLINE PREPARATION cannot match {Path.GetFileName(sourcePath)} to a prepared artist DMX. Run a normal PREPARE FOR CSDK.", $"ОНЛАЙН-ПОДГОТОВКА не может сопоставить {Path.GetFileName(sourcePath)} с подготовленным DMX. Выполните обычный ПОДГОТОВИТЬ ДЛЯ CSDK."),
                        sourcePath);
                    continue;
                }

                var committed = TryStageAndCommitDmx(artistDmx, preparedDmx, out var staged);
                _sourceHashes[sourcePath] = hash;
                _sourceHashes[artistDmx] = ComputeStableHash(artistDmx);
                if (!committed)
                {
                    RaiseUpdated(
                        LocalizedText.T(
                        $"ONLINE PREPARATION kept the previous prepared DMX. Waiting for a valid Vertex Color source pair for {Path.GetFileName(artistDmx)}. Vertex Color [{staged.VertexColor.Status}]: {staged.Message}",
                        $"ОНЛАЙН-ПОДГОТОВКА сохранила предыдущий подготовленный DMX. Ожидается корректная пара исходников Vertex Color для {Path.GetFileName(artistDmx)}."),
                        sourcePath,
                        PrepareRequired);
                    continue;
                }

                RaiseUpdated(
                    LocalizedText.T($"ONLINE PREPARATION synchronized Vertex Color source: {Path.GetFileName(sourcePath)}. {staged.Message}", $"ОНЛАЙН-ПОДГОТОВКА синхронизировала исходник Vertex Color: {Path.GetFileName(sourcePath)}."),
                    sourcePath,
                    PrepareRequired);
                continue;
            }

            if (isDmx)
            {
                var currentMaterialReferences = ReadDmxMaterialReferences(sourcePath);
                if (!_dmxMaterialReferences.TryGetValue(sourcePath, out var previousMaterialReferences)
                    || !previousMaterialReferences.SequenceEqual(
                        currentMaterialReferences,
                        StringComparer.OrdinalIgnoreCase))
                {
                    _sourceHashes[sourcePath] = hash;
                    _dmxMaterialReferences[sourcePath] = currentMaterialReferences;
                    MarkPrepareRequired(
                        LocalizedText.T($"ONLINE PREPARATION detected changed material references in {Path.GetFileName(sourcePath)}. A normal PREPARE FOR CSDK is required before this DMX can be synchronized safely.", $"ОНЛАЙН-ПОДГОТОВКА обнаружила изменённые ссылки на материалы в {Path.GetFileName(sourcePath)}. Перед безопасной синхронизацией этого DMX требуется обычный ПОДГОТОВИТЬ ДЛЯ CSDK."),
                        sourcePath);
                    continue;
                }

                if (!_dmxTargets.TryGetValue(sourcePath, out var dmxTarget))
                {
                    MarkPrepareRequired(
                        LocalizedText.T($"ONLINE PREPARATION has no prepared DMX target for {Path.GetFileName(sourcePath)}. Run a normal PREPARE FOR CSDK.", $"Для {Path.GetFileName(sourcePath)} нет подготовленного целевого DMX. Выполните обычный ПОДГОТОВИТЬ ДЛЯ CSDK."),
                        sourcePath);
                    continue;
                }

                var committed = TryStageAndCommitDmx(sourcePath, dmxTarget, out var staged);
                _sourceHashes[sourcePath] = hash;
                if (!committed)
                {
                    RaiseUpdated(
                        LocalizedText.T(
                        $"ONLINE PREPARATION detected a new DMX but kept the previous prepared copy until its Vertex Color source is safe. {Path.GetFileName(sourcePath)} — Vertex Color [{staged.VertexColor.Status}]: {staged.Message}",
                        $"ОНЛАЙН-ПОДГОТОВКА обнаружила новый DMX, но сохранила предыдущую подготовленную копию до получения безопасного исходника Vertex Color: {Path.GetFileName(sourcePath)}."),
                        sourcePath,
                        PrepareRequired);
                    continue;
                }

                RaiseUpdated(
                    LocalizedText.T(
                    $"ONLINE PREPARATION synchronized DMX: {Path.GetFileName(sourcePath)}. Vertex Color [{staged.VertexColor.Status}]: {staged.Message}",
                    $"ОНЛАЙН-ПОДГОТОВКА синхронизировала DMX: {Path.GetFileName(sourcePath)}."),
                    sourcePath,
                    PrepareRequired);
                continue;
            }

            var textureTarget = Path.Combine(_textureTargetFolder, Path.GetFileName(sourcePath));
            CopyStable(sourcePath, textureTarget);
            _sourceHashes[sourcePath] = hash;
            RaiseUpdated(
                LocalizedText.T($"ONLINE PREPARATION synchronized texture: {Path.GetFileName(sourcePath)}", $"ОНЛАЙН-ПОДГОТОВКА синхронизировала текстуру: {Path.GetFileName(sourcePath)}"),
                sourcePath,
                PrepareRequired);
        }
    }

    private bool TryStageAndCommitDmx(
        string artistDmxPath,
        string targetPath,
        out VertexColorStagedResult stagedResult)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var stagingPath = Path.Combine(
            Path.GetDirectoryName(targetPath)!,
            $".{Path.GetFileNameWithoutExtension(targetPath)}.deadlimit-online-{Guid.NewGuid():N}.dmx");

        try
        {
            CopyStable(artistDmxPath, stagingPath);
            stagedResult = VertexColorSourceGuard.PrepareStagedDmx(artistDmxPath, stagingPath);
            if (!stagedResult.Ready)
            {
                return false;
            }

            File.Move(stagingPath, targetPath, overwrite: true);
            return true;
        }
        finally
        {
            try
            {
                if (File.Exists(stagingPath))
                {
                    File.Delete(stagingPath);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A failed staging cleanup must not replace or invalidate the last good prepared DMX.
            }
        }
    }

    private void ReportProtectedSidecarRemoval(string sidecarPath)
    {
        try
        {
            var artistDmx = VertexColorSidecarService.GetArtistDmxPath(sidecarPath);
            if (!File.Exists(artistDmx))
            {
                return;
            }

            var state = VertexColorSourceGuard.Inspect(artistDmx);
            if (!state.NeedsExternalSidecar)
            {
                return;
            }

            RaiseUpdated(
                $"ONLINE PREPARATION detected removal of {Path.GetFileName(sidecarPath)}. The existing prepared DMX was kept unchanged; live sync will wait for a fresh Vertex Color FBX before replacing it.",
                sidecarPath,
                PrepareRequired);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            RaiseUpdated(
                $"ONLINE PREPARATION detected a Vertex Color sidecar removal and kept the existing prepared DMX unchanged: {ex.Message}",
                sidecarPath,
                PrepareRequired);
        }
    }

    private void MarkPrepareRequired(string message, string? sourcePath)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _prepareRequired = true;
        }

        RaiseUpdated(message, sourcePath, prepareRequired: true);
    }

    private void RaiseUpdated(string message, string? sourcePath, bool prepareRequired)
    {
        Updated?.Invoke(this, new OnlinePreparationUpdate(message, sourcePath, prepareRequired));
    }

    private static IEnumerable<string> EnumerateRelevantFiles(string projectFolder)
    {
        return Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .Where(IsRelevantPath)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRelevantPath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase)
            || (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
                && VertexColorSidecarService.IsSidecarPath(path))
            || TextureExtensions.Contains(extension);
    }

    private static string ComputeStableHash(string path)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < StableReadAttempts; attempt++)
        {
            try
            {
                var before = new FileInfo(path);
                var beforeLength = before.Length;
                var beforeWrite = before.LastWriteTimeUtc;

                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    1024 * 128,
                    FileOptions.SequentialScan);
                var hash = Convert.ToHexString(SHA256.HashData(stream));

                var after = new FileInfo(path);
                if (beforeLength == after.Length && beforeWrite == after.LastWriteTimeUtc)
                {
                    return hash;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
            }

            Thread.Sleep(StableReadDelayMilliseconds);
        }

        throw new IOException($"Could not read a stable snapshot of '{path}'.", lastError);
    }

    private static void CopyStable(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

        Exception? lastError = null;
        for (var attempt = 0; attempt < StableReadAttempts; attempt++)
        {
            try
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lastError = ex;
                Thread.Sleep(StableReadDelayMilliseconds);
            }
        }

        throw new IOException(
            $"Could not copy live authoring source '{sourcePath}' to '{targetPath}'.",
            lastError);
    }

    private static string[] ReadDmxMaterialReferences(string dmxPath)
    {
        var raw = File.ReadAllBytes(dmxPath);
        var text = Encoding.Latin1.GetString(raw).Replace('\\', '/');
        return DmxMaterialReferenceRegex.Matches(text)
            .Select(match => match.Value.TrimEnd('/', '.', '-'))
            .Where(value =>
            {
                var extension = Path.GetExtension(value);
                return extension.Length == 0
                    || extension.Equals(".vmat", StringComparison.OrdinalIgnoreCase);
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher.EnableRaisingEvents = false;
        }

        _watcher.Changed -= OnFileSystemEvent;
        _watcher.Created -= OnFileSystemEvent;
        _watcher.Deleted -= OnFileSystemEvent;
        _watcher.Renamed -= OnRenamed;
        _watcher.Error -= OnWatcherError;
        _watcher.Dispose();
        _debounceTimer.Dispose();
    }
}
