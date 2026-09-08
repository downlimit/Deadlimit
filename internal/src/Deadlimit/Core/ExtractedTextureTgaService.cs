namespace Deadlimit.Core;

internal static class ExtractedTextureTgaService
{
    private static readonly HashSet<string> SupportedLdrExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png",
        ".jpg",
        ".jpeg",
        ".webp",
    };

    public static int CreateCopies(string extractedSourceRoot, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(extractedSourceRoot))
        {
            return 0;
        }

        var imageFiles = Directory.EnumerateFiles(extractedSourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => SupportedLdrExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var written = 0;
        foreach (var imagePath in imageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var tgaPath = Path.ChangeExtension(imagePath, ".tga");
            if (File.Exists(tgaPath))
            {
                // A real extracted TGA with the same stem is authoritative; never overwrite it.
                continue;
            }

            var imageBytes = File.ReadAllBytes(imagePath);
            var tgaBytes = TgaImageEncoder.EncodeImage(imageBytes);
            File.WriteAllBytes(tgaPath, tgaBytes);
            written++;
        }

        return written;
    }
}
