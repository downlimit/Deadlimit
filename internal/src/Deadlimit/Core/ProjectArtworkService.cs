using System.Drawing;
using System.Drawing.Imaging;

namespace Deadlimit.Core;

public static class ProjectArtworkService
{
    public const string HeaderFileName = "project-header.png";
    public static readonly Size HeaderSize = new(640, 170);

    public static string GetHeaderPath(string projectFolder) =>
        Path.Combine(ProjectStore.GetMetadataFolder(projectFolder), HeaderFileName);

    public static string EnsureDefaultHeader(string projectFolder)
    {
        var metadataFolder = ProjectStore.GetMetadataFolder(projectFolder);
        Directory.CreateDirectory(metadataFolder);

        if (OperatingSystem.IsWindows())
        {
            var attributes = File.GetAttributes(metadataFolder);
            File.SetAttributes(metadataFolder, attributes | FileAttributes.Hidden);
        }

        var headerPath = GetHeaderPath(projectFolder);
        if (File.Exists(headerPath))
        {
            return headerPath;
        }

        using var bitmap = new Bitmap(HeaderSize.Width, HeaderSize.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(31, 33, 36));
        }

        bitmap.Save(headerPath, ImageFormat.Png);
        return headerPath;
    }
}
