"""Capture a MostaqlK window for mockup comparison.

Crops the OS/title chrome band from the top so app captures align with HTML mockups
that have no window chrome. Always appends numbered history via the caller.

Usage:
  python tools/capture_page.py --pid 12345 --output tools/temp/projects/app_vN.png
"""

from __future__ import annotations

import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from snip_tool import (  # noqa: E402
    capture_window,
    find_target_windows,
    setup_logging,
)


def detect_top_chrome_height(img, max_scan=64, dark_threshold=45):
    """Find how many leading rows are mostly dark title-bar chrome."""
    w, h = img.size
    limit = min(max_scan, h // 4)
    chrome = 0
    px = img.load()
    for y in range(limit):
        dark = 0
        step = max(1, w // 80)
        samples = 0
        for x in range(0, w, step):
            r, g, b = px[x, y][:3]
            samples += 1
            if r < dark_threshold and g < dark_threshold and b < dark_threshold:
                dark += 1
        # Caption button island can be light; require a majority of dark samples.
        if samples and (dark / samples) >= 0.55:
            chrome = y + 1
        elif chrome > 0:
            # allow a couple of mixed rows after dark band
            if y > chrome + 2:
                break
    # Also detect a thin light title strip under black by checking for a horizontal
    # seam: if first non-chrome rows are still very short title residual, keep going
    # until content density increases. Fallback minimum if black band found.
    if chrome > 0:
        return min(h // 3, max(chrome, 28))
    return 0


def crop_top(img, pixels):
    if pixels <= 0:
        return img
    w, h = img.size
    pixels = min(pixels, h - 1)
    return img.crop((0, pixels, w, h))


def main():
    parser = argparse.ArgumentParser(description="Capture MostaqlK window for design parity.")
    parser.add_argument("--pid", type=int)
    parser.add_argument("--name", default=None)
    parser.add_argument("--title", default=None)
    parser.add_argument("--output", required=True)
    parser.add_argument("--crop-top", type=int, default=-1,
                        help="Pixels to crop from top. -1 = auto-detect chrome.")
    parser.add_argument("--no-crop", action="store_true")
    parser.add_argument("--target-size", default="960x725",
                        help="Pad/crop canvas to WxH after chrome crop (default: 960x725).")
    parser.add_argument("--wait", type=float, default=0.0)
    parser.add_argument("--flip-horizontal", action="store_true")
    parser.add_argument("--no-flip-fix", action="store_true")
    parser.add_argument("--debug", action="store_true")
    args = parser.parse_args()
    setup_logging(debug=args.debug)

    if args.wait > 0:
        time.sleep(args.wait)

    candidates = find_target_windows(name=args.name, pid=args.pid, title=args.title)
    if not candidates:
        print("No matching window found.", file=sys.stderr)
        sys.exit(1)

    hwnd = candidates[0]["hwnd"]
    flip = True if args.flip_horizontal else (False if args.no_flip_fix else None)
    img = capture_window(hwnd, flip_override=flip)
    original_size = img.size

    crop_px = 0
    if not args.no_crop:
        crop_px = args.crop_top if args.crop_top >= 0 else detect_top_chrome_height(img)
        img = crop_top(img, crop_px)

    if args.target_size and args.target_size.lower() not in {"", "none", "0x0"} and "x" in args.target_size.lower():
        tw, th = args.target_size.lower().split("x", 1)
        tw_i, th_i = int(tw), int(th)
        if tw_i > 0 and th_i > 0 and img.size != (tw_i, th_i):
            from PIL import Image as PILImage
            canvas = PILImage.new("RGB", (tw_i, th_i), color=(241, 245, 249))  # #F1F5F9
            # Top-align content after chrome crop.
            paste_x = max(0, (tw_i - img.width) // 2)
            canvas.paste(img, (paste_x, 0))
            img = canvas

    out_dir = os.path.dirname(os.path.abspath(args.output))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)
    img.save(args.output)
    print(
        f"Saved: {args.output} ({img.width}x{img.height}) "
        f"crop_top={crop_px} original={original_size[0]}x{original_size[1]}"
    )


if __name__ == "__main__":
    main()
