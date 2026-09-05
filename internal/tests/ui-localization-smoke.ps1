$ErrorActionPreference = 'Stop'

function Assert-Contains([string]$Path, [string]$Pattern) {
    if (-not (Select-String -LiteralPath $Path -SimpleMatch $Pattern -Quiet)) {
        throw "Localization contract missing in ${Path}: ${Pattern}"
    }
}

function Assert-NotContains([string]$Path, [string]$Pattern) {
    if (Select-String -LiteralPath $Path -SimpleMatch $Pattern -Quiet) {
        throw "Unlocalized user-facing text remains in ${Path}: ${Pattern}"
    }
}

Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' '"DeadlockTools установлен, но проверить актуальность не удалось: GitHub недоступен."'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' ': status.Detail'
Assert-Contains 'internal/src/Deadlimit/App/SettingsToolchainProgressFeature.cs' 'update.State == ToolchainOperationState.Failed'
Assert-Contains 'internal/src/Deadlimit/Core/PrepareAuthoringService.cs' 'LocalizedText.T("Validating Vertex Color source pairs'
Assert-Contains 'internal/src/Deadlimit/Core/BuildAndTestService.cs' 'LocalizedText.T("Starting Build & Test...'
Assert-Contains 'internal/src/Deadlimit/Core/OnlinePreparationSession.cs' 'ОНЛАЙН-ПОДГОТОВКА'
Assert-Contains 'internal/src/Deadlimit/App/OnlinePreparationFeature.cs' 'ОНЛАЙН-ПОДГОТОВКА'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Text = "📂 CSDK Fast Startup Fix"'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'UiText.T("APPLY", "ПРИМЕНИТЬ")'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'UiText.T("CLOSE", "ЗАКРЫТЬ")'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'UiText.T("CANCEL", "ОТМЕНА")'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' '_applyButton.Enabled = hasPendingChanges && !_busy;'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'UiText.T("SAVE", "СОХРАНИТЬ")'
Assert-Contains 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs' 'UiText.T("Could not open project cover", "Не удалось открыть обложку проекта")'
Assert-Contains 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs' 'UiText.T("Open Project Cover", "Открыть обложку проекта")'
Assert-Contains 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs' 'ProjectHeaderFeature.GetHeaderImagePath(folder)'
Assert-NotContains 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs' 'OpenHeaderFolder()'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'SettingsVersionFeature.AddManagerRow(toolsGrid, 0)'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'UiText.T("UPDATE…", "ОБНОВИТЬ…")'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'LatestReleaseApiUrl'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'Deadlimit-release.json'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'ReleaseChannelPolicy.IsPortableRelease'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'Path.Combine(updateRoot, "Update Deadlimit.cmd")'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'DeadlimitManagerPath'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'DeadlimitRelocationService.PrepareRelocationAsync'
Assert-Contains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'ManagerVersionStateKind.Cancelled'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'ToolchainStatusKind.Cancelled'
Assert-Contains 'internal/src/Deadlimit/App/SettingsForm.cs' 'v{GetFriendlyVersion()}'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Tool status is checked when this window opens.'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsForm.cs' 'Состояние инструментов проверяется при открытии окна.'
Assert-Contains 'internal/src/Deadlimit/App/DeadlimitRelocationService.cs' 'Rewrite-Shortcut'
Assert-Contains 'internal/src/Deadlimit/Deadlimit.csproj' '<Version>0.1.0-beta.2</Version>'
Assert-NotContains 'internal/src/Deadlimit/App/SettingsVersionFeature.cs' 'UiText.T("UPDATE DEADLIMIT", "ОБНОВИТЬ DEADLIMIT")'

# BUILD FOR TEST must temporarily cover and disable LAUNCH GAME with the active blue state.
Assert-Contains 'internal/src/Deadlimit/App/BuildLaunchInterlockFeature.cs' '[ModuleInitializer]'
Assert-Contains 'internal/src/Deadlimit/App/BuildLaunchInterlockFeature.cs' 'launchGameButton.Enabled = false;'
Assert-Contains 'internal/src/Deadlimit/App/BuildLaunchInterlockFeature.cs' 'UiText.T("BUILDING...", "ИДЁТ СБОРКА")'
Assert-Contains 'internal/src/Deadlimit/App/BuildLaunchInterlockFeature.cs' 'BuildGradientStart'
Assert-Contains 'internal/src/Deadlimit/App/BuildLaunchInterlockFeature.cs' 'if (interlocked && buildButton.Enabled)'

# Russian error dialogs must keep actionable context instead of collapsing to a title-only message.
Assert-Contains 'internal/src/Deadlimit/App/MessageBox.cs' 'BuildVertexColorPrepareContext'
Assert-Contains 'internal/src/Deadlimit/App/MessageBox.cs' 'рядом нет актуального Vertex Color FBX'
Assert-Contains 'internal/src/Deadlimit/App/MessageBox.cs' 'Подробности:\n{text}'

# Every app tooltip uses the RichToolTip alias, so width, wrapping and emphasis rules apply consistently.
Assert-Contains 'internal/src/Deadlimit/App/GlobalToolTipAlias.cs' 'global using ToolTip = Deadlimit.App.RichToolTip;'
Assert-Contains 'internal/src/Deadlimit/App/RichToolTip.cs' 'private const int MaxContentWidth = 440;'
$appTooltipFiles = Get-ChildItem 'internal/src/Deadlimit/App' -Filter *.cs -File |
    Where-Object { $_.Name -ne 'RichToolTip.cs' }
foreach ($file in $appTooltipFiles) {
    if (Select-String -LiteralPath $file.FullName -SimpleMatch 'System.Windows.Forms.ToolTip' -Quiet) {
        throw "Tooltip bypasses RichToolTip in $($file.Name)."
    }
}

# Run representative English and Russian tooltip copy through the same rewrite pipeline used by RichToolTip.
$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$flags = [Reflection.BindingFlags]::Static -bor [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Public
$fixupsType = $assembly.GetType('Deadlimit.App.TooltipCopyPolicyFixups', $true)
$policyType = $assembly.GetType('Deadlimit.App.TooltipCopyPolicy', $true)
$beforeMethod = $fixupsType.GetMethod('BeforeRewrite', $flags)
$rewriteMethod = $policyType.GetMethod('Rewrite', $flags)
$afterMethod = $fixupsType.GetMethod('AfterRewrite', $flags)

function Rewrite-Tooltip([string]$Text) {
    $value = [string]$beforeMethod.Invoke($null, @($Text))
    $value = [string]$rewriteMethod.Invoke($null, @($value))
    return [string]$afterMethod.Invoke($null, @($value))
}

function Assert-TooltipPlain([string]$Name, [string]$InputText, [string[]]$Required, [string[]]$Forbidden) {
    $result = Rewrite-Tooltip $InputText
    foreach ($term in $Required) {
        if (-not $result.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Tooltip '$Name' lost required user-facing term '$term':`n$result"
        }
    }
    foreach ($term in $Forbidden) {
        if ($result.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Tooltip '$Name' still exposes technical term '$term':`n$result"
        }
    }
}

Assert-TooltipPlain 'prepare-en' `
    "Prepare the selected project's working files for Reduced CSDK.\n\nA normal click preserves manual VMAT tuning and synchronizes matching project textures. Hold SHIFT to regenerate custom materials; the confirmation dialog lets you choose whether to create a backup first." `
    @('**PREPARE FOR CSDK**', '**SHIFT**') `
    @('VMAT', 'ModelDoc', 'Material Editor')

Assert-TooltipPlain 'prepare-ru' `
    'Подготовить рабочие файлы выбранного проекта для Reduced CSDK.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы; в окне подтверждения можно выбрать, создавать ли резервную копию.' `
    @('**ПОДГОТОВИТЬ ДЛЯ CSDK**', '**SHIFT**') `
    @('VMAT', 'custom-материалы', 'ModelDoc')

Assert-TooltipPlain 'release-en' `
    "Game-client VPK release slot: 01-99. Type the number directly or change it with the arrows by ±1.\n\nThe slot becomes part of the deployed VPK filename, for example Release ID 07 → pak07_dir.vpk." `
    @('**Release ID**', '01-99') `
    @('VPK', 'pak07_dir.vpk', 'slot')

Assert-TooltipPlain 'release-ru' `
    'Слот VPK игрового клиента Deadlock: 01-99. Число можно ввести вручную или менять стрелками на ±1.\n\nСлот входит в имя установленного VPK-файла, например Release ID 07 → pak07_dir.vpk.' `
    @('**Release ID**', '01-99') `
    @('VPK', 'pak07_dir.vpk', 'слот')

Assert-TooltipPlain 'vertex-color-en' `
    'Copies the repository MaxScript fileIn command. The helper exports selected geometry and renderable Shape/Spline objects to a **Vertex Color FBX** beside the latest Wall Worm DMX.\n\nOptional **Fixed Gamma** writes RGB^(1/2.2) for Source 2; leave it off for unchanged/Marmoset export.\n\n**PREPARE FOR CSDK** matches multi-color meshes by UV or polygon positions and keeps a rejected sidecar for retry.' `
    @('**Vertex Color**', '**Fixed Gamma**', 'Wall Worm', 'CSDK') `
    @('MaxScript', 'fileIn', 'DMX', 'FBX', 'RGB^', 'sidecar', 'polygon positions')

Assert-TooltipPlain 'vertex-color-ru' `
    'Копирует команду fileIn для MaxScript из репозитория. Скрипт экспортирует выделенную геометрию и renderable Shape/Spline в **Vertex Color FBX** рядом с последним DMX Wall Worm.\n\nОпциональный **Fixed Gamma** записывает RGB^(1/2.2) для Source 2; для обычного экспорта и Marmoset оставьте его выключенным.\n\n**ПОДГОТОВИТЬ ДЛЯ CSDK** сопоставляет многоцветные меши по UV или позициям полигонов и сохраняет отклонённый sidecar для повтора.' `
    @('**Vertex Color**', '**Fixed Gamma**', 'Wall Worm', 'CSDK') `
    @('MaxScript', 'fileIn', 'DMX', 'FBX', 'RGB^', 'sidecar', 'позициям полигонов')

Assert-TooltipPlain 'csdk-fine-tune-en' `
    'Run the optional CSDK fine-tuning from the current installation guide.\n\nDeadlimit downloads the required Deadlock depots, extracts the downloaded VPK as-is, removes the temporary pak01 VPK set, then re-applies Reduced CSDK.\n\nDepotDownloader may open a console for Steam QR authentication.\n\nThe configured Deadlock client folder is only validated and is **never modified**.' `
    @('Reduced CSDK', 'QR code', 'never changed') `
    @('depot', 'VPK', 'pak01', 'DepotDownloader', 'console', 'validated')

Assert-TooltipPlain 'deadlocktools-path-en' `
    'DeadlockTools.exe was not found in the selected DeadlockTools folder.' `
    @('**DeadlockTools**', 'Choose') `
    @('.exe')

Assert-TooltipPlain 'apply-ru' `
    'Проверить и применить изменённые папки и настройки интерфейса.\n\nПути к внешним инструментам можно оставить неуказанными.' `
    @('**ПРИМЕНИТЬ**', 'сохраняет') `
    @('неуказанными', 'внешним инструментам')

Assert-TooltipPlain 'launch-game-en' `
    "Launch Deadlock game client through Steam.\n\nHold SHIFT while clicking to copy 'cl_lock_camera true' to the clipboard without launching the game." `
    @('**LAUNCH GAME**', '**SHIFT**') `
    @('cl_lock_camera', 'command')

# Verify the concrete missing-Vertex-Color-FBX message remains understandable in Russian.
$messageBoxType = $assembly.GetType('Deadlimit.App.MessageBox', $true)
$vertexContextMethod = $messageBoxType.GetMethod('BuildVertexColorPrepareContext', $flags)
if ($null -eq $vertexContextMethod) { throw 'MessageBox.BuildVertexColorPrepareContext was not found.' }
$sampleVertexError = "PREPARE stopped before changing CSDK content because ivy.dmx requires Vertex Color but its source pair is not safe.`n`nVertex Color source is incomplete: ivy_vertexcolor.fbx is missing. Export the Vertex Color FBX after the latest DMX export."
$vertexContext = [string]$vertexContextMethod.Invoke($null, @($sampleVertexError))
foreach ($term in @('ivy.dmx', 'ivy_vertexcolor.fbx', 'актуального Vertex Color FBX', 'ПОДГОТОВИТЬ ДЛЯ CSDK')) {
    if (-not $vertexContext.Contains($term, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Vertex Color error context lost '$term':`n$vertexContext"
    }
}

# Static control text in App should either be localized, a technical/product token, a glyph, or data-driven.
$allowed = @(
    'OK', 'CSDK', 'DMX', 'PNG', '📂', 'Deadlimit Manager', 'Deadlimit Scripts'
)
$files = Get-ChildItem 'internal/src/Deadlimit/App' -Filter *.cs -File
foreach ($file in $files) {
    $lineNumber = 0
    foreach ($line in Get-Content $file.FullName) {
        $lineNumber++
        if ($line -notmatch 'Text\\s*=\\s*"([^"\\r\n]*)"') { continue }
        $literal = $Matches[1]
        if ($literal -notmatch '[A-Za-z]{3,}') { continue }
        if ($allowed | Where-Object { $literal -eq $_ -or $literal.StartsWith($_ + ':') }) { continue }
        if ($line -match 'UiText\\.T') { continue }
        throw "Possible unlocalized static UI text at $($file.Name):${lineNumber}: $literal"
    }
}

Write-Host 'UI localization and tooltip copy smoke passed.'
