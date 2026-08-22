namespace Deadlimit.Core;

public sealed record ProjectScanResult(
    IReadOnlyList<string> DmxFiles,
    IReadOnlyList<string> PngTextures);

public static class ProjectScanner
{
    public static ProjectScanResult Scan(string projectFolder)
    {
        if (!Directory.Exists(projectFolder))
        {
            throw new DirectoryNotFoundException(projectFolder);
        }

        var files = Directory.EnumerateFiles(projectFolder, "*", SearchOption.TopDirectoryOnly)
            .ToArray();

        var dmx = files
            .Where(path => string.Equals(Path.GetExtension(path), ".dmx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var png = files
            .Where(path => string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectScanResult(dmx, png);
    }
}
