#!/usr/bin/env python3
"""
region_cropper.py
==================
Crops an image by named region, using the exact same region-layout system
as image_similarity.py's --region-layout / --regional-score. This means a
crop of "mid-left" is guaranteed to be the exact same pixels that region
was scored on there -- no eyeballing/guessing coordinates.

Layouts (pick with --region-layout, same names as image_similarity.py):
  3x3         (default) top-left, top, top-right,
                         mid-left, mid, mid-right,
                         bottom-left, bottom, bottom-right
  3-columns   left, center, right
  3-rows      top, middle, bottom

Usage
-----
    # see what region names are available for a layout
    python region_cropper.py --list-regions --region-layout 3x3

    # crop a single named region
    python region_cropper.py photo.jpg --region mid-left --out crop.png

    # crop several named regions in one go
    python region_cropper.py photo.jpg --regions top-left,mid,bottom-right --out-dir crops/

    # crop every region of a layout
    python region_cropper.py photo.jpg --all --region-layout 3-columns --out-dir crops/

    # add a small margin around each crop (percent of that region's own size)
    python region_cropper.py photo.jpg --region mid --margin-pct 10 --out crop.png

Requires: numpy, pillow
"""

from __future__ import annotations

import argparse
import os
import sys

import numpy as np
from PIL import Image


# --------------------------------------------------------------------------- #
# Region layouts -- kept name-for-name identical to image_similarity.py so a
# region name means the exact same slice of the frame in both scripts.
# --------------------------------------------------------------------------- #

REGION_LAYOUTS = {
    "3x3": [
        ["top-left", "top", "top-right"],
        ["mid-left", "mid", "mid-right"],
        ["bottom-left", "bottom", "bottom-right"],
    ],
    "3-columns": [
        ["left", "center", "right"],
    ],
    "3-rows": [
        ["top"],
        ["middle"],
        ["bottom"],
    ],
}


def list_region_layouts() -> str:
    lines = ["Available region layouts (use with --region-layout NAME):"]
    lines.append("  3x3         - 3 rows x 3 columns (default): top-left...bottom-right")
    lines.append("  3-columns   - 1 row x 3 columns: left, center, right")
    lines.append("  3-rows      - 3 rows x 1 column: top, middle, bottom")
    return "\n".join(lines)


def list_regions(layout: str) -> str:
    labels_grid = REGION_LAYOUTS[layout]
    names = [lbl for row in labels_grid for lbl in row]
    lines = [f"Regions available for layout '{layout}':"]
    for row in labels_grid:
        lines.append("  " + ", ".join(row))
    lines.append(f"\n(use any of: {', '.join(names)})")
    return "\n".join(lines)


# --------------------------------------------------------------------------- #
# Region bounds -- uses np.array_split, matching image_similarity.py's
# regional_breakdown() split points exactly, so crops line up with scores.
# --------------------------------------------------------------------------- #

def region_bounds(h: int, w: int, layout: str) -> dict:
    """
    Returns {region_name: (y0, y1, x0, x1)} pixel bounds for every named
    region in the given layout, using the identical split logic
    image_similarity.py uses to score each region.
    """
    if layout not in REGION_LAYOUTS:
        raise ValueError(f"Unknown region layout '{layout}'. Available: {', '.join(REGION_LAYOUTS)}")

    labels_grid = REGION_LAYOUTS[layout]
    grid_rows, grid_cols = len(labels_grid), len(labels_grid[0])

    row_chunks = np.array_split(np.arange(h), grid_rows)
    col_chunks = np.array_split(np.arange(w), grid_cols)

    bounds = {}
    for r_idx, row_chunk in enumerate(row_chunks):
        y0, y1 = int(row_chunk[0]), int(row_chunk[-1]) + 1
        for c_idx, col_chunk in enumerate(col_chunks):
            x0, x1 = int(col_chunk[0]), int(col_chunk[-1]) + 1
            label = labels_grid[r_idx][c_idx]
            bounds[label] = (y0, y1, x0, x1)
    return bounds


def crop_region(
    image: np.ndarray, region_name: str, layout: str = "3x3", margin_pct: float = 0.0,
) -> np.ndarray:
    """Crop `image` (H x W x C array) to the named region of the given layout.
    margin_pct expands the crop by that percent of the region's own
    width/height in each direction (clamped to the image bounds)."""
    h, w = image.shape[:2]
    bounds = region_bounds(h, w, layout)

    if region_name not in bounds:
        raise ValueError(
            f"Unknown region '{region_name}' for layout '{layout}'. "
            f"Available: {', '.join(bounds.keys())}"
        )

    y0, y1, x0, x1 = bounds[region_name]

    if margin_pct:
        region_h, region_w = y1 - y0, x1 - x0
        dy = int(region_h * (margin_pct / 100.0))
        dx = int(region_w * (margin_pct / 100.0))
        y0, y1 = max(0, y0 - dy), min(h, y1 + dy)
        x0, x1 = max(0, x0 - dx), min(w, x1 + dx)

    return image[y0:y1, x0:x1]


# --------------------------------------------------------------------------- #
# CLI
# --------------------------------------------------------------------------- #

def _safe_stem(path: str) -> str:
    base = os.path.basename(path)
    stem, _ = os.path.splitext(base)
    return stem


def main():
    parser = argparse.ArgumentParser(
        description="Crop an image by named region (matches image_similarity.py's --region-layout)."
    )
    parser.add_argument("image", nargs="?", help="Path to the image to crop")
    parser.add_argument(
        "--region-layout", default="3x3", choices=list(REGION_LAYOUTS.keys()),
        help="Region layout to crop from: 3x3 (default), 3-columns, or 3-rows"
    )
    parser.add_argument(
        "--region", default=None,
        help="Single region name to crop, e.g. --region mid-left"
    )
    parser.add_argument(
        "--regions", default=None,
        help="Comma-separated region names to crop, e.g. --regions top-left,mid,bottom-right"
    )
    parser.add_argument(
        "--all", action="store_true",
        help="Crop every region in the chosen layout"
    )
    parser.add_argument(
        "--margin-pct", type=float, default=0.0,
        help="Expand each crop by this percent of its own width/height, "
             "in all directions (clamped to image bounds). Default: 0"
    )
    parser.add_argument(
        "--out", metavar="PATH", default=None,
        help="Output path (only valid when cropping a single --region)"
    )
    parser.add_argument(
        "--out-dir", metavar="DIR", default=".",
        help="Output directory when cropping multiple regions (default: current directory)"
    )
    parser.add_argument(
        "--prefix", default=None,
        help="Filename prefix for multi-region output, e.g. 'photo' -> photo_mid-left.png "
             "(default: derived from the input filename)"
    )
    parser.add_argument(
        "--list-regions", action="store_true",
        help="Print region names available for --region-layout, then exit"
    )
    args = parser.parse_args()

    if args.list_regions:
        print(list_regions(args.region_layout))
        return

    if not args.image:
        parser.error("image is required (unless using --list-regions)")

    requested = sum(bool(x) for x in [args.region, args.regions, args.all])
    if requested == 0:
        parser.error("specify one of --region, --regions, or --all")
    if requested > 1:
        parser.error("use only one of --region, --regions, or --all")

    if args.region and args.out and (args.regions or args.all):
        parser.error("--out only applies to a single --region crop")

    try:
        img = np.array(Image.open(args.image).convert("RGB"))
    except FileNotFoundError:
        print(f"Error: file not found: {args.image}", file=sys.stderr)
        sys.exit(1)

    h, w = img.shape[:2]
    labels_grid = REGION_LAYOUTS[args.region_layout]
    all_names = [lbl for row in labels_grid for lbl in row]

    if args.all:
        targets = all_names
    elif args.regions:
        targets = [r.strip() for r in args.regions.split(",") if r.strip()]
    else:
        targets = [args.region]

    invalid = [r for r in targets if r not in all_names]
    if invalid:
        parser.error(
            f"Unknown region(s) for layout '{args.region_layout}': {', '.join(invalid)}. "
            f"Available: {', '.join(all_names)}"
        )

    stem = args.prefix or _safe_stem(args.image)
    ext = os.path.splitext(args.image)[1] or ".png"

    saved = []
    for region_name in targets:
        crop = crop_region(img, region_name, layout=args.region_layout, margin_pct=args.margin_pct)

        if len(targets) == 1 and args.out:
            out_path = args.out
        else:
            os.makedirs(args.out_dir, exist_ok=True)
            out_path = os.path.join(args.out_dir, f"{stem}_{region_name}{ext}")

        Image.fromarray(crop).save(out_path)
        saved.append((region_name, out_path, crop.shape[1], crop.shape[0]))

    print(f"Source image: {args.image} ({w}x{h}), layout: {args.region_layout}")
    print("-" * 56)
    for region_name, out_path, cw, ch in saved:
        print(f"  {region_name:<12} -> {out_path}  ({cw}x{ch})")


if __name__ == "__main__":
    main()
