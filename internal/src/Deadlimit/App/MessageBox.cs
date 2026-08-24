using Deadlimit.Core;

namespace Deadlimit.App;

internal enum DeadlimitDialogChoice
{
    None,
    Ok,
    Yes,
    No,
    Cancel,
    Retry,
    Abort,
    Ignore,
    TryAgain,
    Continue,
    YesWithoutBackup,
}

internal sealed record DeadlimitDialogButton(
    string Text,
    DeadlimitDialogChoice Choice,
    bool IsDefault = false,
    bool IsCancel = false);

// Namespace-local replacement for System.Windows.Forms.MessageBox.
// All unqualified MessageBox.Show calls in Deadlimit.App are routed through the
// same themed dialog so app-generated modal windows stay visually consistent.
internal static class MessageBox
{
    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon) =>
        ToDialogResult(ShowCore(owner, text, caption, CreateButtons(buttons)));

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons) =>
        Show(owner, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption) =>
        Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon) =>
        ToDialogResult(ShowCore(null, text, caption, CreateButtons(buttons)));

    public static DialogResult Show(string text, string caption) =>
        Show(text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(string text) =>
        Show(text, "Deadlimit", MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DeadlimitDialogChoice ShowCustom(
        IWin32Window owner,
        string text,
        string caption,
        params DeadlimitDialogButton[] buttons) =>
        ShowCore(owner, text, caption, buttons);

    private static DeadlimitDialogChoice ShowCore(
        IWin32Window? owner,
        string text,
        string caption,
        IReadOnlyList<DeadlimitDialogButton> buttons)
    {
        var effectiveButtons = buttons.Count == 0
            ? [new DeadlimitDialogButton("OK", DeadlimitDialogChoice.Ok, IsDefault: true, IsCancel: true)]
            : buttons;

        using var dialog = new Form
        {
            Text = string.IsNullOrWhiteSpace(caption) ? "Deadlimit" : caption,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            ShowIcon = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = Padding.Empty,
        };

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(18),
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = text,
            Margin = new Padding(0, 0, 0, 16),
        };

        var buttonRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
        };

        var result = DeadlimitDialogChoice.None;
        Button? defaultButton = null;
        Button? cancelButton = null;

        // RightToLeft places the first added control at the right edge, matching the
        // existing build-summary dialog and standard Deadlimit action layout.
        foreach (var definition in effectiveButtons)
        {
            var button = new Button
            {
                Text = definition.Text,
                AutoSize = true,
                MinimumSize = new Size(72, 0),
                Margin = new Padding(6, 0, 0, 0),
            };
            button.Click += (_, _) =>
            {
                result = definition.Choice;
                dialog.Close();
            };

            if (definition.IsDefault && defaultButton is null)
            {
                defaultButton = button;
            }
            if (definition.IsCancel && cancelButton is null)
            {
                cancelButton = button;
            }

            buttonRow.Controls.Add(button);
        }

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(buttonRow, 0, 1);
        dialog.Controls.Add(root);

        UiTheme.ApplyCustomPalette(dialog, ProjectStore.GetToolPathSettings().UiTheme);

        defaultButton ??= buttonRow.Controls.OfType<Button>().FirstOrDefault();
        cancelButton ??= effectiveButtons.Count == 1
            ? defaultButton
            : null;

        if (defaultButton is not null)
        {
            dialog.AcceptButton = defaultButton;
        }
        if (cancelButton is not null)
        {
            dialog.CancelButton = cancelButton;
        }

        dialog.FormClosing += (_, _) =>
        {
            if (result == DeadlimitDialogChoice.None && cancelButton is not null)
            {
                var cancelDefinition = effectiveButtons
                    .FirstOrDefault(definition => definition.IsCancel);
                result = cancelDefinition?.Choice
                    ?? effectiveButtons.First().Choice;
            }
        };

        if (owner is null)
        {
            dialog.StartPosition = FormStartPosition.CenterScreen;
            dialog.ShowDialog();
        }
        else
        {
            dialog.ShowDialog(owner);
        }

        return result == DeadlimitDialogChoice.None
            ? DeadlimitDialogChoice.Cancel
            : result;
    }

    private static DeadlimitDialogButton[] CreateButtons(MessageBoxButtons buttons) => buttons switch
    {
        MessageBoxButtons.OK =>
        [
            new("OK", DeadlimitDialogChoice.Ok, IsDefault: true, IsCancel: true),
        ],
        MessageBoxButtons.OKCancel =>
        [
            new(UiText.T("CANCEL", "ОТМЕНА"), DeadlimitDialogChoice.Cancel, IsCancel: true),
            new("OK", DeadlimitDialogChoice.Ok, IsDefault: true),
        ],
        MessageBoxButtons.YesNo =>
        [
            new(UiText.T("NO", "НЕТ"), DeadlimitDialogChoice.No, IsCancel: true),
            new(UiText.T("YES", "ДА"), DeadlimitDialogChoice.Yes, IsDefault: true),
        ],
        MessageBoxButtons.YesNoCancel =>
        [
            new(UiText.T("CANCEL", "ОТМЕНА"), DeadlimitDialogChoice.Cancel, IsCancel: true),
            new(UiText.T("NO", "НЕТ"), DeadlimitDialogChoice.No),
            new(UiText.T("YES", "ДА"), DeadlimitDialogChoice.Yes, IsDefault: true),
        ],
        MessageBoxButtons.RetryCancel =>
        [
            new(UiText.T("CANCEL", "ОТМЕНА"), DeadlimitDialogChoice.Cancel, IsCancel: true),
            new(UiText.T("RETRY", "ПОВТОРИТЬ"), DeadlimitDialogChoice.Retry, IsDefault: true),
        ],
        MessageBoxButtons.AbortRetryIgnore =>
        [
            new(UiText.T("IGNORE", "ИГНОРИРОВАТЬ"), DeadlimitDialogChoice.Ignore),
            new(UiText.T("RETRY", "ПОВТОРИТЬ"), DeadlimitDialogChoice.Retry, IsDefault: true),
            new(UiText.T("ABORT", "ПРЕРВАТЬ"), DeadlimitDialogChoice.Abort, IsCancel: true),
        ],
        MessageBoxButtons.CancelTryContinue =>
        [
            new(UiText.T("CANCEL", "ОТМЕНА"), DeadlimitDialogChoice.Cancel, IsCancel: true),
            new(UiText.T("CONTINUE", "ПРОДОЛЖИТЬ"), DeadlimitDialogChoice.Continue),
            new(UiText.T("TRY AGAIN", "ПОВТОРИТЬ"), DeadlimitDialogChoice.TryAgain, IsDefault: true),
        ],
        _ =>
        [
            new("OK", DeadlimitDialogChoice.Ok, IsDefault: true, IsCancel: true),
        ],
    };

    private static DialogResult ToDialogResult(DeadlimitDialogChoice result) => result switch
    {
        DeadlimitDialogChoice.Ok => DialogResult.OK,
        DeadlimitDialogChoice.Yes => DialogResult.Yes,
        DeadlimitDialogChoice.No => DialogResult.No,
        DeadlimitDialogChoice.Cancel => DialogResult.Cancel,
        DeadlimitDialogChoice.Retry => DialogResult.Retry,
        DeadlimitDialogChoice.Abort => DialogResult.Abort,
        DeadlimitDialogChoice.Ignore => DialogResult.Ignore,
        DeadlimitDialogChoice.TryAgain => DialogResult.TryAgain,
        DeadlimitDialogChoice.Continue => DialogResult.Continue,
        _ => DialogResult.Cancel,
    };
}
