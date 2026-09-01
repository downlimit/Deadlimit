using System.ComponentModel;
using System.Reflection;
using Deadlimit.Core;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.App;

internal static class ProjectLibraryHotfixFeature
{
    public static void Attach(MainForm form)
    {
        var library = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Projects", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проекты", StringComparison.Ordinal)
                || string.Equals(group.Text, "Library", StringComparison.Ordinal)
                || string.Equals(group.Text, "Библиотека", StringComparison.Ordinal))
            ?.Controls.OfType<ListBox>()
            .FirstOrDefault();

        var session = new Session(form, library);
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
        private const string ImagesPrefix = "file://{images}/";
        private const string ShortImagesPrefix = "{images}/";
        private const string ResourcePrefix = "s2r://";
        private readonly MainForm _form;
        private readonly ListBox? _library;
        private bool _disposed;
        private bool _libraryRefreshHeld;

        public Session(MainForm form, ListBox? library)
        {
            _form = form;
            _library = library;
        }

        public void Attach()
        {
            // Subscribe before ProjectLibraryFeature so icon extraction finishes before
            // the library reacts to CatalogRefreshed and clears its missing-image cache.
            HeroCatalogService.CatalogRefreshed += OnHeroCatalogRefreshed;
            _form.Shown += OnShown;
            _form.Deactivate += OnFormDeactivate;
            _form.Activated += OnFormActivated;
            _form.Disposed += (_, _) => Dispose();

            // Repair the cache produced by the previous implementation once, before the
            // owner-drawn library has a chance to remember those icons as missing.
            TryRepairExistingCache();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HeroCatalogService.CatalogRefreshed -= OnHeroCatalogRefreshed;
            _form.Shown -= OnShown;
            _form.Deactivate -= OnFormDeactivate;
            _form.Activated -= OnFormActivated;
            ReleaseLibraryRefresh();
        }

        private void OnShown(object? sender, EventArgs e)
        {
            if (_library is null || _library.IsDisposed)
            {
                return;
            }

            // The owner-drawn text is produced directly by DrawItem, so WinForms list
            // formatting is redundant and causes extra native redraw work.
            _library.FormattingEnabled = false;
            EnableDoubleBuffering(_library);
            DisableLegacyDragPulse(_library);
            _library.Invalidate();
        }

        private void OnFormDeactivate(object? sender, EventArgs e)
        {
            HoldLibraryRefresh();
        }

        private void OnFormActivated(object? sender, EventArgs e)
        {
            if (!_libraryRefreshHeld || _form.IsDisposed || !_form.IsHandleCreated)
            {
                return;
            }

            // MainForm's Activated handler runs first and rebuilds the native ListBox.
            // ProjectLibraryFeature's Activated handler runs after this one and restores
            // the persisted manual order. Keep redraw suspended until the current window
            // message finishes so the intermediate alphabetical state is never painted.
            _form.BeginInvoke((Action)ReleaseLibraryRefresh);
        }

        private void HoldLibraryRefresh()
        {
            if (_libraryRefreshHeld || _library is null || _library.IsDisposed)
            {
                return;
            }

            _library.BeginUpdate();
            _libraryRefreshHeld = true;
        }

        private void ReleaseLibraryRefresh()
        {
            if (!_libraryRefreshHeld)
            {
                return;
            }

            _libraryRefreshHeld = false;
            if (_library is null || _library.IsDisposed)
            {
                return;
            }

            _library.EndUpdate();
            _library.Invalidate();
        }

        private static void EnableDoubleBuffering(Control control)
        {
            var property = typeof(Control).GetProperty(
                "DoubleBuffered",
                BindingFlags.Instance | BindingFlags.NonPublic);
            property?.SetValue(control, true);
        }

        private static void DisableLegacyDragPulse(ListBox library)
        {
            try
            {
                var eventsProperty = typeof(Component).GetProperty(
                    "Events",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var dragEnterKeyField = typeof(Control).GetField(
                    "s_dragEnterEvent",
                    BindingFlags.Static | BindingFlags.NonPublic);
                if (eventsProperty?.GetValue(library) is not EventHandlerList eventHandlers
                    || dragEnterKeyField?.GetValue(null) is not object dragEnterKey
                    || eventHandlers[dragEnterKey] is not Delegate handlers)
                {
                    return;
                }

                foreach (var handler in handlers.GetInvocationList())
                {
                    var target = handler.Target;
                    if (target is null)
                    {
                        continue;
                    }

                    var timerField = target.GetType().GetField(
                        "_dragPulseTimer",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    if (timerField?.GetValue(target) is not System.Windows.Forms.Timer timer)
                    {
                        continue;
                    }

                    timer.Stop();

                    // ProjectLibraryFeature still calls Start() on DragEnter. Keeping a
                    // very long interval preserves that code path without repainting the
                    // complete native ListBox every 45 ms. The insertion marker itself is
                    // still updated immediately whenever the target position changes.
                    timer.Interval = 60_000;
                }
            }
            catch (Exception exception) when (exception is ArgumentException
                or InvalidOperationException
                or MemberAccessException
                or TargetInvocationException)
            {
                // Painting hardening is best-effort. Failure here must never prevent the
                // project library from opening; the normal drag/drop behavior remains.
            }
        }

        private void OnHeroCatalogRefreshed(object? sender, EventArgs e)
        {
            if (_disposed)
            {
                return;
            }

            try
            {
                RepairHeroIcons(HeroCatalogService.LoadCached(), requireAtLeastOne: true);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new InvalidDataException(
                    "The hero list was read successfully, but Deadlimit could not refresh the Deadlock minimap icons.",
                    exception);
            }
        }

        private void TryRepairExistingCache()
        {
            try
            {
                var heroes = HeroCatalogService.LoadCached();
                var heroesWithImages = heroes
                    .Where(hero => !string.IsNullOrWhiteSpace(hero.MinimapImageResourcePath))
                    .ToArray();
                if (heroesWithImages.Length == 0
                    || heroesWithImages.All(hero => File.Exists(HeroCatalogService.GetCachedIconPath(hero.LookupName))))
                {
                    return;
                }

                RepairHeroIcons(heroesWithImages, requireAtLeastOne: false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A stale cache must not block application startup. Explicit REFRESH LIST
                // will run the same repair and surface a useful error if it still fails.
            }
        }

        private static void RepairHeroIcons(
            IReadOnlyList<HeroCatalogEntry> heroes,
            bool requireAtLeastOne)
        {
            var imageHeroes = heroes
                .Where(hero => !string.IsNullOrWhiteSpace(hero.MinimapImageResourcePath))
                .ToArray();
            if (imageHeroes.Length == 0)
            {
                if (requireAtLeastOne)
                {
                    throw new InvalidDataException(
                        "The current heroes.vdata did not expose minimap image references for selectable heroes.");
                }
                return;
            }

            var settings = ProjectStore.GetToolPathSettings();
            var vpkPath = Path.Combine(
                settings.RetailDeadlockRoot,
                "game",
                "citadel",
                "pak01_dir.vpk");
            if (!File.Exists(vpkPath))
            {
                throw new FileNotFoundException(
                    "Retail Deadlock pak01_dir.vpk was not found while refreshing hero minimap icons.",
                    vpkPath);
            }

            using var package = new Package();
            package.Read(vpkPath);
            var packageEntries = package.Entries
                ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");
            var entries = packageEntries.SelectMany(group => group.Value).ToArray();
            var entriesByPath = entries
                .GroupBy(entry => NormalizeResourcePath(entry.GetFullPath()), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var written = 0;
            foreach (var hero in imageHeroes)
            {
                var candidates = BuildCompiledTextureCandidates(hero.MinimapImageResourcePath);
                var resolvedPath = candidates.FirstOrDefault(entriesByPath.ContainsKey);

                if (resolvedPath is null)
                {
                    // Some resources move between panorama subfolders while retaining the
                    // compiled filename. Accept that only when it resolves uniquely.
                    foreach (var candidate in candidates)
                    {
                        var fileName = Path.GetFileName(candidate);
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            continue;
                        }

                        var matches = entriesByPath.Keys
                            .Where(path => path.StartsWith("panorama/images/", StringComparison.OrdinalIgnoreCase)
                                && string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase))
                            .Take(2)
                            .ToArray();
                        if (matches.Length == 1)
                        {
                            resolvedPath = matches[0];
                            break;
                        }
                    }
                }

                if (resolvedPath is null)
                {
                    continue;
                }

                try
                {
                    var iconEntry = entriesByPath[resolvedPath];
                    package.ReadEntry(iconEntry, out byte[] iconRawData);
                    using var iconStream = new MemoryStream(iconRawData, writable: false);
                    using var iconResource = new Resource { FileName = resolvedPath };
                    iconResource.Read(iconStream);
                    if (iconResource.DataBlock is not Texture texture)
                    {
                        continue;
                    }

                    using var bitmap = texture.GenerateBitmap();
                    var png = TextureExtract.ToPngImage(bitmap);
                    WriteBytesAtomically(HeroCatalogService.GetCachedIconPath(hero.LookupName), png);
                    written++;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // One hero can reference a transitional/unsupported resource. Continue
                    // resolving the rest; total failure is handled below.
                }
            }

            if (requireAtLeastOne && written == 0)
            {
                var sample = string.Join(
                    ", ",
                    imageHeroes.Take(4).Select(hero => hero.MinimapImageResourcePath));
                throw new InvalidDataException(
                    $"No hero minimap texture could be resolved from the retail VPK. Sample references: {sample}");
            }
        }

        private static IReadOnlyList<string> BuildCompiledTextureCandidates(string sourceValue)
        {
            var value = sourceValue.Trim().Replace('\\', '/');
            var candidates = new List<string>();

            if (value.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddCompiledResourceCandidate(candidates, value[ResourcePrefix.Length..]);
                return candidates;
            }

            if (value.StartsWith(ImagesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddPanoramaSourceCandidate(candidates, value[ImagesPrefix.Length..]);
                return candidates;
            }

            if (value.StartsWith(ShortImagesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                AddPanoramaSourceCandidate(candidates, value[ShortImagesPrefix.Length..]);
                return candidates;
            }

            if (value.StartsWith("panorama/images/", StringComparison.OrdinalIgnoreCase))
            {
                AddCompiledResourceCandidate(candidates, value);
                AddPanoramaSourceCandidate(candidates, value["panorama/images/".Length..]);
                return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }

            AddCompiledResourceCandidate(candidates, value);
            AddPanoramaSourceCandidate(candidates, value);
            return candidates.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static void AddCompiledResourceCandidate(List<string> candidates, string value)
        {
            var normalized = NormalizeResourcePath(value);
            if (normalized.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
                return;
            }

            if (normalized.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(normalized + "_c");
            }
        }

        private static void AddPanoramaSourceCandidate(List<string> candidates, string relativeValue)
        {
            var relative = NormalizeResourcePath(relativeValue);
            if (relative.StartsWith("panorama/images/", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative["panorama/images/".Length..];
            }

            if (relative.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("panorama/images/" + relative);
                return;
            }

            if (relative.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add("panorama/images/" + relative + "_c");
                return;
            }

            var extension = Path.GetExtension(relative);
            if (string.IsNullOrWhiteSpace(extension))
            {
                return;
            }

            var sourceExtension = extension[1..].ToLowerInvariant();
            if (sourceExtension is not ("png" or "psd" or "svg" or "tga" or "jpg" or "jpeg"))
            {
                return;
            }

            var withoutExtension = relative[..^extension.Length];
            candidates.Add($"panorama/images/{withoutExtension}_{sourceExtension}.vtex_c");
        }

        private static string NormalizeResourcePath(string value) =>
            value.Replace('\\', '/').TrimStart('/');

        private static void WriteBytesAtomically(string path, byte[] bytes)
        {
            var target = Path.GetFullPath(path);
            var folder = Path.GetDirectoryName(target)
                ?? throw new ArgumentException("Target path has no parent folder.", nameof(path));
            Directory.CreateDirectory(folder);

            var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}.tmp-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(target))
                {
                    File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, target);
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The published cache entry or its original failure remains authoritative.
                }
            }
        }
    }
}
