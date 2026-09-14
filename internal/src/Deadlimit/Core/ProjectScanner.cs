namespace Deadlimit.Core;

public sealed record ProjectScanResult(
    IReadOnlyList<string> DmxFiles,
    IReadOnlyList<string> FbxFiles,
    IReadOnlyList<string> GltfFiles,
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
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
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

        var fbx = files
            .Where(path => string.Equals(Path.GetExtension(path), ".fbx", StringComparison.OrdinalIgnoreCase))
            .Where(path => !VertexColorSidecarService.IsSidecarPath(path))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var gltf = files
            .Where(path => Path.GetExtension(path).Equals(".gltf", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".glb", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ProjectScanResult(dmx, fbx, gltf, png);
    }
}
