using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed record HeroExtractionDialogResult(
    bool Accepted,
    bool RemoveBackupAfterSuccess,
    HeroExtractionOptions Options);

internal static class HeroExtractionOptionsDialog
{
    internal static HeroExtractionDialogResult Show(IWin32Window owner, bool hasExistingSource)
    {
        using var dialog = new Form
        {
            Text = UiText.T("Extract hero source", "Извлечь исходники героя"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
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
            RowCount = 4,
            Margin = Padding.Empty,
            Padding = new Padding(18),
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(720, 0),
            Text = hasExistingSource
                ? UiText.T(
                    "0source already contains files. Choose what to include in the refreshed extraction.",
                    "0source уже содержит файлы. Выберите, что включить в обновлённое извлечение.")
                : UiText.T(
                    "Choose what to include in this hero source extraction.",
                    "Выберите, что включить в извлечение исходников героя."),
            Margin = new Padding(0, 0, 0, 12),
        };

        var extractTexturesCheck = new CheckBox
        {
            Text = UiText.T("Extract textures", "Извлекать текстуры"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var extractAbilitiesCheck = new CheckBox
        {
            Text = UiText.T("Extract abilities", "Извлекать способности"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 16),
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

        var accepted = false;
        var removeBackupAfterSuccess = false;

        var noButton = DialogUiFactory.CreateActionButton(UiText.T("NO", "НЕТ"));
        noButton.DialogResult = DialogResult.Cancel;

        var noBackupButton = DialogUiFactory.CreateActionButton(
            UiText.T("YES, NO BACKUP", "ДА, БЕЗ БЭКАПА"));
        noBackupButton.Enabled = hasExistingSource;
        noBackupButton.Click += (_, _) =>
        {
            accepted = true;
            removeBackupAfterSuccess = true;
            dialog.Close();
        };

        var yesButton = DialogUiFactory.CreateActionButton(UiText.T("YES", "ДА"));
        yesButton.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };

        buttonRow.Controls.Add(noButton);
        buttonRow.Controls.Add(noBackupButton);
        buttonRow.Controls.Add(yesButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(extractTexturesCheck, 0, 1);
        root.Controls.Add(extractAbilitiesCheck, 0, 2);
        root.Controls.Add(buttonRow, 0, 3);
        dialog.Controls.Add(root);

        dialog.AcceptButton = yesButton;
        dialog.CancelButton = noButton;
        UiTheme.ApplyCustomPalette(dialog, ProjectStore.GetToolPathSettings().UiTheme);

        dialog.ShowDialog(owner);

        return new HeroExtractionDialogResult(
            accepted,
            removeBackupAfterSuccess,
            new HeroExtractionOptions(
                extractTexturesCheck.Checked,
                extractAbilitiesCheck.Checked));
    }
}
