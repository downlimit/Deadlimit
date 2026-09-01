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

# Static control text in App should either be localized, a technical/product token, a glyph, or data-driven.
$allowed = @(
    'OK', '3ds Max', 'CSDK', 'DMX', 'PNG', '📂', 'Deadlimit Aggregator'
)
$files = Get-ChildItem 'internal/src/Deadlimit/App' -Filter *.cs -File
foreach ($file in $files) {
    $lineNumber = 0
    foreach ($line in Get-Content $file.FullName) {
        $lineNumber++
        if ($line -notmatch 'Text\\s*=\\s*"([^"\\r\
]*)"') { continue }
        $literal = $Matches[1]
        if ($literal -notmatch '[A-Za-z]{3,}') { continue }
        if ($allowed | Where-Object { $literal -eq $_ -or $literal.StartsWith($_ + ':') }) { continue }
        if ($line -match 'UiText\\.T') { continue }
        throw "Possible unlocalized static UI text at $($file.Name):${lineNumber}: $literal"
    }
}

Write-Host 'UI localization smoke passed.'
