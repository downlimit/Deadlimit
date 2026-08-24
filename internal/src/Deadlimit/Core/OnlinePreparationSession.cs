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
                "ONLINE PREPARATION needs prepared CSDK content first. Run PREPARE FOR CSDK once and try again.");
        }

        var sourceVmdlFullPath = Path.GetFullPath(manifest.SourceVmdl);
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
        var addonContentRoot = Path.Combine(paths.CsdkContentRoot, "citadel_addons", addonName);
        var textureTargetFolder = Path.Combine(addonContentRoot, "materials", addonName, "textures");

        var rootDmxFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.dmx", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFullPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rootDmxFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "ONLINE PREPARATION found no root-level DMX files in the current project.");
        }

        var dmxMappings = RetailVmdlInheritance.ResolveArtistDmxTargets(
            sourceVmdlFullPath,
            manifest.Hero,
            rootDmxFiles);

        var dmxTargets = dmxMappings.ToDictionary(
            mapping => Path.GetFullPath(mapping.ArtistDmxPath),
            mapping => Path.Combine(
                addonContentRoot,
                mapping.TargetResourcePath.Replace('/', Path.DirectorySeparatorChar)),
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
            $"ONLINE PREPARATION watcher error: {e.GetException().Message}. Run a normal PREPARE FOR CSDK to re-establish the live-sync baseline.",
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
            _debounceTimer.Change(DebounceMilliseconds, Timeout.Infinite);
        }
    }

    private void QueueProcessPending()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_processing)
            {
                _rerunRequested = true;
                return;
            }

            _processing = true;
        }

        _ = Task.Run(ProcessPendingLoop);
    }

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
                    if (!_rerunRequested && _pendingPaths.Count == 0)
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
                $"ONLINE PREPARATION sync failed: {ex.Message}. Run a normal PREPARE FOR CSDK before continuing live sync.",
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

        if (!_knownRelevantFiles.SetEquals(currentRelevantFiles))
        {
            MarkPrepareRequired(
                "ONLINE PREPARATION detected a new, deleted, or renamed root DMX/texture file. A normal PREPARE FOR CSDK is required to rebuild project structure and bindings.",
                null);
            return;
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
            if (extension.Equals(".dmx", StringComparison.OrdinalIgnoreCase))
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
                        $"ONLINE PREPARATION detected changed material references in {Path.GetFileName(sourcePath)}. A normal PREPARE FOR CSDK is required before this DMX can be synchronized safely.",
                        sourcePath);
                    continue;
                }

                if (!_dmxTargets.TryGetValue(sourcePath, out var dmxTarget))
                {
                    MarkPrepareRequired(
                        $"ONLINE PREPARATION has no prepared DMX target for {Path.GetFileName(sourcePath)}. Run a normal PREPARE FOR CSDK.",
                        sourcePath);
                    continue;
                }

                CopyStable(sourcePath, dmxTarget);
                _sourceHashes[sourcePath] = hash;
                RaiseUpdated(
                    $"ONLINE PREPARATION synchronized DMX: {Path.GetFileName(sourcePath)}",
                    sourcePath,
                    PrepareRequired);
                continue;
            }

            var textureTarget = Path.Combine(_textureTargetFolder, Path.GetFileName(sourcePath));
            CopyStable(sourcePath, textureTarget);
            _sourceHashes[sourcePath] = hash;
            RaiseUpdated(
                $"ONLINE PREPARATION synchronized texture: {Path.GetFileName(sourcePath)}",
                sourcePath,
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
