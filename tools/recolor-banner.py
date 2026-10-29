"""Re-map one color family in a source module's artwork onto another.

The toast banners the modules ship were authored in whichever color the site happened
to use, and a band whose blue has nothing to do with the platform's palette is a
branding bug the notification code cannot fix. This tool moves a whole family of
shades at once rather than replacing one exact pixel value:

  * the source's dominant color is measured (or given with --from),
  * every pixel within --tolerance degrees of its hue, and above a saturation floor,
    gets its hue OFFSET by (target - source) and its saturation and value SCALED by
    the target's ratio to the source,
  * neutrals and other hues are left completely alone.

Offsetting instead of substituting is what keeps the artwork intact: the darker edge
of a shape, the anti-aliased pixels where a white glyph meets the background, and any
gradient in the original all land on the corresponding shade of the new color rather
than collapsing into one flat block.

Usage:
    python tools/recolor-banner.py --in Resources/Images/banner_x.png \
        --to "#FFA200" --out scratch/banner_x-orange.png

    python tools/recolor-banner.py --in ... --to ... --apply   # overwrite in place
"""

import argparse
import colorsys
import sys

import numpy as np
from PIL import Image


def parse_hex(value):
    text = value.strip().lstrip("#")
    if len(text) == 3:
        text = "".join(ch * 2 for ch in text)
    if len(text) != 6:
        raise argparse.ArgumentTypeError(f"'{value}' is not #rgb or #rrggbb")
    return tuple(int(text[i:i + 2], 16) for i in (0, 2, 4))


def to_hsv(rgb):
    return colorsys.rgb_to_hsv(rgb[0] / 255.0, rgb[1] / 255.0, rgb[2] / 255.0)


def dominant_color(pixels):
    """The average color of the largest sufficiently-chromatic group.

    Pixels are bucketed so anti-aliasing noise cannot out-vote a flat background in small
    slivers, but the value returned is the MEAN of the winning bucket rather than the bucket
    label: reporting the label shifts the measured hue and saturation by a few units, and
    those two numbers become the scale factors for every repainted pixel — so a sloppy
    measurement here is a visible color error in the output."""
    bucket = 8
    centered = (pixels.astype(np.int64) // bucket) * bucket + bucket // 2
    packed = (centered[:, 0] << 16) | (centered[:, 1] << 8) | centered[:, 2]
    values, inverse, counts = np.unique(packed, return_inverse=True, return_counts=True)

    for order in np.argsort(-counts):
        members = pixels[inverse == order]
        mean = tuple(int(round(float(v))) for v in members.mean(axis=0))
        _, saturation, value = to_hsv(mean)
        # Saturation is what excludes white and grey; a brightness ceiling would also throw away a
        # fully-saturated background, which is exactly the color being measured. Only near-black is
        # rejected, and only because its hue is meaningless.
        if saturation > 0.15 and value > 0.08:
            return mean

    key = int(values[np.argmax(counts)])
    return tuple(int(c) for c in [(key >> 16) & 255, (key >> 8) & 255, key & 255])


def hue_delta(a, b):
    """Signed shortest distance between two hues, each in [0, 1)."""
    d = a - b
    return (d + 0.5) % 1.0 - 0.5


def recolor(image, source_rgb, target_rgb, tolerance_degrees, saturation_floor):
    source_hsv = to_hsv(source_rgb)
    target_hsv = to_hsv(target_rgb)

    array = np.asarray(image.convert("RGB"), dtype=np.float32) / 255.0
    maximum = array.max(axis=2)
    minimum = array.min(axis=2)
    span = maximum - minimum

    value = maximum
    saturation = np.where(maximum > 0, span / np.maximum(maximum, 1e-6), 0.0)

    # Hue in [0, 360), matching colorsys' sector arithmetic so the comparison against the
    # source hue is meaningful on both sides of the wrap.
    r, g, b = array[:, :, 0], array[:, :, 1], array[:, :, 2]
    hue = np.zeros_like(value)
    with np.errstate(invalid="ignore", divide="ignore"):
        when_r = np.where(span > 0, ((g - b) / np.maximum(span, 1e-6)) % 6.0, 0.0)
        when_g = np.where(span > 0, ((b - r) / np.maximum(span, 1e-6)) + 2.0, 0.0)
        when_b = np.where(span > 0, ((r - g) / np.maximum(span, 1e-6)) + 4.0, 0.0)
    hue = np.where(maximum == r, when_r, np.where(maximum == g, when_g, when_b)) * 60.0

    source_hue = source_hsv[0] * 360.0
    tolerance = float(np.clip(tolerance_degrees, 1.0, 180.0))
    distance = np.abs(hue - source_hue)
    distance = np.minimum(distance, 360.0 - distance)

    mask = (distance <= tolerance) & (saturation >= saturation_floor)

    hue_shift = (target_hsv[0] * 360.0) - source_hue
    saturation_scale = target_hsv[1] / max(source_hsv[1], 1e-6)
    value_scale = target_hsv[2] / max(source_hsv[2], 1e-6)

    out_hue = (hue + hue_shift) % 360.0
    out_saturation = np.clip(saturation * saturation_scale, 0.0, 1.0)
    out_value = np.clip(value * value_scale, 0.0, 1.0)

    result = np.stack([out_hue, out_saturation, out_value], axis=-1)
    converted = np.apply_along_axis(
        lambda h: np.array(colorsys.hsv_to_rgb((h[0] % 360.0) / 360.0, h[1], h[2])), axis=2, arr=result)

    blended = np.where(mask[:, :, None], converted, array)
    changed = int(mask.sum())
    return Image.fromarray((np.clip(blended, 0.0, 1.0) * 255.0 + 0.5).astype(np.uint8), "RGB"), changed, mask.size


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--in", dest="src", required=True, help="image to read")
    parser.add_argument("--to", required=True, type=parse_hex, help="target color, e.g. \"#FFA200\"")
    parser.add_argument("--from", dest="frm", type=parse_hex, default=None,
                        help="color to replace; measured from the image when omitted")
    parser.add_argument("--out", dest="dst", default=None, help="where to write (default: <name>-recolored.png)")
    parser.add_argument("--tolerance", type=float, default=60.0, help="hue window in degrees (default 60)")
    parser.add_argument("--saturation-floor", type=float, default=0.12, help="ignore near-grey pixels (default 0.12)")
    parser.add_argument("--apply", action="store_true", help="overwrite the input file instead of writing a copy")
    args = parser.parse_args(argv)

    image = Image.open(args.src)
    flat = np.asarray(image.convert("RGB")).reshape(-1, 3)
    source = args.frm or dominant_color(flat)

    result, changed, total = recolor(image, source, args.to, args.tolerance, args.saturation_floor)

    destination = args.src if args.apply else (args.dst or args.src.replace(".png", "-recolored.png"))
    result.save(destination, optimize=True)

    hex_of = lambda c: "#{:02X}{:02X}{:02X}".format(*c)
    print(f"in        : {args.src} {image.size[0]}x{image.size[1]}")
    print(f"from      : {hex_of(source)} (hsv {tuple(round(v, 3) for v in to_hsv(source))})")
    print(f"to        : {hex_of(args.to)} (hsv {tuple(round(v, 3) for v in to_hsv(args.to))})")
    print(f"tolerance : {args.tolerance} deg, saturation floor {args.saturation_floor}")
    print(f"repainted : {changed} of {total} pixels ({100.0 * changed / total:.1f}%)")
    print(f"out       : {destination}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
