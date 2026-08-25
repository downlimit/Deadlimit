using System.Text;

namespace Deadlimit.Core;

internal static class AtomicFile
{
    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        var target = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(target)
            ?? throw new ArgumentException("Target path has no parent folder.", nameof(path));
        Directory.CreateDirectory(folder);

        var temporary = Path.Combine(folder, $".{Path.GetFileName(target)}.tmp-{Guid.NewGuid():N}");
        try
        {
            using (var stream = new FileStream(
                       temporary,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       64 * 1024,
                       FileOptions.WriteThrough))
            using (var writer = new StreamWriter(
                       stream,
                       encoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(contents);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            Replace(temporary, target);
        }
        finally
        {
            try
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The publish result or its original failure remains authoritative.
            }
        }
    }

    public static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
        WriteAllText(path, JsonSerializer.Serialize(value, options));

    private static void Replace(string temporary, string target)
    {
        if (File.Exists(target))
        {
            File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
            return;
        }

        File.Move(temporary, target);
    }
}
