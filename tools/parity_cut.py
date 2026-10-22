"""Exact-region visual parity cuts: v3 mockup vs the running MostaqlK window.

Both sides are normalized to the same 1280x800 client-space coordinate system before
any crop is taken, so a region box means the same thing on both images:

  - design: Playwright capture of the mockup at a 1280x800 viewport, DPR 1.
  - app:    PrintWindow capture of the live window, resized so its *client* area is
            1280x800 (window 1296x840, frame inset 8px, chrome 40px), then cropped to
            exactly that client rectangle (no borders, no title bar).

Each region is written twice (design_<name>.png / app_<name>.png), as a side-by-side
pair (pair_<name>.png), and scored with tools/image_similarity.py (SSIM, palette,
regional grid) plus a heatmap for localization.

Usage:
  python tools/parity_cut.py --url http://localhost:8124/projects.html --theme light \
      --output tools/temp/parity

  # Reuse already-captured full images (no app/design capture):
  python tools/parity_cut.py --design-image d.png --app-image a.png --output tools/temp/parity

The app must already be in the state you want to measure (theme, source, panel state).
"""
from __future__ import annotations

import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import win32gui
from PIL import Image, ImageDraw

from capture_page import detect_top_chrome_height
from design_screenshot import capture as capture_design
from image_similarity import run_comparison
from snip_tool import capture_window, find_target_windows

# The mockups are authored against a 1280x800 client viewport.
CLIENT_W, CLIENT_H = 1280, 800
# Windows' invisible resize frame around the client area (AppWindowMetrics.FrameInset).
FRAME_INSET = 8
# Caption band reserved by MAUI's Window.Height (AppWindowMetrics.ChromeHeight).
CHROME_H = 40
WINDOW_W = CLIENT_W + FRAME_INSET * 2
WINDOW_H = CLIENT_H + CHROME_H

# Boxes in 1280x800 client coordinates, identical for both images.
REGIONS = {
    # Header top-right: the "أُضيفت اليوم" counter card (design: absolute top-4, inset-inline-start 1.5rem)
    "today_card": (1120, 0, 1280, 104),
    # Feed search row: full-width input + filters button (px-6 py-4 inside the feed column)
    "search_row": (64, 104, 1028, 184),
    # Filters button (feed inline-end / physical left)
    "filters_button": (80, 116, 214, 176),
    # Search input
    "search_box": (206, 116, 1024, 176),
    # Pipeline panel column (right)
    "pipeline_panel": (1020, 104, 1280, 800),
}


def capture_app(pid: int | None) -> Image.Image:
    candidates = find_target_windows(pid=pid) if pid else find_target_windows(name="MostaqlK.exe")
    if not candidates:
        raise SystemExit("No MostaqlK window found. Launch the app first.")
    hwnd = candidates[0]["hwnd"]

    win32gui.ShowWindow(hwnd, 9)  # SW_RESTORE
    time.sleep(0.6)
    # Position partially off-screen so the *client* area is exactly 1280x800: the invisible
    # 8px resize frame on each side is what would otherwise shift every x coordinate.
    win32gui.SetWindowPos(hwnd, 0, -FRAME_INSET, 77, WINDOW_W, WINDOW_H, 0x0004 | 0x0010)
    time.sleep(1.2)

    img = capture_window(hwnd)
    left = (img.width - CLIENT_W) // 2
    top = img.height - CLIENT_H
    client = img.crop((left, top, left + CLIENT_W, top + CLIENT_H))
    if client.size != (CLIENT_W, CLIENT_H):
        raise SystemExit(f"Unexpected client crop {client.size}; window was {img.size}")
    return client


def make_pair(design: Image.Image, app: Image.Image, path: str) -> None:
    gap, label_h = 12, 22
    pair = Image.new("RGB", (design.width * 2 + gap, design.height + label_h), (241, 245, 249))
    pair.paste(design, (0, label_h))
    pair.paste(app, (design.width + gap, label_h))
    draw = ImageDraw.Draw(pair)
    draw.text((6, 5), "DESIGN", fill=(15, 23, 42))
    draw.text((design.width + gap + 6, 5), "APP", fill=(15, 23, 42))
    pair.save(path)


def main() -> None:
    parser = argparse.ArgumentParser(description="Exact-region parity cuts (design vs app).")
    parser.add_argument("--url", default="http://localhost:8124/projects.html")
    parser.add_argument("--theme", default="light", choices=["light", "dark"])
    parser.add_argument("--source", default="mostaql", choices=["mostaql", "nafezly"],
                        help="Mockup data-source state (localStorage 'mostaqlk-source')")
    parser.add_argument("--output", default=os.path.join("tools", "temp", "parity"))
    parser.add_argument("--design-image", help="Reuse a captured design image")
    parser.add_argument("--app-image", help="Reuse a captured app image")
    parser.add_argument("--pid", type=int, help="Target app pid (default: first MostaqlK window)")
    parser.add_argument("--regions", nargs="*", choices=sorted(REGIONS), default=sorted(REGIONS))
    parser.add_argument("--methods", default="pixel,ssim,histogram,hash")
    args = parser.parse_args()

    os.makedirs(args.output, exist_ok=True)

    if args.design_image:
        design = Image.open(args.design_image).convert("RGB")
    else:
        design_path = os.path.join(args.output, f"design_full_{args.theme}_{args.source}.png")
        capture_design(
            url=args.url, output=design_path, width=CLIENT_W, height=CLIENT_H,
            full_page=False, wait_ms=700, theme=args.theme,
            init_scripts=[
                f"try{{localStorage.setItem('mostaqlk-source','{args.source}');"
                "localStorage.setItem('mostaqlk-panel-collapsed','0');}catch(e){}",
            ],
        )
        design = Image.open(design_path).convert("RGB")

    if args.app_image:
        app = Image.open(args.app_image).convert("RGB")
    else:
        app = capture_app(args.pid)
        app.save(os.path.join(args.output, "app_full.png"))

    print(f"design={design.size} app={app.size}")
    if design.size != app.size:
        raise SystemExit("Full-image sizes differ; frame normalization failed.")

    methods = [m.strip() for m in args.methods.split(",") if m.strip()]
    for name in args.regions:
        box = REGIONS[name]
        d = design.crop(box)
        a = app.crop(box)
        d_path = os.path.join(args.output, f"design_{name}.png")
        a_path = os.path.join(args.output, f"app_{name}.png")
        d.save(d_path)
        a.save(a_path)
        make_pair(d, a, os.path.join(args.output, f"pair_{name}.png"))

        report = run_comparison(
            d_path, a_path,
            resize_mode="stretch",
            methods=methods,
            region_report=True,
            regional_score=4,
            palette=8,
            heatmap_out=os.path.join(args.output, f"heatmap_{name}.png"),
        )
        ssim = report.scores.get("ssim", 0.0)
        palette = (report.palette_report or {}).get("palette_similarity", 0.0)
        print(f"\n== {name} {box} ==")
        print(f"overall={report.overall_score:.4f} ssim={ssim:.4f} palette={palette:.4f}")
        for cell in (report.regional_score_grid or {}).get("worst", [])[:3]:
            print(f"  worst r{cell['row']}c{cell['col']} sim={cell['similarity']:.3f} box={cell['box']}")


if __name__ == "__main__":
    main()
