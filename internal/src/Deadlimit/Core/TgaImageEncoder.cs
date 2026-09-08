using SkiaSharp;

namespace Deadlimit.Core;

internal static class TgaImageEncoder
{
    public static byte[] EncodeImage(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        using var data = SKData.CreateCopy(imageBytes);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The extracted image could not be decoded for TGA export.");

        var sourceInfo = codec.Info;
        if (sourceInfo.Width <= 0 || sourceInfo.Height <= 0)
        {
            throw new InvalidDataException("The decoded texture has invalid dimensions for TGA export.");
        }

        var alphaType = sourceInfo.AlphaType == SKAlphaType.Opaque
            ? SKAlphaType.Opaque
            : SKAlphaType.Unpremul;
        var decodeInfo = new SKImageInfo(
            sourceInfo.Width,
            sourceInfo.Height,
            SKColorType.Rgba8888,
            alphaType);

        using var bitmap = SKBitmap.Decode(codec, decodeInfo)
            ?? throw new InvalidDataException("The extracted image could not be decoded for TGA export.");

        return EncodeBitmap(bitmap);
    }

    private static byte[] EncodeBitmap(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            throw new InvalidDataException("The decoded texture has invalid dimensions for TGA export.");
        }

        if (bitmap.Width > ushort.MaxValue || bitmap.Height > ushort.MaxValue)
        {
            throw new NotSupportedException(
                $"TGA export supports dimensions up to {ushort.MaxValue}x{ushort.MaxValue}; " +
                $"decoded texture is {bitmap.Width}x{bitmap.Height}.");
        }

        const int headerSize = 18;
        var pixelCount = checked(bitmap.Width * bitmap.Height);
        var output = new byte[checked(headerSize + pixelCount * 4)];

        // Uncompressed true-color TGA, 32-bit BGRA, top-left image origin.
        output[2] = 2;
        WriteUInt16(output, 12, (ushort)bitmap.Width);
        WriteUInt16(output, 14, (ushort)bitmap.Height);
        output[16] = 32;
        output[17] = 0x28; // 8 alpha bits + top-left origin.

        var offset = headerSize;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                output[offset++] = color.Blue;
                output[offset++] = color.Green;
                output[offset++] = color.Red;
                output[offset++] = color.Alpha;
            }
        }

        return output;
    }

    private static void WriteUInt16(byte[] output, int offset, ushort value)
    {
        output[offset] = (byte)(value & 0xFF);
        output[offset + 1] = (byte)(value >> 8);
    }
}
