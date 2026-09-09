using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static class RetailTexturePackagingPolicy
{
    private static readonly HashSet<string> AuthoringImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".tga", ".jpg", ".jpeg", ".tif", ".tiff", ".exr",
    };

    private static readonly Regex ActiveTextureEntryRegex = new(
        "^[ \\t]*\\\"?(?:Texture|g_t)[A-Za-z0-9_$]*\\\"?[ \\t]*(?:=[ \\t]*)?(?:resource[ \\t]*:[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTexturesHeaderRegex = new(
        "^[ \\t]*\\\"?Compiled Textures\\\"?[ \\t]*(?:=[ \\t]*)?(?=\\{|$)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex CompiledTextureEntryRegex = new(
        "^[ \\t]*\\\"?[A-Za-z_$][A-Za-z0-9_$]*\\\"?[ \\t]*(?:=[ \\t]*)?\\\"(?<path>[^\\\"\\r\\n]+)\\\"",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Multiline);

    internal static IReadOnlySet<string> ResolveReusableRetailCompiledTextures(
        string extractedSourceRoot,
        string addonContentRoot,
        string addonGameRoot)
    {
        var reusable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(extractedSourceRoot)
            || !Directory.Exists(addonContentRoot)
            || !Directory.Exists(addonGameRoot))
        {
            return reusable;
        }

        foreach (var vmatPath in Directory.EnumerateFiles(extractedSourceRoot, "*.vmat", SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var text = File.ReadAllText(vmatPath);
            var header = CompiledTexturesHeaderRegex.Match(text);
            if (!header.Success)
            {
                continue;
            }

            var openBrace = text.IndexOf('{', header.Index + header.Length);
            if (openBrace < 0)
            {
                continue;
            }

            var closeBrace = FindMatchingBrace(text, openBrace);
            if (closeBrace < 0)
            {
                continue;
            }

            var compiledTexturePaths = CompiledTextureEntryRegex.Matches(text[(openBrace + 1)..closeBrace])
                .Cast<Match>()
                .Select(match => NormalizeResourcePath(match.Groups["path"].Value))
                .Where(path => path.EndsWith(".vtex", StringComparison.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (compiledTexturePaths.Count == 0)
            {
                continue;
            }

            var editableText = text[..header.Index];
            foreach (Match match in ActiveTextureEntryRegex.Matches(editableText))
            {
                var authoringPath = NormalizeResourcePath(match.Groups["path"].Value);
                if (!AuthoringImageExtensions.Contains(Path.GetExtension(authoringPath)))
                {
                    continue;
                }

                // Only omit a stock compiled texture when the decompiled authoring image maps
                // one-to-one back to the exact retail VTEX resource path. Generated/hash/channel
                // remaps are intentionally kept in the addon because their runtime identity is
                // not proven to be the retail identity.
                var retailVtexPath = NormalizeResourcePath(Path.ChangeExtension(authoringPath, ".vtex"));
                if (!compiledTexturePaths.Contains(retailVtexPath))
                {
                    continue;
                }

                var extractedSource = TryResolve(extractedSourceRoot, authoringPath);
                var addonSource = TryResolve(addonContentRoot, authoringPath);
                if (extractedSource is null
                    || addonSource is null
                    || !File.Exists(extractedSource)
                    || !File.Exists(addonSource)
                    || !FilesEqual(extractedSource, addonSource))
                {
                    continue;
                }

                var compiledRelativePath = retailVtexPath + "_c";
                var compiledOutput = TryResolve(addonGameRoot, compiledRelativePath);
                if (compiledOutput is null || !File.Exists(compiledOutput))
                {
                    continue;
                }

                reusable.Add(compiledRelativePath);
            }
        }

        return reusable;
    }

    private static string? TryResolve(string root, string resourcePath)
    {
        try
        {
            return SafePath.ResolveUnderRoot(
                root,
                resourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Reusable retail texture path");
        }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
        {
            return false;
        }

        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }

    private static int FindMatchingBrace(string text, int openBrace)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var index = openBrace; index < text.Length; index++)
        {
            var value = text[index];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (value == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (value == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (value == '"')
            {
                inString = true;
                continue;
            }

            if (value == '{')
            {
                depth++;
            }
            else if (value == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return index;
                }
            }
        }

        return -1;
    }

    private static string NormalizeResourcePath(string value) =>
        SafePath.NormalizeRelative(value.Replace('\\', '/').Trim().Trim('"'), "Retail texture resource path");
}
