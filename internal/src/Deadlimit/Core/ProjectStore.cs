namespace Deadlimit.Core;

public static class ProjectStore
{
    private const string MetadataFolderName = ".deadlimit";
    private const string ManifestFileName = "project.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static string GetManifestPath(string projectFolder) =>
        Path.Combine(projectFolder, MetadataFolderName, ManifestFileName);

    public static string GetMetadataFolder(string projectFolder) =>
        Path.Combine(projectFolder, MetadataFolderName);

    public static ProjectManifest? TryLoad(string projectFolder)
    {
        var path = GetManifestPath(projectFolder);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(ProjectManifest manifest)
    {
        var metadataFolder = GetMetadataFolder(manifest.ProjectFolder);
        Directory.CreateDirectory(metadataFolder);

        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(metadataFolder);
            File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
        }

        manifest.UpdatedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(Path.Combine(metadataFolder, ManifestFileName), json);
        SaveLastProject(manifest.ProjectFolder);
    }

    public static ProjectManifest? TryLoadLastProject()
    {
        var settings = LoadSettings();
        if (string.IsNullOrWhiteSpace(settings.LastProjectFolder))
        {
            return null;
        }

        return TryLoad(settings.LastProjectFolder);
    }

    public static string? TryGetSource2ViewerCliPath()
    {
        var path = LoadSettings().Source2ViewerCliPath;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    public static void SaveSource2ViewerCliPath(string path)
    {
        var settings = LoadSettings();
        settings.Source2ViewerCliPath = Path.GetFullPath(path);
        SaveSettings(settings);
    }

    private static void SaveLastProject(string projectFolder)
    {
        var settings = LoadSettings();
        settings.LastProjectFolder = projectFolder;
        SaveSettings(settings);
    }

    private static LocalSettings LoadSettings()
    {
        var settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return new LocalSettings();
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            return JsonSerializer.Deserialize<LocalSettings>(json, JsonOptions) ?? new LocalSettings();
        }
        catch (JsonException)
        {
            return new LocalSettings();
        }
        catch (IOException)
        {
            return new LocalSettings();
        }
        catch (UnauthorizedAccessException)
        {
            return new LocalSettings();
        }
    }

    private static void SaveSettings(LocalSettings settings)
    {
        var settingsPath = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }

    private static string GetSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Deadlimit",
            "settings.json");

    private sealed class LocalSettings
    {
        public string LastProjectFolder { get; set; } = string.Empty;
        public string? Source2ViewerCliPath { get; set; }
    }
}
