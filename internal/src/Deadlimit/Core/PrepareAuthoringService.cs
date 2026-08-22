using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

public sealed record PrepareAuthoringProgress(string Message);

public sealed record PrepareAuthoringResult(
    string AddonName,
    string AddonContentRoot,
    string SourceVmdlPath,
    int DmxCount,
    int MaterialRemapCount,
    int RetailSourceFilesCopied,
    bool GameOutputCleaned,
    string LogPath);

public sealed class PrepareAuthoringService
{
    private const string GenericEyeFallbackMaterial = "materials/dev/vertcolor_pbr_basic.vmat";

    private static readonly Regex InvalidMaterialRegex = new(
        @"materials/models/[A-Za-z0-9_./\\-]+\.vmat",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MaterialRemapRegex = new(
        "\\bfrom\\s*=\\s*\"(?<from>[^\"]+)\"\\s+to\\s*=\\s*\"(?<to>[^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex EyeIdentifierRegex = new(
        @"(^|[^a-z0-9])(eye|eyes|eyeball|pupil|iris)([^a-z0-9]|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly DeadlimitPaths _paths;

    public PrepareAuthoringService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public Task<PrepareAuthoringResult> PrepareAsync(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Prepare(manifest, progress, cancellationToken), cancellationToken);

    private PrepareAuthoringResult Prepare(
        ProjectManifest manifest,
        IProgress<PrepareAuthoringProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateEnvironment(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        var rootDmxFiles = Directory.EnumerateFiles(manifest.ProjectFolder, "*.dmx", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (rootDmxFiles.Length == 0)
        {
            throw new InvalidOperationException(
                "No .dmx files were found in the project root. Export the current artist model to the project root first.");
        }

        if (string.IsNullOrWhiteSpace(manifest.RetailMainModel))
        {
            throw new InvalidOperationException(
                "Retail main model is unknown. Run EXTRACT HERO SOURCE once before PREPARE FOR CSDK.");
        }

        var addonName = MakeAddonName(manifest.ProjectName);
        var addonContentRoot = Path.Combine(_paths.CsdkContentRoot, "citadel_addons", addonName);
        var addonGameRoot = Path.Combine(_paths.CsdkGameRoot, "citadel_addons", addonName);

        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        var logFolder = Path.Combine(metadataFolder, "logs");
        Directory.CreateDirectory(logFolder);
        var logPath = Path.Combine(logFolder, $"prepare-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var log = new StringBuilder();
        log.AppendLine($"Deadlimit authoring prepare — {DateTimeOffset.Now:O}");
        log.AppendLine($"Project: {manifest.ProjectName}");
        log.AppendLine($"Hero: {manifest.Hero}");
        log.AppendLine($"Addon: {addonName}");
        log.AppendLine($"Retail model: {manifest.RetailMainModel}");
        log.AppendLine($"CSDK content root: {addonContentRoot}");
        log.AppendLine($"CSDK game output root: {addonGameRoot}");
        log.AppendLine();

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress("Cleaning stale compiled output for this addon..."));

            var gameOutputCleaned = false;
            if (Directory.Exists(addonGameRoot))
            {
                Directory.Delete(addonGameRoot, recursive: true);
                gameOutputCleaned = true;
            }

            log.AppendLine(gameOutputCleaned
                ? $"Removed stale addon runtime output: {addonGameRoot}"
                : $"No stale addon runtime output existed: {addonGameRoot}");
            log.AppendLine("Deadlimit does not compile content during PREPARE FOR CSDK; CSDK12 rebuilds game output from content when launched/compiled.");

            progress?.Report(new PrepareAuthoringProgress("Refreshing retail authoring template in CSDK content..."));
            Directory.CreateDirectory(addonContentRoot);

            var sourceCopy = RetailVmdlInheritance.CopyRetailModelSourceTree(manifest, addonContentRoot);
            log.AppendLine($"Retail source template: {sourceCopy.SourceVmdlPath}");
            log.AppendLine($"Retail source files copied: {sourceCopy.FilesCopied}");
            log.AppendLine($"Destination VMDL: {sourceCopy.DestinationVmdlPath}");

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress("Overlaying artist DMX on matching retail render meshes..."));

            var replacedRenderMeshes = RetailVmdlInheritance.OverlayArtistDmx(
                sourceCopy,
                addonContentRoot,
                manifest.Hero,
                rootDmxFiles);

            log.AppendLine($"Artist DMX overlays: {replacedRenderMeshes.Count}");
            foreach (var resourcePath in replacedRenderMeshes)
            {
                log.AppendLine($"  replace {resourcePath}");
            }

            var generatedRemaps = DiscoverMaterialRepairs(
                rootDmxFiles,
                sourceCopy.DestinationVmdlPath,
                manifest.Hero,
                log);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress("Applying narrow CSDK compatibility patches to retail VMDL..."));

            var patchResult = RetailVmdlInheritance.PatchAuthoringVmdl(
                sourceCopy.DestinationVmdlPath,
                generatedRemaps);

            log.AppendLine($"Retail material remaps preserved: {patchResult.ExistingMaterialRemapCount}");
            log.AppendLine($"Additional material repairs added: {patchResult.AddedMaterialRemapCount}");
            foreach (var remap in generatedRemaps)
            {
                log.AppendLine($"  candidate {remap.From} -> {remap.To}");
            }

            log.AppendLine($"Retail RenderMeshFile entries preserved: {patchResult.RenderMeshCount}");
            foreach (var removedClass in patchResult.RemovedClasses)
            {
                log.AppendLine($"Removed current-CSDK-incompatible root node: {removedClass}");
            }

            log.AppendLine("VMDL policy: preserve the extracted retail document/header/order and patch only proven incompatible or project-owned data.");
            log.AppendLine("Material policy: preserve retail MaterialGroupList remaps and merge only narrow path/fallback repairs supported by artist-source evidence.");
            log.AppendLine("Render-mesh policy: preserve retail RenderMeshList/bodygroups/LODs; overlay artist DMX at the original render-mesh resource path.");

            manifest.SourceVmdl = sourceCopy.DestinationVmdlPath;
            manifest.CompiledVmdl = null;
            ProjectStore.Save(manifest);

            log.AppendLine();
            log.AppendLine("RESULT: AUTHORING CONTENT PREPARED; ADDON GAME OUTPUT CLEAN");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareAuthoringProgress("Authoring content prepared. Launch CSDK to rebuild clean game output."));

            return new PrepareAuthoringResult(
                addonName,
                addonContentRoot,
                sourceCopy.DestinationVmdlPath,
                replacedRenderMeshes.Count,
                patchResult.ExistingMaterialRemapCount + patchResult.AddedMaterialRemapCount,
                sourceCopy.FilesCopied,
                gameOutputCleaned,
                logPath);
        }
        catch (Exception ex)
        {
            log.AppendLine();
            log.AppendLine($"RESULT: FAILED — {ex}");
            File.WriteAllText(logPath, log.ToString());
            throw;
        }
    }

    private void ValidateEnvironment(ProjectManifest manifest)
    {
        if (!Directory.Exists(manifest.ProjectFolder))
        {
            throw new DirectoryNotFoundException(manifest.ProjectFolder);
        }

        if (!Directory.Exists(_paths.CsdkContentRoot))
        {
            throw new DirectoryNotFoundException($"CSDK content root was not found: {_paths.CsdkContentRoot}");
        }

        if (!Directory.Exists(_paths.CsdkGameRoot))
        {
            throw new DirectoryNotFoundException($"CSDK game root was not found: {_paths.CsdkGameRoot}");
        }
    }

    private static List<VmdlMaterialRemap> DiscoverMaterialRepairs(
        IEnumerable<string> dmxFiles,
        string vmdlPath,
        string hero,
        StringBuilder log)
    {
        var files = dmxFiles.ToArray();
        var remaps = new Dictionary<string, VmdlMaterialRemap>(StringComparer.OrdinalIgnoreCase);

        foreach (var dmxPath in files)
        {
            var raw = File.ReadAllBytes(dmxPath);
            var text = Encoding.Latin1.GetString(raw).Replace('\\', '/');

            foreach (Match match in InvalidMaterialRegex.Matches(text))
            {
                var from = match.Value;
                var to = from["materials/".Length..];
                remaps.TryAdd(from, new VmdlMaterialRemap(from, to));
            }
        }

        var eyeFallback = DiscoverEyeFallbackRepair(files, vmdlPath, hero, log);
        if (eyeFallback is not null)
        {
            remaps.TryAdd(eyeFallback.From, eyeFallback);
        }

        return remaps.Values
            .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static VmdlMaterialRemap? DiscoverEyeFallbackRepair(
        IReadOnlyList<string> dmxFiles,
        string vmdlPath,
        string hero,
        StringBuilder log)
    {
        var hasEyeFallbackSignature = dmxFiles.Any(DmxHasEyeFallbackSignature);
        if (!hasEyeFallbackSignature)
        {
            return null;
        }

        var existingRemaps = ReadMaterialRemaps(vmdlPath);
        if (existingRemaps.Any(remap => string.Equals(
                remap.From,
                GenericEyeFallbackMaterial,
                StringComparison.OrdinalIgnoreCase)))
        {
            log.AppendLine("Eye fallback repair: retail VMDL already contains the generic dev-material remap; no inferred repair needed.");
            return null;
        }

        var target = ChooseLikelyCharacterSurfaceMaterial(existingRemaps, hero);
        if (target is null)
        {
            log.AppendLine(
                "Eye fallback repair: artist DMX contains an eye mesh adjacent to the generic dev material, " +
                "but no unique body/head/face/skin retail material target could be inferred. No automatic remap was added.");
            return null;
        }

        log.AppendLine(
            $"Eye fallback repair inferred from artist DMX signature: {GenericEyeFallbackMaterial} -> {target}");

        return new VmdlMaterialRemap(GenericEyeFallbackMaterial, target);
    }

    private static bool DmxHasEyeFallbackSignature(string dmxPath)
    {
        var raw = File.ReadAllBytes(dmxPath);
        var tokens = Encoding.Latin1.GetString(raw)
            .Replace('\\', '/')
            .Split('\0', StringSplitOptions.None);

        for (var index = 0; index < tokens.Length; index++)
        {
            if (!string.Equals(tokens[index], GenericEyeFallbackMaterial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = Math.Max(0, index - 4);
            for (var cursor = start; cursor < index; cursor++)
            {
                if (EyeIdentifierRegex.IsMatch(tokens[cursor]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IReadOnlyList<VmdlMaterialRemap> ReadMaterialRemaps(string vmdlPath)
    {
        var text = File.ReadAllText(vmdlPath);
        return MaterialRemapRegex.Matches(text)
            .Select(match => new VmdlMaterialRemap(
                match.Groups["from"].Value.Replace('\\', '/'),
                match.Groups["to"].Value.Replace('\\', '/')))
            .ToArray();
    }

    private static string? ChooseLikelyCharacterSurfaceMaterial(
        IReadOnlyList<VmdlMaterialRemap> existingRemaps,
        string hero)
    {
        var heroToken = NormalizeToken(hero);

        var scored = existingRemaps
            .Select(remap => remap.To)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path =>
            {
                var fileToken = NormalizeToken(Path.GetFileNameWithoutExtension(path));
                var score = 0;

                if (fileToken.Contains("body", StringComparison.Ordinal))
                {
                    score += 500;
                }
                if (fileToken.Contains("skin", StringComparison.Ordinal))
                {
                    score += 420;
                }
                if (fileToken.Contains("head", StringComparison.Ordinal))
                {
                    score += 360;
                }
                if (fileToken.Contains("face", StringComparison.Ordinal))
                {
                    score += 320;
                }
                if (heroToken.Length > 0 && fileToken.Contains(heroToken, StringComparison.Ordinal))
                {
                    score += 50;
                }

                if (fileToken.Contains("wing", StringComparison.Ordinal)
                    || fileToken.Contains("gear", StringComparison.Ordinal)
                    || fileToken.Contains("weapon", StringComparison.Ordinal)
                    || fileToken.Contains("gun", StringComparison.Ordinal))
                {
                    score -= 300;
                }

                return (Path: path, Score: score);
            })
            .Where(item => item.Score >= 300)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (scored.Length == 0)
        {
            return null;
        }

        if (scored.Length > 1 && scored[0].Score == scored[1].Score)
        {
            return null;
        }

        return scored[0].Path;
    }

    private static string NormalizeToken(string value) =>
        new(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string MakeAddonName(string projectName)
    {
        var sb = new StringBuilder();
        var previousUnderscore = false;

        foreach (var ch in projectName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                previousUnderscore = true;
            }
        }

        var value = sb.ToString().Trim('_');
        if (value.Length == 0)
        {
            value = "deadlimit_project";
        }
        if (char.IsDigit(value[0]))
        {
            value = $"deadlimit_{value}";
        }
        return value;
    }
}
