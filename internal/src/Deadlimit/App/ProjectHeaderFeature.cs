using System.Drawing;
using Deadlimit.Core;

namespace Deadlimit.App;

internal static class ProjectHeaderFeature
{
    private const string DeadlockSteamUri = "steam://rungameid/1422450";

    public static void Attach(MainForm form)
    {
        var settingsButton = FindDescendants<Button>(form)
            .FirstOrDefault(button =>
                string.Equals(button.Text, "SETTINGS", StringComparison.Ordinal)
                || string.Equals(button.Text, "НАСТРОЙКИ", StringComparison.Ordinal));
        if (settingsButton?.Parent is not FlowLayoutPanel topBar
            || topBar.Parent is not TableLayoutPanel workspace)
        {
            return;
        }

        var prepareButton = FindButton(topBar, "PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK");
        var buildButton = FindButton(topBar, "BUILD & TEST", "СОБРАТЬ И ТЕСТИРОВАТЬ");
        var launchCsdkButton = FindButton(topBar, "LAUNCH CSDK", "ЗАПУСТИТЬ CSDK");
        if (prepareButton is null || buildButton is null || launchCsdkButton is null)
        {
            return;
        }

        var folderText = FindProjectFolderText(form);
        if (folderText is null)
        {
            return;
        }

        topBar.Controls.Remove(settingsButton);
        topBar.Controls.Remove(prepareButton);
        topBar.Controls.Remove(buildButton);
        topBar.Controls.Remove(launchCsdkButton);
        workspace.Controls.Remove(topBar);
        topBar.Dispose();

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            Height = ProjectArtworkService.HeaderSize.Height,
            MinimumSize = new Size(0, ProjectArtworkService.HeaderSize.Height),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.FromArgb(31, 33, 36),
            BackgroundImageLayout = ImageLayout.Stretch,
        };

        var overlay = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12, 9, 12, 12),
        };
        overlay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        overlay.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        overlay.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        overlay.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        overlay.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        overlay.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        StyleSecondaryButton(settingsButton);
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.Margin = Padding.Empty;
        var settingsHost = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        settingsHost.Controls.Add(settingsButton);
        overlay.Controls.Add(settingsHost, 0, 0);
        overlay.SetColumnSpan(settingsHost, 2);

        StyleSecondaryButton(prepareButton);
        prepareButton.Text = UiText.T("PREPARE FOR CSDK", "ПОДГОТОВИТЬ ДЛЯ CSDK");
        prepareButton.Anchor = AnchorStyles.Bottom;
        prepareButton.Margin = new Padding(0, 0, 0, 4);

        StyleSecondaryButton(buildButton);
        buildButton.Text = UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТОВ");
        buildButton.Anchor = AnchorStyles.Bottom;
        buildButton.Margin = new Padding(0, 0, 0, 4);

        StyleLaunchButton(launchCsdkButton, Color.FromArgb(91, 46, 199));
        launchCsdkButton.Text = UiText.T("▶  LAUNCH CSDK", "▶  ЗАПУСК CSDK");
        launchCsdkButton.Anchor = AnchorStyles.Top;

        var launchDeadlockButton = new Button
        {
            Text = UiText.T("▶  LAUNCH GAME", "▶  ЗАПУСК ИГРЫ"),
            AutoSize = false,
            Width = 178,
            Height = 38,
            Anchor = AnchorStyles.Top,
            Margin = Padding.Empty,
            TabStop = false,
        };
        StyleLaunchButton(launchDeadlockButton, Color.FromArgb(73, 178, 57));
        launchDeadlockButton.Click += (_, _) => LaunchDeadlock(form);

        overlay.Controls.Add(prepareButton, 0, 2);
        overlay.Controls.Add(buildButton, 1, 2);
        overlay.Controls.Add(launchCsdkButton, 0, 3);
        overlay.Controls.Add(launchDeadlockButton, 1, 3);

        header.Controls.Add(overlay);
        workspace.Controls.Add(header, 0, 0);

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            AutoPopDelay = 8000,
        };
        toolTip.SetToolTip(
            launchDeadlockButton,
            UiText.T("Launch retail Deadlock through Steam.", "Запустить retail Deadlock через Steam."));

        void RefreshArtwork()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder))
            {
                ReplaceBackgroundImage(header, null);
                toolTip.SetToolTip(header, string.Empty);
                return;
            }

            try
            {
                var artworkPath = ProjectArtworkService.EnsureDefaultHeader(folder);
                ReplaceBackgroundImage(header, LoadUnlockedImage(artworkPath));
                toolTip.SetToolTip(
                    header,
                    UiText.T(
                        $"Project header: {artworkPath} ({ProjectArtworkService.HeaderSize.Width}×{ProjectArtworkService.HeaderSize.Height} PNG). Replace this file to customize the project cover.",
                        $"Обложка проекта: {artworkPath} ({ProjectArtworkService.HeaderSize.Width}×{ProjectArtworkService.HeaderSize.Height} PNG). Замените этот файл, чтобы изменить шапку проекта."));
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or System.Runtime.InteropServices.ExternalException)
            {
                ReplaceBackgroundImage(header, null);
            }
        }

        folderText.TextChanged += (_, _) => RefreshArtwork();
        form.Activated += (_, _) => RefreshArtwork();
        form.FormClosed += (_, _) => ReplaceBackgroundImage(header, null);
        RefreshArtwork();
    }

    private static void StyleSecondaryButton(Button button)
    {
        button.AutoSize = true;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(103, 108, 114);
        button.BackColor = Color.FromArgb(58, 62, 67);
        button.ForeColor = Color.FromArgb(205, 210, 216);
        button.Font = new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static void StyleLaunchButton(Button button, Color background)
    {
        var hover = Color.FromArgb(
            Math.Min(255, background.R + 12),
            Math.Min(255, background.G + 12),
            Math.Min(255, background.B + 12));
        var pressed = Color.FromArgb(
            Math.Max(0, background.R - 14),
            Math.Max(0, background.G - 14),
            Math.Max(0, background.B - 14));

        void Apply(Color color)
        {
            button.BackColor = color;
            button.ForeColor = Color.White;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor = color;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = pressed;
        }

        button.AutoSize = false;
        button.Width = 178;
        button.Height = 38;
        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
        button.Margin = Padding.Empty;
        button.TabStop = false;
        Apply(background);

        // Existing BuildFeature buttons were already themed before this feature rearranges
        // them. These handlers run after the theme handlers and keep the Steam-like accent.
        button.MouseEnter += (_, _) => Apply(hover);
        button.MouseLeave += (_, _) => Apply(background);
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                Apply(pressed);
            }
        };
        button.MouseUp += (_, _) =>
        {
            var pointer = button.PointToClient(Cursor.Position);
            Apply(button.ClientRectangle.Contains(pointer) ? hover : background);
        };
    }

    private static Button? FindButton(Control root, string english, string russian) =>
        FindDescendants<Button>(root)
            .FirstOrDefault(button =>
                string.Equals(button.Text, english, StringComparison.Ordinal)
                || string.Equals(button.Text, russian, StringComparison.Ordinal));

    private static TextBox? FindProjectFolderText(MainForm form)
    {
        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        return projectGroup is null
            ? null
            : FindDescendants<TextBox>(projectGroup).FirstOrDefault(textBox => textBox.ReadOnly);
    }

    private static Image? LoadUnlockedImage(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var source = Image.FromStream(stream);
        return new Bitmap(source);
    }

    private static void ReplaceBackgroundImage(Control control, Image? image)
    {
        var previous = control.BackgroundImage;
        control.BackgroundImage = image;
        previous?.Dispose();
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
                UiText.T("Could not launch Deadlock through Steam", "Не удалось запустить Deadlock через Steam"),
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
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
}
