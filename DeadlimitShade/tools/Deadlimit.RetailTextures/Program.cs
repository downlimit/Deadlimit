using System.Security.Cryptography;
using System.Text.Json;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

var options = ParseArguments(args);
var vpkPath = Required(options, "vpk");
var materialPath = NormalizeResourcePath(Required(options, "material"));
var outputRoot = Path.GetFullPath(Required(options, "output"));
var sourceRoot = options.TryGetValue("source-root", out var configuredSourceRoot)
    && !string.IsNullOrWhiteSpace(configuredSourceRoot)
        ? Path.GetFullPath(configuredSourceRoot)
        : null;

if (!File.Exists(vpkPath))
{
    throw new FileNotFoundException("Retail VPK was not found.", vpkPath);
}

Directory.CreateDirectory(outputRoot);

using var package = new Package();
package.Read(vpkPath);
var entries = (package.Entries
        ?? throw new InvalidDataException($"VPK entry table was not available: {vpkPath}"))
    .SelectMany(group => group.Value)
    .ToDictionary(entry => NormalizeResourcePath(entry.GetFullPath()), StringComparer.OrdinalIgnoreCase);

var materialEntry = FindEntry(entries, materialPath);
var loadedMaterial = ReadResource(package, materialEntry, materialPath);
using var materialResource = loadedMaterial.Resource;
using var materialStream = loadedMaterial.Stream;
if (materialResource.DataBlock is not Material material)
{
    throw new InvalidDataException($"Resource is not a Source 2 material: {materialPath}");
}

var written = new List<object>();
foreach (var parameter in material.TextureParams.OrderBy(item => item.Key, StringComparer.Ordinal))
{
    var texturePath = NormalizeCompiledTexturePath(parameter.Value);
    if (!entries.TryGetValue(texturePath, out var textureEntry))
    {
        continue;
    }

    var loadedTexture = ReadResource(package, textureEntry, texturePath);
    using var textureResource = loadedTexture.Resource;
    using var textureStream = loadedTexture.Stream;
    if (textureResource.DataBlock is not Texture texture)
    {
        continue;
    }

    var fileName = $"{Sanitize(parameter.Key)}.png";
    var outputPath = Path.Combine(outputRoot, fileName);
    var extractedSource = FindExactExtractedSource(sourceRoot, texturePath);
    byte[] png;
    string origin;
    if (extractedSource is not null)
    {
        png = File.ReadAllBytes(extractedSource);
        File.WriteAllBytes(outputPath, png);
        origin = "0source";
    }
    else
    {
        using var bitmap = texture.GenerateBitmap();
        png = TextureExtract.ToPngImage(bitmap);
        File.WriteAllBytes(outputPath, png);
        origin = "retail-vpk";
    }
    written.Add(new
    {
        parameter = parameter.Key,
        source = texturePath,
        origin,
        file = fileName,
        sha256 = Convert.ToHexStringLower(SHA256.HashData(png)),
    });
}

var manifest = new
{
    schemaVersion = 1,
    sourceVpk = Path.GetFullPath(vpkPath),
    material = materialPath,
    shader = material.ShaderName,
    intParams = material.IntParams,
    floatParams = material.FloatParams,
    vectorParams = material.VectorParams,
    textures = written,
};
var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
{
    WriteIndented = true,
    IncludeFields = true,
});
File.WriteAllText(Path.Combine(outputRoot, "manifest.json"), json);
Console.WriteLine(json);

static (Resource Resource, MemoryStream Stream) ReadResource(
    Package package,
    PackageEntry entry,
    string resourcePath)
{
    package.ReadEntry(entry, out byte[] rawData);
    var stream = new MemoryStream(rawData, writable: false);
    var resource = new Resource { FileName = resourcePath };
    resource.Read(stream);
    return (resource, stream);
}

static PackageEntry FindEntry(IReadOnlyDictionary<string, PackageEntry> entries, string path)
{
    if (entries.TryGetValue(path, out var exact))
    {
        return exact;
    }

    throw new FileNotFoundException($"Resource was not found in the retail VPK: {path}");
}

static string NormalizeCompiledTexturePath(string value)
{
    var path = NormalizeResourcePath(value);
    if (path.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase))
    {
        return path;
    }
    if (path.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
    {
        return path + "_c";
    }
    return path + ".vtex_c";
}

static string NormalizeResourcePath(string value) => value.Replace('\\', '/').TrimStart('/');

static string Sanitize(string value) => string.Concat(value.Select(character =>
    char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '_'));

static string? FindExactExtractedSource(string? sourceRoot, string compiledTexturePath)
{
    if (sourceRoot is null || !Directory.Exists(sourceRoot))
    {
        return null;
    }

    var relative = compiledTexturePath.EndsWith(".vtex_c", StringComparison.OrdinalIgnoreCase)
        ? compiledTexturePath[..^7] + ".png"
        : Path.ChangeExtension(compiledTexturePath, ".png");
    var exact = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar));
    return File.Exists(exact) ? exact : null;
}

static Dictionary<string, string> ParseArguments(string[] values)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var index = 0; index < values.Length; index += 2)
    {
        if (index + 1 >= values.Length || !values[index].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("Usage: Deadlimit.RetailTextures --vpk <pak01_dir.vpk> --material <resource.vmat_c> --output <folder>");
        }
        result[values[index][2..]] = values[index + 1];
    }
    return result;
}

static string Required(IReadOnlyDictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"Missing --{name}.");
