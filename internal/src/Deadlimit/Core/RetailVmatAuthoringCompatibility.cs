using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal static partial class RetailVmatAuthoringCompatibility
{
    [GeneratedRegex(
        "(?m)^(?<indent>[ \\t]*)\\\"(?<key>Texture[A-Za-z0-9_]+)\\\"[ \\t]+\\\"(?<value>[^\\\"\\r\\n]+)\\\"[ \\t]*\\r?$",
        RegexOptions.CultureInvariant)]
    private static partial Regex TextureAssignmentRegex();

    public static int RepairTree(string modelSourceFolder, string addonContentRoot)
    {
        var repaired = 0;
        foreach (var path in Directory.EnumerateFiles(modelSourceFolder, "*.vmat", SearchOption.AllDirectories))
        {
            if (Repair(path, addonContentRoot))
            {
                repaired++;
            }
        }

        return repaired;
    }

    internal static bool Repair(string vmatPath, string addonContentRoot)
    {
        var original = File.ReadAllText(vmatPath);
        if (!Regex.IsMatch(
                original,
                "\\\"shader\\\"[ \\t]+\\\"pbr\\.vfx\\\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return false;
        }

        // Keep the retail compiled-resource map. It points at the exact stock VTEX
        // resources already present in Deadlock/CSDK and prevents unchanged default
        // hero materials from being repacked into a very large addon. Explicit artist
        // texture overrides remove this cache later in RetailTextureOverrideService.
        var text = original;
        var assignments = TextureAssignmentRegex().Matches(text)
            .Select(match => (Key: match.Groups["key"].Value, Value: match.Groups["value"].Value))
            .ToArray();

        foreach (var (key, value) in assignments)
        {
            switch (key)
            {
                case "TextureAmbientOcclusion":
                    text = ReplaceLegacyAssignment(text, key, "TextureAmbientOcclusion1", value);
                    break;
                case "TextureColor":
                    text = ReplaceLegacyAssignment(text, key, "TextureColor1", value);
                    if (TryCreateChannelTexture(
                            addonContentRoot,
                            value,
                            "metalness",
                            PackedChannel.Alpha,
                            out var metalnessPath))
                    {
                        text = UpsertAssignment(text, "TextureMetalness1", metalnessPath);
                    }
                    break;
                case "TextureNormalRoughness":
                    text = ReplaceLegacyAssignment(text, key, "TextureNormal1", value);
                    if (TryCreateChannelTexture(
                            addonContentRoot,
                            value,
                            "roughness",
                            PackedChannel.Alpha,
                            out var roughnessPath))
                    {
                        text = UpsertAssignment(text, "TextureRoughness1", roughnessPath);
                    }
                    break;
                case "TextureNprOutlineMask":
                    text = ReplaceLegacyAssignment(text, key, "TextureNprOutlineMask1", value);
                    break;
                case "TextureNprTransmissiveColor":
                    text = ReplaceLegacyAssignment(text, key, "TextureNprTramsissiveColor1", value);
                    break;
                case "TextureSelfIllumMask":
                    text = ReplaceLegacyAssignment(text, key, "TextureSelfIllumMask1", value);
                    break;
                case "TextureTintMask":
                    text = ReplaceLegacyAssignment(text, key, "TextureTintMask1", value);
                    break;
                case "TextureTintMaskRimLightMask":
                    text = RemoveAssignment(text, key);
                    if (TryCreateChannelTexture(
                            addonContentRoot,
                            value,
                            "tint",
                            PackedChannel.Red,
                            out var tintPath))
                    {
                        text = UpsertAssignment(text, "TextureTintMask1", tintPath);
                    }
                    else
                    {
                        text = UpsertAssignment(text, "TextureTintMask1", value);
                    }
                    if (TryCreateChannelTexture(
                            addonContentRoot,
                            value,
                            "rim",
                            PackedChannel.Green,
                            out var rimPath))
                    {
                        text = UpsertAssignment(text, "TextureRimLightMask1", rimPath);
                    }
                    else
                    {
                        text = UpsertAssignment(text, "TextureRimLightMask1", value);
                    }
                    break;
                case "TextureGlass":
                    text = ReplaceLegacyAssignment(text, key, "TextureGlassMask1", value);
                    break;
            }
        }

        if (string.Equals(original, text, StringComparison.Ordinal))
        {
            return false;
        }

        File.WriteAllText(vmatPath, text);
        return true;
    }

    private static string ReplaceLegacyAssignment(
        string text,
        string legacyKey,
        string authoringKey,
        string value) =>
        UpsertAssignment(RemoveAssignment(text, legacyKey), authoringKey, value);

    private static string RemoveAssignment(string text, string key) =>
        Regex.Replace(
            text,
            $"(?m)^[ \\t]*\\\"{Regex.Escape(key)}\\\"[ \\t]+\\\"[^\\\"\\r\\n]+\\\"[ \\t]*\\r?\\n?",
            string.Empty,
            RegexOptions.CultureInvariant);

    private static string UpsertAssignment(string text, string key, string value)
    {
        var pattern = $"(?m)^(?<indent>[ \\t]*)\\\"{Regex.Escape(key)}\\\"[ \\t]+\\\"[^\\\"\\r\\n]+\\\"[ \\t]*\\r?$";
        var replacement = $"${{indent}}\"{key}\"\t\"{value}\"";
        if (Regex.IsMatch(text, pattern, RegexOptions.CultureInvariant))
        {
            return Regex.Replace(text, pattern, replacement, RegexOptions.CultureInvariant);
        }

        var closingBrace = text.LastIndexOf('}');
        if (closingBrace < 0)
        {
            throw new InvalidDataException("Retail VMAT did not contain a closing Layer0 brace.");
        }

        return text.Insert(closingBrace, $"\t\"{key}\"\t\"{value}\"{Environment.NewLine}");
    }

    private static bool TryCreateChannelTexture(
        string addonContentRoot,
        string sourceResourcePath,
        string suffix,
        PackedChannel channel,
        out string outputResourcePath)
    {
        outputResourcePath = string.Empty;
        if (!IsImageResource(sourceResourcePath))
        {
            return false;
        }

        string sourcePath;
        try
        {
            sourcePath = SafePath.ResolveUnderRoot(
                addonContentRoot,
                sourceResourcePath.Replace('/', Path.DirectorySeparatorChar),
                "Packed retail material texture");
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        if (!File.Exists(sourcePath))
        {
            return false;
        }

        var directory = Path.GetDirectoryName(sourceResourcePath)?.Replace('\\', '/') ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(sourceResourcePath);
        var filename = $"{stem}_{suffix}.png";
        outputResourcePath = directory.Length == 0 ? filename : directory + "/" + filename;
        var outputPath = SafePath.ResolveUnderRoot(
            addonContentRoot,
            outputResourcePath.Replace('/', Path.DirectorySeparatorChar),
            "Derived retail material texture");

        WriteChannelPng(sourcePath, outputPath, channel);
        return true;
    }

    private static bool IsImageResource(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".png" or ".tga" or ".jpg" or ".jpeg" or ".tif" or ".tiff";

    private static void WriteChannelPng(string sourcePath, string outputPath, PackedChannel channel)
    {
        using var sourceOriginal = new Bitmap(sourcePath);
        using var source = new Bitmap(sourceOriginal.Width, sourceOriginal.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(source))
        {
            graphics.DrawImageUnscaled(sourceOriginal, 0, 0);
        }

        using var output = new Bitmap(source.Width, source.Height, PixelFormat.Format24bppRgb);
        var rectangle = new Rectangle(0, 0, source.Width, source.Height);
        var sourceData = source.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var outputData = output.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        try
        {
            var sourceBytes = new byte[sourceData.Stride * source.Height];
            var outputBytes = new byte[outputData.Stride * output.Height];
            Marshal.Copy(sourceData.Scan0, sourceBytes, 0, sourceBytes.Length);
            for (var y = 0; y < source.Height; y++)
            {
                for (var x = 0; x < source.Width; x++)
                {
                    var sourceOffset = y * sourceData.Stride + x * 4;
                    var value = channel switch
                    {
                        PackedChannel.Red => sourceBytes[sourceOffset + 2],
                        PackedChannel.Green => sourceBytes[sourceOffset + 1],
                        PackedChannel.Alpha => sourceBytes[sourceOffset + 3],
                        _ => throw new ArgumentOutOfRangeException(nameof(channel)),
                    };
                    var outputOffset = y * outputData.Stride + x * 3;
                    outputBytes[outputOffset] = value;
                    outputBytes[outputOffset + 1] = value;
                    outputBytes[outputOffset + 2] = value;
                }
            }
            Marshal.Copy(outputBytes, 0, outputData.Scan0, outputBytes.Length);
        }
        finally
        {
            source.UnlockBits(sourceData);
            output.UnlockBits(outputData);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        output.Save(outputPath, ImageFormat.Png);
    }

    private enum PackedChannel
    {
        Red,
        Green,
        Alpha,
    }
}
