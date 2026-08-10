"""Row-band diff report: locate *where* vertically two captures diverge.

The 3x3 regional breakdown in image_similarity.py is too coarse to tell "the card is 35px too
short" from "the card is styled wrong". This slices both images into horizontal bands and prints
per-band mean absolute difference, so cumulative vertical drift shows up as a band where the
error suddenly jumps.

Also supports `--shift-scan` to find the vertical offset that best aligns the two images, which
distinguishes a pure offset from a real layout difference.

Usage:
  python tools/band_report.py design.png app.png
  python tools/band_report.py design.png app.png --bands 40 --shift-scan
"""

from __future__ import annotations

import argparse

import numpy as np
from PIL import Image


def load_gray(path: str) -> np.ndarray:
    return np.asarray(Image.open(path).convert("L"), dtype=np.float64)


def band_diffs(a: np.ndarray, b: np.ndarray, bands: int):
    height = min(a.shape[0], b.shape[0])
    width = min(a.shape[1], b.shape[1])
    a, b = a[:height, :width], b[:height, :width]
    step = max(1, height // bands)
    rows = []
    for top in range(0, height, step):
        bottom = min(height, top + step)
        mad = float(np.abs(a[top:bottom] - b[top:bottom]).mean())
        rows.append((top, bottom, mad))
    return rows


def best_shift(a: np.ndarray, b: np.ndarray, limit: int):
    height = min(a.shape[0], b.shape[0])
    width = min(a.shape[1], b.shape[1])
    a, b = a[:height, :width], b[:height, :width]
    best = (0, float(np.abs(a - b).mean()))
    for shift in range(-limit, limit + 1):
        if shift >= 0:
            diff = np.abs(a[shift:] - b[: height - shift]).mean()
        else:
            diff = np.abs(a[: height + shift] - b[-shift:]).mean()
        if diff < best[1]:
            best = (shift, float(diff))
    return best


def main():
    parser = argparse.ArgumentParser(description="Per-band vertical diff report for two captures.")
    parser.add_argument("image_a")
    parser.add_argument("image_b")
    parser.add_argument("--bands", type=int, default=20)
    parser.add_argument("--shift-scan", action="store_true")
    parser.add_argument("--shift-limit", type=int, default=40)
    args = parser.parse_args()

    a, b = load_gray(args.image_a), load_gray(args.image_b)
    print(f"a={a.shape[1]}x{a.shape[0]}  b={b.shape[1]}x{b.shape[0]}")
    print(f"overall mean-abs-diff: {np.abs(a[:min(a.shape[0], b.shape[0])] - b[:min(a.shape[0], b.shape[0])]).mean():.2f}")

    for top, bottom, mad in band_diffs(a, b, args.bands):
        bar = "#" * int(min(60, mad))
        print(f"  y {top:4d}-{bottom:4d}: {mad:6.2f} {bar}")

    if args.shift_scan:
        shift, mad = best_shift(a, b, args.shift_limit)
        print(f"best vertical shift of A relative to B: {shift:+d}px (mean-abs-diff {mad:.2f})")


if __name__ == "__main__":
    main()
