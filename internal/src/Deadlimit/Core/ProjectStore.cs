namespace Deadlimit.Core;

public sealed class ToolPathSettings
{
    public string ProjectsRoot { get; set; } = DeadlimitPaths.DefaultWorkspaceRoot;
    public string CsdkRoot { get; set; } = string.Empty;
    public string DeadlockToolsRoot { get; set; } = string.Empty;
    public string RetailDeadlockRoot { get; set; } = string.Empty;
    public string UiLanguage { get; set; } = "en";
    public string UiTheme { get; set; } = "system";
}

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
            var manifest = JsonSerializer.Deserialize<ProjectManifest>(json, JsonOptions);
            if (manifest is null)
            {
                return null;
            }

            CanonicalizeProjectIdentity(manifest, projectFolder);
            return manifest;
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
        catch (Exception exception) when (exception is InvalidDataException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException)
        {
            return null;
        }
    }

    public static void Save(ProjectManifest manifest)
    {
        CanonicalizeProjectIdentity(manifest, manifest.ProjectFolder);

        var metadataFolder = GetMetadataFolder(manifest.ProjectFolder);
        Directory.CreateDirectory(metadataFolder);

        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(metadataFolder);
            File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
        }

        manifest.UpdatedUtc = DateTimeOffset.UtcNow;
        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        AtomicFile.WriteAllText(Path.Combine(metadataFolder, ManifestFileName), json);
        RememberLastProject(manifest.ProjectFolder);
    }

    public static ProjectManifest? TryLoadLastProject()
    {
        var folder = GetLastProjectFolder();
        return string.IsNullOrWhiteSpace(folder) ? null : TryLoad(folder);
    }

    public static string? GetLastProjectFolder()
    {
        var settings = LoadSettings();
        return string.IsNullOrWhiteSpace(settings.LastProjectFolder)
            ? null
            : settings.LastProjectFolder;
    }

    public static void RememberLastProject(string projectFolder)
    {
        if (string.IsNullOrWhiteSpace(projectFolder))
        {
            return;
        }

        var settings = LoadSettings();
        settings.LastProjectFolder = Path.GetFullPath(projectFolder.Trim());
        SaveSettings(settings);
    }

    public static ToolPathSettings GetToolPathSettings()
    {
        var settings = LoadSettings();
        return new ToolPathSettings
        {
            ProjectsRoot = NormalizeOptionalPath(settings.ProjectsRoot),
            CsdkRoot = settings.CsdkRoot,
            DeadlockToolsRoot = settings.DeadlockToolsRoot,
            RetailDeadlockRoot = settings.RetailDeadlockRoot,
            UiLanguage = NormalizeUiLanguage(settings.UiLanguage),
            UiTheme = NormalizeUiTheme(settings.UiTheme),
        };
    }

    public static void SaveToolPathSettings(ToolPathSettings toolPaths)
    {
        var settings = LoadSettings();
        settings.ProjectsRoot = NormalizeOptionalPath(toolPaths.ProjectsRoot);
        settings.CsdkRoot = NormalizeOptionalPath(toolPaths.CsdkRoot);
        settings.DeadlockToolsRoot = NormalizeOptionalPath(toolPaths.DeadlockToolsRoot);
        settings.RetailDeadlockRoot = NormalizeOptionalPath(toolPaths.RetailDeadlockRoot);
        settings.UiLanguage = NormalizeUiLanguage(toolPaths.UiLanguage);
        settings.UiTheme = NormalizeUiTheme(toolPaths.UiTheme);
        SaveSettings(settings);
    }

    private static void CanonicalizeProjectIdentity(ProjectManifest manifest, string projectFolder)
    {
        var fullFolder = Path.GetFullPath(projectFolder.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        manifest.ProjectFolder = fullFolder;
        manifest.ProjectName = Path.GetFileName(fullFolder);
        manifest.SourceDumpFolderName = SafePath.NormalizeRelative(
            string.IsNullOrWhiteSpace(manifest.SourceDumpFolderName) ? "0source" : manifest.SourceDumpFolderName,
            "Project source-dump folder");
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
        AtomicFile.WriteAllText(settingsPath, json);
    }

    private static string NormalizeOptionalPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return Path.GetFullPath(value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string NormalizeUiLanguage(string? value) =>
        string.Equals(value?.Trim(), "ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    private static string NormalizeUiTheme(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "light" or "gray" or "dark" ? normalized : "system";
    }

    private static string GetSettingsPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Deadlimit",
            "settings.json");

    private sealed class LocalSettings
    {
        public string LastProjectFolder { get; set; } = string.Empty;
        public string ProjectsRoot { get; set; } = DeadlimitPaths.DefaultWorkspaceRoot;
        public string CsdkRoot { get; set; } = string.Empty;
        public string DeadlockToolsRoot { get; set; } = string.Empty;
        public string RetailDeadlockRoot { get; set; } = string.Empty;
        public string UiLanguage { get; set; } = "en";
        public string UiTheme { get; set; } = "system";
    }
}
