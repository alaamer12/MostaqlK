using System;
using System.Collections.Generic;
using System.Globalization;

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
/// The output is a minimal but fully valid PNG: IHDR, one IDAT of stored (uncompressed) deflate
/// blocks, IEND, with real CRC32 and Adler-32. Uncompressed is the right trade — the payload is a few
/// KB, and doing it here avoids pulling an encoder dependency into the notification path.
/// </para>
/// </summary>
public static class BrandImageEncoder
{
    /// <summary>An 8-bit sRGB color, alpha always opaque (a toast band has no reason to be translucent).</summary>
    public readonly record struct Rgb(byte R, byte G, byte B);

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>
    /// A <c>widthPx × heightPx</c> image split into horizontal bands, first band on top. Rows are
    /// divided evenly; a remainder goes to the last band so the height is always exact.
    /// Returns no bytes for degenerate input rather than throwing — the caller degrades to neutral.
    /// </summary>
    public static byte[] EncodeBanner(IReadOnlyList<Rgb> bandsTopToBottom, int widthPx, int heightPx)
    {
        if (bandsTopToBottom.Count == 0 || widthPx <= 0 || heightPx <= 0
            || widthPx > 4096 || heightPx > 1024)
        {
            // The caps are deliberate: this is a toast decoration, and an unbounded size here would be
            // an unbounded memory write on the notification path.
            return [];
        }

        var stride = 1 + (widthPx * 4); // filter byte + RGBA per pixel
        var raw = new byte[stride * heightPx];
        var bandCount = bandsTopToBottom.Count;

        for (var y = 0; y < heightPx; y++)
        {
            var band = bandsTopToBottom[(y * bandCount) / heightPx];
            var row = y * stride;
            raw[row] = 0; // filter: none
            for (var x = 0; x < widthPx; x++)
            {
                var p = row + 1 + (x * 4);
                raw[p] = band.R;
                raw[p + 1] = band.G;
                raw[p + 2] = band.B;
                raw[p + 3] = 255;
            }
        }

        var png = new List<byte>(raw.Length / 2 + 128);
        png.AddRange([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        AddChunk(png, "IHDR", [
            (byte)(widthPx >> 24), (byte)(widthPx >> 16), (byte)(widthPx >> 8), (byte)widthPx,
            (byte)(heightPx >> 24), (byte)(heightPx >> 16), (byte)(heightPx >> 8), (byte)heightPx,
            8,   // bit depth
            6,   // color type: RGBA
            0, 0, 0, // compression, filter, interlace
        ]);
        AddChunk(png, "IDAT", ZlibStore(raw));
        AddChunk(png, "IEND", []);
        return png.ToArray();
    }

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

    private static byte[] ZlibStore(byte[] raw)
    {
        var outBytes = new List<byte>(raw.Length + 64);
        outBytes.Add(0x78); // deflate, 32K window
        outBytes.Add(0x01); // no preset dictionary, default compression

        var offset = 0;
        do
        {
            var blockLength = Math.Min(raw.Length - offset, 0xFFFF);
            var last = offset + blockLength >= raw.Length;

            outBytes.Add((byte)(last ? 1 : 0)); // BFINAL + BTYPE=00 (stored)
            outBytes.Add((byte)blockLength);
            outBytes.Add((byte)(blockLength >> 8));
            var complement = (ushort)~blockLength;
            outBytes.Add((byte)complement);
            outBytes.Add((byte)(complement >> 8));
            for (var i = 0; i < blockLength; i++)
            {
                outBytes.Add(raw[offset + i]);
            }

            offset += blockLength;
        }
        while (offset < raw.Length);

        var adler = Adler32(raw);
        outBytes.Add((byte)(adler >> 24));
        outBytes.Add((byte)(adler >> 16));
        outBytes.Add((byte)(adler >> 8));
        outBytes.Add((byte)adler);
        return outBytes.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint a = 1, b = 0;
        foreach (var value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
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
