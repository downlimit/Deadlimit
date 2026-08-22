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
    string LogPath);

public sealed class PrepareAuthoringService
{
    private static readonly Regex InvalidMaterialRegex = new(
        @"materials/models/[A-Za-z0-9_./\\-]+\.vmat",
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
        log.AppendLine("CSDK game output: untouched by Deadlimit during prepare.");
        log.AppendLine();

        try
        {
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

            var generatedRemaps = DiscoverMaterialPathRepairs(rootDmxFiles);

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new PrepareAuthoringProgress("Applying narrow CSDK compatibility patches to retail VMDL..."));

            var patchResult = RetailVmdlInheritance.PatchAuthoringVmdl(
                sourceCopy.DestinationVmdlPath,
                generatedRemaps);

            log.AppendLine($"Retail material remaps preserved: {patchResult.ExistingMaterialRemapCount}");
            log.AppendLine($"Additional material path remaps added: {patchResult.AddedMaterialRemapCount}");
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
            log.AppendLine("Material policy: preserve retail MaterialGroupList remaps; merge Deadlimit path repairs instead of replacing the retail table.");
            log.AppendLine("Render-mesh policy: preserve retail RenderMeshList/bodygroups/LODs; overlay artist DMX at the original render-mesh resource path.");

            manifest.SourceVmdl = sourceCopy.DestinationVmdlPath;
            // Prepare owns only content. Any runtime output may be stale until CSDK compiles it.
            manifest.CompiledVmdl = null;
            ProjectStore.Save(manifest);

            log.AppendLine();
            log.AppendLine("RESULT: AUTHORING CONTENT PREPARED");
            File.WriteAllText(logPath, log.ToString());

            progress?.Report(new PrepareAuthoringProgress("Authoring content prepared. Launch CSDK to compile/preview it."));

            return new PrepareAuthoringResult(
                addonName,
                addonContentRoot,
                sourceCopy.DestinationVmdlPath,
                replacedRenderMeshes.Count,
                patchResult.ExistingMaterialRemapCount + patchResult.AddedMaterialRemapCount,
                sourceCopy.FilesCopied,
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
    }

    private static List<VmdlMaterialRemap> DiscoverMaterialPathRepairs(IEnumerable<string> dmxFiles)
    {
        var remaps = new Dictionary<string, VmdlMaterialRemap>(StringComparer.OrdinalIgnoreCase);

        foreach (var dmxPath in dmxFiles)
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

        return remaps.Values
            .OrderBy(remap => remap.From, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

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
