using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static class ManagedVmatTextureSafetyNet
{
    private const string NeutralColor = "[0.500000 0.500000 0.500000 0.000000]";
    private const string NeutralWhite = "[1.000000 1.000000 1.000000 0.000000]";
    private const string NeutralNormal = "[0.501961 0.501961 1.000000 0.000000]";
    private const string NeutralRoughness = "[0.800000 0.800000 0.800000 0.000000]";
    private const string NeutralBlack = "[0.000000 0.000000 0.000000 0.000000]";

    private static readonly Regex TextureSourceReferenceRegex = new(
        "^(?<prefix>[ \\t]*(?:\\\"(?<quotedKey>Texture[A-Za-z0-9_]+)\\\"|(?<bareKey>Texture[A-Za-z0-9_]+))[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\")(?<value>[^\\\"\\r\\n]+)(?<suffix>\\\"[^\\r\\n]*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

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
                || TextureSourceExists(addonContentRoot, value))
            {
                return match.Value;
            }

            var key = GetTextureKey(match);
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
        return TextureSourceReferenceRegex.Matches(text)
            .Cast<Match>()
            .Select(match => match.Groups["value"].Value)
            .Where(LooksLikeTextureSourcePath)
            .Where(value => !TextureSourceExists(addonContentRoot, value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetTextureKey(Match match)
    {
        var quoted = match.Groups["quotedKey"];
        return quoted.Success ? quoted.Value : match.Groups["bareKey"].Value;
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

    private static bool LooksLikeTextureSourcePath(string value)
    {
        var extension = Path.GetExtension(value.Replace('\\', '/'));
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".tga", StringComparison.OrdinalIgnoreCase)
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
            return NeutralNormal;
        }
        if (semantic.Contains("rough", StringComparison.Ordinal))
        {
            return NeutralRoughness;
        }
        if (semantic.Contains("ambientocclusion", StringComparison.Ordinal)
            || semantic.Contains("occlusion", StringComparison.Ordinal)
            || string.Equals(semantic, "ao", StringComparison.Ordinal)
            || semantic.StartsWith("ao", StringComparison.Ordinal))
        {
            return NeutralWhite;
        }
        if (semantic.Contains("color", StringComparison.Ordinal)
            || semantic.Contains("albedo", StringComparison.Ordinal)
            || semantic.Contains("diffuse", StringComparison.Ordinal))
        {
            return NeutralColor;
        }

        return NeutralBlack;
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
