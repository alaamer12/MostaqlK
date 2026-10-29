using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace MostaqlK.Core.Notifications;

/// <summary>An 8-bit RGBA pixel buffer: <see cref="Rgba"/> is <c>Width × Height × 4</c>, row-major, non-premultiplied.</summary>
public sealed record BrandRaster(int Width, int Height, byte[] Rgba)
{
    /// <summary>Reads the RGBA bytes of one pixel. Callers are expected to be in bounds.</summary>
    public void Pixel(int x, int y, out byte r, out byte g, out byte b, out byte a)
    {
        var i = ((y * Width) + x) * 4;
        r = Rgba[i];
        g = Rgba[i + 1];
        b = Rgba[i + 2];
        a = Rgba[i + 3];
    }
}

/// <summary>
/// Reads the PNGs the app already ships — a source's baked tile and the app's own logo — so their
/// pixels can be recomposed into branded toast art.
/// <para>
/// The subset is the one MAUI's resizetizer actually produces: 8-bit, non-interlaced, color types
/// grayscale / RGB / RGBA / palette. Anything outside it returns null and the caller degrades to
/// unbranded art, because a toast that cannot be decorated is still a toast.
/// </para>
/// <para>
/// Chunk CRCs are not re-verified here. <see cref="ZLibStream"/> checks the Adler-32 over the whole
/// pixel payload on a full read, which is what actually catches the failure mode we care about — a
/// cache file truncated by a crash — and it costs nothing.
/// </para>
/// </summary>
public static class BrandImageDecoder
{
    public static BrandRaster? TryDecode(byte[]? png)
    {
        if (png is null || png.Length < 8 + 12 + 12)
        {
            return null;
        }

        if (!(png[0] == 0x89 && png[1] == 0x50 && png[2] == 0x4E && png[3] == 0x47))
        {
            return null;
        }

        byte[]? header = null;
        var compressed = new List<byte>();

        var i = 8;
        while (i + 12 <= png.Length)
        {
            var length = ReadInt32(png, i);
            if (length < 0 || i + 12 + length > png.Length)
            {
                return null;
            }

            var type = png[i + 4];
            var data = new byte[length];
            Array.Copy(png, i + 8, data, 0, length);

            if (type == 0x49 && png[i + 5] == 0x48 && png[i + 6] == 0x44 && png[i + 7] == 0x52) // IHDR
            {
                if (header is not null || length != 13)
                {
                    return null;
                }

                header = data;
            }
            else if (type == 0x49 && png[i + 5] == 0x44 && png[i + 6] == 0x41 && png[i + 7] == 0x54) // IDAT
            {
                compressed.AddRange(data);
            }
            else if (type == 0x49 && png[i + 5] == 0x45 && png[i + 6] == 0x4E && png[i + 7] == 0x44) // IEND
            {
                break;
            }

            i += 12 + length;
        }

        if (header is null || compressed.Count == 0)
        {
            return null;
        }

        var width = ReadInt32(header, 0);
        var height = ReadInt32(header, 4);
        byte bitDepth = header[8];
        byte colorType = header[9];
        var interlace = header[12];

        if (width <= 0 || height <= 0 || width > 4096 || height > 4096 || bitDepth != 8 || interlace != 0)
        {
            return null;
        }

        var channels = colorType switch
        {
            0 => 1, // grayscale
            2 => 3, // truecolor
            3 => 1, // palette (indexed)
            4 => 2, // grayscale + alpha
            6 => 4, // truecolor + alpha
            _ => 0,
        };

        if (channels == 0)
        {
            return null;
        }

        byte[]? palette = null;
        byte[]? transparency = null;
        if (colorType == 3)
        {
            if (!TryFindChunk(png, 0x504C5445 /* PLTE */, out palette))
            {
                return null;
            }
        }

        TryFindChunk(png, 0x74524E53 /* tRNS */, out transparency);

        var stride = width * channels;
        byte[] raw;
        try
        {
            using var source = new MemoryStream(compressed.ToArray());
            using var zlib = new ZLibStream(source, CompressionMode.Decompress);
            using var buffer = new MemoryStream(height * (stride + 1));
            zlib.CopyTo(buffer);
            raw = buffer.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }

        if (raw.LongLength != (long)height * (stride + 1))
        {
            return null;
        }

        var unfiltered = new byte[height * stride];
        if (!Unfilter(raw, unfiltered, height, stride, channels))
        {
            return null;
        }

        var rgba = new byte[width * height * 4];
        for (var p = 0; p < width * height; p++)
        {
            var s = p * channels;
            switch (colorType)
            {
                case 6:
                    rgba[p * 4] = unfiltered[s];
                    rgba[p * 4 + 1] = unfiltered[s + 1];
                    rgba[p * 4 + 2] = unfiltered[s + 2];
                    rgba[p * 4 + 3] = unfiltered[s + 3];
                    break;
                case 2:
                    rgba[p * 4] = unfiltered[s];
                    rgba[p * 4 + 1] = unfiltered[s + 1];
                    rgba[p * 4 + 2] = unfiltered[s + 2];
                    rgba[p * 4 + 3] = (byte)(transparency is { Length: >= 3 }
                        && unfiltered[s] == transparency[0] && unfiltered[s + 1] == transparency[1] && unfiltered[s + 2] == transparency[2]
                        ? 0
                        : 255);
                    break;
                case 0:
                    rgba[p * 4] = rgba[p * 4 + 1] = rgba[p * 4 + 2] = unfiltered[s];
                    rgba[p * 4 + 3] = (byte)(transparency is { Length: >= 1 } && unfiltered[s] == transparency[0] ? 0 : 255);
                    break;
                case 4:
                    rgba[p * 4] = rgba[p * 4 + 1] = rgba[p * 4 + 2] = unfiltered[s];
                    rgba[p * 4 + 3] = unfiltered[s + 1];
                    break;
                default: // 3 — palette
                    var index = unfiltered[s];
                    if (palette is null || index * 3 + 2 >= palette.Length)
                    {
                        return null;
                    }

                    rgba[p * 4] = palette[index * 3];
                    rgba[p * 4 + 1] = palette[(index * 3) + 1];
                    rgba[p * 4 + 2] = palette[(index * 3) + 2];
                    rgba[p * 4 + 3] = transparency is not null && transparency.Length > index ? transparency[index] : (byte)255;
                    break;
            }
        }

        return new BrandRaster(width, height, rgba);
    }

    /// <summary>The five PNG row filters. Every one of them appears in real baked tiles, so all five are implemented.</summary>
    private static bool Unfilter(byte[] raw, byte[] output, int height, int stride, int channels)
    {
        var prev = new byte[stride];
        for (var y = 0; y < height; y++)
        {
            var srcRow = y * (stride + 1);
            var filter = raw[srcRow];
            if (filter > 4)
            {
                return false;
            }

            var line = new byte[stride];
            Array.Copy(raw, srcRow + 1, line, 0, stride);

            for (var x = 0; x < stride; x++)
            {
                var a = x >= channels ? line[x - channels] : (byte)0;
                var b = prev[x];
                var c = x >= channels ? prev[x - channels] : (byte)0;
                line[x] = filter switch
                {
                    0 => line[x],
                    1 => (byte)(line[x] + a),
                    2 => (byte)(line[x] + b),
                    3 => (byte)(line[x] + ((a + b) / 2)),
                    _ => (byte)(line[x] + Paeth(a, b, c)),
                };
            }

            Array.Copy(line, 0, output, y * stride, stride);
            prev = line;
        }

        return true;
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : (pb <= pc ? b : c);
    }

    private static bool TryFindChunk(byte[] png, uint typeCode, out byte[]? data)
    {
        data = null;
        var i = 8;
        while (i + 12 <= png.Length)
        {
            var length = ReadInt32(png, i);
            if (length < 0 || i + 12 + length > png.Length)
            {
                return false;
            }

            var code = ((uint)png[i + 4] << 24) | ((uint)png[i + 5] << 16) | ((uint)png[i + 6] << 8) | png[i + 7];
            if (code == typeCode)
            {
                data = new byte[length];
                Array.Copy(png, i + 8, data, 0, length);
                return true;
            }

            i += 12 + length;
        }

        return false;
    }

    private static int ReadInt32(byte[] bytes, int offset) =>
        (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
}
