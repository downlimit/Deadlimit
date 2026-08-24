using System.Drawing.Imaging;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectHeaderFeature
{
    private const int HeaderRowHeight = 180;
    private const int SidePadding = 24;
    private const int ActionWidth = 178;
    private const int PrepareHeight = 24;
    private const int LaunchHeight = 40;
    private const string HeaderFileName = "project-header.png";
    private const string DeadlockSteamUri = "steam://rungameid/1422450";

    private static readonly Color DefaultHeaderColor = Color.FromArgb(36, 39, 43);
    private static readonly Color CsdkColor = Color.FromArgb(91, 45, 196);
    private static readonly Color CsdkHoverColor = Color.FromArgb(108, 55, 220);
    private static readonly Color CsdkPressedColor = Color.FromArgb(74, 35, 164);
    private static readonly Color GameColor = Color.FromArgb(63, 187, 55);
    private static readonly Color GameHoverColor = Color.FromArgb(74, 205, 65);
    private static readonly Color GamePressedColor = Color.FromArgb(48, 154, 42);

    public static void Attach(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        if (projectGroup?.Parent is not TableLayoutPanel workspace)
        {
            return;
        }

        var projectGrid = projectGroup.Controls.OfType<TableLayoutPanel>().FirstOrDefault();
        var folderText = projectGrid?.GetControlFromPosition(1, 0) as TextBox;
        if (folderText is null)
        {
            return;
        }

        var topBar = workspace.GetControlFromPosition(0, 0) as FlowLayoutPanel;
        if (topBar is null)
        {
            return;
        }

        var settingsButton = FindButton(topBar, "SETTINGS", "НАСТРОЙКИ");
        var prepareButton = FindButton(topBar, "PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK");
        var buildButton = FindButton(topBar, "BUILD & TEST", "СОБРАТЬ И ТЕСТИРОВАТЬ");
        var launchCsdkButton = FindButton(topBar, "LAUNCH CSDK", "ЗАПУСТИТЬ CSDK");
        if (settingsButton is null || prepareButton is null || buildButton is null || launchCsdkButton is null)
        {
            return;
        }

        foreach (var button in new[] { settingsButton, prepareButton, buildButton, launchCsdkButton })
        {
            topBar.Controls.Remove(button);
        }
        workspace.Controls.Remove(topBar);
        topBar.Dispose();

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10),
            BackColor = DefaultHeaderColor,
            BackgroundImageLayout = ImageLayout.Stretch,
        };

        workspace.RowStyles[0].SizeType = SizeType.Absolute;
        workspace.RowStyles[0].Height = HeaderRowHeight;
        workspace.Controls.Add(header, 0, 0);

        settingsButton.AutoSize = true;
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.Margin = Padding.Empty;

        prepareButton.AutoSize = false;
        prepareButton.Size = new Size(ActionWidth, PrepareHeight);
        prepareButton.TextAlign = ContentAlignment.MiddleCenter;
        prepareButton.Margin = Padding.Empty;

        buildButton.AutoSize = false;
        buildButton.Size = new Size(ActionWidth, PrepareHeight);
        buildButton.TextAlign = ContentAlignment.MiddleCenter;
        buildButton.Margin = Padding.Empty;

        launchCsdkButton.AutoSize = false;
        launchCsdkButton.Size = new Size(ActionWidth, LaunchHeight);
        launchCsdkButton.Text = UiText.T("▶  LAUNCH CSDK", "▶  ЗАПУСК CSDK");
        launchCsdkButton.TextAlign = ContentAlignment.MiddleCenter;
        launchCsdkButton.Font = new Font(launchCsdkButton.Font, FontStyle.Bold);
        launchCsdkButton.Margin = Padding.Empty;

        var launchGameButton = new Button
        {
            AutoSize = false,
            Size = new Size(ActionWidth, LaunchHeight),
            Text = UiText.T("▶  LAUNCH GAME", "▶  ЗАПУСК ИГРЫ"),
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font(launchCsdkButton.Font, FontStyle.Bold),
            Margin = Padding.Empty,
            TabStop = false,
        };
        launchGameButton.Click += (_, _) => LaunchDeadlock(form);

        header.Controls.Add(settingsButton);
        header.Controls.Add(prepareButton);
        header.Controls.Add(buildButton);
        header.Controls.Add(launchCsdkButton);
        header.Controls.Add(launchGameButton);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            AutoPopDelay = 7000,
        };
        toolTip.SetToolTip(
            launchGameButton,
            UiText.T(
                "Launch retail Deadlock through Steam.",
                "Запустить retail Deadlock через Steam."));

        void PositionControls()
        {
            settingsButton.Location = new Point(
                Math.Max(0, header.ClientSize.Width - settingsButton.Width - 8),
                8);

            var launchY = Math.Max(72, header.ClientSize.Height - LaunchHeight - 12);
            var prepareY = Math.Max(40, launchY - PrepareHeight - 6);
            var rightX = Math.Max(SidePadding, header.ClientSize.Width - SidePadding - ActionWidth);

            prepareButton.Location = new Point(SidePadding, prepareY);
            launchCsdkButton.Location = new Point(SidePadding, launchY);
            buildButton.Location = new Point(rightX, prepareY);
            launchGameButton.Location = new Point(rightX, launchY);
        }

        void RefreshHeaderImage()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder) || header.ClientSize.Width <= 0 || header.ClientSize.Height <= 0)
            {
                ReplaceBackgroundImage(header, null);
                return;
            }

            var path = EnsureHeaderImage(folder, header.ClientSize);
            ReplaceBackgroundImage(header, TryLoadImage(path));
        }

        header.Resize += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
        };
        folderText.TextChanged += (_, _) => RefreshHeaderImage();
        form.Activated += (_, _) => RefreshHeaderImage();

        form.Shown += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
            ConfigureAccentButton(launchCsdkButton, CsdkColor, CsdkHoverColor, CsdkPressedColor);
            ConfigureAccentButton(launchGameButton, GameColor, GameHoverColor, GamePressedColor);
        };

        PositionControls();
    }

    private static string EnsureHeaderImage(string projectFolder, Size headerSize)
    {
        var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
        Directory.CreateDirectory(metadataFolder);

        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(metadataFolder);
            File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
        }

        var path = Path.Combine(metadataFolder, HeaderFileName);
        if (File.Exists(path))
        {
            return path;
        }

        var width = Math.Max(1, headerSize.Width);
        var height = Math.Max(1, headerSize.Height);
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(DefaultHeaderColor);
        }
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    private static Image? TryLoadImage(string path)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var source = Image.FromStream(stream);
            return new Bitmap(source);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or OutOfMemoryException)
        {
            return null;
        }
    }

    private static void ReplaceBackgroundImage(Panel header, Image? replacement)
    {
        var previous = header.BackgroundImage;
        header.BackgroundImage = replacement;
        previous?.Dispose();
    }

    private static void ConfigureAccentButton(
        Button button,
        Color normal,
        Color hover,
        Color pressed)
    {
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.ForeColor = Color.White;
        button.BackColor = normal;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = hover;
        button.FlatAppearance.MouseDownBackColor = pressed;

        button.MouseEnter += (_, _) =>
        {
            button.ForeColor = Color.White;
            button.BackColor = hover;
            button.FlatAppearance.BorderSize = 0;
        };
        button.MouseLeave += (_, _) =>
        {
            button.ForeColor = Color.White;
            button.BackColor = normal;
            button.FlatAppearance.BorderSize = 0;
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                button.ForeColor = Color.White;
                button.BackColor = pressed;
                button.FlatAppearance.BorderSize = 0;
            }
        };
        button.MouseUp += (_, _) =>
        {
            var pointer = button.PointToClient(Cursor.Position);
            button.ForeColor = Color.White;
            button.BackColor = button.ClientRectangle.Contains(pointer) ? hover : normal;
            button.FlatAppearance.BorderSize = 0;
        };
    }

    private static void LaunchDeadlock(MainForm form)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = DeadlockSteamUri,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                form,
                ex.Message,
                UiText.T("Could not launch Deadlock", "Не удалось запустить Deadlock"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static Button? FindButton(Control root, string english, string russian) =>
        FindDescendants<Button>(root)
            .FirstOrDefault(button =>
                string.Equals(button.Text, english, StringComparison.Ordinal)
                || string.Equals(button.Text, russian, StringComparison.Ordinal));

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
}
