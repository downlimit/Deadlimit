$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Contains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -lt 0) {
        throw "$Label contract is missing: $Pattern"
    }
}

function Assert-NotContains([string]$Text, [string]$Pattern, [string]$Label) {
    if ($Text.IndexOf($Pattern, [StringComparison]::Ordinal) -ge 0) {
        throw "$Label contains forbidden text: $Pattern"
    }
}

$csdkWatcherCompatibility = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/CsdkAssetWatcherCompatibility.cs' -Raw
foreach ($required in @(
    '"citadel"',
    '"addons"',
    '"luaunlocker"',
    'Directory.CreateDirectory(contentAddon)',
    'Directory.Exists(gameAddon)'
)) {
    Assert-Contains $csdkWatcherCompatibility $required 'CSDK luaunlocker asset-watcher compatibility'
}

$toolchain = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ToolchainDependencyService.cs' -Raw
foreach ($required in @(
    'PinnedCsdkGeneration = 12',
    'CsdkInstallFolderName = "Reduced_CSDK_12"',
    'CsdkPinnedDriveId = "1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS"',
    'CsdkPinnedDrivePage = "https://drive.google.com/file/d/1-Z-4CszWQNudzwzs6e6abPsp5RGFOURS/view"',
    'GoogleDriveDownloadUnavailableException',
    'https://drive.google.com/uc?export=download&id=',
    'ResolveCsdkInstallRoot(',
    'OpenDownloadResponseAsync(',
    'TryGetGoogleDriveConfirmationUri(',
    'download-form',
    '"downloadUrl"',
    'Google Drive did not provide a downloadable file.',
    'DeadlockToolsPinnedTag = "v1.1.0"',
    'DeadlockToolsWindowsSha256 = "7E4668DA796E4CA67B1EE684CF03270E07FECEBECCF66D04DDF1F3A3E7409DCF"',
    'DepotDownloaderPinnedTag = "DepotDownloader_3.4.0"',
    'DepotDownloaderWindowsSha256 = "41C9E9F0DF54B3AD02E67A11726756E5C73283BD7C2E1B04ACFA5AE4C2ED3767"',
    'IncrementalHash.CreateHash(HashAlgorithmName.SHA256)',
    'ApplyOverlayTransaction(',
    'IsTrustedManagedDeadlockTools(',
    'IsTrustedDepotDownloaderCache(',
    'executableSha256'
)) {
    Assert-Contains $toolchain $required 'Toolchain hardening'
}
Assert-Contains $toolchain 'CsdkAssetWatcherCompatibility.EnsureLuaUnlockerContentMirror(installRoot);' 'CSDK install watcher repair'
Assert-Contains $toolchain 'CsdkAssetWatcherCompatibility.EnsureLuaUnlockerContentMirror(root);' 'CSDK update watcher repair'
Assert-Contains $toolchain 'CsdkAssetWatcherCompatibility.EnsureLuaUnlockerContentMirror(csdkRoot);' 'CSDK setup watcher repair'
Assert-NotContains $toolchain 'repos/dotryen/DeadlockTools/releases/latest' 'Toolchain hardening'
Assert-NotContains $toolchain 'repos/SteamRE/DepotDownloader/releases/latest' 'Toolchain hardening'
Assert-NotContains $toolchain 'ReadCsdkPageAsync' 'Toolchain hardening'
Assert-NotContains $toolchain 'CsdkGenerationRegex' 'Toolchain hardening'

$settingsForm = Get-Content -LiteralPath 'internal/src/Deadlimit/App/SettingsForm.cs' -Raw
foreach ($required in @(
    'Choose the parent folder where Reduced_CSDK_12 will be created',
    'Выберите родительскую папку, внутри которой будет создана Reduced_CSDK_12',
    'string.Equals(new DirectoryInfo(current).Name, "Reduced_CSDK_12"',
    'creates **Reduced_CSDK_12** inside it',
    'ShowGoogleDriveDownloadFallback(',
    'OPEN GOOGLE DRIVE',
    'DeadlimitDialogChoice.OpenGoogleDrive',
    'UseShellExecute = true'
)) {
    Assert-Contains $settingsForm $required 'CSDK parent-folder install'
}
Assert-NotContains $settingsForm 'Choose the folder that will become the Reduced CSDK root' 'CSDK parent-folder install'

$messageBox = Get-Content -LiteralPath 'internal/src/Deadlimit/App/MessageBox.cs' -Raw
Assert-Contains $messageBox 'OpenGoogleDrive' 'CSDK Google Drive fallback dialog'

$vpk = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/VpkSlotOwnershipService.cs' -Raw
foreach ($required in @(
    'LegacyVpkOwnershipException',
    'AdoptLegacySlot(',
    'EnsureSlotUnchanged(',
    'ExistingFamilySha256'
)) {
    Assert-Contains $vpk $required 'VPK ownership hardening'
}

$build = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/BuildAndTestService.cs' -Raw
Assert-Contains $build 'slotOwnership.EnsureSlotUnchanged(manifest, slotSnapshot);' 'Authoring VPK deployment'

$imported = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkBuildAndTestService.cs' -Raw
Assert-Contains $imported 'slotGuard.EnsureSlotUnchanged(manifest, slotCheck);' 'Imported VPK deployment'

$buildUi = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
foreach ($required in @(
    'ApplicationMutationCoordinator.Begin',
    'form.FormClosing',
    'CANCELLING BUILD',
    'CANCELLING PREPARATION'
)) {
    Assert-Contains $buildUi $required 'Pipeline mutation interlock'
}

$mainForm = Get-Content -LiteralPath 'internal/src/Deadlimit/App/MainForm.cs' -Raw
foreach ($required in @(
    'EnsureLegacyRootAuthoringMigrated',
    'The originals will be left unchanged',
    '_heroExtractionCancellation.Cancel()',
    'Project switching is locked while'
)) {
    Assert-Contains $mainForm $required 'Project migration and extraction shutdown'
}

$deadlockInstallLocator = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/DeadlockInstallLocator.cs' -Raw
foreach ($required in @(
    'DeadlockAppId = "1422450"',
    'appmanifest_{DeadlockAppId}.acf',
    'SteamInstallDirRegex',
    '"Deadlock"',
    '"Project8Staging"',
    'EnumerateLibraryCandidates(libraryRoot)'
)) {
    Assert-Contains $deadlockInstallLocator $required 'Deadlock Steam install discovery'
}

$settingsStartup = Get-Content -LiteralPath 'internal/src/Deadlimit/App/SettingsStartupFeature.cs' -Raw
foreach ($required in @(
    'string.IsNullOrWhiteSpace(settings.ProjectsRoot)',
    'string.IsNullOrWhiteSpace(settings.CsdkRoot)',
    'string.IsNullOrWhiteSpace(settings.DeadlockToolsRoot)',
    'string.IsNullOrWhiteSpace(settings.RetailDeadlockRoot)'
)) {
    Assert-Contains $settingsStartup $required 'Startup settings prompt'
}
$program = Get-Content -LiteralPath 'internal/src/Deadlimit/Program.cs' -Raw
foreach ($required in @(
    'SettingsStartupFeature.RequiresSetup(ProjectStore.GetToolPathSettings())',
    'form.ShowSettings();',
    '&& !_startupSmoke'
)) {
    Assert-Contains $program $required 'Startup settings prompt'
}
Assert-Contains $mainForm 'internal void ShowSettings()' 'Startup settings prompt'

$settingsPulse = Get-Content -LiteralPath 'internal/src/Deadlimit/App/SettingsAttentionPulseFeature.cs' -Raw
foreach ($required in @(
    'PulsePeriodSeconds = 1.4',
    'Color.FromArgb(244, 67, 54)',
    'Color.White',
    'SettingsStartupFeature.RequiresSetup(ProjectStore.GetToolPathSettings())',
    'SettingsOpened()',
    'SettingsClosed()',
    'button.ForeColor = Blend(PulseBaseColor, AlertColor, mix)',
    'button.ForeColor = _restingColor'
)) {
    Assert-Contains $settingsPulse $required 'Settings attention pulse'
}
foreach ($required in @(
    'SettingsAttentionPulseFeature.SettingsOpened();',
    'SettingsAttentionPulseFeature.SettingsClosed();'
)) {
    Assert-Contains $mainForm $required 'Settings attention pulse'
}
Assert-Contains $program 'SettingsAttentionPulseFeature.Attach(form);' 'Settings attention pulse'

$projectFilesUi = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ProjectFilesFeature.cs' -Raw
foreach ($required in @(
    'ToAuthoringDisplayPath(file)',
    'ProjectAuthoringLayout.AuthoringFolderName + "/"',
    '"PNG / TGA / PSD",',
    'textureList.Items.Add(ToAuthoringDisplayPath(file))',
    'out var dmxTitleLabel',
    'out var textureTitleLabel',
    'dmxList.MouseDoubleClick',
    'textureList.MouseDoubleClick',
    'AttachBackgroundSelectionClear(form, ClearFileSelection)',
    'IndexFromPoint(eventArgs.Location) < 0',
    'ResolveAuthoringFilePath(projectFolder, displayPath)',
    'Arguments = $"/select,\"{filePath}\""'
)) {
    Assert-Contains $projectFilesUi $required 'Authoring file-list display'
}
foreach ($forbidden in @(
    'dmxList.Items.Add($"[DMX]',
    'dmxList.Items.Add($"[FBX]',
    'dmxList.Items.Add($"[{Path.GetExtension'
)) {
    Assert-NotContains $projectFilesUi $forbidden 'Authoring file-list display'
}
foreach ($required in @(
    'summaryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50))',
    'summaryRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 16))',
    'root.RowStyles.Add(new RowStyle(SizeType.Absolute, 18))',
    'TextAlign = ContentAlignment.MiddleLeft',
    'MAIN FILE:',
    'ГЛАВНЫЙ ФАЙЛ:',
    'SOURCE FILE COUNT:',
    'КОЛИЧЕСТВО ИСХОДНЫХ ФАЙЛОВ:',
    'DMX / FBX / glTF: {modelCount} files',
    'DMX / FBX / glTF: {modelCount} файлов',
    'PNG / TGA / PSD: {scan.PngTextures.Count} files',
    'PNG / TGA / PSD: {scan.PngTextures.Count} файлов'
)) {
    Assert-Contains $projectFilesUi $required 'Compact project file summary'
}
foreach ($forbidden in @(
    'TextAlign = ContentAlignment.MiddleCenter',
    'ОСНОВНОЙ ФАЙЛ:',
    'AUTHORING MODELS:',
    'АВТОРСКИЕ МОДЕЛИ:',
    'AUTHORING TEXTURES:',
    'АВТОРСКИЕ ТЕКСТУРЫ:'
)) {
    Assert-NotContains $projectFilesUi $forbidden 'Compact project file summary'
}
foreach ($required in @(
    'assetsGroup.Padding = new Padding(3, 8, 3, 3)',
    'Margin = new Padding(0, 8, 0, 0)',
    'Margin = new Padding(0, 0, 0, 2)'
)) {
    Assert-Contains $projectFilesUi $required 'Project file vertical spacing'
}

$projectScanner = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ProjectScanner.cs' -Raw
foreach ($required in @(
    'Path.GetExtension(path).Equals(".png"',
    'Path.GetExtension(path).Equals(".tga"',
    'Path.GetExtension(path).Equals(".psd"'
)) {
    Assert-Contains $projectScanner $required 'Texture authoring format scan'
}

$projectEntry = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ProjectCreationChoiceFeature.cs' -Raw
Assert-Contains $projectEntry 'Cannot extract a VPK while' 'VPK extraction mutation interlock'
foreach ($required in @(
    'ExtractVpkIntoCurrentProjectAsync',
    'var currentProject = form.LoadedManifest;',
    'await Task.Run(',
    'VpkImportProgressPresenter',
    'SteamStatusFeature.ReportProgress(_form, update.Percent)',
    'SteamStatusFeature.ReportProgress(_form, null)',
    'ImportedVpkProjectService.ExtractIntoExisting('
)) {
    Assert-Contains $projectEntry $required 'Responsive current-project VPK extraction flow'
}
foreach ($removed in @(
    'ProjectEntryChoiceDialog',
    'ImportedVpkProjectNameDialog',
    'How do you want to add the project?',
    'VPK extracted into a new project.'
)) {
    Assert-NotContains $projectEntry $removed 'Direct current-project VPK extraction flow'
}

$mainForm = Get-Content -LiteralPath 'internal/src/Deadlimit/App/MainForm.cs' -Raw
foreach ($required in @(
    '(ModifierKeys & Keys.Shift) == Keys.Shift',
    'ProjectCreationChoiceFeature.ExtractVpkIntoCurrentProjectAsync(this, _extractHeroButton)',
    'await ExtractHeroSourceAsync()'
)) {
    Assert-Contains $mainForm $required 'EXTRACT SOURCE modifier routing'
}

$projectLibrary = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ProjectLibraryFeature.cs' -Raw
Assert-Contains $projectLibrary 'addButton.Click += (_, _) => CreateProjectFolder();' 'Direct new-project creation action'
Assert-Contains $mainForm 'ReleaseSlotAllocationService.AllocateFirstFree(' 'New-project first-free Release ID allocation'

$importedProject = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkProjectService.cs' -Raw
Assert-Contains $importedProject 'ProjectAuthoringLayout.EnsureStructure(projectFolder);' 'Imported VPK project folder structure'
Assert-Contains $importedProject 'var scan = ProjectScanner.Scan(projectFolder);' 'Imported VPK reconstructed-file index'
Assert-Contains $importedProject 'ReleaseTarget = source.ReleaseTarget' 'Current project Release ID preservation'
Assert-Contains $importedProject 'importedManifest.ImportedVpk!.SourceReleaseTarget' 'Imported VPK source and target slot separation'
Assert-NotContains $importedProject 'Directory.CreateDirectory(projectFolder)' 'VPK extraction must not create another project folder'

$addonIdentity = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/AddonIdentityService.cs' -Raw
Assert-Contains $addonIdentity 'sameAddonAndProjectFolder' 'Recreated project CSDK addon continuity'
Assert-Contains $addonIdentity 'projectId = ownership.ProjectId;' 'Recreated project CSDK addon continuity'

$importedPayload = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkPayloadService.cs' -Raw
Assert-Contains $importedPayload 'public const string AuthoringFolderName = ProjectAuthoringLayout.AuthoringFolderName;' 'Imported VPK 1authoring destination'
foreach ($required in @(
    'public const string CompiledFolderName = "imported-compiled";',
    'new TextureExtract(resource).ToContentFile()',
    'VrfContentExtraction.Extract(resource, fileLoader)',
    'modelExtract.AnimationsToExtract.Clear()',
    'ModelExtract.ModelExtractType.Map_AggregateSplit',
    'NormalizeTextureAuthoringPath(decompiledPath)',
    'imported-authoring-map.json'
)) {
    Assert-Contains $importedPayload $required 'Imported VPK editable-resource reconstruction'
}
if ($importedPayload.Contains('public const string PayloadFolderName = "payload";', [StringComparison]::Ordinal)) {
    throw 'Imported VPK extraction must not restore the legacy payload destination.'
}

$heroExtraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/HeroExtractionService.cs' -Raw
foreach ($required in @(
    'ExtractContentFileWithShaderCompatibilityFallback(',
    'TextureAuthoringPathPolicy.Normalize(decompiledPath)',
    'VrfContentExtraction.Extract('
)) {
    Assert-Contains $heroExtraction $required 'Hero extraction newer VCS material fallback'
}

$vrfExtraction = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/VrfContentExtraction.cs' -Raw
foreach ($required in @(
    'resource.ResourceType == ResourceType.Material',
    'IsUnsupportedVcsVersion(exception)',
    'new MaterialExtract(resource, fileLoader: null).ToContentFile()',
    'Only VCS file versions',
    'vcsFileVersion'
)) {
    Assert-Contains $vrfExtraction $required 'Shared newer VCS material fallback'
}
Assert-Contains $importedPayload 'VrfContentExtraction.Extract(resource, fileLoader)' 'Imported VPK newer VCS material fallback'

$customMaterials = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/CustomMaterialAuthoringService.cs' -Raw
Assert-Contains $customMaterials 'VrfContentExtraction.Extract(resource, fileLoader)' 'PREPARE newer VCS material fallback'

$sourceResolver = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ExtractedSourceAssetResolver.cs' -Raw
Assert-Contains $sourceResolver 'manifest.Mode == ProjectMode.ImportedVpk' 'Imported VPK multi-model source routing'
Assert-Contains $sourceResolver 'ProjectAuthoringLayout.GetAuthoringRoot(manifest)' 'Imported VPK multi-model source routing'

$importedAuthoringBuild = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkAuthoringBuildService.cs' -Raw
foreach ($required in @(
    'AuthoringMatchesImport(authoringMap.AuthoringFiles, currentAuthoring)',
    'ResolveOriginalCompiledFolder(manifest.ProjectFolder)',
    'StageChangedTextureDescriptors(',
    'Direct ResourceCompiler inputs:',
    'ImportedVpkPayloadService.BuiltCompiledFolderName',
    'new ImportedVpkAuthoringBuildSnapshot'
)) {
    Assert-Contains $importedAuthoringBuild $required 'Imported VPK 1authoring build input'
}

$importedRepack = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ImportedVpkRepackService.cs' -Raw
Assert-Contains $importedRepack 'ImportedVpkRepackEntryStatus.RebuiltFromAuthoring' 'Imported VPK authoring provenance'
Assert-Contains $importedRepack 'ImportedVpkAuthoringBuildService.TryLoadSnapshot' 'Imported VPK authoring report validation'

$projectCreation = Get-Content -LiteralPath 'internal/src/Deadlimit/App/ProjectCreationChoiceFeature.cs' -Raw
if ($projectCreation.Contains('ImportedProjectModeFeature.Attach', [StringComparison]::Ordinal)) {
    throw 'Imported VPK projects must keep normal source extraction and CSDK preparation actions enabled.'
}

$authoring = Get-Content -LiteralPath 'internal/src/Deadlimit/Core/ProjectAuthoringLayout.cs' -Raw
Assert-Contains $authoring "More than one authoring model uses the filename" 'Authoring ambiguity guard'

Write-Host 'Critical stabilization contracts passed.'
