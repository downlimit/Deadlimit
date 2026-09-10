using Deadlimit.Core;

namespace Deadlimit.App;

internal sealed record HeroExtractionDialogResult(
    bool Accepted,
    bool RemoveBackupAfterSuccess,
    HeroExtractionOptions Options);

internal static class HeroExtractionOptionsDialog
{
    internal static HeroExtractionDialogResult Show(
        IWin32Window owner,
        bool hasExistingDmxSource,
        bool hasExistingGltfSource)
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
            RowCount = 14,
            Margin = Padding.Empty,
            Padding = new Padding(18),
        };

        var message = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Text = hasExistingDmxSource || hasExistingGltfSource
                ? UiText.T(
                    "Extracted source already exists. Choose its format, the scopes to refresh and which editable source files should also be copied into this project's CSDK addon.",
                    "Извлечённые исходники уже существуют. Выберите формат, обновляемые группы и редактируемые файлы для копирования в CSDK-аддон проекта.")
                : UiText.T(
                    "Choose the source format, extraction scopes and which editable source files should also be copied into this project's CSDK addon.",
                    "Выберите формат исходников, группы для извлечения и редактируемые файлы для копирования в CSDK-аддон проекта."),
            Margin = new Padding(0, 0, 0, 14),
        };

        var formatHeader = new Label
        {
            Text = UiText.T("SOURCE FORMAT", "ФОРМАТ ИСХОДНИКОВ"),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 5),
        };

        var formatRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
        };

        var dmxFormatRadio = new RadioButton
        {
            Text = UiText.T("DMX — CSDK build source", "DMX — исходники для сборки CSDK"),
            Checked = true,
            AutoSize = true,
            Margin = new Padding(0, 3, 18, 5),
        };

        var gltfFormatRadio = new RadioButton
        {
            Text = UiText.T(
                "glTF — DCC source in 0source\\glTFsource",
                "glTF — DCC-исходники в 0source\\glTFsource"),
            AutoSize = true,
            Margin = new Padding(0, 3, 0, 5),
        };

        var formatNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(22, 0, 0, 14),
        };

        var sourceHeader = new Label
        {
            Text = UiText.T("SOURCE", "ИСХОДНИКИ"),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
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
            Text = UiText.T(
                "Extract textures by dependencies",
                "Извлекать текстуры по зависимостям"),
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 16),
        };

        var csdkHeader = new Label
        {
            Text = UiText.T("CSDK EDITING", "РЕДАКТИРОВАНИЕ В CSDK"),
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold),
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
            Enabled = false,
            Checked = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0, 3, 0, 5),
        };

        var copyAbilityFxNote = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            ForeColor = SystemColors.GrayText,
            Text = UiText.T(
                "Disabled for Reduced CSDK 12: current Deadlock ability effects may use a newer VPCF format and make project builds fail. Models, materials and textures referenced by the retail effect can still be replaced; editing the particle graph requires a compatible newer CSDK.",
                "Отключено для Reduced CSDK 12: актуальные эффекты Deadlock могут использовать более новую версию VPCF и ломать сборку проекта. Модели, материалы и текстуры, на которые ссылается retail-эффект, можно заменять; для редактирования самого графа частиц нужен совместимый более новый CSDK."),
            Margin = new Padding(22, 0, 0, 16),
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
            var isGltf = gltfFormatRadio.Checked;

            extractTexturesCheck.Enabled = hasModelScope;
            if (!hasModelScope)
            {
                extractTexturesCheck.Checked = false;
            }

            copyMaterialsCheck.Enabled = hasModelScope && !isGltf;
            if (!copyMaterialsCheck.Enabled)
            {
                copyMaterialsCheck.Checked = false;
            }

            copyAbilityFxCheck.Enabled = false;
            copyAbilityFxCheck.Checked = false;

            formatNote.Text = isGltf
                ? UiText.T(
                    "glTF files, buffers and texture data are refreshed only inside 0source\\glTFsource. PREPARE and BUILD continue using the DMX source tree in 0source.",
                    "Файлы glTF, буферы и данные текстур обновляются только внутри 0source\\glTFsource. ПОДГОТОВКА и СБОРКА продолжают использовать DMX-дерево в 0source.")
                : UiText.T(
                    "DMX keeps the current compile-ready extraction layout directly in 0source.",
                    "DMX сохраняет текущую готовую к компиляции структуру непосредственно в 0source.");

            yesButton.Enabled = extractHeroCheck.Checked
                                || extractAbilitiesCheck.Checked
                                || extractPortraitsAndUiCheck.Checked;
            noBackupButton.Enabled = yesButton.Enabled
                                     && ((isGltf ? hasExistingGltfSource : hasExistingDmxSource)
                                         || copyMaterialsCheck.Checked
                                         || copyAbilityFxCheck.Checked);
        }

        dmxFormatRadio.CheckedChanged += (_, _) => RefreshDependencies();
        gltfFormatRadio.CheckedChanged += (_, _) => RefreshDependencies();
        extractHeroCheck.CheckedChanged += (_, _) => RefreshDependencies();
        extractAbilitiesCheck.CheckedChanged += (_, _) => RefreshDependencies();
        extractPortraitsAndUiCheck.CheckedChanged += (_, _) => RefreshDependencies();
        copyMaterialsCheck.CheckedChanged += (_, _) => RefreshDependencies();

        formatRow.Controls.Add(dmxFormatRadio);
        formatRow.Controls.Add(gltfFormatRadio);

        buttonRow.Controls.Add(noButton);
        buttonRow.Controls.Add(noBackupButton);
        buttonRow.Controls.Add(yesButton);

        root.Controls.Add(message, 0, 0);
        root.Controls.Add(formatHeader, 0, 1);
        root.Controls.Add(formatRow, 0, 2);
        root.Controls.Add(formatNote, 0, 3);
        root.Controls.Add(sourceHeader, 0, 4);
        root.Controls.Add(extractHeroCheck, 0, 5);
        root.Controls.Add(extractAbilitiesCheck, 0, 6);
        root.Controls.Add(extractPortraitsAndUiCheck, 0, 7);
        root.Controls.Add(extractTexturesCheck, 0, 8);
        root.Controls.Add(csdkHeader, 0, 9);
        root.Controls.Add(copyMaterialsCheck, 0, 10);
        root.Controls.Add(copyAbilityFxCheck, 0, 11);
        root.Controls.Add(copyAbilityFxNote, 0, 12);
        root.Controls.Add(buttonRow, 0, 13);
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
                BackupCsdkOverwrites: !removeBackupAfterSuccess,
                Format: gltfFormatRadio.Checked
                    ? HeroExtractionFormat.Gltf
                    : HeroExtractionFormat.Dmx));
    }
}
