using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace Deadlimit.Core;

public enum ImportedVpkRepairTargetStatus
{
    BindingsAlreadyCurrent,
    BindingsMissing,
    BindingsDiffer,
    MissingRetailCounterpart,
    UnsupportedOrUnreadable,
}

public sealed record ModelAnimationBindingSnapshot(
    bool HasAnimGraph2Field,
    IReadOnlyList<string> AnimGraph2Refs,
    bool HasNmSkeletonField,
    IReadOnlyList<string> NmSkeletonRefs);

public sealed record ImportedVpkRepairTarget(
    string ResourcePath,
    bool RetailMatched,
    string? RetailVpkPath,
    ImportedVpkRepairTargetStatus Status,
    ModelAnimationBindingSnapshot? ImportedBindings,
    ModelAnimationBindingSnapshot? RetailBindings,
    string Detail);

public sealed record ImportedVpkRepairInspectionResult(
    DateTimeOffset InspectedUtc,
    string RetailGameRoot,
    IReadOnlyList<ImportedVpkRepairTarget> Targets,
    string ReportPath);

internal sealed class ImportedVpkRepairInspectionSnapshot
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset InspectedUtc { get; set; }
    public string RetailGameRoot { get; set; } = string.Empty;
    public List<ImportedVpkRepairTarget> Targets { get; set; } = [];
}

public sealed class ImportedVpkRepairInspectionService
{
    public const string InspectionFileName = "repair-inspection.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeadlimitPaths _paths;

    public ImportedVpkRepairInspectionService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public ImportedVpkRepairInspectionResult InspectAndSave(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.Mode != ProjectMode.ImportedVpk || manifest.ImportedVpk is null)
        {
            throw new InvalidOperationException(
                "Repair-target inspection requires an ImportedVpk project.");
        }

        var snapshot = ImportedVpkPayloadService.TryLoadSnapshot(manifest.ProjectFolder)
            ?? throw new InvalidOperationException(
                "The imported project's original-vpk.json snapshot is missing or unreadable.");
        var payloadRoot = SafePath.ResolveUnderRoot(
            manifest.ProjectFolder,
            ImportedVpkPayloadService.PayloadFolderName,
            "Imported VPK payload folder");
        if (!Directory.Exists(payloadRoot))
        {
            throw new DirectoryNotFoundException(payloadRoot);
        }

        var retailGameRoot = Path.Combine(_paths.RetailDeadlockRoot, "game");
        if (!Directory.Exists(retailGameRoot))
        {
            throw new DirectoryNotFoundException(
                $"Retail Deadlock game folder was not found: {retailGameRoot}");
        }

        var modelPaths = snapshot.Entries
            .Select(entry => NormalizeResourcePath(entry.InternalPath))
            .Where(IsCharacterModelPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(path => manifest.ImportedVpk.PrimaryModelResources.Contains(
                path,
                StringComparer.OrdinalIgnoreCase))
            .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var retailMatches = FindRetailMatches(retailGameRoot, modelPaths);
        var targets = new List<ImportedVpkRepairTarget>(modelPaths.Length);

        foreach (var resourcePath in modelPaths)
        {
            var importedPath = SafePath.ResolveUnderRoot(
                payloadRoot,
                resourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Imported compiled model");
            if (!File.Exists(importedPath))
            {
                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: false,
                    RetailVpkPath: null,
                    ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable,
                    ImportedBindings: null,
                    RetailBindings: null,
                    "The model is listed in original-vpk.json but is missing from the preserved payload."));
                continue;
            }

            if (!retailMatches.TryGetValue(resourcePath, out var retailMatch))
            {
                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: false,
                    RetailVpkPath: null,
                    ImportedVpkRepairTargetStatus.MissingRetailCounterpart,
                    ImportedBindings: null,
                    RetailBindings: null,
                    "No exact current-retail model resource path was found outside the retail addons folder."));
                continue;
            }

            if (retailMatch.Ambiguous)
            {
                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: false,
                    RetailVpkPath: null,
                    ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable,
                    ImportedBindings: null,
                    RetailBindings: null,
                    "More than one different current-retail resource matched this exact model path."));
                continue;
            }

            if (retailMatch.Bytes is null)
            {
                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: true,
                    retailMatch.VpkPath,
                    ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable,
                    ImportedBindings: null,
                    RetailBindings: null,
                    retailMatch.Error ?? "The current-retail model entry could not be read."));
                continue;
            }

            try
            {
                var importedBindings = ReadBindings(File.ReadAllBytes(importedPath), resourcePath);
                var retailBindings = ReadBindings(retailMatch.Bytes, resourcePath);
                var status = CompareBindings(importedBindings, retailBindings);
                var detail = status switch
                {
                    ImportedVpkRepairTargetStatus.BindingsAlreadyCurrent =>
                        "Exact retail counterpart matched; AG2/NmSkeleton bindings are already current.",
                    ImportedVpkRepairTargetStatus.BindingsMissing =>
                        "Exact retail counterpart matched; one or more current AG2/NmSkeleton binding fields are missing from the imported model.",
                    ImportedVpkRepairTargetStatus.BindingsDiffer =>
                        "Exact retail counterpart matched; imported AG2/NmSkeleton binding values differ from current retail.",
                    _ => throw new InvalidOperationException("Unexpected binding comparison status."),
                };

                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: true,
                    retailMatch.VpkPath,
                    status,
                    importedBindings,
                    retailBindings,
                    detail));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                targets.Add(new ImportedVpkRepairTarget(
                    resourcePath,
                    RetailMatched: true,
                    retailMatch.VpkPath,
                    ImportedVpkRepairTargetStatus.UnsupportedOrUnreadable,
                    ImportedBindings: null,
                    RetailBindings: null,
                    $"Compiled model binding inspection failed: {exception.Message}"));
            }
        }

        var inspectedUtc = DateTimeOffset.UtcNow;
        var reportPath = Path.Combine(
            ProjectStore.GetMetadataFolder(manifest.ProjectFolder),
            InspectionFileName);
        AtomicFile.WriteJson(
            reportPath,
            new ImportedVpkRepairInspectionSnapshot
            {
                InspectedUtc = inspectedUtc,
                RetailGameRoot = Path.GetFullPath(retailGameRoot),
                Targets = targets,
            },
            JsonOptions);

        return new ImportedVpkRepairInspectionResult(
            inspectedUtc,
            Path.GetFullPath(retailGameRoot),
            targets,
            reportPath);
    }

    private static ImportedVpkRepairTargetStatus CompareBindings(
        ModelAnimationBindingSnapshot imported,
        ModelAnimationBindingSnapshot retail)
    {
        if (BindingsEqual(imported, retail))
        {
            return ImportedVpkRepairTargetStatus.BindingsAlreadyCurrent;
        }

        var graphMissing = retail.AnimGraph2Refs.Count > 0
            && (!imported.HasAnimGraph2Field || imported.AnimGraph2Refs.Count == 0);
        var skeletonMissing = retail.NmSkeletonRefs.Count > 0
            && (!imported.HasNmSkeletonField || imported.NmSkeletonRefs.Count == 0);
        return graphMissing || skeletonMissing
            ? ImportedVpkRepairTargetStatus.BindingsMissing
            : ImportedVpkRepairTargetStatus.BindingsDiffer;
    }

    private static bool BindingsEqual(
        ModelAnimationBindingSnapshot left,
        ModelAnimationBindingSnapshot right) =>
        left.HasAnimGraph2Field == right.HasAnimGraph2Field
        && left.HasNmSkeletonField == right.HasNmSkeletonField
        && left.AnimGraph2Refs.SequenceEqual(right.AnimGraph2Refs, StringComparer.OrdinalIgnoreCase)
        && left.NmSkeletonRefs.SequenceEqual(right.NmSkeletonRefs, StringComparer.OrdinalIgnoreCase);

    private static ModelAnimationBindingSnapshot ReadBindings(byte[] bytes, string resourcePath)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var resource = new Resource { FileName = resourcePath };
        resource.Read(stream);
        if (resource.ResourceType != ResourceType.Model || resource.DataBlock is not Model model)
        {
            throw new InvalidDataException("Resource is not a compiled model.");
        }

        var data = model.Data;
        var hasGraphField = data.TryGetValue("m_animGraph2Refs", out var graphArray);
        var graphRefs = new List<string>();
        if (hasGraphField)
        {
            if (graphArray is null || !graphArray.IsArray)
            {
                throw new InvalidDataException("m_animGraph2Refs exists but is not an array.");
            }

            foreach (var graphRef in graphArray.Values)
            {
                if (graphRef.ValueType != KVValueType.Collection)
                {
                    throw new InvalidDataException("m_animGraph2Refs contains a non-object entry.");
                }

                var graphPath = graphRef.GetStringProperty("m_hGraph", null);
                if (string.IsNullOrWhiteSpace(graphPath))
                {
                    throw new InvalidDataException("m_animGraph2Refs contains an entry without m_hGraph.");
                }
                var identifier = graphRef.GetStringProperty("m_sIdentifier", string.Empty) ?? string.Empty;
                graphRefs.Add(identifier + "|" + NormalizeResourcePath(graphPath));
            }
        }

        var hasSkeletonField = data.TryGetValue("m_vecNmSkeletonRefs", out var skeletonArray);
        var skeletonRefs = new List<string>();
        if (hasSkeletonField)
        {
            if (skeletonArray is null || !skeletonArray.IsArray)
            {
                throw new InvalidDataException("m_vecNmSkeletonRefs exists but is not an array.");
            }

            foreach (var skeletonRef in skeletonArray.Values)
            {
                if (skeletonRef.ValueType != KVValueType.String)
                {
                    throw new InvalidDataException("m_vecNmSkeletonRefs contains a non-string entry.");
                }
                skeletonRefs.Add(NormalizeResourcePath((string)skeletonRef));
            }
        }

        return new ModelAnimationBindingSnapshot(
            hasGraphField,
            graphRefs,
            hasSkeletonField,
            skeletonRefs);
    }

    private static Dictionary<string, RetailModelMatch> FindRetailMatches(
        string retailGameRoot,
        IReadOnlyCollection<string> targetPaths)
    {
        var targets = targetPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = new Dictionary<string, RetailModelMatch>(StringComparer.OrdinalIgnoreCase);
        if (targets.Count == 0)
        {
            return matches;
        }

        foreach (var vpkPath in EnumerateRetailVpks(retailGameRoot))
        {
            try
            {
                using var package = new Package();
                package.Read(vpkPath);
                var packageEntries = package.Entries
                    ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}");

                foreach (var entry in packageEntries.SelectMany(group => group.Value))
                {
                    var resourcePath = NormalizeResourcePath(entry.GetFullPath());
                    if (!targets.Contains(resourcePath))
                    {
                        continue;
                    }

                    try
                    {
                        package.ReadEntry(entry, out byte[] rawData);
                        AddRetailMatch(matches, resourcePath, vpkPath, rawData, null);
                    }
                    catch (Exception exception) when (exception is not OutOfMemoryException)
                    {
                        AddRetailMatch(
                            matches,
                            resourcePath,
                            vpkPath,
                            null,
                            $"Could not read current-retail model entry: {exception.Message}");
                    }
                }
            }
            catch (Exception exception) when (exception is IOException
                or UnauthorizedAccessException
                or InvalidDataException
                or NotSupportedException)
            {
                // A VPK that cannot expose its directory cannot prove an exact match.
                // Other current retail VPKs are still scanned.
            }
        }

        return matches;
    }

    private static void AddRetailMatch(
        IDictionary<string, RetailModelMatch> matches,
        string resourcePath,
        string vpkPath,
        byte[]? bytes,
        string? error)
    {
        if (!matches.TryGetValue(resourcePath, out var existing))
        {
            matches[resourcePath] = new RetailModelMatch(vpkPath, bytes, error, Ambiguous: false);
            return;
        }

        if (existing.Ambiguous)
        {
            return;
        }

        if (existing.Bytes is not null && bytes is not null)
        {
            var leftHash = SHA256.HashData(existing.Bytes);
            var rightHash = SHA256.HashData(bytes);
            if (leftHash.AsSpan().SequenceEqual(rightHash))
            {
                return;
            }
        }
        else if (existing.Bytes is null && bytes is null
                 && string.Equals(existing.Error, error, StringComparison.Ordinal))
        {
            return;
        }

        matches[resourcePath] = existing with { Ambiguous = true };
    }

    private static IReadOnlyList<string> EnumerateRetailVpks(string retailGameRoot)
    {
        var addonsRoot = Path.Combine(retailGameRoot, "citadel", "addons");
        var primary = Path.Combine(retailGameRoot, "citadel", "pak01_dir.vpk");
        var result = new List<string>();
        if (File.Exists(primary))
        {
            result.Add(Path.GetFullPath(primary));
        }

        foreach (var vpkPath in Directory.EnumerateFiles(retailGameRoot, "*_dir.vpk", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fullPath = Path.GetFullPath(vpkPath);
            if (IsPathUnderRoot(addonsRoot, fullPath)
                || result.Contains(fullPath, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            result.Add(fullPath);
        }

        return result;
    }

    private static bool IsPathUnderRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCharacterModelPath(string path) =>
        path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)
        && (path.StartsWith("models/heroes/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_wip/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("models/heroes_staging/", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeResourcePath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/'), "Source 2 resource path")
            .TrimStart('/');

    private sealed record RetailModelMatch(
        string VpkPath,
        byte[]? Bytes,
        string? Error,
        bool Ambiguous);
}
