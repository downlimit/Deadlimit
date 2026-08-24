using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static class ManagedVmatTextureSafetyNet
{
    private const string DefaultColor = "materials/default/default_color.tga";
    private const string DefaultNormal = "materials/default/default_normal.tga";
    private const string DefaultRoughness = "materials/default/default_rough.tga";
    private const string DefaultAo = "materials/default/default_ao.tga";
    private const string DefaultBlackMask = "materials/default/default_black_mask.tga";

    private static readonly Regex TextureSourceReferenceRegex = new(
        @"(?<prefix>\b(?<key>Texture[A-Za-z0-9_]+)\b(?:(?!\bTexture[A-Za-z0-9_]+\b).){0,2048}?"")(?<value>[^""]+)(?<suffix>"")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex QuotedTextureSourceRegex = new(
        "\\\"(?<value>[^\\\"\\r\\n]+\\.(?:png|tga|vtex|jpg|jpeg|tif|tiff))\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string RepairMissingTextureSources(
        string text,
        string addonContentRoot,
        StringBuilder log,
        out int repairedCount)
    {
        var localRepairedCount = 0;
        var repaired = TextureSourceReferenceRegex.Replace(text, match =>
        {
            var value = match.Groups["value"].Value;
            if (!LooksLikeTextureSourcePath(value)
                || IsKnownSafeDefault(value)
                || TextureSourceExists(addonContentRoot, value))
            {
                return match.Value;
            }

            var key = match.Groups["key"].Value;
            var fallback = GetTextureFallback(key);
            localRepairedCount++;
            log.AppendLine($"Managed VMAT final safety-net repair {key}: {value} -> {fallback}");
            return match.Groups["prefix"].Value + fallback + match.Groups["suffix"].Value;
        });

        repairedCount = localRepairedCount;
        return repaired;
    }

    public static IReadOnlyList<string> FindMissingTextureSources(string text, string addonContentRoot)
    {
        return QuotedTextureSourceRegex.Matches(text)
            .Cast<Match>()
            .Select(match => match.Groups["value"].Value)
            .Where(value => !IsKnownSafeDefault(value))
            .Where(value => !TextureSourceExists(addonContentRoot, value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TextureSourceExists(string addonContentRoot, string value)
    {
        if (Path.IsPathRooted(value))
        {
            return File.Exists(value);
        }

        var relative = value
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        return File.Exists(Path.Combine(addonContentRoot, relative));
    }

    private static bool IsKnownSafeDefault(string value)
    {
        var normalized = NormalizeResourcePath(value);
        return normalized.StartsWith("materials/default/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTextureSourcePath(string value)
    {
        var extension = Path.GetExtension(value.Replace('\\', '/'));
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vtex", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTextureFallback(string key)
    {
        var semantic = NormalizeMatchToken(key.StartsWith("Texture", StringComparison.OrdinalIgnoreCase)
            ? key["Texture".Length..]
            : key);

        if (semantic.Contains("normal", StringComparison.Ordinal))
        {
            return DefaultNormal;
        }
        if (semantic.Contains("rough", StringComparison.Ordinal))
        {
            return DefaultRoughness;
        }
        if (semantic.Contains("ambientocclusion", StringComparison.Ordinal)
            || semantic.Contains("occlusion", StringComparison.Ordinal)
            || string.Equals(semantic, "ao", StringComparison.Ordinal)
            || semantic.StartsWith("ao", StringComparison.Ordinal))
        {
            return DefaultAo;
        }
        if (semantic.Contains("color", StringComparison.Ordinal)
            || semantic.Contains("albedo", StringComparison.Ordinal)
            || semantic.Contains("diffuse", StringComparison.Ordinal))
        {
            return DefaultColor;
        }

        return DefaultBlackMask;
    }

    private static string NormalizeMatchToken(string value) =>
        new(value
            .Trim()
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());

    private static string NormalizeResourcePath(string value) =>
        value.Replace('\\', '/').TrimStart('/');
}
