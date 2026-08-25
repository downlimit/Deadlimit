using System.Text;

namespace Deadlimit.Core;

public sealed record AddonIdentity(
    string AddonId,
    string ContentRoot,
    string GameRoot);

public sealed class AddonIdentityService
{
    private const string OwnershipFileName = ".deadlimit-addon-owner.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly DeadlimitPaths _paths;

    public AddonIdentityService(DeadlimitPaths paths)
    {
        _paths = paths;
    }

    public AddonIdentity ResolveAndClaim(ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        var projectFolder = NormalizePath(manifest.ProjectFolder);
        var projectIdWasMissing = string.IsNullOrWhiteSpace(manifest.ProjectId);
        var projectId = projectIdWasMissing
            ? CreateProjectId()
            : manifest.ProjectId.Trim();
        var addonIdWasMissing = string.IsNullOrWhiteSpace(manifest.AddonId);
        var addonId = addonIdWasMissing
            ? ResolveLegacyOrUniqueAddonId(manifest, projectFolder, ref projectId)
            : NormalizeAddonId(manifest.AddonId);

        while (true)
        {
            var identity = CreateIdentity(addonId);
            var ownershipPath = Path.Combine(identity.ContentRoot, OwnershipFileName);
            var ownershipExists = File.Exists(ownershipPath);
            var ownership = TryLoadOwnership(ownershipPath);

            if (ownershipExists && ownership is null)
            {
                if (addonIdWasMissing)
                {
                    addonId = FindAvailableUniqueAddonId(manifest.ProjectName);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Deadlimit found an unreadable addon ownership file:\n\n{ownershipPath}\n\n" +
                    "The CSDK addon folder will not be modified until its ownership state is repaired intentionally.");
            }

            if (ownership is not null)
            {
                if (projectIdWasMissing
                    && string.Equals(ownership.AddonId, addonId, StringComparison.OrdinalIgnoreCase)
                    && PathsEqual(ownership.ProjectFolder, projectFolder))
                {
                    projectId = ownership.ProjectId;
                    projectIdWasMissing = false;
                }

                var ownedByProject = string.Equals(ownership.ProjectId, projectId, StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(ownership.AddonId, addonId, StringComparison.OrdinalIgnoreCase);
                if (!ownedByProject)
                {
                    if (addonIdWasMissing)
                    {
                        addonId = FindAvailableUniqueAddonId(manifest.ProjectName);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"CSDK addon '{addonId}' belongs to another Deadlimit project.\n\n" +
                        $"Owner project: {ownership.ProjectFolder}\n" +
                        $"Current project: {projectFolder}\n\n" +
                        "Deadlimit will not delete or overwrite this addon folder.");
                }

                if (!PathsEqual(ownership.ProjectFolder, projectFolder))
                {
                    if (Directory.Exists(ownership.ProjectFolder))
                    {
                        throw new InvalidOperationException(
                            $"CSDK addon '{addonId}' is linked to another existing project folder.\n\n" +
                            $"Owner project: {ownership.ProjectFolder}\n" +
                            $"Current project: {projectFolder}\n\n" +
                            "This project appears to be a copied manifest. Assign it a new addon identity before preparing it.");
                    }

                    WriteOwnership(ownershipPath, addonId, projectId, projectFolder);
                }
            }
            else
            {
                var rootsAlreadyExist = Directory.Exists(identity.ContentRoot) || Directory.Exists(identity.GameRoot);
                if (rootsAlreadyExist && !CanAdoptExistingRoots(manifest, identity))
                {
                    if (addonIdWasMissing)
                    {
                        addonId = FindAvailableUniqueAddonId(manifest.ProjectName);
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"CSDK addon '{addonId}' already exists without Deadlimit ownership proof.\n\n" +
                        $"Content: {identity.ContentRoot}\n" +
                        $"Game: {identity.GameRoot}\n\n" +
                        "Deadlimit will not adopt, delete, or overwrite these folders automatically.");
                }

                Directory.CreateDirectory(identity.ContentRoot);
                WriteOwnership(ownershipPath, addonId, projectId, projectFolder);
            }

            manifest.SchemaVersion = Math.Max(manifest.SchemaVersion, 3);
            manifest.ProjectId = projectId;
            manifest.AddonId = addonId;
            ProjectStore.Save(manifest);
            return identity;
        }
    }

    public static string CreateProjectId() => Guid.NewGuid().ToString("N");

    public static string ResolveInitialAddonId(ProjectManifest? existing, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(existing?.AddonId))
        {
            return NormalizeAddonId(existing.AddonId);
        }

        if (existing is not null
            && (!string.IsNullOrWhiteSpace(existing.SourceVmdl)
                || !string.IsNullOrWhiteSpace(existing.CompiledVmdl)))
        {
            return MakeLegacyAddonId(projectName);
        }

        return CreateUniqueAddonId(projectName);
    }

    public static string CreateUniqueAddonId(string projectName)
    {
        const int maximumBaseLength = 48;
        var baseId = MakeLegacyAddonId(projectName);
        if (baseId.Length > maximumBaseLength)
        {
            baseId = baseId[..maximumBaseLength].TrimEnd('_');
        }

        return $"{baseId}_{Guid.NewGuid():N}"[..(baseId.Length + 9)];
    }

    public static string MakeLegacyAddonId(string projectName)
    {
        var builder = new StringBuilder();
        var previousUnderscore = false;

        foreach (var character in projectName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousUnderscore = false;
            }
            else if (!previousUnderscore && builder.Length > 0)
            {
                builder.Append('_');
                previousUnderscore = true;
            }
        }

        var value = builder.ToString().Trim('_');
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

    private string ResolveLegacyOrUniqueAddonId(
        ProjectManifest manifest,
        string projectFolder,
        ref string projectId)
    {
        var legacyAddonId = MakeLegacyAddonId(manifest.ProjectName);
        var legacyIdentity = CreateIdentity(legacyAddonId);
        var legacyOwnershipPath = Path.Combine(legacyIdentity.ContentRoot, OwnershipFileName);
        var legacyOwnership = TryLoadOwnership(legacyOwnershipPath);

        if (legacyOwnership is not null
            && PathsEqual(legacyOwnership.ProjectFolder, projectFolder))
        {
            projectId = legacyOwnership.ProjectId;
            return legacyAddonId;
        }

        var rootsAlreadyExist = Directory.Exists(legacyIdentity.ContentRoot)
                                || Directory.Exists(legacyIdentity.GameRoot);
        if (!rootsAlreadyExist || CanAdoptExistingRoots(manifest, legacyIdentity))
        {
            return legacyAddonId;
        }

        return FindAvailableUniqueAddonId(manifest.ProjectName);
    }

    private string FindAvailableUniqueAddonId(string projectName)
    {
        while (true)
        {
            var candidate = CreateUniqueAddonId(projectName);
            var identity = CreateIdentity(candidate);
            if (!Directory.Exists(identity.ContentRoot) && !Directory.Exists(identity.GameRoot))
            {
                return candidate;
            }
        }
    }

    private AddonIdentity CreateIdentity(string addonId) =>
        new(
            addonId,
            Path.Combine(_paths.CsdkContentRoot, "citadel_addons", addonId),
            Path.Combine(_paths.CsdkGameRoot, "citadel_addons", addonId));

    private static bool CanAdoptExistingRoots(ProjectManifest manifest, AddonIdentity identity) =>
        IsPathInside(manifest.SourceVmdl, identity.ContentRoot)
        || IsPathInside(manifest.CompiledVmdl, identity.GameRoot);

    private static bool IsPathInside(string? candidate, string root)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var normalizedRoot = NormalizePath(root) + Path.DirectorySeparatorChar;
        var normalizedCandidate = NormalizePath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeAddonId(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length == 0
            || normalized.Any(character => !char.IsLetterOrDigit(character) && character != '_')
            || char.IsDigit(normalized[0]))
        {
            throw new InvalidDataException($"Invalid Deadlimit addon ID: '{value}'.");
        }
        return normalized;
    }

    private static AddonOwnership? TryLoadOwnership(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var ownership = JsonSerializer.Deserialize<AddonOwnership>(File.ReadAllText(path), JsonOptions);
            return ownership is not null
                   && ownership.SchemaVersion == 1
                   && !string.IsNullOrWhiteSpace(ownership.AddonId)
                   && !string.IsNullOrWhiteSpace(ownership.ProjectId)
                   && !string.IsNullOrWhiteSpace(ownership.ProjectFolder)
                ? ownership
                : null;
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void WriteOwnership(
        string path,
        string addonId,
        string projectId,
        string projectFolder)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var ownership = new AddonOwnership
        {
            AddonId = addonId,
            ProjectId = projectId,
            ProjectFolder = projectFolder,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

        AtomicFile.WriteJson(path, ownership, JsonOptions);
    }

    private static string NormalizePath(string path) =>
        Path.GetFullPath(path.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(NormalizePath(left), NormalizePath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private sealed class AddonOwnership
    {
        public int SchemaVersion { get; set; } = 1;
        public string AddonId { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectFolder { get; set; } = string.Empty;
        public DateTimeOffset UpdatedUtc { get; set; }
    }
}
