using System.Text;

namespace Deadlimit.Core;

internal static class AtomicFile
{
    private static readonly object WriteGate = new();

    private static readonly int[] ReplaceRetryDelaysMilliseconds =
    [
        20,
        40,
        80,
        160,
        250,
        400,
        650,
        1000,
    ];

    public static void WriteAllText(string path, string contents, Encoding? encoding = null)
    {
        lock (WriteGate)
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
                TryDeleteTemporary(temporary);
            }
        }
    }

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> contents)
    {
        lock (WriteGate)
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
                {
                    stream.Write(contents);
                    stream.Flush(flushToDisk: true);
                }

                Replace(temporary, target);
            }
            finally
            {
                TryDeleteTemporary(temporary);
            }
        }
    }

    public static void WriteJson<T>(string path, T value, JsonSerializerOptions options) =>
        WriteAllText(path, JsonSerializer.Serialize(value, options));

    private static void Replace(string temporary, string target)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt <= ReplaceRetryDelaysMilliseconds.Length; attempt++)
        {
            try
            {
                if (File.Exists(target))
                {
                    File.Replace(temporary, target, destinationBackupFileName: null, ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(temporary, target);
                }

                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                if (!File.Exists(temporary))
                {
                    if (File.Exists(target))
                    {
                        return;
                    }

                    throw;
                }

                if (attempt >= ReplaceRetryDelaysMilliseconds.Length)
                {
                    break;
                }

                Thread.Sleep(ReplaceRetryDelaysMilliseconds[attempt]);
            }
        }

        throw new IOException(
            $"Could not atomically replace '{target}' after {ReplaceRetryDelaysMilliseconds.Length + 1} attempts.",
            lastError);
    }

    private static void TryDeleteTemporary(string temporary)
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
