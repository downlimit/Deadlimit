namespace Deadlimit.Core;

internal sealed record ManagedCustomMaterialOwnership(
    string SourceReference,
    string TargetResource,
    bool VertexColor);

internal sealed class ManagedCustomMaterialRegistry
{
    public int Version { get; set; } = 1;
    public List<ManagedCustomMaterialOwnership> Materials { get; set; } = [];
}

internal static class ManagedCustomMaterialRegistryStore
{
    private const string FileName = "managed-custom-materials.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static ManagedCustomMaterialRegistry Load(ProjectManifest manifest)
    {
        var path = GetPath(manifest);
        if (!File.Exists(path))
        {
            return new ManagedCustomMaterialRegistry();
        }

        try
        {
            return JsonSerializer.Deserialize<ManagedCustomMaterialRegistry>(
                       File.ReadAllText(path),
                       JsonOptions)
                   ?? new ManagedCustomMaterialRegistry();
        }
        catch (JsonException)
        {
            return new ManagedCustomMaterialRegistry();
        }
    }

    public static IReadOnlyList<ManagedCustomMaterialOwnership> BuildCurrent(
        IEnumerable<VmdlMaterialRemap> remaps)
    {
        return remaps
            .Select(remap => new ManagedCustomMaterialOwnership(
                NormalizeResourcePath(remap.From),
                NormalizeResourcePath(remap.To),
                IsVertexColorReference(remap.From)))
            .GroupBy(item => item.TargetResource, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.TargetResource, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<ManagedCustomMaterialOwnership> MergeKnownWithCurrent(
        ManagedCustomMaterialRegistry registry,
        IReadOnlyList<ManagedCustomMaterialOwnership> current)
    {
        return registry.Materials
            .Concat(current)
            .GroupBy(item => NormalizeResourcePath(item.TargetResource), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
    }

    public static void SaveCurrent(
        ProjectManifest manifest,
        IReadOnlyList<ManagedCustomMaterialOwnership> current)
    {
        var metadataFolder = ProjectStore.GetMetadataFolder(manifest.ProjectFolder);
        Directory.CreateDirectory(metadataFolder);

        var registry = new ManagedCustomMaterialRegistry
        {
            Materials = current.ToList(),
        };

        var path = GetPath(manifest);
        var temporaryPath = path + $".tmp-{Guid.NewGuid():N}";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(registry, JsonOptions));
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static string GetPath(ProjectManifest manifest) =>
        Path.Combine(ProjectStore.GetMetadataFolder(manifest.ProjectFolder), FileName);

    private static bool IsVertexColorReference(string reference)
    {
        var normalized = NormalizeResourcePath(reference);
        var slash = normalized.LastIndexOf('/');
        var leaf = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var token = new string(leaf
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
        return token.Contains("vertexcolor", StringComparison.Ordinal);
    }

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
