using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static partial class TextureAuthoringPathPolicy
{
    [GeneratedRegex(
        @"_(?:png|tga|psd|jpg|jpeg)_[0-9a-f]{7,8}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SourceTextureSuffix();

    [GeneratedRegex(
        @"_[0-9a-f]{7,8}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeneratedHashSuffix();

    public static string Normalize(string path)
    {
        var directory = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? string.Empty;
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);
        var sourceMatch = SourceTextureSuffix().Match(stem);
        if (sourceMatch.Success)
        {
            stem = stem[..sourceMatch.Index];
        }
        else
        {
            var generatedMatch = GeneratedHashSuffix().Match(stem);
            if (generatedMatch.Success)
            {
                stem = stem[..generatedMatch.Index];
            }
        }

        var filename = stem + extension;
        return string.IsNullOrEmpty(directory) ? filename : directory + "/" + filename;
    }
}
