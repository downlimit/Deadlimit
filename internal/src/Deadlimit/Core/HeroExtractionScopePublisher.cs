using System.Text.Json;

namespace Deadlimit.Core;

internal sealed class SourceExtractionScopeState
{
    public int SchemaVersion { get; set; } = 1;

    public Dictionary<string, List<string>> Scopes { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

internal sealed record ScopedSourcePublication(
    SourceExtractionScopeState State,
    int FreshFileCount,
    int FinalFileCount);

internal static class HeroExtractionScopePublisher
{
    public const string HeroScope = "hero";
    public const string AbilitiesScope = "abilities";
    public const string PortraitsAndUiScope = "portraits-ui";

    private static readonly string[] AllScopes =
    [
        HeroScope,
        AbilitiesScope,
        PortraitsAndUiScope,
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static SourceExtractionScopeState? TryLoadState(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<SourceExtractionScopeState>(
                File.ReadAllText(path),
                JsonOptions);
            if (state is null || state.SchemaVersion != 1)
            {
                return null;
            }

            state.Scopes = NormalizeScopes(state.Scopes);
            return state;
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException)
        {
            // An absent or unreadable legacy state must never make a partial refresh destructive.
            return null;
        }
    }

    public static void SaveState(string path, SourceExtractionScopeState state) =>
        AtomicFile.WriteJson(path, state, JsonOptions);

    public static ScopedSourcePublication Prepare(
        string existingOutputFolder,
        string publishFolder,
        IReadOnlyDictionary<string, string> freshScopeFolders,
        SourceExtractionScopeState? previousState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(existingOutputFolder);
        ArgumentException.ThrowIfNullOrWhiteSpace(publishFolder);
        ArgumentNullException.ThrowIfNull(freshScopeFolders);

        var selectedScopes = freshScopeFolders.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedScopes.Count == 0
            || selectedScopes.Any(scope => !AllScopes.Contains(scope, StringComparer.OrdinalIgnoreCase)))
        {
            throw new ArgumentException("At least one known extraction scope is required.", nameof(freshScopeFolders));
        }

        var allScopesSelected = AllScopes.All(selectedScopes.Contains);
        DeleteDirectoryIfExists(publishFolder);
        Directory.CreateDirectory(publishFolder);

        // A partial refresh starts from the published source so unselected scopes and unknown
        // artist files survive. A full refresh is authoritative and starts from an empty folder.
        if (!allScopesSelected && Directory.Exists(existingOutputFolder))
        {
            CopyDirectory(existingOutputFolder, publishFolder);
        }

        if (!allScopesSelected && previousState is not null)
        {
            RemovePreviouslyOwnedSelectedFiles(
                publishFolder,
                selectedScopes,
                previousState);
        }

        var nextState = allScopesSelected
            ? new SourceExtractionScopeState()
            : CloneState(previousState);
        var freshFileCount = 0;

        foreach (var scope in AllScopes.Where(selectedScopes.Contains))
        {
            var scopeFolder = freshScopeFolders[scope];
            if (!Directory.Exists(scopeFolder))
            {
                throw new DirectoryNotFoundException(
                    $"Fresh extraction scope folder was not found: {scopeFolder}");
            }

            var relativeFiles = EnumerateRelativeFiles(scopeFolder);
            freshFileCount += relativeFiles.Count;
            nextState.Scopes[scope] = relativeFiles;
            CopyDirectory(scopeFolder, publishFolder);
        }

        if (freshFileCount == 0)
        {
            throw new InvalidOperationException(
                "ValveResourceFormat completed without an error, but no files were written for the selected extraction scopes.");
        }

        var finalFileCount = Directory.EnumerateFiles(publishFolder, "*", SearchOption.AllDirectories).Count();
        return new ScopedSourcePublication(nextState, freshFileCount, finalFileCount);
    }

    private static void RemovePreviouslyOwnedSelectedFiles(
        string publishFolder,
        HashSet<string> selectedScopes,
        SourceExtractionScopeState previousState)
    {
        var filesOwnedByUnselectedScopes = previousState.Scopes
            .Where(entry => !selectedScopes.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var staleCandidates = previousState.Scopes
            .Where(entry => selectedScopes.Contains(entry.Key))
            .SelectMany(entry => entry.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var relativePath in staleCandidates)
        {
            if (filesOwnedByUnselectedScopes.Contains(relativePath))
            {
                continue;
            }

            var destination = SafePath.ResolveUnderRoot(
                publishFolder,
                relativePath.Replace('/', Path.DirectorySeparatorChar),
                "Previously extracted source file");
            if (File.Exists(destination))
            {
                File.Delete(destination);
            }
        }

        RemoveEmptyDirectories(publishFolder);
    }

    private static SourceExtractionScopeState CloneState(SourceExtractionScopeState? state) =>
        new()
        {
            Scopes = state is null
                ? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                : NormalizeScopes(state.Scopes),
        };

    private static Dictionary<string, List<string>> NormalizeScopes(
        IReadOnlyDictionary<string, List<string>>? scopes)
    {
        var normalized = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        if (scopes is null)
        {
            return normalized;
        }

        foreach (var (scope, files) in scopes)
        {
            normalized[scope] = files
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => SafePath.NormalizeRelative(path, "Extracted source scope path"))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return normalized;
    }

    private static List<string> EnumerateRelativeFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Select(path => SafePath.NormalizeRelative(path, "Fresh extracted source path"))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void CopyDirectory(string sourceFolder, string destinationFolder)
    {
        foreach (var sourceFile in Directory.EnumerateFiles(sourceFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceFolder, sourceFile);
            var destination = SafePath.ResolveUnderRoot(
                destinationFolder,
                relativePath,
                "Scoped extracted source file");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(sourceFile, destination, overwrite: true);
        }
    }

    private static void RemoveEmptyDirectories(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(path => path.Length))
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
