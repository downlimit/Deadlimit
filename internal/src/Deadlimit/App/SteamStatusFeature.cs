using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SteamStatusFeature
{
    private const int StatusHeight = 40;

    public static void Attach(MainForm form, string theme)
    {
        var statusStrip = FindDescendants<StatusStrip>(form).FirstOrDefault();
        if (statusStrip?.Parent is not TableLayoutPanel root)
        {
            return;
        }

        var statusSource = statusStrip.Items
            .OfType<ToolStripStatusLabel>()
            .FirstOrDefault(item => !item.Spring);
        var progressSource = statusStrip.Items
            .OfType<ToolStripProgressBar>()
            .FirstOrDefault();
        if (statusSource is null)
        {
            return;
        }

        var row = root.GetRow(statusStrip);
        var palette = ResolvePalette(theme);

        root.Controls.Remove(statusStrip);
        statusStrip.Visible = false;
        if (row >= 0 && row < root.RowStyles.Count)
        {
            root.RowStyles[row].SizeType = SizeType.Absolute;
            root.RowStyles[row].Height = StatusHeight;
        }

        var bar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = palette.Bar,
        };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 44));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftLabel = CreateZoneLabel(ContentAlignment.MiddleLeft, palette.StrongText);
        var rightLabel = CreateZoneLabel(ContentAlignment.MiddleRight, palette.Text);

        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(10, 3, 10, 5),
            BackColor = palette.Bar,
        };
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        center.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));

        var centerTop = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = palette.Bar,
        };
        centerTop.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        centerTop.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        centerTop.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var centerLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = palette.Text,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
        };
        var percentLabel = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = palette.Text,
            BackColor = Color.Transparent,
            Margin = new Padding(8, 0, 0, 0),
        };
        var progress = new SteamProgressBar(palette.Track, palette.Progress)
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Visible = false,
        };

        centerTop.Controls.Add(centerLabel, 0, 0);
        centerTop.Controls.Add(percentLabel, 1, 0);
        center.Controls.Add(centerTop, 0, 0);
        center.Controls.Add(progress, 0, 1);

        bar.Controls.Add(WrapZone(leftLabel, palette.Bar), 0, 0);
        bar.Controls.Add(CreateSeparator(palette.Separator), 1, 0);
        bar.Controls.Add(center, 2, 0);
        bar.Controls.Add(CreateSeparator(palette.Separator), 3, 0);
        bar.Controls.Add(WrapZone(rightLabel, palette.Bar), 4, 0);

        root.Controls.Add(bar, 0, row);
        root.SetColumnSpan(bar, root.ColumnCount);

        var projectGroup = FindDescendants<GroupBox>(form)
            .FirstOrDefault(group =>
                string.Equals(group.Text, "Project", StringComparison.Ordinal)
                || string.Equals(group.Text, "Проект", StringComparison.Ordinal));
        var folderText = projectGroup is null
            ? null
            : FindDescendants<TextBox>(projectGroup).FirstOrDefault(textBox => textBox.ReadOnly);
        var releaseId = projectGroup is null
            ? null
            : FindDescendants<NumericUpDown>(projectGroup).FirstOrDefault();

        var toolTip = new ToolTip
        {
            ShowAlways = true,
            InitialDelay = 350,
            ReshowDelay = 100,
            AutoPopDelay = 10000,
        };

        void UpdateContext()
        {
            var folder = folderText?.Text.Trim() ?? string.Empty;
            if (!Directory.Exists(folder))
            {
                leftLabel.Text = UiText.T("◇  Library", "◇  Библиотека");
                rightLabel.Text = "ID —   ·   pak##_dir.vpk";
                toolTip.SetToolTip(leftLabel, string.Empty);
                toolTip.SetToolTip(
                    rightLabel,
                    UiText.T(
                        "Release ID determines the retail VPK file name.\n\nFor example: ID 05 → pak05_dir.vpk.",
                        "Release ID определяет имя retail VPK-файла.\n\nНапример: ID 05 → pak05_dir.vpk."));
                return;
            }

            var projectName = Path.GetFileName(folder.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            leftLabel.Text = $"◆  {projectName}";
            toolTip.SetToolTip(leftLabel, folder);

            var manifest = ProjectStore.TryLoad(folder);
            var release = releaseId?.Text.Trim();
            if (string.IsNullOrWhiteSpace(release))
            {
                release = manifest?.ReleaseTarget;
            }

            var releaseText = string.IsNullOrWhiteSpace(release) ? "—" : release;
            var vpkName = int.TryParse(release, out var slot) && slot is >= 1 and <= 99
                ? $"pak{slot:D2}_dir.vpk"
                : "pak##_dir.vpk";

            rightLabel.Text = $"ID {releaseText}   ·   {vpkName}";
            toolTip.SetToolTip(
                rightLabel,
                UiText.T(
                    $"Release ID: {releaseText}\nRetail VPK: {vpkName}\n\nChanging the ID changes the VPK slot/file name.",
                    $"Release ID: {releaseText}\nRetail VPK: {vpkName}\n\nИзменение ID меняет VPK-слот и имя файла."));
        }

        void UpdateOperation()
        {
            centerLabel.Text = statusSource.Text;

            var progressVisible = progressSource is not null
                && (progressSource.Available || progressSource.Visible);
            progress.Visible = progressVisible;
            if (progressVisible && progressSource is not null)
            {
                progress.SetValue(progressSource.Value);
                percentLabel.Text = $"{progressSource.Value}%";
            }
            else
            {
                progress.SetValue(0);
                percentLabel.Text = string.Empty;
            }
        }

        statusSource.TextChanged += (_, _) => UpdateOperation();
        if (folderText is not null)
        {
            folderText.TextChanged += (_, _) => UpdateContext();
        }
        if (releaseId is not null)
        {
            releaseId.ValueChanged += (_, _) => UpdateContext();
            releaseId.TextChanged += (_, _) => UpdateContext();
        }
        form.Activated += (_, _) => UpdateContext();

        // ToolStripProgressBar does not expose a useful ValueChanged event, so mirror its
        // short-lived build animation at the same cadence as the existing spinner.
        var timer = new System.Windows.Forms.Timer
        {
            Interval = 120,
        };
        timer.Tick += (_, _) => UpdateOperation();
        timer.Start();

        form.FormClosed += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            statusStrip.Dispose();
        };

        UpdateContext();
        UpdateOperation();
    }

    private static Label CreateZoneLabel(ContentAlignment alignment, Color color) => new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = alignment,
        ForeColor = color,
        BackColor = Color.Transparent,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
    };

    private static Control WrapZone(Control content, Color backColor)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(10, 0, 10, 0),
            BackColor = backColor,
        };
        panel.Controls.Add(content);
        return panel;
    }

    private static Control CreateSeparator(Color color) => new Panel
    {
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 6, 0, 6),
        BackColor = color,
    };

    private static StatusPalette ResolvePalette(string theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant();
        var dark = normalized == "dark"
            || (normalized == "system" && Application.IsDarkModeEnabled)
            || normalized is not ("light" or "gray" or "dark" or "system") && Application.IsDarkModeEnabled;

        if (dark)
        {
            return new StatusPalette(
                Bar: Color.FromArgb(33, 33, 33),
                Separator: Color.FromArgb(63, 63, 63),
                Text: Color.FromArgb(160, 160, 160),
                StrongText: Color.FromArgb(210, 210, 210),
                Track: Color.FromArgb(11, 16, 22),
                Progress: Color.FromArgb(26, 159, 255));
        }

        if (normalized == "gray")
        {
            return new StatusPalette(
                Bar: Color.FromArgb(65, 65, 65),
                Separator: Color.FromArgb(96, 96, 96),
                Text: Color.FromArgb(184, 184, 184),
                StrongText: Color.FromArgb(226, 226, 226),
                Track: Color.FromArgb(30, 34, 40),
                Progress: Color.FromArgb(63, 169, 240));
        }

        return new StatusPalette(
            Bar: Color.FromArgb(248, 248, 248),
            Separator: Color.FromArgb(190, 190, 190),
            Text: Color.FromArgb(86, 86, 86),
            StrongText: Color.FromArgb(34, 34, 34),
            Track: Color.FromArgb(201, 207, 214),
            Progress: Color.FromArgb(34, 132, 205));
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

    private sealed class SteamProgressBar : Control
    {
        private readonly Color _track;
        private readonly Color _fill;
        private int _value;

        public SteamProgressBar(Color track, Color fill)
        {
            _track = track;
            _fill = fill;
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer,
                true);
            TabStop = false;
        }

        public void SetValue(int value)
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (_value == clamped)
            {
                return;
            }

            _value = clamped;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var trackBrush = new SolidBrush(_track);
            e.Graphics.FillRectangle(trackBrush, ClientRectangle);

            if (_value <= 0)
            {
                return;
            }

            var width = (int)Math.Round(ClientSize.Width * (_value / 100.0));
            using var fillBrush = new SolidBrush(_fill);
            e.Graphics.FillRectangle(fillBrush, 0, 0, Math.Max(1, width), ClientSize.Height);
        }
    }

    private sealed record StatusPalette(
        Color Bar,
        Color Separator,
        Color Text,
        Color StrongText,
        Color Track,
        Color Progress);
}
