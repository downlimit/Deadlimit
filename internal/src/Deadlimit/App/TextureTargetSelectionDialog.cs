using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed record TextureTargetSelectionResult(
    bool Accepted,
    bool Remember,
    IReadOnlyList<string> ResourcePaths);

internal static class TextureTargetSelectionDialog
{
    public static TextureTargetSelectionResult Show(
        IWin32Window owner,
        AmbiguousTextureTargetException ambiguity)
    {
        using var dialog = new Form
        {
            Text = UiText.T("Choose texture target", "Выберите назначение текстуры"),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = true,
            ShowIcon = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var root = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18),
        };
        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = UiText.T(
                $"The authoring file '{ambiguity.AuthoringIdentity}' matches several Deadlock resources. Choose the resource to replace.",
                $"Авторский файл «{ambiguity.AuthoringIdentity}» подходит к нескольким ресурсам Deadlock. Выберите ресурс для замены."),
            Margin = new Padding(0, 0, 0, 12),
        };
        var targets = new ListBox
        {
            Width = 760,
            Height = Math.Min(220, 28 + ambiguity.CandidateResourcePaths.Count * 19),
            SelectionMode = SelectionMode.One,
            Margin = new Padding(0, 0, 0, 10),
        };
        targets.Items.AddRange(ambiguity.CandidateResourcePaths.Cast<object>().ToArray());
        targets.SelectedIndex = 0;

        var replaceAll = new CheckBox
        {
            AutoSize = true,
            Text = UiText.T(
                "Replace all matching resources",
                "Заменить все подходящие ресурсы"),
            Margin = new Padding(0, 0, 0, 6),
        };
        replaceAll.CheckedChanged += (_, _) => targets.Enabled = !replaceAll.Checked;

        var remember = new CheckBox
        {
            AutoSize = true,
            Checked = true,
            Text = UiText.T(
                "Do not ask again for this specific authoring file",
                "Больше не спрашивать об этом конкретном авторском файле"),
            Margin = new Padding(0, 0, 0, 14),
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };
        var accepted = false;
        var cancel = DialogUiFactory.CreateActionButton(UiText.T("CANCEL", "ОТМЕНА"));
        cancel.DialogResult = DialogResult.Cancel;
        var apply = DialogUiFactory.CreateActionButton(UiText.T("USE SELECTION", "ИСПОЛЬЗОВАТЬ"));
        apply.Click += (_, _) =>
        {
            accepted = true;
            dialog.Close();
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(apply);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(targets, 0, 1);
        root.Controls.Add(replaceAll, 0, 2);
        root.Controls.Add(remember, 0, 3);
        root.Controls.Add(buttons, 0, 4);
        dialog.Controls.Add(root);
        dialog.AcceptButton = apply;
        dialog.CancelButton = cancel;
        UiTheme.ApplyCustomPalette(dialog, ProjectStore.GetToolPathSettings().UiTheme);
        dialog.ShowDialog(owner);

        var selected = replaceAll.Checked
            ? ambiguity.CandidateResourcePaths
            : accepted && targets.SelectedItem is string path
                ? [path]
                : [];
        return new TextureTargetSelectionResult(accepted, remember.Checked, selected);
    }
}
