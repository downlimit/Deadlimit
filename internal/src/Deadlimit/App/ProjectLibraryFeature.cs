using System.Text.Json;
using Deadlimit.Core;
using Microsoft.VisualBasic.FileIO;

namespace Deadlimit.App;

internal static class ProjectLibraryFeature
{
    private static readonly Color SelectedProjectBackColor = Color.FromArgb(0x3E, 0x4E, 0x69);

    public static void Attach(MainForm form)
    {
        ProjectIdentityFeature.Attach(form);

        var libraryGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Projects", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проекты", StringComparison.Ordinal)
                || string.Equals(group.Text, "Library", StringComparison.Ordinal)
                || string.Equals(group.Text, "Библиотека", StringComparison.Ordinal));
        if (libraryGroup is null)
        {
            return;
        }

        libraryGroup.Text = UiText.T("Library", "Библиотека");

        var library = libraryGroup.Controls.OfType<ListBox>().FirstOrDefault();
        if (library is null)
        {
            return;
        }

        var controller = new ProjectLibraryController(form, libraryGroup, library);
        controller.Attach();
    }

    private static void WidenLibraryColumn(GroupBox libraryGroup)
    {
        if (libraryGroup.Parent is not TableLayoutPanel root || root.ColumnStyles.Count == 0)
        {
            return;
        }

        if (root.ColumnStyles[0].SizeType == SizeType.Absolute)
        {
            root.ColumnStyles[0].Width = 260;
        }
    }

    private static string GetLibraryDisplayText(object? item)
    {
        var folderName = item?.ToString();
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return string.Empty;
        }

        var settings = ProjectStore.GetToolPathSettings();
        if (string.IsNullOrWhiteSpace(settings.ProjectsRoot))
        {
            return folderName;
        }

        var folder = Path.Combine(settings.ProjectsRoot, folderName);
        var manifest = ProjectStore.TryLoad(folder);
        var hasMetadataFile = File.Exists(ProjectStore.GetManifestPath(folder));

        var id = string.IsNullOrWhiteSpace(manifest?.ReleaseTarget)
            ? "—"
            : manifest.ReleaseTarget.Trim();

        var errorSuffix = hasMetadataFile && manifest is null
            ? $"   · {UiText.T("JSON ERROR", "ОШИБКА JSON")}"
            : string.Empty;

        return $"ID {id}   {folderName}{errorSuffix}";
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

    private sealed class ProjectLibraryController : IDisposable
    {
        private const int ProjectIconSize = 22;
        private readonly MainForm _form;
        private readonly GroupBox _libraryGroup;
        private readonly ListBox _library;
        private readonly Dictionary<string, HeroCatalogEntry> _heroesByName =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Image> _heroImages =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missingHeroImages =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Windows.Forms.Timer _dragPulseTimer;

        private Point _dragStartPoint;
        private int _dragStartIndex = -1;
        private int _dropIndex = -1;
        private int _dropMarkerAlpha = 230;
        private int _dropMarkerAlphaStep = -12;
        private bool _disposed;

        public ProjectLibraryController(MainForm form, GroupBox libraryGroup, ListBox library)
        {
            _form = form;
            _libraryGroup = libraryGroup;
            _library = library;
            _dragPulseTimer = new System.Windows.Forms.Timer { Interval = 45 };
            _dragPulseTimer.Tick += (_, _) =>
            {
                _dropMarkerAlpha += _dropMarkerAlphaStep;
                if (_dropMarkerAlpha <= 120 || _dropMarkerAlpha >= 230)
                {
                    _dropMarkerAlpha = Math.Clamp(_dropMarkerAlpha, 120, 230);
                    _dropMarkerAlphaStep = -_dropMarkerAlphaStep;
                }
                _library.Invalidate();
            };
        }

        public void Attach()
        {
            WidenLibraryColumn(_libraryGroup);
            ConfigureLibraryFormatting();
            ConfigureLibraryOrdering();
            ConfigureContextMenu();
            ConfigureLibraryDoubleClick();
            AddCreateProjectButton();
            ReloadHeroCatalog();

            _form.Shown += OnLibraryRefreshBoundary;
            _form.Activated += OnLibraryRefreshBoundary;
            _form.Disposed += (_, _) => Dispose();
            HeroCatalogService.CatalogRefreshed += OnHeroCatalogRefreshed;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HeroCatalogService.CatalogRefreshed -= OnHeroCatalogRefreshed;
            _dragPulseTimer.Stop();
            _dragPulseTimer.Dispose();
            DisposeHeroImages();
        }

        private void ConfigureLibraryFormatting()
        {
            _library.FormattingEnabled = true;
            _library.Format += (_, e) => e.Value = GetLibraryDisplayText(e.ListItem);

            _library.DrawMode = DrawMode.OwnerDrawFixed;
            _library.ItemHeight = Math.Max(30, _library.Font.Height + 10);
            _library.DrawItem += (_, e) =>
            {
                if (e.Index < 0 || e.Index >= _library.Items.Count)
                {
                    return;
                }

                var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
                using var background = new SolidBrush(selected ? SelectedProjectBackColor : _library.BackColor);
                e.Graphics.FillRectangle(background, e.Bounds);

                var textColor = selected ? Color.White : _library.ForeColor;
                var iconBounds = new Rectangle(
                    e.Bounds.X + 4,
                    e.Bounds.Y + Math.Max(1, (e.Bounds.Height - ProjectIconSize) / 2),
                    ProjectIconSize,
                    ProjectIconSize);
                DrawProjectIcon(e.Graphics, _library.Items[e.Index], iconBounds, textColor);

                var textBounds = new Rectangle(
                    iconBounds.Right + 7,
                    e.Bounds.Y,
                    Math.Max(0, e.Bounds.Right - iconBounds.Right - 10),
                    e.Bounds.Height);
                TextRenderer.DrawText(
                    e.Graphics,
                    GetLibraryDisplayText(_library.Items[e.Index]),
                    _library.Font,
                    textBounds,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                DrawDropMarker(e);
            };
        }

        private void DrawProjectIcon(Graphics graphics, object? item, Rectangle bounds, Color textColor)
        {
            var folderName = item?.ToString();
            var projectsRoot = ProjectStore.GetToolPathSettings().ProjectsRoot;
            if (string.IsNullOrWhiteSpace(folderName) || string.IsNullOrWhiteSpace(projectsRoot))
            {
                DrawNeutralIcon(graphics, bounds, textColor, "?");
                return;
            }

            var folder = Path.Combine(projectsRoot, folderName);
            var manifest = ProjectStore.TryLoad(folder);
            var hasMetadataFile = File.Exists(ProjectStore.GetManifestPath(folder));
            if (hasMetadataFile && manifest is null)
            {
                DrawErrorIcon(graphics, bounds);
                return;
            }

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Hero))
            {
                DrawNeutralIcon(graphics, bounds, textColor, "?");
                return;
            }

            if (!_heroesByName.TryGetValue(manifest.Hero.Trim(), out var hero))
            {
                DrawNeutralIcon(graphics, bounds, textColor, "?");
                return;
            }

            var image = GetHeroImage(hero);
            if (image is null)
            {
                DrawNeutralIcon(graphics, bounds, textColor, "?");
                return;
            }

            graphics.DrawImage(image, bounds);
        }

        private static void DrawNeutralIcon(Graphics graphics, Rectangle bounds, Color color, string glyph)
        {
            using var pen = new Pen(Color.FromArgb(155, color), 1.35F);
            graphics.DrawEllipse(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            TextRenderer.DrawText(
                graphics,
                glyph,
                SystemFonts.MessageBoxFont,
                bounds,
                Color.FromArgb(190, color),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private static void DrawErrorIcon(Graphics graphics, Rectangle bounds)
        {
            var color = Color.FromArgb(0xD9, 0x65, 0x65);
            using var pen = new Pen(color, 1.6F);
            graphics.DrawEllipse(pen, bounds.X + 1, bounds.Y + 1, bounds.Width - 3, bounds.Height - 3);
            TextRenderer.DrawText(
                graphics,
                "!",
                SystemFonts.MessageBoxFont,
                bounds,
                color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }

        private Image? GetHeroImage(HeroCatalogEntry hero)
        {
            if (_heroImages.TryGetValue(hero.LookupName, out var cached))
            {
                return cached;
            }

            if (_missingHeroImages.Contains(hero.LookupName))
            {
                return null;
            }

            var path = HeroCatalogService.GetCachedIconPath(hero.LookupName);
            if (!File.Exists(path))
            {
                _missingHeroImages.Add(hero.LookupName);
                return null;
            }

            try
            {
                var bytes = File.ReadAllBytes(path);
                using var stream = new MemoryStream(bytes, writable: false);
                using var source = Image.FromStream(stream);
                var image = new Bitmap(source);
                _heroImages[hero.LookupName] = image;
                return image;
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or OutOfMemoryException)
            {
                _missingHeroImages.Add(hero.LookupName);
                return null;
            }
        }

        private void ReloadHeroCatalog()
        {
            _heroesByName.Clear();
            foreach (var hero in HeroCatalogService.LoadCached())
            {
                _heroesByName.TryAdd(hero.LookupName, hero);
                _heroesByName.TryAdd(hero.DisplayName, hero);
            }

            DisposeHeroImages();
            _missingHeroImages.Clear();
        }

        private void DisposeHeroImages()
        {
            foreach (var image in _heroImages.Values)
            {
                image.Dispose();
            }
            _heroImages.Clear();
        }

        private void OnHeroCatalogRefreshed(object? sender, EventArgs e)
        {
            if (_disposed || _form.IsDisposed || !_form.IsHandleCreated)
            {
                return;
            }

            _form.BeginInvoke((Action)(() =>
            {
                if (_disposed || _form.IsDisposed)
                {
                    return;
                }

                ReloadHeroCatalog();
                _library.Invalidate();
            }));
        }

        private void OnLibraryRefreshBoundary(object? sender, EventArgs e)
        {
            ApplyStoredOrder();
            _library.Invalidate();
        }

        private void ApplyStoredOrder()
        {
            var root = ProjectStore.GetToolPathSettings().ProjectsRoot;
            if (string.IsNullOrWhiteSpace(root) || _library.Items.Count == 0)
            {
                return;
            }

            ProjectLibraryOrderStore.Apply(_library, root);
        }

        private void ConfigureLibraryOrdering()
        {
            _library.AllowDrop = true;
            _library.MouseDown += (_, e) =>
            {
                var index = _library.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Right && index >= 0)
                {
                    _library.SelectedIndex = index;
                }

                if (e.Button != MouseButtons.Left || index < 0)
                {
                    _dragStartIndex = -1;
                    return;
                }

                _dragStartIndex = index;
                _dragStartPoint = e.Location;
            };
            _library.MouseMove += (_, e) =>
            {
                if (e.Button != MouseButtons.Left
                    || _dragStartIndex < 0
                    || _dragStartIndex >= _library.Items.Count)
                {
                    return;
                }

                var dragRect = new Rectangle(
                    _dragStartPoint.X - SystemInformation.DragSize.Width / 2,
                    _dragStartPoint.Y - SystemInformation.DragSize.Height / 2,
                    SystemInformation.DragSize.Width,
                    SystemInformation.DragSize.Height);
                if (dragRect.Contains(e.Location))
                {
                    return;
                }

                var item = _library.Items[_dragStartIndex];
                _library.DoDragDrop(new ProjectDragPayload(item), DragDropEffects.Move);
                _dragStartIndex = -1;
            };
            _library.MouseUp += (_, _) => _dragStartIndex = -1;
            _library.DragEnter += (_, e) =>
            {
                e.Effect = e.Data?.GetDataPresent(typeof(ProjectDragPayload)) == true
                    ? DragDropEffects.Move
                    : DragDropEffects.None;
                if (e.Effect == DragDropEffects.Move)
                {
                    _dragPulseTimer.Start();
                }
            };
            _library.DragOver += (_, e) =>
            {
                if (e.Data?.GetDataPresent(typeof(ProjectDragPayload)) != true)
                {
                    e.Effect = DragDropEffects.None;
                    return;
                }

                e.Effect = DragDropEffects.Move;
                var client = _library.PointToClient(new Point(e.X, e.Y));
                var nextDropIndex = CalculateDropIndex(client.Y);
                if (nextDropIndex != _dropIndex)
                {
                    _dropIndex = nextDropIndex;
                    _library.Invalidate();
                }
            };
            _library.DragLeave += (_, _) =>
            {
                _dropIndex = -1;
                _dragPulseTimer.Stop();
                _library.Invalidate();
            };
            _library.DragDrop += (_, e) =>
            {
                _dragPulseTimer.Stop();
                try
                {
                    if (e.Data?.GetData(typeof(ProjectDragPayload)) is not ProjectDragPayload payload)
                    {
                        return;
                    }

                    var sourceIndex = _library.Items.IndexOf(payload.Item);
                    if (sourceIndex < 0)
                    {
                        return;
                    }

                    var targetIndex = Math.Clamp(_dropIndex, 0, _library.Items.Count);
                    if (targetIndex > sourceIndex)
                    {
                        targetIndex--;
                    }

                    if (targetIndex == sourceIndex)
                    {
                        return;
                    }

                    _library.BeginUpdate();
                    try
                    {
                        _library.Items.RemoveAt(sourceIndex);
                        _library.Items.Insert(targetIndex, payload.Item);
                        _library.SelectedIndex = targetIndex;
                    }
                    finally
                    {
                        _library.EndUpdate();
                    }

                    var root = ProjectStore.GetToolPathSettings().ProjectsRoot;
                    if (!string.IsNullOrWhiteSpace(root))
                    {
                        ProjectLibraryOrderStore.SaveCurrent(_library, root);
                    }
                }
                finally
                {
                    _dropIndex = -1;
                    _library.Invalidate();
                }
            };
        }

        private int CalculateDropIndex(int y)
        {
            if (_library.Items.Count == 0)
            {
                return 0;
            }

            if (y <= 0)
            {
                return 0;
            }

            var index = _library.IndexFromPoint(new Point(4, y));
            if (index < 0)
            {
                return _library.Items.Count;
            }

            var bounds = _library.GetItemRectangle(index);
            return y < bounds.Top + bounds.Height / 2
                ? index
                : index + 1;
        }

        private void DrawDropMarker(DrawItemEventArgs e)
        {
            if (_dropIndex < 0)
            {
                return;
            }

            var y = -1;
            if (_dropIndex == e.Index)
            {
                y = e.Bounds.Top;
            }
            else if (_dropIndex == _library.Items.Count && e.Index == _library.Items.Count - 1)
            {
                y = e.Bounds.Bottom - 1;
            }

            if (y < 0)
            {
                return;
            }

            var markerColor = Color.FromArgb(_dropMarkerAlpha, SystemColors.Highlight);
            using var pen = new Pen(markerColor, 2F);
            e.Graphics.DrawLine(pen, e.Bounds.Left + 4, y, e.Bounds.Right - 4, y);
        }

        private void ConfigureContextMenu()
        {
            var menu = new ContextMenuStrip();
            var renameItem = new ToolStripMenuItem(UiText.T("Rename", "Переименовать"));
            var openItem = new ToolStripMenuItem(UiText.T("Open in File Explorer", "Открыть в проводнике"));
            var openCoverItem = new ToolStripMenuItem(UiText.T("Open Project Cover", "Открыть обложку проекта"));
            var deleteItem = new ToolStripMenuItem(UiText.T("Delete...", "Удалить..."));

            renameItem.Click += (_, _) => RenameSelectedProject();
            openItem.Click += (_, _) => OpenSelectedProjectFolder();
            openCoverItem.Click += (_, _) => OpenSelectedProjectCover();
            deleteItem.Click += (_, _) => DeleteSelectedProject();

            menu.Items.Add(renameItem);
            menu.Items.Add(openItem);
            menu.Items.Add(openCoverItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(deleteItem);
            menu.Opening += (sender, e) =>
            {
                var hasSelection = TryGetSelectedProject(out _, out _);
                renameItem.Enabled = hasSelection;
                openItem.Enabled = hasSelection;
                openCoverItem.Enabled = hasSelection && HasSelectedProjectCover();
                deleteItem.Enabled = hasSelection;
                if (!hasSelection)
                {
                    e.Cancel = true;
                }
            };
            _library.ContextMenuStrip = menu;
            _form.Disposed += (_, _) => menu.Dispose();
        }

        private void ConfigureLibraryDoubleClick()
        {
            _library.MouseDoubleClick += (_, e) =>
            {
                var index = _library.IndexFromPoint(e.Location);
                if (index < 0)
                {
                    return;
                }

                _library.SelectedIndex = index;
                OpenSelectedProjectFolder();
            };
        }

        private bool TryGetSelectedProject(out string projectName, out string folder)
        {
            projectName = _library.SelectedItem?.ToString()?.Trim() ?? string.Empty;
            var projectsRoot = ProjectStore.GetToolPathSettings().ProjectsRoot;
            if (projectName.Length == 0 || string.IsNullOrWhiteSpace(projectsRoot))
            {
                folder = string.Empty;
                return false;
            }

            folder = Path.Combine(projectsRoot, projectName);
            return Directory.Exists(folder);
        }

        private bool HasSelectedProjectCover()
        {
            return TryGetSelectedProject(out _, out var folder)
                && File.Exists(ProjectHeaderFeature.GetHeaderImagePath(folder));
        }

        private void OpenSelectedProjectCover()
        {
            if (!TryGetSelectedProject(out _, out var folder))
            {
                return;
            }

            var imagePath = ProjectHeaderFeature.GetHeaderImagePath(folder);
            if (!File.Exists(imagePath))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = imagePath,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.ComponentModel.Win32Exception
                or InvalidOperationException
                or System.Runtime.InteropServices.ExternalException)
            {
                MessageBox.Show(
                    _form,
                    ex.Message,
                    UiText.T("Could not open project cover", "Не удалось открыть обложку проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OpenSelectedProjectFolder()
        {
            if (!TryGetSelectedProject(out _, out var folder))
            {
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folder}\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                MessageBox.Show(
                    _form,
                    ex.Message,
                    UiText.T("Could not open project folder", "Не удалось открыть папку проекта"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void RenameSelectedProject()
        {
            if (!TryGetSelectedProject(out var projectName, out var folder))
            {
                return;
            }

            var settings = ProjectStore.GetToolPathSettings();
            using var dialog = new RenameProjectDialog(settings.ProjectsRoot, folder, projectName, settings.UiTheme);
            if (dialog.ShowDialog(_form) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.RenamedFolder))
            {
                return;
            }

            var renamedName = Path.GetFileName(dialog.RenamedFolder);
            _form.BeginInvoke((Action)(() => SelectLibraryItem(renamedName)));
        }

        private void DeleteSelectedProject()
        {
            if (!TryGetSelectedProject(out var projectName, out var folder))
            {
                return;
            }

            var oldIndex = _library.SelectedIndex;
            var settings = ProjectStore.GetToolPathSettings();
            using var dialog = new DeleteProjectDialog(settings.ProjectsRoot, folder, projectName, settings.UiTheme);
            if (dialog.ShowDialog(_form) != DialogResult.OK || !dialog.Deleted)
            {
                return;
            }

            _form.BeginInvoke((Action)(() =>
            {
                if (_library.Items.Count == 0)
                {
                    return;
                }

                _library.SelectedIndex = Math.Clamp(oldIndex, 0, _library.Items.Count - 1);
                _library.Focus();
            }));
        }

        private void AddCreateProjectButton()
        {
            _libraryGroup.Padding = new Padding(10, 30, 10, 10);

            var addButton = new Button
            {
                Text = string.Empty,
                Width = 26,
                Height = 23,
                TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Padding = Padding.Empty,
            };
            addButton.Paint += (_, e) =>
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    "+",
                    addButton.Font,
                    addButton.ClientRectangle,
                    addButton.ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            };

            void PositionButton()
            {
                addButton.Location = new Point(
                    Math.Max(0, _libraryGroup.ClientSize.Width - addButton.Width - 9),
                    1);
            }

            PositionButton();
            _libraryGroup.Resize += (_, _) => PositionButton();

            var toolTip = new ToolTip
            {
                ShowAlways = true,
                InitialDelay = 350,
                ReshowDelay = 100,
                AutoPopDelay = 10000,
            };
            toolTip.SetToolTip(
                addButton,
                UiText.T(
                    "Create a new empty project folder inside the configured Projects folder.\n\nThe folder name becomes the project name. Choose a hero and Release ID, then save the project metadata.",
                    "Создать новую пустую папку проекта внутри настроенной Папки проектов.\n\nИмя папки станет именем проекта. После создания выберите героя и Release ID, затем сохраните метаданные проекта."));

            addButton.Click += (_, _) => CreateProjectFolder();
            _libraryGroup.Controls.Add(addButton);
            addButton.BringToFront();
            _form.Disposed += (_, _) => toolTip.Dispose();
        }

        private void CreateProjectFolder()
        {
            var settings = ProjectStore.GetToolPathSettings();
            if (!Directory.Exists(settings.ProjectsRoot))
            {
                MessageBox.Show(
                    _form,
                    UiText.T(
                        "Projects folder is unavailable. Set it in Settings first.",
                        "Папка проектов недоступна. Сначала укажите её в настройках."),
                    "Deadlimit Manager",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            using var dialog = new NewProjectFolderDialog(settings.ProjectsRoot, settings.UiTheme);
            if (dialog.ShowDialog(_form) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.CreatedFolder))
            {
                return;
            }

            var createdName = Path.GetFileName(dialog.CreatedFolder);
            _form.BeginInvoke((Action)(() => SelectLibraryItem(createdName)));
        }

        private void SelectLibraryItem(string name)
        {
            for (var index = 0; index < _library.Items.Count; index++)
            {
                if (!string.Equals(_library.Items[index]?.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _library.SelectedIndex = index;
                _library.Focus();
                break;
            }
        }

        private sealed record ProjectDragPayload(object Item);
    }

    private static class ProjectLibraryOrderStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        public static void Apply(ListBox library, string projectsRoot)
        {
            var currentItems = library.Items.Cast<object>().ToArray();
            if (currentItems.Length == 0)
            {
                return;
            }

            var snapshot = Load();
            var root = FindRoot(snapshot, projectsRoot);
            var savedOrder = root?.Projects ?? [];
            var savedIndexes = savedOrder
                .Select((name, index) => (name, index))
                .Where(pair => !string.IsNullOrWhiteSpace(pair.name))
                .GroupBy(pair => pair.name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().index, StringComparer.OrdinalIgnoreCase);

            var ordered = currentItems
                .OrderBy(item => savedIndexes.TryGetValue(item.ToString() ?? string.Empty, out var index)
                    ? index
                    : int.MaxValue)
                .ToArray();

            var currentNames = currentItems.Select(item => item.ToString() ?? string.Empty).ToArray();
            var orderedNames = ordered.Select(item => item.ToString() ?? string.Empty).ToArray();
            if (!currentNames.SequenceEqual(orderedNames, StringComparer.OrdinalIgnoreCase))
            {
                var selected = library.SelectedItem;
                library.BeginUpdate();
                try
                {
                    library.Items.Clear();
                    library.Items.AddRange(ordered);
                    if (selected is not null)
                    {
                        library.SelectedItem = selected;
                    }
                }
                finally
                {
                    library.EndUpdate();
                }
            }

            SaveNames(projectsRoot, orderedNames);
        }

        public static void SaveCurrent(ListBox library, string projectsRoot) =>
            SaveNames(
                projectsRoot,
                library.Items.Cast<object>().Select(item => item.ToString() ?? string.Empty));

        public static void Replace(string projectsRoot, string oldName, string newName)
        {
            var snapshot = Load();
            var root = GetOrCreateRoot(snapshot, projectsRoot);
            var index = root.Projects.FindIndex(name =>
                string.Equals(name, oldName, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                root.Projects[index] = newName;
            }
            else if (!root.Projects.Contains(newName, StringComparer.OrdinalIgnoreCase))
            {
                root.Projects.Add(newName);
            }
            Save(snapshot);
        }

        public static void Remove(string projectsRoot, string projectName)
        {
            var snapshot = Load();
            var root = FindRoot(snapshot, projectsRoot);
            if (root is null)
            {
                return;
            }

            root.Projects.RemoveAll(name =>
                string.Equals(name, projectName, StringComparison.OrdinalIgnoreCase));
            Save(snapshot);
        }

        private static void SaveNames(string projectsRoot, IEnumerable<string> names)
        {
            var snapshot = Load();
            var root = GetOrCreateRoot(snapshot, projectsRoot);
            root.Projects = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            Save(snapshot);
        }

        private static ProjectLibraryOrderSnapshot Load()
        {
            var path = GetPath();
            if (!File.Exists(path))
            {
                return new ProjectLibraryOrderSnapshot();
            }

            try
            {
                return JsonSerializer.Deserialize<ProjectLibraryOrderSnapshot>(File.ReadAllText(path), JsonOptions)
                    ?? new ProjectLibraryOrderSnapshot();
            }
            catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
            {
                return new ProjectLibraryOrderSnapshot();
            }
        }

        private static void Save(ProjectLibraryOrderSnapshot snapshot)
        {
            try
            {
                AtomicFile.WriteJson(GetPath(), snapshot, JsonOptions);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Library ordering is presentation state. A write failure must not block project work.
            }
        }

        private static ProjectLibraryRootOrder GetOrCreateRoot(
            ProjectLibraryOrderSnapshot snapshot,
            string projectsRoot)
        {
            var existing = FindRoot(snapshot, projectsRoot);
            if (existing is not null)
            {
                return existing;
            }

            var root = new ProjectLibraryRootOrder
            {
                ProjectsRoot = NormalizeRoot(projectsRoot),
            };
            snapshot.Roots.Add(root);
            return root;
        }

        private static ProjectLibraryRootOrder? FindRoot(
            ProjectLibraryOrderSnapshot snapshot,
            string projectsRoot)
        {
            var normalized = NormalizeRoot(projectsRoot);
            return snapshot.Roots.FirstOrDefault(root =>
                string.Equals(NormalizeRoot(root.ProjectsRoot), normalized, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizeRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFullPath(path.Trim())
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return path.Trim();
            }
        }

        private static string GetPath() => UserDataPaths.Combine("project_library.json");

        private sealed class ProjectLibraryOrderSnapshot
        {
            public List<ProjectLibraryRootOrder> Roots { get; set; } = [];
        }

        private sealed class ProjectLibraryRootOrder
        {
            public string ProjectsRoot { get; set; } = string.Empty;
            public List<string> Projects { get; set; } = [];
        }
    }

    private sealed class NewProjectFolderDialog : Form
    {
        private readonly string _projectsRoot;
        private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };

        public NewProjectFolderDialog(string projectsRoot, string theme)
        {
            _projectsRoot = projectsRoot;

            Text = UiText.T("New project", "Новый проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(430, 132);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
            Shown += (_, _) => _nameText.Focus();
        }

        public string? CreatedFolder { get; private set; }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Text = UiText.T("Project name", "Название проекта"),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            _nameText.Margin = new Padding(0, 0, 0, 12);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };

            var cancelButton = new Button
            {
                Text = UiText.T("CANCEL", "ОТМЕНА"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
            };
            var createButton = new Button
            {
                Text = UiText.T("CREATE", "СОЗДАТЬ"),
                AutoSize = true,
            };
            createButton.Click += (_, _) => TryCreate();

            var toolTip = new ToolTip
            {
                ShowAlways = true,
                InitialDelay = 350,
                ReshowDelay = 100,
                AutoPopDelay = 10000,
            };
            toolTip.SetToolTip(
                createButton,
                UiText.T(
                    "Create the folder and add it to the Library.\n\nThe project is not initialized until you choose a hero and save it.",
                    "Создать папку и добавить её в Библиотеку.\n\nПроект будет инициализирован только после выбора героя и сохранения."));
            toolTip.SetToolTip(
                cancelButton,
                UiText.T(
                    "Close this dialog without creating a project folder.",
                    "Закрыть окно без создания папки проекта."));

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(createButton);

            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_nameText, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = createButton;
            CancelButton = cancelButton;
        }

        private void TryCreate()
        {
            var name = _nameText.Text.Trim();
            if (!ValidateProjectName(name, out var validationMessage))
            {
                ShowError(validationMessage);
                return;
            }

            var folder = Path.Combine(_projectsRoot, name);
            if (Directory.Exists(folder) || File.Exists(folder))
            {
                ShowError(UiText.T(
                    "A folder or file with this name already exists in the projects folder.",
                    "В папке проектов уже существует папка или файл с таким именем."));
                return;
            }

            try
            {
                Directory.CreateDirectory(folder);
                CreatedFolder = Path.GetFullPath(folder);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                ShowError(ex.Message);
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                this,
                message,
                UiText.T("Could not create project", "Не удалось создать проект"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private sealed class RenameProjectDialog : Form
    {
        private readonly string _projectsRoot;
        private readonly string _sourceFolder;
        private readonly string _oldName;
        private readonly TextBox _nameText = new() { Dock = DockStyle.Fill };

        public RenameProjectDialog(
            string projectsRoot,
            string sourceFolder,
            string oldName,
            string theme)
        {
            _projectsRoot = projectsRoot;
            _sourceFolder = sourceFolder;
            _oldName = oldName;
            _nameText.Text = oldName;

            Text = UiText.T("Rename project", "Переименовать проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(430, 132);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
            Shown += (_, _) =>
            {
                _nameText.Focus();
                _nameText.SelectAll();
            };
        }

        public string? RenamedFolder { get; private set; }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var label = new Label
            {
                Text = UiText.T("Project name", "Название проекта"),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            _nameText.Margin = new Padding(0, 0, 0, 12);

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };
            var cancelButton = new Button
            {
                Text = UiText.T("CANCEL", "ОТМЕНА"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
            };
            var renameButton = new Button
            {
                Text = UiText.T("RENAME", "ПЕРЕИМЕНОВАТЬ"),
                AutoSize = true,
            };
            renameButton.Click += (_, _) => TryRename();

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(renameButton);
            root.Controls.Add(label, 0, 0);
            root.Controls.Add(_nameText, 0, 1);
            root.Controls.Add(buttons, 0, 2);
            Controls.Add(root);

            AcceptButton = renameButton;
            CancelButton = cancelButton;
        }

        private void TryRename()
        {
            var newName = _nameText.Text.Trim();
            if (!ValidateProjectName(newName, out var validationMessage))
            {
                ShowError(validationMessage);
                return;
            }

            if (string.Equals(newName, _oldName, StringComparison.Ordinal))
            {
                RenamedFolder = _sourceFolder;
                DialogResult = DialogResult.OK;
                Close();
                return;
            }

            var destination = Path.Combine(_projectsRoot, newName);
            var sameIgnoringCase = string.Equals(newName, _oldName, StringComparison.OrdinalIgnoreCase);
            if (!sameIgnoringCase && (Directory.Exists(destination) || File.Exists(destination)))
            {
                ShowError(UiText.T(
                    "A folder or file with this name already exists in the projects folder.",
                    "В папке проектов уже существует папка или файл с таким именем."));
                return;
            }

            try
            {
                if (sameIgnoringCase)
                {
                    RenameCaseOnly(_sourceFolder, destination);
                }
                else
                {
                    Directory.Move(_sourceFolder, destination);
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                ShowError(ex.Message);
                return;
            }

            try
            {
                var manifest = ProjectStore.TryLoad(destination);
                if (manifest is not null)
                {
                    ProjectStore.Save(manifest);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                MessageBox.Show(
                    this,
                    UiText.T(
                        $"The project folder was renamed, but its metadata could not be refreshed: {ex.Message}",
                        $"Папка проекта переименована, но метаданные не удалось обновить: {ex.Message}"),
                    UiText.T("Project renamed with warning", "Проект переименован с предупреждением"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }

            ProjectLibraryOrderStore.Replace(_projectsRoot, _oldName, newName);
            ProjectStore.RememberLastProject(destination);
            RenamedFolder = Path.GetFullPath(destination);
            DialogResult = DialogResult.OK;
            Close();
        }

        private static void RenameCaseOnly(string source, string destination)
        {
            var parent = Path.GetDirectoryName(source)
                ?? throw new ArgumentException("Project folder has no parent folder.", nameof(source));
            var temporary = Path.Combine(parent, $".deadlimit-rename-{Guid.NewGuid():N}");
            Directory.Move(source, temporary);
            try
            {
                Directory.Move(temporary, destination);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(temporary) && !Directory.Exists(source))
                    {
                        Directory.Move(temporary, source);
                    }
                }
                catch (Exception rollbackException) when (rollbackException is IOException or UnauthorizedAccessException)
                {
                    // The original rename error remains authoritative.
                }
                throw;
            }
        }

        private void ShowError(string message)
        {
            MessageBox.Show(
                this,
                message,
                UiText.T("Could not rename project", "Не удалось переименовать проект"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private sealed class DeleteProjectDialog : Form
    {
        private readonly string _projectsRoot;
        private readonly string _folder;
        private readonly string _projectName;
        private readonly TextBox _confirmationText = new() { Dock = DockStyle.Fill };
        private readonly Button _deleteButton = new()
        {
            AutoSize = true,
            Enabled = false,
        };

        public DeleteProjectDialog(
            string projectsRoot,
            string folder,
            string projectName,
            string theme)
        {
            _projectsRoot = projectsRoot;
            _folder = folder;
            _projectName = projectName;

            Text = UiText.T("Delete project", "Удалить проект");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(500, 190);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;

            BuildUi();
            UiTheme.ApplyCustomPalette(this, theme);
            Shown += (_, _) => _confirmationText.Focus();
        }

        public bool Deleted { get; private set; }

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14),
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var warning = new Label
            {
                Text = UiText.T(
                    "The entire project folder will be moved to the Windows Recycle Bin.",
                    "Вся папка проекта будет перемещена в корзину Windows."),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 10),
            };
            var instruction = new Label
            {
                Text = UiText.T(
                    $"Type the project name exactly to confirm: {_projectName}",
                    $"Введите название проекта полностью с учётом регистра: {_projectName}"),
                AutoSize = true,
                Margin = new Padding(0, 0, 0, 6),
            };
            _confirmationText.Margin = new Padding(0, 0, 0, 12);
            _confirmationText.TextChanged += (_, _) =>
            {
                _deleteButton.Enabled = string.Equals(
                    _confirmationText.Text,
                    _projectName,
                    StringComparison.Ordinal);
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };
            var cancelButton = new Button
            {
                Text = UiText.T("CANCEL", "ОТМЕНА"),
                AutoSize = true,
                DialogResult = DialogResult.Cancel,
            };
            _deleteButton.Text = UiText.T("DELETE PROJECT", "УДАЛИТЬ ПРОЕКТ");
            _deleteButton.Click += (_, _) => TryDelete();

            buttons.Controls.Add(cancelButton);
            buttons.Controls.Add(_deleteButton);
            root.Controls.Add(warning, 0, 0);
            root.Controls.Add(instruction, 0, 1);
            root.Controls.Add(_confirmationText, 0, 2);
            root.Controls.Add(buttons, 0, 3);
            Controls.Add(root);

            CancelButton = cancelButton;
        }

        private void TryDelete()
        {
            if (!string.Equals(_confirmationText.Text, _projectName, StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                FileSystem.DeleteDirectory(
                    _folder,
                    UIOption.OnlyErrorDialogs,
                    RecycleOption.SendToRecycleBin,
                    UICancelOption.ThrowException);

                if (Directory.Exists(_folder))
                {
                    throw new IOException("Windows did not move the project folder to the Recycle Bin.");
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or OperationCanceledException
                or ArgumentException
                or NotSupportedException)
            {
                MessageBox.Show(
                    this,
                    ex.Message,
                    UiText.T("Could not delete project", "Не удалось удалить проект"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            ProjectLibraryOrderStore.Remove(_projectsRoot, _projectName);
            Deleted = true;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private static bool ValidateProjectName(string name, out string message)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            message = UiText.T("Enter a project name.", "Введите название проекта.");
            return false;
        }

        if (name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            message = UiText.T(
                "The project name contains characters that cannot be used in a folder name.",
                "Название проекта содержит символы, которые нельзя использовать в имени папки.");
            return false;
        }

        message = string.Empty;
        return true;
    }
}
