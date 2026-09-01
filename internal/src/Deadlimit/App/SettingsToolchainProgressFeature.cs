using Deadlimit.Core;

namespace Deadlimit.App;

internal static class SettingsToolchainProgressFeature
{
    private static readonly Dictionary<SettingsForm, ProgressUi> AttachedForms = [];
    private static bool _attached;

    public static void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Application.Idle += OnApplicationIdle;
        ToolchainOperationHub.Changed += OnToolchainOperationChanged;
    }

    internal static void Prepare(SettingsForm form)
    {
        ArgumentNullException.ThrowIfNull(form);
        Attach();
        if (!AttachedForms.ContainsKey(form))
        {
            AttachToForm(form);
        }
    }

    private static void OnApplicationIdle(object? sender, EventArgs e)
    {
        foreach (var form in Application.OpenForms.OfType<SettingsForm>().ToArray())
        {
            Prepare(form);
        }
    }

    private static void AttachToForm(SettingsForm form)
    {
        var root = form.Controls.OfType<TableLayoutPanel>()
            .FirstOrDefault(panel => panel.Dock == DockStyle.Fill && panel.ColumnCount == 1 && panel.RowCount == 3);
        if (root is null)
        {
            return;
        }

        var footer = root.GetControlFromPosition(0, 2);
        if (footer is null)
        {
            return;
        }

        root.SuspendLayout();
        try
        {
            root.RowCount = 4;
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.SetRow(footer, 3);

            var label = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Height = 24,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = true,
                Margin = new Padding(0, 3, 8, 3),
            };

            var progressBar = new ProgressBar
            {
                Dock = DockStyle.Fill,
                Height = 18,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Style = ProgressBarStyle.Blocks,
                Margin = new Padding(0, 5, 8, 5),
            };

            var cancelButton = new Button
            {
                Text = UiText.T("CANCEL", "ОТМЕНА"),
                AutoSize = false,
                Width = 82,
                Height = 26,
                Margin = new Padding(0, 2, 0, 2),
                Enabled = false,
            };
            cancelButton.Click += (_, _) => ToolchainOperationHub.CancelActive();

            var panel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3,
                RowCount = 1,
                Visible = false,
                Margin = new Padding(0, 6, 0, 0),
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
            panel.Controls.Add(label, 0, 0);
            panel.Controls.Add(progressBar, 1, 0);
            panel.Controls.Add(cancelButton, 2, 0);

            root.Controls.Add(panel, 0, 2);

            // The dialog itself is already themed by SettingsForm. Theme only this newly
            // inserted subtree so opening Settings never requires a second full-form theme
            // pass after its HWND already exists.
            UiTheme.ApplyCustomPalette(panel, ProjectStore.GetToolPathSettings().UiTheme);

            var ui = new ProgressUi(panel, label, progressBar, cancelButton);
            AttachedForms[form] = ui;
            form.FormClosed += (_, _) =>
            {
                if (AttachedForms.Remove(form, out var removed))
                {
                    removed.Dispose();
                }
            };
        }
        finally
        {
            root.ResumeLayout(true);
        }
    }

    private static void OnToolchainOperationChanged(object? sender, ToolchainOperationUpdate update)
    {
        foreach (var pair in AttachedForms.ToArray())
        {
            var form = pair.Key;
            if (form.IsDisposed || !form.IsHandleCreated)
            {
                continue;
            }

            form.BeginInvoke((Action)(() => ApplyUpdate(form, pair.Value, update)));
        }
    }

    private static void ApplyUpdate(SettingsForm form, ProgressUi ui, ToolchainOperationUpdate update)
    {
        if (form.IsDisposed)
        {
            return;
        }

        ui.HideTimer.Stop();
        ui.Panel.Visible = true;
        ui.Label.Text = UiText.IsRussian && update.State == ToolchainOperationState.Failed
            ? "Операция с инструментом завершилась ошибкой."
            : update.Message;
        ui.CancelButton.Enabled = update.State == ToolchainOperationState.Running;

        if (update.State == ToolchainOperationState.Running && update.Percent is null)
        {
            ui.ProgressBar.Style = ProgressBarStyle.Marquee;
            ui.ProgressBar.MarqueeAnimationSpeed = 28;
        }
        else
        {
            ui.ProgressBar.Style = ProgressBarStyle.Blocks;
            ui.ProgressBar.MarqueeAnimationSpeed = 0;
            ui.ProgressBar.Value = Math.Clamp(update.Percent ?? 0, 0, 100);
        }

        if (update.State != ToolchainOperationState.Running)
        {
            ui.HideTimer.Interval = update.State == ToolchainOperationState.Completed ? 1200 : 2200;
            ui.HideTimer.Start();
        }
    }

    private sealed class ProgressUi : IDisposable
    {
        public ProgressUi(TableLayoutPanel panel, Label label, ProgressBar progressBar, Button cancelButton)
        {
            Panel = panel;
            Label = label;
            ProgressBar = progressBar;
            CancelButton = cancelButton;
            HideTimer = new System.Windows.Forms.Timer();
            HideTimer.Tick += (_, _) =>
            {
                HideTimer.Stop();
                Panel.Visible = false;
                ProgressBar.Style = ProgressBarStyle.Blocks;
                ProgressBar.Value = 0;
            };
        }

        public TableLayoutPanel Panel { get; }
        public Label Label { get; }
        public ProgressBar ProgressBar { get; }
        public Button CancelButton { get; }
        public System.Windows.Forms.Timer HideTimer { get; }

        public void Dispose()
        {
            HideTimer.Stop();
            HideTimer.Dispose();
        }
    }
}
