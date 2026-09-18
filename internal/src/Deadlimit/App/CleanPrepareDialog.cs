using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed class CleanPrepareDialog : Form
{
    private readonly CheckBox _materials;
    private readonly CheckBox _physics;
    private readonly CheckBox _effects;
    private readonly Button _backupButton;
    private readonly Button _withoutBackupButton;

    private CleanPrepareDialog()
    {
        Text = UiText.T("Choose what to reprepare", "Что переподготовить начисто");
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = true;
        ShowIcon = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(680, 390);

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 78,
            Padding = new Padding(16, 14, 16, 4),
            Text = UiText.T(
                "Selected sections will be restored from the extracted retail source. Unselected materials, physics and effects keep their current artist edits.",
                "Выбранные разделы будут восстановлены из извлечённого retail-исходника. Текущие правки материалов, физики и эффектов в невыбранных разделах сохранятся."),
        };

        _materials = CreateOption(
            UiText.T("Materials", "Материалы"),
            UiText.T("Regenerate Deadlimit-managed custom VMAT files.", "Пересоздать custom-VMAT, которыми управляет Deadlimit."));
        _physics = CreateOption(
            UiText.T("Physics", "Физика"),
            UiText.T("Restore retail collision bodies and ragdoll joints. Custom jiggle and physics edits will be replaced.", "Восстановить retail-тела коллизии и ragdoll-joints. Пользовательские jiggle- и physics-правки будут заменены."));
        _effects = CreateOption(
            UiText.T("Effects", "Эффекты"),
            UiText.T("Restore existing particle-effect VPCF files from the extracted retail source.", "Восстановить существующие VPCF-файлы эффектов из извлечённого retail-исходника."));

        var options = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(16, 8, 16, 8),
        };
        options.Controls.Add(_materials);
        options.Controls.Add(_physics);
        options.Controls.Add(_effects);

        _backupButton = CreateActionButton(UiText.T("BACK UP & REPREPARE", "СДЕЛАТЬ БЭКАП И ПЕРЕПОДГОТОВИТЬ"));
        _withoutBackupButton = CreateActionButton(UiText.T("REPREPARE WITHOUT BACKUP", "ПЕРЕПОДГОТОВИТЬ БЕЗ БЭКАПА"));
        var cancelButton = CreateActionButton(UiText.T("CANCEL", "ОТМЕНА"));
        cancelButton.DialogResult = DialogResult.Cancel;
        CancelButton = cancelButton;

        _backupButton.Click += (_, _) => Finish(createBackup: true);
        _withoutBackupButton.Click += (_, _) => Finish(createBackup: false);
        _materials.CheckedChanged += (_, _) => UpdateActions();
        _physics.CheckedChanged += (_, _) => UpdateActions();
        _effects.CheckedChanged += (_, _) => UpdateActions();

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 64,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8, 12, 8, 8),
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(_withoutBackupButton);
        buttons.Controls.Add(_backupButton);

        Controls.Add(options);
        Controls.Add(buttons);
        Controls.Add(intro);
        UiTheme.ApplyCustomPalette(this, ProjectStore.GetToolPathSettings().UiTheme);
        UpdateActions();
    }

    public PrepareAuthoringOptions? Selection { get; private set; }

    public static PrepareAuthoringOptions? Choose(IWin32Window owner)
    {
        using var dialog = new CleanPrepareDialog();
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.Selection : null;
    }

    private static CheckBox CreateOption(string title, string description, bool isChecked = false) => new()
    {
        AutoSize = false,
        Width = 630,
        Height = 68,
        Checked = isChecked,
        Text = $"{title}\r\n{description}",
        Padding = new Padding(8, 4, 4, 4),
    };

    private static Button CreateActionButton(string text) =>
        DialogUiFactory.CreateActionButton(text);

    private void UpdateActions()
    {
        var hasSelection = _materials.Checked || _physics.Checked || _effects.Checked;
        _backupButton.Enabled = hasSelection;
        _withoutBackupButton.Enabled = hasSelection;
    }

    private void Finish(bool createBackup)
    {
        var sections = PrepareResetSections.None;
        if (_materials.Checked)
        {
            sections |= PrepareResetSections.Materials;
        }
        if (_physics.Checked)
        {
            sections |= PrepareResetSections.Physics;
        }
        if (_effects.Checked)
        {
            sections |= PrepareResetSections.Effects;
        }

        Selection = new PrepareAuthoringOptions(sections, createBackup);
        DialogResult = DialogResult.OK;
        Close();
    }
}
