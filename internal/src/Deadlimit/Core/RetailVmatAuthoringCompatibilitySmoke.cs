using System.Drawing;
using System.Drawing.Imaging;

namespace Deadlimit.Core;

public static class RetailVmatAuthoringCompatibilitySmoke
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deadlimit-retail-vmat-{Guid.NewGuid():N}");
        var materialFolder = Path.Combine(root, "models", "test");
        Directory.CreateDirectory(materialFolder);
        try
        {
            WritePackedTexture(Path.Combine(materialFolder, "body_color.png"), Color.FromArgb(37, 10, 20, 30));
            WritePackedTexture(Path.Combine(materialFolder, "body_normal.png"), Color.FromArgb(173, 128, 128, 255));
            var vmatPath = Path.Combine(materialFolder, "body.vmat");
            File.WriteAllText(vmatPath, """
                "Layer0"
                {
                    "shader" "pbr.vfx"
                    "TextureColor" "models/test/body_color.png"
                    "TextureNormalRoughness" "models/test/body_normal.png"
                    "TextureSelfIllumMask" "materials/default/default_mask.tga"
                    "Compiled Textures"
                    {
                        "g_tColor" "models/test/body_color_png_deadbee.vtex"
                    }
                }
                """);

            if (!RetailVmatAuthoringCompatibility.Repair(vmatPath, root))
            {
                throw new InvalidOperationException("Fallback retail VMAT was not repaired.");
            }

            var repaired = File.ReadAllText(vmatPath);
            foreach (var required in new[]
                     {
                         "\"TextureColor1\"\t\"models/test/body_color.png\"",
                         "\"TextureMetalness1\"\t\"models/test/body_color_metalness.png\"",
                         "\"TextureNormal1\"\t\"models/test/body_normal.png\"",
                         "\"TextureRoughness1\"\t\"models/test/body_normal_roughness.png\"",
                         "\"TextureSelfIllumMask1\"\t\"materials/default/default_mask.tga\"",
                     })
            {
                if (!repaired.Contains(required, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"Repaired retail VMAT is missing: {required}\n{repaired}");
                }
            }
            if (!repaired.Contains("Compiled Textures", StringComparison.Ordinal)
                || repaired.Contains("\\\"Texture", StringComparison.Ordinal)
                || repaired.Contains("\\t\\\"", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Repaired retail VMAT lost its retail cache or contains escaped authoring syntax.\n{repaired}");
            }

            AssertMask(Path.Combine(materialFolder, "body_color_metalness.png"), 37);
            AssertMask(Path.Combine(materialFolder, "body_normal_roughness.png"), 173);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePackedTexture(string path, Color color)
    {
        using var bitmap = new Bitmap(2, 2, PixelFormat.Format32bppArgb);
        bitmap.SetPixel(0, 0, color);
        bitmap.SetPixel(1, 0, color);
        bitmap.SetPixel(0, 1, color);
        bitmap.SetPixel(1, 1, color);
        bitmap.Save(path, ImageFormat.Png);
    }

    private static void AssertMask(string path, int expected)
    {
        using var bitmap = new Bitmap(path);
        var pixel = bitmap.GetPixel(0, 0);
        if (pixel.R != expected || pixel.G != expected || pixel.B != expected)
        {
            throw new InvalidOperationException(
                $"Derived packed-channel mask is wrong: {Path.GetFileName(path)} = {pixel}, expected {expected}.");
        }
    }
}
