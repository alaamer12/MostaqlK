"""Design-parity harness: launch one app page in one theme, capture it, compare to the mockup.

For a given page+theme it:
  1. launches MostaqlK.exe with `--default-page=<page> --theme=<theme>`,
  2. waits for the window to render,
  3. captures it (PrintWindow), crops the Windows title-bar chrome and normalizes the canvas to
     the mockup viewport size,
  4. saves it as `tools/temp/<page>/app_<theme>_v<N>.png` (never overwriting history),
  5. runs image_similarity.py globally (`--no-region-report`) and regionally (default 3x3 report),
     writing a heatmap to `tools/temp/<page>/diff_<theme>_v<N>.png`,
  6. prints the overall score and the regional breakdown.

Usage:
  python tools/parity_check.py --page projects --theme light
  python tools/parity_check.py --all
"""

from __future__ import annotations

import argparse
import glob
import os
import re
import subprocess
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from snip_tool import capture_window, find_target_windows, setup_logging  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TOOLS = os.path.join(ROOT, "tools")
TEMP_ROOT = os.path.join(TOOLS, "temp")
EXE = os.path.join(ROOT, "bin", "Debug", "net10.0-windows10.0.19041.0", "win-x64", "MostaqlK.exe")
PYTHON = os.path.join(TOOLS, ".venv", "Scripts", "python.exe")
PAGES = ["projects", "project-details", "settings", "about"]
THEMES = ["light", "dark"]

# The mockups have no OS window chrome, so the app's title bar is cropped away before comparing
# (explicitly allowed by the task: the caption/window-button band is not reproducible).
VIEWPORT = (1280, 800)


def next_version(page: str, theme: str) -> int:
    pattern = os.path.join(TEMP_ROOT, page, f"app_{theme}_v*.png")
    versions = []
    for path in glob.glob(pattern):
        match = re.search(r"_v(\d+)\.png$", path)
        if match:
            versions.append(int(match.group(1)))
    return max(versions, default=0) + 1


def launch(page: str, theme: str, project_id: int | None) -> subprocess.Popen:
    args = [EXE, f"--default-page={page}", f"--theme={theme}"]
    if project_id:
        args.append(f"--project-id={project_id}")
    return subprocess.Popen(args, cwd=os.path.dirname(EXE))


def detect_chrome_height(img, max_scan: int = 80) -> int:
    """Height of the leading title-bar band, found by locating the first row that differs from
    the title bar's own background run. The band is a flat colour, so the first row whose sampled
    pixels stop matching row 0's dominant colour is where the app content starts."""
    width, height = img.size
    px = img.load()
    step = max(1, width // 60)

    def row_colors(y):
        return [px[x, y][:3] for x in range(0, width, step)]

    base = row_colors(0)
    reference = max(set(base), key=base.count)
    last_flat = 0
    for y in range(1, min(max_scan, height // 4)):
        colors = row_colors(y)
        matching = sum(1 for c in colors if _close(c, reference))
        if matching / len(colors) >= 0.6:
            last_flat = y
        elif last_flat:
            break
    return last_flat + 1 if last_flat else 0


def _close(a, b, tol: int = 12) -> bool:
    return all(abs(int(x) - int(y)) <= tol for x, y in zip(a, b))


def normalize(img, fill):
    from PIL import Image as PILImage

    chrome = detect_chrome_height(img)
    if chrome:
        img = img.crop((0, chrome, img.width, img.height))
    if img.size == VIEWPORT:
        return img, chrome
    canvas = PILImage.new("RGB", VIEWPORT, fill)
    canvas.paste(img.crop((0, 0, min(img.width, VIEWPORT[0]), min(img.height, VIEWPORT[1]))), (0, 0))
    return canvas, chrome


def compare(design: str, app: str, heatmap: str):
    common = [PYTHON, os.path.join(TOOLS, "image_similarity.py"), design, app, "--resize-mode", "pad"]
    overall = subprocess.run(common + ["--no-region-report", "--heatmap-out", heatmap],
                             capture_output=True, text=True)
    regional = subprocess.run(common + ["--regional-score", "6", "--palette", "8"],
                              capture_output=True, text=True)
    return overall.stdout + overall.stderr, regional.stdout + regional.stderr


def run_one(page: str, theme: str, wait: float, project_id: int | None, keep_open: bool):
    out_dir = os.path.join(TEMP_ROOT, page)
    os.makedirs(out_dir, exist_ok=True)
    version = next_version(page, theme)
    app_png = os.path.join(out_dir, f"app_{theme}_v{version}.png")
    heatmap = os.path.join(out_dir, f"diff_{theme}_v{version}.png")
    design = os.path.join(out_dir, f"design_{theme}.png")

    proc = launch(page, theme, project_id)
    try:
        deadline = time.time() + wait
        candidates = []
        while time.time() < deadline:
            time.sleep(1.0)
            candidates = find_target_windows(name="MostaqlK.exe")
            if candidates and candidates[0]["area"] > 200_000:
                break
        # Extra settle time so async SQLite loads and shimmer placeholders finish.
        time.sleep(3.0)
        candidates = find_target_windows(name="MostaqlK.exe")
        if not candidates:
            print("ERROR: no MostaqlK window found", file=sys.stderr)
            return None
        img = capture_window(candidates[0]["hwnd"])
        fill = (15, 23, 42) if theme == "dark" else (241, 245, 249)
        img, chrome = normalize(img, fill)
        img.save(app_png)
        print(f"Saved: {app_png} (cropped {chrome}px chrome)")
    finally:
        if not keep_open:
            _kill(proc)

    overall, regional = compare(design, app_png, heatmap)
    print(f"--- {page} / {theme} / v{version} ---")
    print(overall.strip())
    print(regional.strip())
    print(f"heatmap: {heatmap}")
    return app_png


def _kill(proc: subprocess.Popen):
    subprocess.run(["taskkill", "/PID", str(proc.pid), "/T", "/F"],
                   capture_output=True, text=True)
    for _ in range(10):
        if not find_target_windows(name="MostaqlK.exe"):
            return
        time.sleep(0.5)
    subprocess.run(["taskkill", "/IM", "MostaqlK.exe", "/F"], capture_output=True, text=True)


def main():
    parser = argparse.ArgumentParser(description="Capture + compare one app page against its mockup.")
    parser.add_argument("--page", choices=PAGES)
    parser.add_argument("--theme", choices=THEMES)
    parser.add_argument("--all", action="store_true", help="run all 8 page/theme combinations")
    parser.add_argument("--wait", type=float, default=25.0, help="max seconds to wait for the window")
    parser.add_argument("--project-id", type=int, default=None)
    parser.add_argument("--keep-open", action="store_true", help="leave the app running after capture")
    parser.add_argument("--debug", action="store_true")
    args = parser.parse_args()
    setup_logging(debug=args.debug)

    combos = ([(p, t) for p in PAGES for t in THEMES] if args.all
              else [(args.page or "projects", args.theme or "light")])
    for page, theme in combos:
        run_one(page, theme, args.wait, args.project_id, args.keep_open)


if __name__ == "__main__":
    main()
