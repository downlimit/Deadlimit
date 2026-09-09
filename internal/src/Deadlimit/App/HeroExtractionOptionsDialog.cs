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
            RowCount = 10,
            Margin = Padding.Empty,
            Padding = new Padding(18),
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = hasExistingSource
                ? UiText.T(
                    "0source already contains files. Choose the source scopes to refresh and which editable source files should also be copied into this project's CSDK addon.",
                    "0source уже содержит файлы. Выберите, какие исходники обновить и какие редактируемые файлы дополнительно скопировать в CSDK-аддон этого проекта.")
                : UiText.T(
                    "Choose the source scopes to extract into 0source and which editable source files should also be copied into this project's CSDK addon.",
                    "Выберите, какие исходники извлечь в 0source и какие редактируемые файлы дополнительно скопировать в CSDK-аддон этого проекта."),
            Margin = new Padding(0, 0, 0, 14),
        };

        var sourceHeader = new Label
        {
            Text = UiText.T("SOURCE", "ИСХОДНИКИ"),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5),
        };

        var extractHeroCheck = new CheckBox
        {
            Text = UiText.T("Extract hero", "Извлекать героя"),
            Checked = true,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var extractAbilitiesCheck = new CheckBox
        {
            Text = UiText.T("Extract abilities", "Извлекать способности"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var extractPortraitsAndUiCheck = new CheckBox
        {
            Text = UiText.T("Extract portraits & UI", "Извлекать портреты и UI"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var extractTexturesCheck = new CheckBox
        {
            Text = UiText.T("Extract textures", "Извлекать текстуры"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 16),
        };

        var csdkHeader = new Label
        {
            Text = UiText.T("CSDK EDITING", "РЕДАКТИРОВАНИЕ В CSDK"),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5),
        };

        var copyMaterialsCheck = new CheckBox
        {
            Text = UiText.T(
                "Copy materials to CSDK for editing",
                "Копировать материалы в CSDK для редактирования"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var copyAbilityFxCheck = new CheckBox
        {
            Text = UiText.T(
                "Copy ability FX to CSDK for editing",
                "Копировать FX способностей в CSDK для редактирования"),
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

        void RefreshDependencies()
        {
            var hasModelScope = extractHeroCheck.Checked || extractAbilitiesCheck.Checked;

            extractTexturesCheck.Enabled = hasModelScope;
            if (!hasModelScope)
            {
                extractTexturesCheck.Checked = false;
            }

            copyMaterialsCheck.Enabled = hasModelScope;
            if (!hasModelScope)
            {
                copyMaterialsCheck.Checked = false;
            }

            copyAbilityFxCheck.Enabled = extractAbilitiesCheck.Checked;
            if (!extractAbilitiesCheck.Checked)
            {
                copyAbilityFxCheck.Checked = false;
            }

            yesButton.Enabled = extractHeroCheck.Checked
                                || extractAbilitiesCheck.Checked
                                || extractPortraitsAndUiCheck.Checked;
            noBackupButton.Enabled = yesButton.Enabled
                                     && (hasExistingSource
                                         || copyMaterialsCheck.Checked
                                         || copyAbilityFxCheck.Checked);
        }

        extractHeroCheck.CheckedChanged += (_, _) => RefreshDependencies();
        extractAbilitiesCheck.CheckedChanged += (_, _) => RefreshDependencies();
        extractPortraitsAndUiCheck.CheckedChanged += (_, _) => RefreshDependencies();
        copyMaterialsCheck.CheckedChanged += (_, _) => RefreshDependencies();
        copyAbilityFxCheck.CheckedChanged += (_, _) => RefreshDependencies();

        buttonRow.Controls.Add(noButton);
        buttonRow.Controls.Add(noBackupButton);
        buttonRow.Controls.Add(yesButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(sourceHeader, 0, 1);
        root.Controls.Add(extractHeroCheck, 0, 2);
        root.Controls.Add(extractAbilitiesCheck, 0, 3);
        root.Controls.Add(extractPortraitsAndUiCheck, 0, 4);
        root.Controls.Add(extractTexturesCheck, 0, 5);
        root.Controls.Add(csdkHeader, 0, 6);
        root.Controls.Add(copyMaterialsCheck, 0, 7);
        root.Controls.Add(copyAbilityFxCheck, 0, 8);
        root.Controls.Add(buttonRow, 0, 9);
        dialog.Controls.Add(root);

        dialog.AcceptButton = yesButton;
        dialog.CancelButton = noButton;
        UiTheme.ApplyCustomPalette(dialog, ProjectStore.GetToolPathSettings().UiTheme);
        RefreshDependencies();

        dialog.ShowDialog(owner);

        return new HeroExtractionDialogResult(
            accepted,
            removeBackupAfterSuccess,
            new HeroExtractionOptions(
                ExtractTextures: extractTexturesCheck.Checked,
                ExtractAbilities: extractAbilitiesCheck.Checked,
                ExtractPortraitsAndUi: extractPortraitsAndUiCheck.Checked,
                ExtractHero: extractHeroCheck.Checked,
                CopyMaterialsToCsdkForEditing: copyMaterialsCheck.Checked,
                CopyAbilityFxToCsdkForEditing: copyAbilityFxCheck.Checked,
                BackupCsdkOverwrites: !removeBackupAfterSuccess));
    }
}
