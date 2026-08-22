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
        var metadataFolder = Path.Combine(manifest.ProjectFolder, MetadataFolderName);
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
        var settingsPath = GetSettingsPath();
        if (!File.Exists(settingsPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<LocalSettings>(json, JsonOptions);
            if (settings is null || string.IsNullOrWhiteSpace(settings.LastProjectFolder))
            {
                return null;
            }

            return TryLoad(settings.LastProjectFolder);
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

    private static void SaveLastProject(string projectFolder)
    {
        var settingsPath = GetSettingsPath();
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        var json = JsonSerializer.Serialize(new LocalSettings { LastProjectFolder = projectFolder }, JsonOptions);
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
    }
}
