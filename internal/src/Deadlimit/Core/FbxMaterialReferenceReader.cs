using System.Buffers.Binary;
using System.Text;
using System.Text.RegularExpressions;

namespace Deadlimit.Core;

internal sealed record FbxMaterialReference(
    string SourceName,
    string AuthoringReference);

internal static class FbxMaterialReferenceReader
{
    private const string MaterialNamespacePrefix = "Material::";
    private const int MaxMaterialNameBytes = 16 * 1024;

    private static readonly byte[] BinaryHeader =
        Encoding.ASCII.GetBytes("Kaydara FBX Binary  \0\u001a\0");

    private static readonly byte[] BinaryMaterialPrefix =
        Encoding.ASCII.GetBytes(MaterialNamespacePrefix);

    private static readonly Regex AsciiMaterialRegex = new(
        @"\bMaterial\s*:\s*-?\d+\s*,\s*""Material::(?<name>[^""]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static IReadOnlyList<FbxMaterialReference> ReadMany(IEnumerable<string> paths)
    {
        var result = new Dictionary<string, FbxMaterialReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in paths)
        {
            foreach (var material in Read(path))
            {
                result.TryAdd(material.SourceName, material);
            }
        }

        return result.Values
            .OrderBy(material => material.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<FbxMaterialReference> Read(string path)
    {
        var raw = File.ReadAllBytes(path);
        var names = raw.AsSpan().StartsWith(BinaryHeader)
            ? ReadBinaryNames(raw)
            : ReadAsciiNames(raw);

        return names
            .Select(CreateReference)
            .Where(reference => reference is not null)
            .Select(reference => reference!)
            .GroupBy(reference => reference.SourceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(reference => reference.SourceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ReadAsciiNames(byte[] raw)
    {
        var text = Encoding.UTF8.GetString(raw);
        return AsciiMaterialRegex.Matches(text)
            .Select(match => match.Groups["name"].Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToArray();
    }

    private static IReadOnlyList<string> ReadBinaryNames(byte[] raw)
    {
        var names = new List<string>();
        var cursor = 0;

        while (cursor <= raw.Length - BinaryMaterialPrefix.Length)
        {
            var relative = raw.AsSpan(cursor).IndexOf(BinaryMaterialPrefix);
            if (relative < 0)
            {
                break;
            }

            var prefixOffset = cursor + relative;
            if (TryReadBinaryStringProperty(raw, prefixOffset, out var value)
                && value.StartsWith(MaterialNamespacePrefix, StringComparison.Ordinal)
                && value.Length > MaterialNamespacePrefix.Length)
            {
                names.Add(value[MaterialNamespacePrefix.Length..]);
            }

            cursor = prefixOffset + BinaryMaterialPrefix.Length;
        }

        return names;
    }

    private static bool TryReadBinaryStringProperty(
        byte[] raw,
        int valueOffset,
        out string value)
    {
        value = string.Empty;

        // Scalar FBX strings are encoded as:
        // 'S' + UInt32 byte length + UTF-8 payload.
        if (valueOffset < 5 || raw[valueOffset - 5] != (byte)'S')
        {
            return false;
        }

        var byteLength = BinaryPrimitives.ReadUInt32LittleEndian(
            raw.AsSpan(valueOffset - 4, 4));

        if (byteLength < BinaryMaterialPrefix.Length
            || byteLength > MaxMaterialNameBytes
            || byteLength > raw.Length - valueOffset)
        {
            return false;
        }

        value = Encoding.UTF8.GetString(raw, valueOffset, checked((int)byteLength));
        return true;
    }

    private static FbxMaterialReference? CreateReference(string rawName)
    {
        var sourceName = rawName
            .Replace('\\', '/')
            .Trim()
            .TrimStart('/');

        if (sourceName.StartsWith(MaterialNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            sourceName = sourceName[MaterialNamespacePrefix.Length..].Trim();
        }

        if (sourceName.Length == 0
            || sourceName.Any(char.IsControl))
        {
            return null;
        }

        var authoringReference = sourceName.StartsWith("materials/", StringComparison.OrdinalIgnoreCase)
            ? sourceName
            : "materials/" + sourceName;

        return new FbxMaterialReference(sourceName, authoringReference);
    }
}
