namespace Deadlimit.Core;

internal static class ExtractedTextureTgaService
{
    public static int CreateCopies(string extractedSourceRoot, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(extractedSourceRoot))
        {
            return 0;
        }

        var pngFiles = Directory.EnumerateFiles(extractedSourceRoot, "*.png", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var written = 0;
        foreach (var pngPath in pngFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var pngBytes = File.ReadAllBytes(pngPath);
            var tgaBytes = TgaImageEncoder.EncodePng(pngBytes);
            var tgaPath = Path.ChangeExtension(pngPath, ".tga");
            File.WriteAllBytes(tgaPath, tgaBytes);
            written++;
        }

        return written;
    }
}
