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

        if (manifest.Mode == ProjectMode.ImportedVpk)
        {
            throw new InvalidOperationException(
                "Imported VPK projects do not use the normal CSDK authoring path. " +
                "Use the imported-project repair/build path instead.");
        }

        var projectFolder = NormalizePath(manifest.ProjectFolder);
        var projectIdWasMissing = string.IsNullOrWhiteSpace(manifest.ProjectId);
        var projectId = projectIdWasMissing
            ? CreateProjectId()
            : manifest.ProjectId.Trim();
        var addonId = string.IsNullOrWhiteSpace(manifest.AddonId)
            ? ResolveDeterministicAddonId(manifest, projectFolder, ref projectId)
            : NormalizeAddonId(manifest.AddonId);

        var identity = CreateIdentity(addonId);
        var ownershipPath = Path.Combine(identity.ContentRoot, OwnershipFileName);
        var ownershipExists = File.Exists(ownershipPath);
        var ownership = TryLoadOwnership(ownershipPath);

        if (ownershipExists && ownership is null)
        {
            throw new InvalidOperationException(
                $"CSDK addon '{addonId}' already exists, but its Deadlimit Manager ownership file cannot be read.\n\n" +
                $"Ownership file: {ownershipPath}\n\n" +
                "Deadlimit Manager will not create a suffixed addon name, adopt this folder, delete it, or overwrite it automatically. " +
                "Repair or remove the conflicting addon intentionally, then run the action again.");
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
                throw new InvalidOperationException(
                    $"CSDK addon name conflict: '{addonId}' is already owned by another Deadlimit Manager project.\n\n" +
                    $"Owner project: {ownership.ProjectFolder}\n" +
                    $"Current project: {projectFolder}\n\n" +
                    "Deadlimit Manager will not add a random suffix or overwrite the existing addon. " +
                    "Rename one of the projects/addons intentionally or remove the conflicting addon, then try again.");
            }

            if (!PathsEqual(ownership.ProjectFolder, projectFolder))
            {
                if (Directory.Exists(ownership.ProjectFolder))
                {
                    throw new InvalidOperationException(
                        $"CSDK addon '{addonId}' is linked to another existing project folder.\n\n" +
                        $"Owner project: {ownership.ProjectFolder}\n" +
                        $"Current project: {projectFolder}\n\n" +
                        "This project appears to be a copied manifest. Deadlimit Manager will not create a suffixed addon or overwrite the existing owner.");
                }

                WriteOwnership(ownershipPath, addonId, projectId, projectFolder);
            }
        }
        else
        {
            var rootsAlreadyExist = Directory.Exists(identity.ContentRoot) || Directory.Exists(identity.GameRoot);
            if (rootsAlreadyExist && !CanAdoptExistingRoots(manifest, identity))
            {
                throw new InvalidOperationException(
                    $"CSDK addon name conflict: '{addonId}' already exists without Deadlimit Manager ownership proof.\n\n" +
                    $"Content: {identity.ContentRoot}\n" +
                    $"Game: {identity.GameRoot}\n\n" +
                    "Deadlimit Manager will not add a random suffix, adopt these folders, delete them, or overwrite them automatically. " +
                    "Remove or repair the conflicting addon intentionally, then try again.");
            }

            Directory.CreateDirectory(identity.ContentRoot);
            WriteOwnership(ownershipPath, addonId, projectId, projectFolder);
        }

        manifest.SchemaVersion = Math.Max(manifest.SchemaVersion, 4);
        manifest.ProjectId = projectId;
        manifest.AddonId = addonId;
        ProjectStore.Save(manifest);
        return identity;
    }

    public static string CreateProjectId() => Guid.NewGuid().ToString("N");

    public static string ResolveInitialAddonId(ProjectManifest? existing, string projectName)
    {
        if (!string.IsNullOrWhiteSpace(existing?.AddonId))
        {
            return NormalizeAddonId(existing.AddonId);
        }

        return MakeLegacyAddonId(projectName);
    }

    // Kept for compatibility with existing callers. Addon IDs are intentionally
    // deterministic now; collisions are reported instead of hidden behind a suffix.
    public static string CreateUniqueAddonId(string projectName) =>
        MakeLegacyAddonId(projectName);

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

    private string ResolveDeterministicAddonId(
        ProjectManifest manifest,
        string projectFolder,
        ref string projectId)
    {
        var addonId = MakeLegacyAddonId(manifest.ProjectName);
        var identity = CreateIdentity(addonId);
        var ownership = TryLoadOwnership(Path.Combine(identity.ContentRoot, OwnershipFileName));

        if (ownership is not null
            && PathsEqual(ownership.ProjectFolder, projectFolder))
        {
            projectId = ownership.ProjectId;
        }

        return addonId;
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
            throw new InvalidDataException($"Invalid Deadlimit Manager addon ID: '{value}'.");
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
