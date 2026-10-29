using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;

namespace MostaqlK.Core.Notifications;

/// <summary>
/// Writes the brand-colored PNG a toast needs, from colors alone — no filesystem, no
/// <c>System.Drawing</c>, no SkiaSharp.
/// <para>
/// Why the app has to produce this at all: the toast surface color is owned by the shell, so the
/// only way a source's palette can appear on a notification is as an image the renderer loads from
/// disk. Generating it from <c>BrandTokenSet</c> rather than shipping one authored banner per site is
/// what keeps the promise the rest of this repo makes — a new platform brings its own identity data
/// and needs no shared edit and no new asset. A flat band also has no typography, so RTL text, font
/// fallback and theme contrast cannot go wrong inside it, and it survives whatever crop the shell
/// applies.
/// </para>
/// <para>
/// The output is a minimal but fully valid PNG: IHDR, one IDAT, IEND, with real CRC32 on every chunk.
/// The pixel payload goes through the BCL's own <see cref="System.IO.Compression.ZLibStream"/> rather
/// than a hand-written stored-block writer — a flat gradient compresses to a couple of hundred bytes,
/// where uncompressed scanlines made each cached hero image about 240 KB for no reason.
/// </para>
/// </summary>
public static class BrandImageEncoder
{
    /// <summary>An 8-bit sRGB color, alpha always opaque (a toast band has no reason to be translucent).</summary>
    public readonly record struct Rgb(byte R, byte G, byte B);

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// A <c>widthPx × heightPx</c> image whose rows ramp linearly from <paramref name="from"/> at the
    /// top to <paramref name="to"/> at the bottom. A gradient rather than bands because the shell
    /// scales a hero image up and stretches hard edges into stripes — the first on-screen attempt
    /// looked like a flag for exactly this reason. Returns no bytes for degenerate input, so a
    /// caller degrades to neutral instead of throwing.
    /// </summary>
    public static byte[] EncodeGradient(Rgb from, Rgb to, int widthPx, int heightPx)
    {
        if (!IsSaneSize(widthPx, heightPx))
        {
            return [];
        }

        var rgba = new byte[widthPx * heightPx * 4];
        FillVerticalRamp(rgba, widthPx, heightPx, from, to);
        return EncodeRgba(widthPx, heightPx, rgba);
    }

    /// <summary>
    /// Writes <paramref name="rgba"/> as a valid PNG. The buffer is <c>width × height × 4</c>,
    /// row-major, non-premultiplied — the same shape <see cref="BrandImageDecoder"/> hands back, so a
    /// raster can be read, recomposed and written without an intermediate format.
    /// </summary>
    public static byte[] EncodeRgba(int widthPx, int heightPx, byte[] rgba)
    {
        if (!IsSaneSize(widthPx, heightPx) || rgba.Length < widthPx * heightPx * 4)
        {
            return [];
        }

        var stride = 1 + (widthPx * 4); // filter byte + RGBA per pixel
        var raw = new byte[stride * heightPx];
        for (var y = 0; y < heightPx; y++)
        {
            // Filter byte 0 ("none"): every band we generate is authored, not photographed, so
            // sub-pixel prediction would buy nothing and would make the writer longer than the
            // format requires.
            Array.Copy(rgba, y * (widthPx * 4), raw, (y * stride) + 1, widthPx * 4);
        }

        var png = new List<byte>(raw.Length / 8 + 128);
        png.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        AddChunk(png, "IHDR", [
            (byte)(widthPx >> 24), (byte)(widthPx >> 16), (byte)(widthPx >> 8), (byte)widthPx,
            (byte)(heightPx >> 24), (byte)(heightPx >> 16), (byte)(heightPx >> 8), (byte)heightPx,
            8,   // bit depth
            6,   // color type: RGBA
            0, 0, 0, // compression, filter, interlace
        ]);
        AddChunk(png, "IDAT", ZlibCompress(raw));
        AddChunk(png, "IEND", []);
        return png.ToArray();
    }

    /// <summary>
    /// Fills an RGBA buffer with a linear ramp from <paramref name="from"/> at the top row to
    /// <paramref name="to"/> at the bottom. Shared by the hero band and the badge's background so the
    /// two pieces of one source's art always agree on the gradient.
    /// </summary>
    public static void FillVerticalRamp(byte[] rgba, int widthPx, int heightPx, Rgb from, Rgb to)
    {
        for (var y = 0; y < heightPx; y++)
        {
            var t01 = heightPx == 1 ? 0d : (double)y / (heightPx - 1);
            var r = (byte)Math.Round(from.R + ((to.R - from.R) * t01));
            var g = (byte)Math.Round(from.G + ((to.G - from.G) * t01));
            var b = (byte)Math.Round(from.B + ((to.B - from.B) * t01));

            for (var x = 0; x < widthPx; x++)
            {
                var p = ((y * widthPx) + x) * 4;
                rgba[p] = r;
                rgba[p + 1] = g;
                rgba[p + 2] = b;
                rgba[p + 3] = 255;
            }
        }
    }

    /// <summary>
    /// The caps are deliberate: this is a toast decoration, and an unbounded size here would be an
    /// unbounded memory write on the notification path.
    /// </summary>
    private static bool IsSaneSize(int widthPx, int heightPx) =>
        widthPx > 0 && heightPx > 0 && widthPx <= 4096 && heightPx <= 4096;

    /// <summary>
    /// <c>#rgb</c>, <c>#rrggbb</c> or <c>#aarrggbb</c> → color, false when unparseable.
    /// The ARGB form is not hypothetical: <c>BrandTokenSet.BrandSoftDark</c> values carry an alpha
    /// prefix (e.g. <c>#245CA8DE</c>); it is dropped here because a toast band is opaque by design.
    /// </summary>
    public static bool TryParseColorHex(string? hex, out Rgb color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(hex))
        {
            return false;
        }

        var value = hex.Trim().TrimStart('#');
        if (value.Length is 3 or 6 or 8)
        {
            // An 8-digit token is AARRGGBB in this repo's palette, 6-digit is RRGGBB, 3-digit shorthand.
            if (value.Length == 8)
            {
                value = value[2..];
            }
            else if (value.Length == 3)
            {
                value = string.Concat(value[0], value[0], value[1], value[1], value[2], value[2]);
            }

            if (byte.TryParse(value.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
                && byte.TryParse(value.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
                && byte.TryParse(value.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            {
                color = new Rgb(r, g, b);
                return true;
            }
        }

        return false;
    }

    /// <summary>Blend a color toward white (<paramref name="toWhite"/> true) or black, by 0..1.</summary>
    public static Rgb Mix(Rgb from, Rgb toward, double amount)
    {
        var t = Math.Clamp(amount, 0d, 1d);
        return new Rgb(
            (byte)Math.Round(from.R + ((toward.R - from.R) * t)),
            (byte)Math.Round(from.G + ((toward.G - from.G) * t)),
            (byte)Math.Round(from.B + ((toward.B - from.B) * t)));
    }

    private static void AddChunk(List<byte> png, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        length[0] = (byte)(data.Length >> 24);
        length[1] = (byte)(data.Length >> 16);
        length[2] = (byte)(data.Length >> 8);
        length[3] = (byte)data.Length;
        png.AddRange(length.ToArray());

        var typeBytes = new[] { (byte)type[0], (byte)type[1], (byte)type[2], (byte)type[3] };
        png.AddRange(typeBytes);
        png.AddRange(data);

        var crc = Crc32(typeBytes, data);
        Span<byte> crcBytes = stackalloc byte[4];
        crcBytes[0] = (byte)(crc >> 24);
        crcBytes[1] = (byte)(crc >> 16);
        crcBytes[2] = (byte)(crc >> 8);
        crcBytes[3] = (byte)crc;
        png.AddRange(crcBytes.ToArray());
    }

    private static byte[] ZlibCompress(byte[] raw)
    {
        using var buffer = new MemoryStream(raw.Length / 8 + 128);
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw, 0, raw.Length);
        }

        // ZLibStream writes the two-byte zlib header and the Adler-32 trailer itself, which is the
        // whole reason this method is four lines instead of a hand-rolled deflate writer.
        return buffer.ToArray();
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in type)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        foreach (var value in data)
        {
            crc = (crc >> 8) ^ CrcTable[(crc ^ value) & 0xFF];
        }

        return ~crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }

            table[i] = value;
        }

        return table;
    }
}
