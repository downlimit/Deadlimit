using Deadlimit.Core;

namespace Deadlimit.App;

/// <summary>
/// Keeps project state current without coupling filesystem work to window activation.
/// FileSystemWatcher callbacks are coalesced onto the UI thread so bursts from editors or
/// Explorer produce one settled refresh instead of repainting the Manager repeatedly.
/// </summary>
internal static class ProjectExternalChangeFeature
{
    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        var folderText = projectGroup is null
            ? null
            : FindDescendants<TextBox>(projectGroup).FirstOrDefault(textBox => textBox.ReadOnly);
        var library = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Library", StringComparison.Ordinal)
                || string.Equals(group.Text, "Библиотека", StringComparison.Ordinal)
                || string.Equals(group.Text, "Projects", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проекты", StringComparison.Ordinal))
            ?.Controls.OfType<ListBox>()
            .FirstOrDefault();

        if (folderText is null)
        {
            return;
        }

        var session = new Session(form, folderText, library);
        session.Attach();
    }

    private static IEnumerable<T> FindDescendants<T>(Control root) where T : Control
    {
        foreach (Control child in root.Controls)
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindDescendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    private sealed class Session : IDisposable
    {
        private const int DebounceMilliseconds = 180;

        private readonly MainForm _form;
        private readonly TextBox _folderText;
        private readonly ListBox? _library;
        private readonly System.Windows.Forms.Timer _debounceTimer;

        private FileSystemWatcher? _projectsWatcher;
        private FileSystemWatcher? _selectedProjectWatcher;
        private string? _watchedProjectsRoot;
        private string? _watchedProjectFolder;
        private bool _libraryDirty;
        private bool _projectDirty;
        private bool _headerDirty;
        private bool _metadataDirty;
        private bool _disposed;

        public Session(MainForm form, TextBox folderText, ListBox? library)
        {
            _form = form;
            _folderText = folderText;
            _library = library;
            _debounceTimer = new System.Windows.Forms.Timer { Interval = DebounceMilliseconds };
            _debounceTimer.Tick += OnDebounceTick;
        }

        public void Attach()
        {
            _form.Shown += OnShown;
            _folderText.TextChanged += OnSelectedProjectChanged;
            _form.FormClosed += OnFormClosed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _form.Shown -= OnShown;
            _folderText.TextChanged -= OnSelectedProjectChanged;
            _form.FormClosed -= OnFormClosed;
            _debounceTimer.Stop();
            _debounceTimer.Tick -= OnDebounceTick;
            _debounceTimer.Dispose();
            DisposeProjectsWatcher();
            DisposeSelectedProjectWatcher();
        }

        private void OnShown(object? sender, EventArgs e)
        {
            RetargetProjectsWatcher();
            RetargetSelectedProjectWatcher();
        }

        private void OnSelectedProjectChanged(object? sender, EventArgs e)
        {
            RetargetSelectedProjectWatcher();
        }

        private void OnFormClosed(object? sender, FormClosedEventArgs e)
        {
            Dispose();
        }

        private void RetargetProjectsWatcher()
        {
            var root = ProjectStore.GetToolPathSettings().ProjectsRoot;
            var normalized = NormalizeExistingDirectory(root);
            if (PathsEqual(_watchedProjectsRoot, normalized))
            {
                return;
            }

            DisposeProjectsWatcher();
            _watchedProjectsRoot = normalized;
            if (normalized is null)
            {
                return;
            }

            var watcher = new FileSystemWatcher(normalized)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
                EnableRaisingEvents = false,
            };
            watcher.Created += OnProjectsRootChanged;
            watcher.Deleted += OnProjectsRootChanged;
            watcher.Renamed += OnProjectsRootRenamed;
            watcher.Error += OnProjectsWatcherError;
            watcher.EnableRaisingEvents = true;
            _projectsWatcher = watcher;
        }

        private void RetargetSelectedProjectWatcher()
        {
            var normalized = NormalizeExistingDirectory(_folderText.Text);
            if (PathsEqual(_watchedProjectFolder, normalized))
            {
                return;
            }

            DisposeSelectedProjectWatcher();
            _watchedProjectFolder = normalized;
            if (normalized is null)
            {
                return;
            }

            var watcher = new FileSystemWatcher(normalized)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
                EnableRaisingEvents = false,
            };
            watcher.Created += OnSelectedProjectChangedOnDisk;
            watcher.Changed += OnSelectedProjectChangedOnDisk;
            watcher.Deleted += OnSelectedProjectChangedOnDisk;
            watcher.Renamed += OnSelectedProjectRenamedOnDisk;
            watcher.Error += OnSelectedProjectWatcherError;
            watcher.EnableRaisingEvents = true;
            _selectedProjectWatcher = watcher;
        }

        private void OnProjectsRootChanged(object sender, FileSystemEventArgs e) =>
            QueueRefresh(libraryChanged: true);

        private void OnProjectsRootRenamed(object sender, RenamedEventArgs e) =>
            QueueRefresh(libraryChanged: true);

        private void OnSelectedProjectChangedOnDisk(object sender, FileSystemEventArgs e) =>
            QueueSelectedPathRefresh(e.FullPath);

        private void OnSelectedProjectRenamedOnDisk(object sender, RenamedEventArgs e)
        {
            QueueSelectedPathRefresh(e.OldFullPath);
            QueueSelectedPathRefresh(e.FullPath);
        }

        private void QueueSelectedPathRefresh(string fullPath)
        {
            var projectFolder = _watchedProjectFolder;
            if (projectFolder is null)
            {
                return;
            }

            if (PathsEqualNormalized(fullPath, ProjectHeaderFeature.GetHeaderImagePath(projectFolder)))
            {
                QueueRefresh(headerChanged: true);
                return;
            }

            if (PathsEqualNormalized(fullPath, ProjectStore.GetManifestPath(projectFolder)))
            {
                QueueRefresh(metadataChanged: true);
                return;
            }

            if (IsImmediateChild(fullPath, projectFolder))
            {
                QueueRefresh(projectChanged: true);
            }
        }

        private void OnProjectsWatcherError(object sender, ErrorEventArgs e) =>
            QueueRefresh(libraryChanged: true, retargetWatchers: true);

        private void OnSelectedProjectWatcherError(object sender, ErrorEventArgs e) =>
            QueueRefresh(
                projectChanged: true,
                headerChanged: true,
                metadataChanged: true,
                retargetWatchers: true);

        private void QueueRefresh(
            bool libraryChanged = false,
            bool projectChanged = false,
            bool headerChanged = false,
            bool metadataChanged = false,
            bool retargetWatchers = false)
        {
            if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            {
                return;
            }

            try
            {
                _form.BeginInvoke((Action)(() =>
                {
                    if (_disposed || _form.IsDisposed)
                    {
                        return;
                    }

                    _libraryDirty |= libraryChanged;
                    _projectDirty |= projectChanged;
                    _headerDirty |= headerChanged;
                    _metadataDirty |= metadataChanged;
                    if (retargetWatchers)
                    {
                        _watchedProjectsRoot = null;
                        _watchedProjectFolder = null;
                    }

                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }));
            }
            catch (InvalidOperationException)
            {
                // The form is closing between the watcher callback and UI dispatch.
            }
        }

        private void OnDebounceTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();
            if (_disposed || _form.IsDisposed)
            {
                return;
            }

            var refreshLibrary = _libraryDirty;
            var refreshProject = _projectDirty;
            var refreshHeader = _headerDirty;
            var refreshMetadata = _metadataDirty;
            _libraryDirty = false;
            _projectDirty = false;
            _headerDirty = false;
            _metadataDirty = false;

            if (refreshLibrary)
            {
                var previousOrder = CaptureLibraryOrder();
                _form.RefreshExternalProjectState(projectLibraryChanged: true);
                RestoreLibraryOrder(previousOrder);
            }
            else if (refreshProject)
            {
                _form.RefreshExternalProjectState(projectLibraryChanged: false);
            }

            if (refreshHeader)
            {
                ProjectHeaderFeature.Refresh(_form);
            }

            if (refreshLibrary || refreshProject || refreshMetadata)
            {
                ProjectSaveStateFeature.Refresh(_form);
                SteamStatusFeature.Refresh(_form);
            }

            RetargetProjectsWatcher();
            RetargetSelectedProjectWatcher();
        }

        private IReadOnlyList<string> CaptureLibraryOrder()
        {
            if (_library is null || _library.IsDisposed)
            {
                return [];
            }

            return _library.Items.Cast<object>()
                .Select(item => item.ToString() ?? string.Empty)
                .Where(name => name.Length > 0)
                .ToArray();
        }

        private void RestoreLibraryOrder(IReadOnlyList<string> previousOrder)
        {
            if (_library is null || _library.IsDisposed || _library.Items.Count < 2 || previousOrder.Count == 0)
            {
                return;
            }

            var savedIndexes = previousOrder
                .Select((name, index) => (name, index))
                .GroupBy(pair => pair.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);
            var current = _library.Items.Cast<object>().ToArray();
            var ordered = current
                .OrderBy(item => savedIndexes.TryGetValue(item.ToString() ?? string.Empty, out var index)
                    ? index
                    : int.MaxValue)
                .ThenBy(item => item.ToString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (current.SequenceEqual(ordered))
            {
                return;
            }

            var selectedName = _library.SelectedItem?.ToString();
            _library.BeginUpdate();
            try
            {
                _library.Items.Clear();
                _library.Items.AddRange(ordered);
                if (!string.IsNullOrWhiteSpace(selectedName))
                {
                    for (var index = 0; index < _library.Items.Count; index++)
                    {
                        if (string.Equals(
                            _library.Items[index]?.ToString(),
                            selectedName,
                            StringComparison.OrdinalIgnoreCase))
                        {
                            _library.SelectedIndex = index;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _library.EndUpdate();
            }
        }

        private void DisposeProjectsWatcher()
        {
            if (_projectsWatcher is null)
            {
                return;
            }

            _projectsWatcher.EnableRaisingEvents = false;
            _projectsWatcher.Created -= OnProjectsRootChanged;
            _projectsWatcher.Deleted -= OnProjectsRootChanged;
            _projectsWatcher.Renamed -= OnProjectsRootRenamed;
            _projectsWatcher.Error -= OnProjectsWatcherError;
            _projectsWatcher.Dispose();
            _projectsWatcher = null;
        }

        private void DisposeSelectedProjectWatcher()
        {
            if (_selectedProjectWatcher is null)
            {
                return;
            }

            _selectedProjectWatcher.EnableRaisingEvents = false;
            _selectedProjectWatcher.Created -= OnSelectedProjectChangedOnDisk;
            _selectedProjectWatcher.Changed -= OnSelectedProjectChangedOnDisk;
            _selectedProjectWatcher.Deleted -= OnSelectedProjectChangedOnDisk;
            _selectedProjectWatcher.Renamed -= OnSelectedProjectRenamedOnDisk;
            _selectedProjectWatcher.Error -= OnSelectedProjectWatcherError;
            _selectedProjectWatcher.Dispose();
            _selectedProjectWatcher = null;
        }

        private static string? NormalizeExistingDirectory(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                var full = Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Directory.Exists(full) ? full : null;
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return null;
            }
        }

        private static bool IsImmediateChild(string path, string parent)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);
                var parentDirectory = Path.GetDirectoryName(fullPath);
                return parentDirectory is not null && PathsEqualNormalized(parentDirectory, parent);
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return false;
            }
        }

        private static bool PathsEqualNormalized(string? left, string? right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            try
            {
                var normalizedLeft = Path.GetFullPath(left.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var normalizedRight = Path.GetFullPath(right.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return PathsEqual(normalizedLeft, normalizedRight);
            }
            catch (Exception exception) when (exception is ArgumentException
                or NotSupportedException
                or PathTooLongException)
            {
                return false;
            }
        }

        private static bool PathsEqual(string? left, string? right) =>
            string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
