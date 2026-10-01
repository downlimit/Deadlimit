using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

internal static class VrfContentExtraction
{
    public static ContentFile Extract(
        Resource resource,
        IFileLoader fileLoader,
        Action? onNewerShaderFallback = null)
    {
        if (resource.ResourceType == ResourceType.Texture)
        {
            return new TextureExtract(resource).ToContentFile();
        }

        try
        {
            return FileExtract.Extract(resource, fileLoader, null);
        }
        catch (Exception exception) when (
            resource.ResourceType == ResourceType.Material
            && IsUnsupportedVcsVersion(exception))
        {
            onNewerShaderFallback?.Invoke();

            // The basic provider reconstructs editable VMAT parameters and original
            // texture references without opening the unsupported VCS shader binary.
            return new MaterialExtract(resource, fileLoader: null).ToContentFile();
        }
    }

    private static bool IsUnsupportedVcsVersion(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.Message.Contains("Only VCS file versions", StringComparison.OrdinalIgnoreCase)
                && current.Message.Contains("vcsFileVersion", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
