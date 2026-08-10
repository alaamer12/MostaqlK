"""
Design Screenshot Tool - captures a full-page screenshot of a served design
mockup HTML page using Playwright (Chromium), for pixel comparison against
the real running MostaqlK.exe app (captured separately via snip_tool.py).

Requirements (already installed in tools/.venv):
    pip install playwright
    playwright install chromium

Usage:
    python design_screenshot.py --url http://localhost:8000/projects.html --output tools/temp/design_projects.png
    python design_screenshot.py --url http://localhost:8000/projects.html --output out.png --width 1280 --height 800
"""

import argparse
import os
import sys

from playwright.sync_api import sync_playwright


def capture(url: str, output: str, width: int, height: int, full_page: bool, wait_ms: int,
            theme: str = "light"):
    out_dir = os.path.dirname(os.path.abspath(output))
    if out_dir:
        os.makedirs(out_dir, exist_ok=True)

    with sync_playwright() as p:
        browser = p.chromium.launch()
        page = browser.new_page(
            viewport={"width": width, "height": height},
            color_scheme="dark" if theme == "dark" else "light",
        )
        # The mockups read `localStorage['mostaqlk-theme']` before paint (see the inline head
        # script in each page), so seeding it here is what makes the capture deterministic.
        page.add_init_script(
            "try { localStorage.setItem('mostaqlk-theme', '%s'); } catch (e) {}" % theme
        )
        page.goto(url, wait_until="networkidle")
        page.evaluate(
            "t => document.documentElement.classList.toggle('dark', t === 'dark')", theme
        )
        if wait_ms:
            page.wait_for_timeout(wait_ms)
        page.screenshot(path=output, full_page=full_page)
        browser.close()

    print(f"Saved: {output}")


def build_arg_parser():
    parser = argparse.ArgumentParser(
        prog="design_screenshot.py",
        description="Screenshot a served design mockup HTML page with Playwright/Chromium.",
    )
    parser.add_argument("--url", required=True, help="URL of the served HTML page, e.g. http://localhost:8000/projects.html")
    parser.add_argument("--output", required=True, help="Output PNG file path")
    parser.add_argument("--width", type=int, default=1280, help="Viewport width (default 1280)")
    parser.add_argument("--height", type=int, default=800, help="Viewport height (default 800)")
    parser.add_argument("--full-page", action="store_true", help="Capture the full scrollable page instead of just the viewport")
    parser.add_argument("--wait-ms", type=int, default=300, help="Extra wait after load before capturing (default 300ms)")
    parser.add_argument("--theme", choices=["light", "dark"], default="light",
                        help="Which mockup theme state to capture (default light)")
    return parser


if __name__ == "__main__":
    args = build_arg_parser().parse_args()
    try:
        capture(args.url, args.output, args.width, args.height, args.full_page, args.wait_ms,
                theme=args.theme)
    except Exception as e:
        print(f"ERROR: {e}", file=sys.stderr)
        sys.exit(1)
