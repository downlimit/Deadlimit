$ErrorActionPreference = 'Stop'

function Read-Normalized([string]$Path) {
    return (Get-Content -LiteralPath $Path -Raw).Replace("`r`n", "`n")
}

function Write-Normalized([string]$Path, [string]$Text) {
    Set-Content -LiteralPath $Path -Value $Text -Encoding utf8NoBOM -NoNewline
}

function Replace-Exact([string]$Text, [string]$Old, [string]$New, [string]$Label) {
    $oldNormalized = $Old.Replace("`r`n", "`n")
    $newNormalized = $New.Replace("`r`n", "`n")
    $count = ([regex]::Matches($Text, [regex]::Escape($oldNormalized))).Count
    if ($count -ne 1) {
        throw "${Label}: expected exactly one match, found $count."
    }
    return $Text.Replace($oldNormalized, $newNormalized)
}

# 1) SHIFT+PREPARE: YES / YES, NO BACKUP / NO.
$buildPath = 'internal/src/Deadlimit/App/BuildFeature.cs'
$build = Read-Normalized $buildPath
$build = Replace-Exact $build @'
                "Prepare the selected project's working files for Reduced CSDK12 / ModelDoc / Material Editor.\n\nA normal click preserves manual VMAT tuning while synchronizing project textures. Hold SHIFT to back up and regenerate Deadlimit Aggregator custom materials from their templates.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует текстуры проекта. Удерживайте SHIFT, чтобы создать резервную копию и пересоздать custom-материалы Deadlimit Aggregator из шаблонов."));
'@ @'
                "Prepare the selected project's working files for Reduced CSDK12 / ModelDoc / Material Editor.\n\nA normal click preserves manual VMAT tuning while synchronizing matching project textures. Hold SHIFT to regenerate Deadlimit Manager custom materials; the confirmation dialog lets you choose whether to create a backup first.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12 / ModelDoc / Material Editor.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы Deadlimit Manager; в окне подтверждения можно выбрать, создавать ли резервную копию."));
'@ 'BuildFeature prepare tooltip'

$build = Replace-Exact $build @'
        var regenerateCustomMaterials = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        var manifest = ProjectStore.TryLoadLastProject();
'@ @'
        var regenerateCustomMaterials = (Control.ModifierKeys & Keys.Shift) == Keys.Shift;
        var backupCustomMaterials = true;
        var manifest = ProjectStore.TryLoadLastProject();
'@ 'BuildFeature backup flag'

$build = Replace-Exact $build @'
        if (regenerateCustomMaterials)
        {
            var answer = MessageBox.Show(
                form,
                UiText.T(
                    "SHIFT+PREPARE will back up and regenerate every custom VMAT currently referenced by this project. Manual Material Editor tuning in those VMAT files will be replaced by the current Deadlimit Aggregator templates and project textures.\n\nContinue?",
                    "SHIFT+ПОДГОТОВИТЬ создаст резервную копию и пересоздаст все custom-VMAT, на которые сейчас ссылается проект. Ручные настройки этих VMAT из Material Editor будут заменены текущими шаблонами Deadlimit Aggregator и текстурами проекта.\n\nПродолжить?"),
                UiText.T("Clean material preparation", "Чистая подготовка материалов"),
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes)
            {
                return;
            }
        }
'@ @'
        if (regenerateCustomMaterials)
        {
            var choice = MessageBox.ShowCustom(
                form,
                UiText.T(
                    "SHIFT+PREPARE will regenerate every custom VMAT currently referenced by this project. Manual Material Editor tuning in those VMAT files will be replaced by the current Deadlimit Manager templates and project textures.\n\nYES creates a backup first. YES, NO BACKUP regenerates immediately without creating a backup.\n\nContinue?",
                    "SHIFT+ПОДГОТОВИТЬ пересоздаст все custom-VMAT, на которые сейчас ссылается проект. Ручные настройки этих VMAT из Material Editor будут заменены текущими шаблонами Deadlimit Manager и текстурами проекта.\n\nДА сначала создаст резервную копию. ДА, БЕЗ БЭКАПА пересоздаст материалы сразу, без резервной копии.\n\nПродолжить?"),
                UiText.T("Clean material preparation", "Чистая подготовка материалов"),
                new DeadlimitDialogButton(
                    UiText.T("YES", "ДА"),
                    DeadlimitDialogChoice.Yes,
                    IsDefault: true),
                new DeadlimitDialogButton(
                    UiText.T("YES, NO BACKUP", "ДА, БЕЗ БЭКАПА"),
                    DeadlimitDialogChoice.YesWithoutBackup),
                new DeadlimitDialogButton(
                    UiText.T("NO", "НЕТ"),
                    DeadlimitDialogChoice.No,
                    IsCancel: true));
            if (choice is not DeadlimitDialogChoice.Yes and not DeadlimitDialogChoice.YesWithoutBackup)
            {
                return;
            }

            backupCustomMaterials = choice != DeadlimitDialogChoice.YesWithoutBackup;
        }
'@ 'BuildFeature clean-prepare dialog'

$build = Replace-Exact $build @'
            var result = await service.PrepareAsync(
                manifest,
                progress,
                regenerateCustomMaterials: regenerateCustomMaterials);
'@ @'
            var result = await service.PrepareAsync(
                manifest,
                progress,
                regenerateCustomMaterials: regenerateCustomMaterials,
                backupCustomMaterials: backupCustomMaterials);
'@ 'BuildFeature PrepareAsync call'
Write-Normalized $buildPath $build

# Keep the header tooltip aligned with the actual SHIFT behavior.
$headerPath = 'internal/src/Deadlimit/App/ProjectHeaderFeature.cs'
$header = Read-Normalized $headerPath
$header = Replace-Exact $header @'
                "Prepare the selected project's working files for Reduced CSDK12.\n\nA normal click preserves manual VMAT tuning and synchronizes project textures. Hold SHIFT to back up and regenerate the project's Deadlimit Aggregator custom materials from current templates.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует текстуры проекта. Удерживайте SHIFT, чтобы создать резервную копию и пересоздать custom-материалы Deadlimit Aggregator из текущих шаблонов."));
'@ @'
                "Prepare the selected project's working files for Reduced CSDK12.\n\nA normal click preserves manual VMAT tuning and synchronizes matching project textures. Hold SHIFT to regenerate custom materials; the confirmation dialog lets you choose whether to create a backup first.",
                "Подготовить рабочие файлы выбранного проекта для Reduced CSDK12.\n\nОбычный клик сохраняет ручную настройку VMAT и синхронизирует совпавшие текстуры проекта. Удерживайте SHIFT, чтобы пересоздать custom-материалы; в окне подтверждения можно выбрать, создавать ли резервную копию."));
'@ 'ProjectHeaderFeature prepare tooltip'
Write-Normalized $headerPath $header

# 2) Core clean-prepare backup choice.
$preparePath = 'internal/src/Deadlimit/Core/PrepareAuthoringService.cs'
$prepare = Read-Normalized $preparePath
$prepare = Replace-Exact $prepare @'
    public Task<PrepareAuthoringResult> PrepareAsync(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool regenerateCustomMaterials = false) =>
        Task.Run(
            () => Prepare(manifest, progress, cancellationToken, regenerateCustomMaterials),
            cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken,
        bool regenerateCustomMaterials)
'@ @'
    public Task<PrepareAuthoringResult> PrepareAsync(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress = null,
        CancellationToken cancellationToken = default,
        bool regenerateCustomMaterials = false,
        bool backupCustomMaterials = true) =>
        Task.Run(
            () => Prepare(manifest, progress, cancellationToken, regenerateCustomMaterials, backupCustomMaterials),
            cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken,
        bool regenerateCustomMaterials,
        bool backupCustomMaterials)
'@ 'PrepareAuthoringService signature'

$prepare = Replace-Exact $prepare @'
        log.AppendLine($"Custom material mode: {(regenerateCustomMaterials ? "clean regeneration" : "preserve artist edits and synchronize project textures")}");
        log.AppendLine();
'@ @'
        log.AppendLine($"Custom material mode: {(regenerateCustomMaterials ? "clean regeneration" : "preserve artist edits and synchronize project textures")}");
        if (regenerateCustomMaterials)
        {
            log.AppendLine($"Clean material backup: {(backupCustomMaterials ? "enabled" : "skipped by explicit user choice")}");
        }
        log.AppendLine();
'@ 'PrepareAuthoringService backup log'

$prepare = Replace-Exact $prepare @'
            if (regenerateCustomMaterials)
            {
                BackupCustomMaterialsForCleanPrepare(
                    manifest,
                    addonContentRoot,
                    addonName,
                    log,
                    cancellationToken);
            }
'@ @'
            if (regenerateCustomMaterials && backupCustomMaterials)
            {
                BackupCustomMaterialsForCleanPrepare(
                    manifest,
                    addonContentRoot,
                    addonName,
                    log,
                    cancellationToken);
            }
            else if (regenerateCustomMaterials)
            {
                log.AppendLine("Clean material backup skipped by explicit user choice.");
            }
'@ 'PrepareAuthoringService backup guard'
Write-Normalized $preparePath $prepare

# 3) Normal PREPARE must bind matching project textures even when the VMAT slot is absent.
$bindingPath = 'internal/src/Deadlimit/Core/ProjectTextureBindingService.cs'
$binding = Read-Normalized $bindingPath
$binding = Replace-Exact $binding @'
    private static readonly TextureSemanticDefinition[] KnownSemantics =
    [
        new("color", ["base_color", "basecolor", "diffuse", "albedo", "color"]),
        new("normal", ["normal", "norm"]),
        new("roughness", ["roughness", "rough"]),
        new("ao", ["ambient_occlusion", "ambientocclusion", "occlusion", "ao"]),
        new("metalness", ["metalness", "metallic", "metal"]),
    ];
'@ @'
    private static readonly TextureSemanticDefinition[] KnownSemantics =
    [
        new("color",
        [
            "basecolor", "base_color", "basecolour", "base_colour", "basecol", "base_col",
            "diffuse", "diffusemap", "diffuse_map", "diffusemask", "diffuse_mask", "diff",
            "albedo", "albedomap", "albedo_map", "albedomask", "albedo_mask",
            "color", "colormap", "color_map", "colormask", "color_mask", "colour",
            "colourmap", "colour_map", "colourmask", "colour_mask", "col"
        ]),
        new("normal",
        [
            "normal", "normalmap", "normal_map", "normalmask", "normal_mask", "normals", "norm", "nrm"
        ]),
        new("roughness",
        [
            "roughness", "roughnessmap", "roughness_map", "roughnessmask", "roughness_mask",
            "rough", "roughmap", "rough_map", "roughmask", "rough_mask", "rgh"
        ]),
        new("ao",
        [
            "ambientocclusion", "ambient_occlusion", "ambientocclusionmap", "ambient_occlusion_map",
            "ambientocclusionmask", "ambient_occlusion_mask", "occlusion", "occlusionmap",
            "occlusion_map", "occlusionmask", "occlusion_mask", "ao", "aomap", "ao_map",
            "aomask", "ao_mask"
        ]),
        new("metalness",
        [
            "metalness", "metalnessmap", "metalness_map", "metalnessmask", "metalness_mask",
            "metallic", "metallicmap", "metallic_map", "metallicmask", "metallic_mask",
            "metal", "metalmap", "metal_map", "metalmask", "metal_mask", "mtl"
        ]),
    ];
'@ 'ProjectTextureBindingService aliases'

$binding = Replace-Exact $binding @'
            var assignments = ReadAssignments(text);
            var replacements = new Dictionary<int, string>();
            var boundSemantics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
'@ @'
            var assignments = ReadAssignments(text);
            var replacements = new Dictionary<int, string>();
            var insertions = new List<(string Key, string Value, string Semantic)>();
            var boundSemantics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
'@ 'ProjectTextureBindingService insertion list'

$binding = Replace-Exact $binding @'
                if (compatible.Length == 0)
                {
                    unresolvedTextures++;
                    log.AppendLine($"Project texture has no matching Texture* slot in {targetResource}: {binding.Key} -> {binding.Value}");
                    continue;
                }

                var selected = compatible[0];
'@ @'
                if (compatible.Length == 0)
                {
                    var preferredKey = GetPreferredStandardTextureKey(binding.Key, vertexColorMode);
                    if (preferredKey is null)
                    {
                        unresolvedTextures++;
                        log.AppendLine($"Project texture has no safe standard Texture* slot in {targetResource}: {binding.Key} -> {binding.Value}");
                        continue;
                    }

                    insertions.Add((preferredKey, binding.Value, binding.Key));
                    boundSemantics.Add(binding.Key);
                    boundTextures++;
                    log.AppendLine($"Project texture auto-bind inserted missing slot {targetResource}: {Path.GetFileName(binding.Value)} -> {preferredKey}");
                    continue;
                }

                var selected = compatible[0];
'@ 'ProjectTextureBindingService missing-slot behavior'

$binding = Replace-Exact $binding @'
            text = ReplaceAssignments(text, replacements);

            text = ReconcileUnboundStandardTextureValues(
'@ @'
            text = ReplaceAssignments(text, replacements);
            foreach (var insertion in insertions)
            {
                text = UpsertTextureAssignment(text, insertion.Key, insertion.Value);
            }

            text = ReconcileUnboundStandardTextureValues(
'@ 'ProjectTextureBindingService apply insertions'

$binding = Replace-Exact $binding @'
                foreach (var separator in new[] { "_", "-", " " })
'@ @'
                foreach (var separator in new[] { "_", "-", " ", "." })
'@ 'ProjectTextureBindingService separators'

$binding = Replace-Exact $binding @'
        var separatorIndex = Math.Max(
            stem.LastIndexOf('_'),
            Math.Max(stem.LastIndexOf('-'), stem.LastIndexOf(' ')));
'@ @'
        var separatorIndex = Math.Max(
            Math.Max(stem.LastIndexOf('_'), stem.LastIndexOf('-')),
            Math.Max(stem.LastIndexOf(' '), stem.LastIndexOf('.')));
'@ 'ProjectTextureBindingService generic separator'

$anchor = @'
    private static int SlotRank(string key)
'@
$insert = @'
    private static string? GetPreferredStandardTextureKey(string semantic, bool vertexColorMode)
    {
        return semantic.ToLowerInvariant() switch
        {
            "color" => vertexColorMode ? null : "TextureColor",
            "normal" => vertexColorMode ? "TextureNormal1" : "TextureNormal",
            "roughness" => vertexColorMode ? "TextureRoughness1" : "TextureRoughness",
            "ao" => vertexColorMode ? "TextureAmbientOcclusion1" : "TextureAmbientOcclusion",
            "metalness" => vertexColorMode ? "TextureMetalness1" : "TextureMetalness",
            _ => null,
        };
    }

    private static string UpsertTextureAssignment(string text, string key, string value)
    {
        var found = false;
        var patched = TextureAssignmentRegex.Replace(text, match =>
        {
            if (!string.Equals(GetTextureKey(match), key, StringComparison.OrdinalIgnoreCase))
            {
                return match.Value;
            }

            if (found)
            {
                return string.Empty;
            }

            found = true;
            return match.Groups["prefix"].Value + value + match.Groups["suffix"].Value;
        });

        if (found)
        {
            return patched;
        }

        var closingBrace = patched.LastIndexOf('}');
        if (closingBrace < 0)
        {
            return patched;
        }

        var newline = patched.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        return patched.Insert(closingBrace, $"    \"{key}\"\t\"{value}\"{newline}");
    }

'@
$binding = Replace-Exact $binding $anchor ($insert + $anchor) 'ProjectTextureBindingService helpers'

$binding = Replace-Exact $binding @'
        log.AppendLine("Custom texture naming policy: project textures bind only when the filename material prefix exactly matches the custom material name; Deadlimit does not guess based on there being only one material or one texture.");
'@ @'
        log.AppendLine("Custom texture naming policy: project textures bind only when the filename material prefix exactly matches the custom material name; a matching standard PBR texture replaces the existing compatible Texture* assignment or inserts the canonical slot when that assignment is absent. Deadlimit does not guess based on there being only one material or one texture.");
'@ 'ProjectTextureBindingService policy log'
Write-Normalized $bindingPath $binding

# 4) Restore the red pulsing online indicator for Russian UI as well as English.
$pulsePath = 'internal/src/Deadlimit/App/OnlineCsdkPulseFeature.cs'
$pulse = Read-Normalized $pulsePath
$pulse = Replace-Exact $pulse @'
            var normalized = UiText.T(
                IndicatorReserve + "ONLINE CSDK",
                IndicatorReserve + "CSDK ONLINE");
'@ @'
            var normalized = UiText.T(
                IndicatorReserve + "ONLINE CSDK",
                IndicatorReserve + "CSDK ОНЛАЙН");
'@ 'OnlineCsdkPulseFeature normalized text'

$pulse = Replace-Exact $pulse @'
    private static bool IsOnlineText(string text) =>
        text.Contains("ONLINE CSDK", StringComparison.OrdinalIgnoreCase)
        || text.Contains("CSDK ONLINE", StringComparison.OrdinalIgnoreCase);
'@ @'
    private static bool IsOnlineText(string text) =>
        text.Contains("CSDK", StringComparison.OrdinalIgnoreCase)
        && (text.Contains("ONLINE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ОНЛАЙН", StringComparison.OrdinalIgnoreCase));
'@ 'OnlineCsdkPulseFeature online detector'
Write-Normalized $pulsePath $pulse

# Permanent regression smoke for all three requested behaviors.
$testPath = 'internal/tests/prepare-behavior-smoke.ps1'
$test = @'
$ErrorActionPreference = 'Stop'

$assemblyPath = Resolve-Path 'internal/src/Deadlimit/bin/Release/net10.0-windows/DeadlimitManager.dll'
$assembly = [Reflection.Assembly]::LoadFrom($assemblyPath)
$nonPublicStatic = [Reflection.BindingFlags]::NonPublic -bor [Reflection.BindingFlags]::Static

# Russian and English ONLINE text must both activate the pulse feature.
$pulseType = $assembly.GetType('Deadlimit.App.OnlineCsdkPulseFeature', $true)
$isOnline = $pulseType.GetMethod('IsOnlineText', $nonPublicStatic)
if ($null -eq $isOnline) { throw 'OnlineCsdkPulseFeature.IsOnlineText was not found.' }
foreach ($text in @('▶  ONLINE CSDK', '▶  CSDK ONLINE', '▶  CSDK ОНЛАЙН', 'CSDK ОНЛАЙН')) {
    if (-not [bool]$isOnline.Invoke($null, @($text))) {
        throw "ONLINE pulse detector rejected '$text'."
    }
}
if ([bool]$isOnline.Invoke($null, @('▶  ЗАПУСК CSDK'))) {
    throw 'Normal CSDK launch text must not activate the ONLINE pulse.'
}

# Normal PREPARE parser must recognize the same practical texture naming used by exports.
$bindingType = $assembly.GetType('Deadlimit.Core.ProjectTextureBindingService', $true)
$parse = $bindingType.GetMethod('ParseTextureCandidate', $nonPublicStatic)
if ($null -eq $parse) { throw 'ProjectTextureBindingService.ParseTextureCandidate was not found.' }
$cases = [ordered]@{
    'ivy_builder_body_color.png' = 'color'
    'ivy_builder_body_normal.png' = 'normal'
    'ivy_builder_body_roughness.png' = 'roughness'
    'ivy_builder_body_metalnessmask.png' = 'metalness'
    'ivy_builder_body.MetallicMap.png' = 'metalness'
    'ivy_builder_body-NRM.png' = 'normal'
}
foreach ($entry in $cases.GetEnumerator()) {
    $candidate = $parse.Invoke($null, @("C:\temp\$($entry.Key)", 'materials/ivybuilder'))
    if ($null -eq $candidate) { throw "Normal PREPARE parser rejected $($entry.Key)." }
    if ($candidate.Semantic -ne $entry.Value) {
        throw "$($entry.Key) resolved to semantic '$($candidate.Semantic)', expected '$($entry.Value)'."
    }
    if ($candidate.BaseToken -ne 'ivybuilderbody') {
        throw "$($entry.Key) resolved base token '$($candidate.BaseToken)', expected 'ivybuilderbody'."
    }
}

# Matching maps must be insertable even when a Material Editor VMAT omitted the slot.
$preferred = $bindingType.GetMethod('GetPreferredStandardTextureKey', $nonPublicStatic)
$upsert = $bindingType.GetMethod('UpsertTextureAssignment', $nonPublicStatic)
if ($null -eq $preferred -or $null -eq $upsert) {
    throw 'Project texture replace-or-insert helpers were not found.'
}
$key = [string]$preferred.Invoke($null, @('roughness', $false))
if ($key -ne 'TextureRoughness') { throw "Unexpected standard roughness slot '$key'." }
$source = "Layer0`n{`n    \"TextureColor\"`t\"[0.5 0.5 0.5 0]\"`n}`n"
$texture = 'materials/ivybuilder/textures/ivy_builder_body_roughness.png'
$patched = [string]$upsert.Invoke($null, @($source, $key, $texture))
if (-not $patched.Contains('"TextureRoughness"')) { throw 'Missing roughness slot was not inserted.' }
if (-not $patched.Contains($texture)) { throw 'Inserted roughness slot did not receive the matching project texture.' }
$patchedAgain = [string]$upsert.Invoke($null, @($patched, $key, 'materials/ivybuilder/textures/new_roughness.png'))
if (([regex]::Matches($patchedAgain, '"TextureRoughness"')).Count -ne 1) {
    throw 'Texture upsert created a duplicate standard slot.'
}

# Clean PREPARE keeps backup enabled by default but exposes an explicit no-backup choice.
$prepareType = $assembly.GetType('Deadlimit.Core.PrepareAuthoringService', $true)
$prepareMethod = $prepareType.GetMethods() | Where-Object { $_.Name -eq 'PrepareAsync' } | Select-Object -First 1
$backupParameter = $prepareMethod.GetParameters() | Where-Object { $_.Name -eq 'backupCustomMaterials' }
if ($null -eq $backupParameter) { throw 'PrepareAsync backupCustomMaterials parameter is missing.' }
if (-not $backupParameter.HasDefaultValue -or $backupParameter.DefaultValue -ne $true) {
    throw 'Clean PREPARE backup must remain enabled by default.'
}
$buildSource = Get-Content -LiteralPath 'internal/src/Deadlimit/App/BuildFeature.cs' -Raw
foreach ($required in @('YES, NO BACKUP', 'ДА, БЕЗ БЭКАПА', 'DeadlimitDialogChoice.YesWithoutBackup', 'backupCustomMaterials: backupCustomMaterials')) {
    if (-not $buildSource.Contains($required)) { throw "Clean PREPARE UI contract missing: $required" }
}

Write-Host 'Prepare behavior smoke passed.'
'@
Write-Normalized $testPath $test

# Wire the regression smoke into normal CI.
$workflowPath = '.github/workflows/build.yml'
$workflow = Read-Normalized $workflowPath
$workflow = Replace-Exact $workflow @'
      - name: Texture naming alias smoke
        shell: pwsh
        run: internal/tests/texture-naming-alias-smoke.ps1
      - name: UI localization smoke
'@ @'
      - name: Texture naming alias smoke
        shell: pwsh
        run: internal/tests/texture-naming-alias-smoke.ps1
      - name: Prepare behavior smoke
        shell: pwsh
        run: internal/tests/prepare-behavior-smoke.ps1
      - name: UI localization smoke
'@ 'build.yml prepare behavior smoke'
Write-Normalized $workflowPath $workflow
