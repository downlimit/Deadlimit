using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;

namespace Deadlimit.Core;

/// <summary>Keep unrelated resource blocks byte-identical and verify edited KV3 semantically.</summary>
internal static class CompiledModelIntegrity
{
    public static string Fingerprint(KVObject value, Func<string, bool>? omit = null, string path = "")
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            Write(value, path, writer, omit);
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    private static void Write(KVObject value, string path, BinaryWriter writer, Func<string, bool>? omit)
    {
        writer.Write((int)value.ValueType);
        writer.Write((int)value.Flag);
        if (value.IsCollection)
        {
            var children = value.Children.Where(child => !(omit?.Invoke(
                path.Length == 0 ? child.Key : path + "/" + child.Key) ?? false)).ToArray();
            writer.Write(children.Length);
            foreach (var child in children)
            {
                writer.Write(child.Key);
                Write(child.Value, path.Length == 0 ? child.Key : path + "/" + child.Key, writer, omit);
            }
        }
        else if (value.IsArray)
        {
            writer.Write(value.Count);
            var index = 0;
            foreach (var child in value.Values)
            {
                Write(child, path + "/" + index++, writer, omit);
            }
        }
        else
        {
            writer.Write(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    public static Resource Read(byte[] bytes, string path)
    {
        var resource = new Resource { FileName = path };
        try
        {
            // Resource owns the reader/stream until disposed.
            resource.Read(new MemoryStream(bytes, writable: false), verifyFileSize: true);
            if (resource.DataBlock is not Model)
            {
                throw new InvalidDataException($"Expected compiled model: {path}");
            }
            return resource;
        }
        catch
        {
            resource.Dispose();
            throw;
        }
    }

    public static byte[] Serialize(Resource resource, byte[] original, ISet<int> edited)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0u);
        writer.Write((ushort)12);
        writer.Write(BitConverter.ToUInt16(original, 6));
        writer.Write(8u);
        writer.Write((uint)resource.Blocks.Count);
        writer.Write(new byte[resource.Blocks.Count * 12]);
        for (var i = 0; i < resource.Blocks.Count; i++)
        {
            while (stream.Position % 16 != 0) writer.Write((byte)0);
            var start = stream.Position;
            var block = resource.Blocks[i];
            if (edited.Contains(i)) block.Serialize(stream);
            else writer.Write(original.AsSpan(checked((int)block.Offset), checked((int)block.Size)));
            var end = stream.Position;
            stream.Position = 16 + i * 12;
            writer.Write(Encoding.ASCII.GetBytes(block.Type.ToString()));
            writer.Write(checked((uint)(start - stream.Position)));
            writer.Write(checked((uint)(end - start)));
            stream.Position = end;
        }
        stream.Position = 0;
        writer.Write(checked((uint)stream.Length));
        return stream.ToArray();
    }

    public static void VerifyUneditedBlocks(Resource before, byte[] original, Resource after, byte[] result, ISet<int> edited)
    {
        if (before.Blocks.Count != after.Blocks.Count || BitConverter.ToUInt16(original, 6) != BitConverter.ToUInt16(result, 6))
        {
            throw new InvalidDataException("Model resource version or block count changed during repair.");
        }
        for (var i = 0; i < before.Blocks.Count; i++)
        {
            var a = before.Blocks[i];
            var b = after.Blocks[i];
            if (a.Type != b.Type) throw new InvalidDataException($"Model block type changed at {i}.");
            if (!edited.Contains(i) && (a.Size != b.Size || !original.AsSpan((int)a.Offset, (int)a.Size)
                    .SequenceEqual(result.AsSpan((int)b.Offset, (int)b.Size))))
            {
                throw new InvalidDataException($"Unrelated model block changed: {i} {a.Type}.");
            }
        }
    }
}
