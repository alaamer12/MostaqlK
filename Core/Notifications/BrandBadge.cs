using System;

namespace MostaqlK.Core.Notifications;

/// <summary>
/// The pieces a generated badge is built from. A null <see cref="Glyph"/> or <see cref="CornerMark"/>
/// is a supported answer: the badge simply carries less identity rather than failing.
/// </summary>
public sealed record BrandBadgeArt(
    BrandImageEncoder.Rgb RampTop,
    BrandImageEncoder.Rgb RampBottom,
    BrandRaster? Glyph,
    BrandRaster? CornerMark);

/// <summary>
/// Composes the one toast surface that survives everywhere a notification can be seen: the small
/// logo badge.
/// <para>
/// Why this exists at all. Windows owns the toast's background, text color and fonts — there is no
/// attribute an app may set to paint them, so a source's palette can only reach the notification as
/// imagery. The hero band covers the live popup, but once the toast lands in Action Center only the
/// logo remains. If that logo is the app's own, the platform's identity disappears exactly where the
/// user scrolls back to find it.
/// </para>
/// <para>
/// The second reason is subtler: each module's baked tile is a <em>white</em> glyph on transparency —
/// correct for the app's dark rail, invisible on a light toast. Pointing <c>appLogoOverride</c>
/// straight at that file produced a blank badge. Filling it onto the source's brand ramp is what
/// turns the same asset into a platform mark.
/// </para>
/// </summary>
public static class BrandBadge
{
    /// <summary>
    /// Bump whenever the composition changes — ratios, placement, the plate shape, anything that alters
    /// what the same inputs draw. The cache file name is keyed on the inputs alone, so without this a
    /// build that moved the mark would keep handing every user the badge the previous build wrote.
    /// </summary>
    public const int ArtRecipeVersion = 4;

    /// <summary>
    /// 160px. The shell draws this at roughly 48 logical pixels and Action Center at less, so this is
    /// already generous — but the app's mark is a small fraction of the canvas, and a mark that small
    /// needs source pixels left over after the downscale or it turns to mush.
    /// </summary>
    public const int DefaultSizePx = 160;

    /// <summary>
    /// The platform's mark fills four fifths of the plate. The baked tiles carry their own transparent
    /// padding, which is why this is measured after <see cref="TrimToContent"/>: scaling the padded
    /// square instead of the artwork inside it is what made the first version read as a small ring on
    /// a big disc.
    /// </summary>
    public const double GlyphRatio = 0.80;

    /// <summary>
    /// The app's own mark, as a fraction of the plate. Secondary to the platform's glyph by design, but
    /// large enough that the letter inside it survives the shell drawing this at ~48 pixels.
    /// </summary>
    public const double MarkRatio = 0.26;

    /// <summary>How far the mark sits in from the bottom-right bounds.</summary>
    public const double MarkMarginRatio = 0.054;

    /// <summary>Corner rounding of the plate itself, as a fraction of the canvas.</summary>
    public const double PlateCornerRatio = 0.16;

    /// <summary>
    /// Builds the badge's RGBA buffer, or returns null when the size is unusable. The caller encodes
    /// it to PNG and decides where the file lives.
    /// </summary>
    public static byte[]? TryBuild(BrandBadgeArt art, int sizePx = DefaultSizePx)
    {
        if (sizePx <= 8 || sizePx > 512)
        {
            return null;
        }

        var canvas = new byte[sizePx * sizePx * 4];
        BrandImageEncoder.FillVerticalRamp(canvas, sizePx, sizePx, art.RampTop, art.RampBottom);

        // A full-bleed rounded square rather than a disc: the app's mark lives in the corner, and a
        // circular plate would leave it floating on nothing. Windows is not asked to crop this image,
        // so the shape is ours to draw.
        RoundCorners(canvas, sizePx);

        if (art.Glyph is { } glyph)
        {
            var glyphPx = Math.Max(4, (int)Math.Round(sizePx * GlyphRatio));
            var scaled = Resample(TrimToContent(glyph), glyphPx);
            BlendOver(canvas, sizePx, scaled, (sizePx - glyphPx) / 2, (sizePx - glyphPx) / 2);
        }

        if (art.CornerMark is not null)
        {
            DrawCornerMark(canvas, sizePx, art);
        }

        return canvas;
    }

    /// <summary>
    /// Crops a raster to the bounding box of its non-transparent pixels (falling back to the whole
    /// image when there are none), so a requested scale means the size of the artwork, not the size of
    /// the canvas it was authored on.
    /// </summary>
    public static BrandRaster TrimToContent(BrandRaster source, byte alphaThreshold = 8)
    {
        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.Rgba[((y * source.Width) + x) * 4 + 3] > alphaThreshold)
                {
                    if (x < minX)
                    {
                        minX = x;
                    }

                    if (x > maxX)
                    {
                        maxX = x;
                    }

                    if (y < minY)
                    {
                        minY = y;
                    }

                    if (y > maxY)
                    {
                        maxY = y;
                    }
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return source;
        }

        var width = maxX - minX + 1;
        var height = maxY - minY + 1;
        if (width == source.Width && height == source.Height)
        {
            return source;
        }

        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(source.Rgba, (((y + minY) * source.Width) + minX) * 4, pixels, y * width * 4, width * 4);
        }

        return new BrandRaster(width, height, pixels);
    }

    /// <summary>
    /// Erases everything outside a rounded square inscribed in the canvas, with a half-pixel ramp on
    /// the edge so the rounding is not stair-stepped.
    /// </summary>
    private static void RoundCorners(byte[] canvas, int sizePx)
    {
        var radius = sizePx * PlateCornerRatio;

        for (var y = 0; y < sizePx; y++)
        {
            for (var x = 0; x < sizePx; x++)
            {
                var coverage = CornerCoverage(x, y, radius, sizePx);
                if (coverage >= 1d)
                {
                    continue;
                }

                var p = ((y * sizePx) + x) * 4;
                canvas[p + 3] = (byte)Math.Round(canvas[p + 3] * coverage);
            }
        }
    }

    /// <summary>
    /// Coverage of one pixel in a rounded square, via the usual signed-distance form: clamp the pixel
    /// to the box formed by the four corner-circle centers, and measure what is left over.
    /// </summary>
    private static double CornerCoverage(int x, int y, double radius, int sizePx)
    {
        var half = sizePx / 2d;
        var inner = half - radius;
        var px = (x + 0.5) - half;
        var py = (y + 0.5) - half;
        var dx = px - Math.Clamp(px, -inner, inner);
        var dy = py - Math.Clamp(py, -inner, inner);
        return Math.Clamp(radius - Math.Sqrt((dx * dx) + (dy * dy)) + 0.5, 0d, 1d);
    }

    /// <summary>
    /// The app's own logo as a disc riding the bottom-right edge of the canvas, with the brand plate
    /// knocked out from behind it.
    /// <para>
    /// Placed straight on the plate with no border of any kind. A knocked-out gap looked right on a
    /// light toast, where the shell's own surface shows through it as a keyline, and wrong on a dark
    /// one, where the same hole read as a black ring — and the notification is the same image in both,
    /// so the version that cannot be wrong in either is the one with nothing around the mark.
    /// </para>
    /// <para>
    /// Small and secondary on purpose: the plate and its glyph answer "which platform", and this mark
    /// only has to say "from this app". A second subject the same size would compete with the glyph and
    /// lose.
    /// </para>
    /// </summary>
    private static void DrawCornerMark(byte[] canvas, int sizePx, BrandBadgeArt art)
    {
        var mark = art.CornerMark!;
        var markPx = Math.Max(4, (int)Math.Round(sizePx * MarkRatio));
        var innerRadius = markPx / 2d;

        // Inset from the bounds by MarkMarginRatio, so the mark reads as a control placed on the plate
        // rather than something the plate ran out of room for.
        var center = sizePx - (sizePx * MarkMarginRatio) - innerRadius;
        var scaled = Resample(TrimToContent(mark), markPx);

        for (var y = 0; y < sizePx; y++)
        {
            for (var x = 0; x < sizePx; x++)
            {
                var gx = x + 0.5 - center;
                var gy = y + 0.5 - center;
                var distance = Math.Sqrt((gx * gx) + (gy * gy));
                if (distance > innerRadius)
                {
                    continue;
                }

                var p = ((y * sizePx) + x) * 4;
                var sx = (int)Math.Floor((x - (center - innerRadius)) * (scaled.Width / (double)markPx));
                var sy = (int)Math.Floor((y - (center - innerRadius)) * (scaled.Height / (double)markPx));
                if (sx < 0 || sy < 0 || sx >= scaled.Width || sy >= scaled.Height)
                {
                    continue;
                }

                scaled.Pixel(sx, sy, out var mr, out var mg, out var mb, out var ma);
                var coverage = Math.Clamp(innerRadius - distance + 0.5, 0d, 1d) * (ma / 255d);
                if (coverage <= 0d)
                {
                    continue;
                }

                var inv = 1 - coverage;
                canvas[p] = (byte)Math.Round((mr * coverage) + (canvas[p] * inv));
                canvas[p + 1] = (byte)Math.Round((mg * coverage) + (canvas[p + 1] * inv));
                canvas[p + 2] = (byte)Math.Round((mb * coverage) + (canvas[p + 2] * inv));
                canvas[p + 3] = (byte)Math.Round(255 - (255 - canvas[p + 3]) * inv);
            }
        }
    }

    /// <summary>
    /// Box-average downscale, bilinear upscale. The area filter matters: the baked tiles arrive at
    /// 400px and land in a 55px slot, and nearest-neighbour through that ratio shredded the glyph's
    /// thin strokes.
    /// </summary>
    public static BrandRaster Resample(BrandRaster source, int targetPx)
    {
        var output = new byte[targetPx * targetPx * 4];

        if (source.Width <= targetPx || source.Height <= targetPx)
        {
            Bilinear(source, output, targetPx);
        }
        else
        {
            AreaAverage(source, output, targetPx);
        }

        return new BrandRaster(targetPx, targetPx, output);
    }

    private static void AreaAverage(BrandRaster source, byte[] output, int targetPx)
    {
        var boxX = (double)source.Width / targetPx;
        var boxY = (double)source.Height / targetPx;

        for (var ty = 0; ty < targetPx; ty++)
        {
            var y0 = (int)Math.Floor(ty * boxY);
            var y1 = Math.Min(source.Height, Math.Max(y0 + 1, (int)Math.Ceiling((ty + 1) * boxY)));

            for (var tx = 0; tx < targetPx; tx++)
            {
                var x0 = (int)Math.Floor(tx * boxX);
                var x1 = Math.Min(source.Width, Math.Max(x0 + 1, (int)Math.Ceiling((tx + 1) * boxX)));

                // Averaged in premultiplied space. Blending the raw RGB of transparent pixels into a
                // glyph's edge would tint it with whatever the (invisible) background color was.
                double r = 0, g = 0, b = 0, a = 0;
                var count = 0;
                for (var sy = y0; sy < y1; sy++)
                {
                    for (var sx = x0; sx < x1; sx++)
                    {
                        source.Pixel(sx, sy, out var pr, out var pg, out var pb, out var pa);
                        r += pr * pa;
                        g += pg * pa;
                        b += pb * pa;
                        a += pa;
                        count++;
                    }
                }

                var o = ((ty * targetPx) + tx) * 4;
                var alpha = a / count;
                output[o + 3] = (byte)Math.Round(alpha);
                if (alpha > 0)
                {
                    output[o] = (byte)Math.Round(r / a);
                    output[o + 1] = (byte)Math.Round(g / a);
                    output[o + 2] = (byte)Math.Round(b / a);
                }
            }
        }
    }

    private static void Bilinear(BrandRaster source, byte[] output, int targetPx)
    {
        var scaleX = (double)source.Width / targetPx;
        var scaleY = (double)source.Height / targetPx;

        for (var ty = 0; ty < targetPx; ty++)
        {
            var fy = Math.Min(source.Height - 1, Math.Max(0, ((ty + 0.5) * scaleY) - 0.5));
            var y0 = (int)Math.Floor(fy);
            var y1 = Math.Min(source.Height - 1, y0 + 1);
            var wy = fy - y0;

            for (var tx = 0; tx < targetPx; tx++)
            {
                var fx = Math.Min(source.Width - 1, Math.Max(0, ((tx + 0.5) * scaleX) - 0.5));
                var x0 = (int)Math.Floor(fx);
                var x1 = Math.Min(source.Width - 1, x0 + 1);
                var wx = fx - x0;

                source.Pixel(x0, y0, out var tlr, out var tlg, out var tlb, out var tla);
                source.Pixel(x1, y0, out var trr, out var trg, out var trb, out var tra);
                source.Pixel(x0, y1, out var blr, out var blg, out var blb, out var bla);
                source.Pixel(x1, y1, out var brr, out var brg, out var brb, out var bra);

                var top = 1 - wx;
                var bottom = wx;
                var left = 1 - wy;
                var right = wy;

                var a = (tla * top * left) + (tra * bottom * left) + (bla * top * right) + (bra * bottom * right);
                var o = ((ty * targetPx) + tx) * 4;
                output[o + 3] = (byte)Math.Round(Math.Clamp(a, 0, 255));
                if (a > 0)
                {
                    output[o] = (byte)Math.Round(Math.Clamp(
                        ((tlr * tla) * top * left + (trr * tra) * bottom * left + (blr * bla) * top * right + (brr * bra) * bottom * right) / a,
                        0, 255));
                    output[o + 1] = (byte)Math.Round(Math.Clamp(
                        ((tlg * tla) * top * left + (trg * tra) * bottom * left + (blg * bla) * top * right + (brg * bra) * bottom * right) / a,
                        0, 255));
                    output[o + 2] = (byte)Math.Round(Math.Clamp(
                        ((tlb * tla) * top * left + (trb * tra) * bottom * left + (blb * bla) * top * right + (brb * bra) * bottom * right) / a,
                        0, 255));
                }
            }
        }
    }

    /// <summary>Source-over compositing of <paramref name="src"/> onto <paramref name="canvas"/> at an offset.</summary>
    private static void BlendOver(byte[] canvas, int canvasSize, BrandRaster src, int offsetX, int offsetY)
    {
        for (var y = 0; y < src.Height; y++)
        {
            var dy = offsetY + y;
            if (dy < 0 || dy >= canvasSize)
            {
                continue;
            }

            for (var x = 0; x < src.Width; x++)
            {
                var dx = offsetX + x;
                if (dx < 0 || dx >= canvasSize)
                {
                    continue;
                }

                src.Pixel(x, y, out var sr, out var sg, out var sb, out var sa);
                if (sa == 0)
                {
                    continue;
                }

                var p = ((dy * canvasSize) + dx) * 4;
                if (sa == 255)
                {
                    canvas[p] = sr;
                    canvas[p + 1] = sg;
                    canvas[p + 2] = sb;
                    canvas[p + 3] = 255;
                    continue;
                }

                var aN = sa / 255d;
                var inv = 1 - aN;
                canvas[p] = (byte)Math.Round(sr * aN + canvas[p] * inv);
                canvas[p + 1] = (byte)Math.Round(sg * aN + canvas[p + 1] * inv);
                canvas[p + 2] = (byte)Math.Round(sb * aN + canvas[p + 2] * inv);
                canvas[p + 3] = (byte)Math.Round(sa + canvas[p + 3] * inv);
            }
        }
    }
}
