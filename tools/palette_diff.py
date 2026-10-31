"""App-vs-app pixel regression for the palette token refactor.

Compares two capture-only directories produced by
    tools\\.venv\\Scripts\\python.exe tools/v3_parity.py --capture-only --all --out <dir>
Each state directory holds a normalized 1280x800 app.png. The gate is EXACTNESS: with the
token layer preserving color values, the refactored build must render byte-identical frames.
Only documented, genuinely dynamic regions (masked via JSON) may differ.

Usage:
  python tools/palette_diff.py --baseline tools/parity_out/palette-baseline --current tools/parity_out/palette-after
  python tools/palette_diff.py --baseline B --current C --mask tools/parity_out/palette-masks.json --report C/report.md

Mask schema (optional; auto-loaded from <baseline>/masks.json, else the tracked
tools/palette-masks.json):
  {"global": [[x, y, w, h], ...], "states": {"projects__mostaql__light": [[x, y, w, h], ...]}}
Exit code 1 when any state has an unmasked difference or is missing.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

import numpy as np
from PIL import Image


def load_mask(path: str | None) -> dict:
    if not path or not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as fh:
        return json.load(fh)


def rectangles(mask: dict, sid: str) -> list[list[int]]:
    return list(mask.get("global") or []) + list((mask.get("states") or {}).get(sid) or [])


def apply_mask(diff: np.ndarray, rects: list[list[int]]) -> np.ndarray:
    if not rects or not diff.size:
        return diff
    h, w = diff.shape[:2]
    for x, y, rw, rh in rects:
        x0, y0 = max(0, int(x)), max(0, int(y))
        x1, y1 = min(w, int(x) + int(rw)), min(h, int(y) + int(rh))
        if x1 > x0 and y1 > y0:
            diff[y0:y1, x0:x1] = 0
    return diff


def compare_state(base_png: str, cur_png: str, tolerance: int, rects: list[list[int]],
                  diff_png: str | None):
    a = np.asarray(Image.open(base_png).convert("RGB"), dtype=np.int16)
    b = np.asarray(Image.open(cur_png).convert("RGB"), dtype=np.int16)
    if a.shape != b.shape:
        return {"size_mismatch": [list(a.shape), list(b.shape)], "ok": False}
    delta = np.abs(a - b)
    masked = apply_mask(delta, rects)
    per_pixel = masked.max(axis=2)
    differing = per_pixel > tolerance
    count = int(differing.sum())
    total = int(differing.size)
    info = {
        "ok": count == 0,
        "differing_pixels": count,
        "total_pixels": total,
        "percent": round(100.0 * count / total, 4) if total else 0.0,
        "max_delta": int(per_pixel.max()) if total else 0,
        "masked_rects": rects,
    }
    if count:
        ys, xs = np.nonzero(differing)
        info["bbox"] = [int(xs.min()), int(ys.min()), int(xs.max()) + 1, int(ys.max()) + 1]
    if diff_png:
        heat = np.clip(per_pixel, 0, 255).astype(np.uint8)
        Image.fromarray(heat, mode="L").save(diff_png)
    return info


def main() -> int:
    parser = argparse.ArgumentParser(description="Exact pixel diff between two palette captures.")
    parser.add_argument("--baseline", required=True, help="baseline capture dir")
    parser.add_argument("--current", required=True, help="after-change capture dir")
    parser.add_argument("--mask", default=None, help="mask JSON (default: <baseline>/masks.json)")
    parser.add_argument("--tolerance", type=int, default=0,
                        help="allowed max channel delta per pixel (default 0 = exact)")
    parser.add_argument("--report", default=None, help="write a markdown report here")
    parser.add_argument("--no-heatmaps", action="store_true", help="skip per-state diff PNGs")
    parser.add_argument("--intersect", action="store_true",
                        help="compare only states present in BOTH dirs (stability check)")
    args = parser.parse_args()

    tools_dir = os.path.dirname(os.path.abspath(__file__))
    tracked_mask = os.path.join(tools_dir, "palette-masks.json")
    mask_path = args.mask
    if not mask_path:
        local_mask = os.path.join(args.baseline, "masks.json")
        mask_path = local_mask if os.path.exists(local_mask) else tracked_mask
    mask = load_mask(mask_path)
    states = sorted(
        d for d in os.listdir(args.baseline)
        if os.path.isfile(os.path.join(args.baseline, d, "app.png"))
    )
    if args.intersect:
        states = [s for s in states if os.path.exists(os.path.join(args.current, s, "app.png"))]
    if not states:
        print(f"No comparable states under {args.baseline}")
        return 1

    rows, failures = [], []
    for sid in states:
        base_png = os.path.join(args.baseline, sid, "app.png")
        cur_png = os.path.join(args.current, sid, "app.png")
        if not os.path.exists(cur_png):
            rows.append({"id": sid, "ok": False, "missing": True})
            failures.append(sid)
            print(f"  MISSING  {sid}")
            continue
        diff_png = None if args.no_heatmaps else os.path.join(args.current, sid, "diff.png")
        info = compare_state(base_png, cur_png, args.tolerance, rectangles(mask, sid), diff_png)
        info["id"] = sid
        rows.append(info)
        if not info.get("ok"):
            failures.append(sid)
        status = ("OK" if info.get("ok") else "DIFF") if "size_mismatch" not in info else "SIZE"
        detail = (f"{info.get('differing_pixels', '?')} px ({info.get('percent', '?')}%), "
                  f"max {info.get('max_delta', '?')}, bbox {info.get('bbox', '-')}"
                  if "size_mismatch" not in info else str(info["size_mismatch"]))
        print(f"  {status:5s}  {sid}  {detail}")

    lines = [
        "# Palette regression — exact app-vs-app diff",
        "",
        f"Baseline: `{args.baseline}`  ",
        f"Current: `{args.current}`  ",
        f"Tolerance: {args.tolerance} channel delta  ",
        f"Mask: `{mask_path if os.path.exists(mask_path) else 'none'}`",
        "",
        "| state | differing px | % | max delta | bbox | verdict |",
        "|---|---|---|---|---|---|",
    ]
    for r in rows:
        if r.get("missing"):
            lines.append(f"| {r['id']} | — | — | — | — | MISSING |")
        elif "size_mismatch" in r:
            lines.append(f"| {r['id']} | — | — | — | — | SIZE MISMATCH |")
        else:
            lines.append(f"| {r['id']} | {r['differing_pixels']} | {r['percent']} | "
                         f"{r['max_delta']} | {r.get('bbox', '—')} | "
                         f"{'OK' if r['ok'] else 'DIFF'} |")
    lines += ["", f"FAILED: {len(failures)} state(s)" if failures else "PASSED: 0 unmasked differences."]
    report = "\n".join(lines) + "\n"
    print(report)
    if args.report:
        with open(args.report, "w", encoding="utf-8") as fh:
            fh.write(report)
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
