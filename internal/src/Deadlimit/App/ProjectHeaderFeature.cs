using System.Drawing.Drawing2D;
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
    private const string DeadlockSteamAppId = "1422450";
    private const string DeadlockSteamUri = "steam://rungameid/" + DeadlockSteamAppId;

    private static readonly Color DefaultHeaderColor = Color.FromArgb(36, 39, 43);
    private static readonly Color CsdkGradientStart = Color.FromArgb(0x58, 0x31, 0xC7);
    private static readonly Color CsdkGradientEnd = Color.FromArgb(0x9E, 0x1D, 0xC3);
    private static readonly Color GameGradientStart = Color.FromArgb(0x4C, 0xC7, 0x31);
    private static readonly Color GameGradientEnd = Color.FromArgb(0x13, 0xA5, 0x44);

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
        var buildButton = FindButton(topBar, "BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА")
            ?? FindButton(topBar, "BUILD & TEST", "СОБРАТЬ И ТЕСТИРОВАТЬ");
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

        settingsButton.AutoSize = false;
        settingsButton.Size = new Size(30, 22);
        settingsButton.Text = "⚙";
        settingsButton.Font = new Font("Segoe UI Symbol", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
        settingsButton.TextAlign = ContentAlignment.MiddleCenter;
        settingsButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        settingsButton.Margin = Padding.Empty;
        settingsButton.Padding = Padding.Empty;
        settingsButton.TabStop = false;

        prepareButton.AutoSize = false;
        prepareButton.Size = new Size(ActionWidth, PrepareHeight);
        prepareButton.TextAlign = ContentAlignment.MiddleCenter;
        prepareButton.Margin = Padding.Empty;

        buildButton.AutoSize = false;
        buildButton.Size = new Size(ActionWidth, PrepareHeight);
        buildButton.Text = UiText.T("BUILD FOR TEST", "СОБРАТЬ ДЛЯ ТЕСТА");
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
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };
        toolTip.SetToolTip(
            settingsButton,
            UiText.T(
                "Open Deadlimit settings.\n\nConfigure the projects folder, tool locations, interface language and theme.",
                "Открыть настройки Deadlimit.\n\nЗдесь задаются папка проектов, пути к инструментам, язык и тема интерфейса."));
        toolTip.SetToolTip(
            launchGameButton,
            UiText.T(
                "Launch retail Deadlock through Steam.\n\nThis button only starts the game; it does not build or redeploy the current project.",
                "Запустить retail Deadlock через Steam.\n\nЭта кнопка только запускает игру и не собирает заново текущий проект."));
        toolTip.SetToolTip(
            header,
            UiText.T(
                "This image is the current project's editable cover.\n\nDouble-click the cover to open the hidden .deadlimit folder containing project-header.png. Replace that PNG to customize the header.",
                "Это редактируемая обложка текущего проекта.\n\nДважды щёлкните по обложке, чтобы открыть скрытую папку .deadlimit с файлом project-header.png. Замените этот PNG, чтобы изменить шапку."));

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

        void OpenHeaderFolder()
        {
            var folder = folderText.Text.Trim();
            if (!Directory.Exists(folder))
            {
                return;
            }

            try
            {
                var imagePath = EnsureHeaderImage(folder, header.ClientSize);
                var artworkFolder = Path.GetDirectoryName(imagePath);
                if (string.IsNullOrWhiteSpace(artworkFolder) || !Directory.Exists(artworkFolder))
                {
                    return;
                }

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{artworkFolder}\"",
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
                    form,
                    ex.Message,
                    UiText.T("Could not open artwork folder", "Не удалось открыть папку обложки"),
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        header.Resize += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
        };
        header.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                OpenHeaderFolder();
            }
        };
        folderText.TextChanged += (_, _) => RefreshHeaderImage();
        form.Activated += (_, _) => RefreshHeaderImage();

        form.Shown += (_, _) =>
        {
            PositionControls();
            RefreshHeaderImage();
            ConfigureGradientButton(launchCsdkButton, CsdkGradientStart, CsdkGradientEnd);
            ConfigureGradientButton(launchGameButton, GameGradientStart, GameGradientEnd);
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

    private static void ConfigureGradientButton(Button button, Color gradientStart, Color gradientEnd)
    {
        var hovered = false;
        var pressed = false;

        button.UseVisualStyleBackColor = false;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.ForeColor = Color.White;
        button.BackColor = gradientStart;

        button.Paint += (_, e) =>
        {
            var start = gradientStart;
            var end = gradientEnd;
            if (hovered)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.05, valueFactor: 1.10);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.05, valueFactor: 1.10);
            }
            if (pressed)
            {
                start = IncreaseSaturationAndValue(start, saturationFactor: 1.0, valueFactor: 0.90);
                end = IncreaseSaturationAndValue(end, saturationFactor: 1.0, valueFactor: 0.90);
            }

            using var brush = new LinearGradientBrush(button.ClientRectangle, start, end, LinearGradientMode.Horizontal);
            e.Graphics.FillRectangle(brush, button.ClientRectangle);
            TextRenderer.DrawText(
                e.Graphics,
                button.Text,
                button.Font,
                button.ClientRectangle,
                Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        };

        button.MouseEnter += (_, _) =>
        {
            hovered = true;
            button.Invalidate();
        };
        button.MouseLeave += (_, _) =>
        {
            hovered = false;
            pressed = false;
            button.Invalidate();
        };
        button.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                button.Invalidate();
            }
        };
        button.MouseUp += (_, _) =>
        {
            pressed = false;
            button.Invalidate();
        };
    }

    private static Color IncreaseSaturationAndValue(Color color, double saturationFactor, double valueFactor)
    {
        var hue = color.GetHue();
        var max = Math.Max(color.R, Math.Max(color.G, color.B)) / 255.0;
        var min = Math.Min(color.R, Math.Min(color.G, color.B)) / 255.0;
        var saturation = max <= 0.0 ? 0.0 : (max - min) / max;
        saturation = Math.Clamp(saturation * saturationFactor, 0.0, 1.0);
        var value = Math.Clamp(max * valueFactor, 0.0, 1.0);
        return ColorFromHsv(hue, saturation, value);
    }

    private static Color ColorFromHsv(double hue, double saturation, double value)
    {
        var chroma = value * saturation;
        var sector = hue / 60.0;
        var x = chroma * (1.0 - Math.Abs(sector % 2.0 - 1.0));

        (double r, double g, double b) = sector switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        var m = value - chroma;
        return Color.FromArgb(
            (int)Math.Round((r + m) * 255),
            (int)Math.Round((g + m) * 255),
            (int)Math.Round((b + m) * 255));
    }

    private static void LaunchDeadlock(MainForm form)
    {
        if (TryLaunchDeadlockThroughSteamExecutable())
        {
            return;
        }

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

    private static bool TryLaunchDeadlockThroughSteamExecutable()
    {
        var steamExecutable = FindSteamExecutable();
        if (steamExecutable is null)
        {
            return false;
        }

        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = steamExecutable,
                Arguments = $"-applaunch {DeadlockSteamAppId}",
                UseShellExecute = false,
            };

            var steamDirectory = Path.GetDirectoryName(steamExecutable);
            if (!string.IsNullOrWhiteSpace(steamDirectory))
            {
                startInfo.WorkingDirectory = steamDirectory;
            }

            System.Diagnostics.Process.Start(startInfo);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static string? FindSteamExecutable()
    {
        var steamProcesses = System.Diagnostics.Process.GetProcessesByName("steam");
        try
        {
            foreach (var process in steamProcesses)
            {
                try
                {
                    var path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        return path;
                    }
                }
                catch (InvalidOperationException)
                {
                }
                catch (System.ComponentModel.Win32Exception)
                {
                }
                catch (NotSupportedException)
                {
                }
            }
        }
        finally
        {
            foreach (var process in steamProcesses)
            {
                process.Dispose();
            }
        }

        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steam.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Steam", "steam.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
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
