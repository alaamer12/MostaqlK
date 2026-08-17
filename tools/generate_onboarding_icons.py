#!/usr/bin/env python3
"""
Generates static, pre-baked colour variants of the icon SVGs used on the
onboarding page (Resources/Images/icon_*.svg).

Why this exists
----------------
The onboarding page used to bind the "Next" button's icon dynamically
(AppIconGlyph.ChevronLeft <-> AppIconGlyph.CircleCheck) via AppIcon /
AppIconGlyphExtensions.ToImageSource, which resolves the image at *runtime*
with a synchronous ImageSource.FromFile call against an absolute path. Doing
that repeatedly during a step transition (while other bound properties -
heading, description, badge, illustration - are also changing at once) is
exactly the crash scenario documented in
docs/reports/onboarding-icon-crash-report.md.

The fix keeps icons as plain rasterized images (MAUI's existing MauiImage
pipeline already turns every Resources/Images/*.svg into scale-100/125/.../400
PNGs at build time - this part works reliably everywhere else in the app).
What changes is that the onboarding page no longer swaps a *single* dynamic
icon at runtime; instead it pre-declares both static icon variants in XAML
(same "two named elements toggled by IsVisible" pattern already used for the
Save/Next spinner icons) and needs no fresh runtime resolution at all.

This script only bakes the specific colour variant SVGs needed for that
static XAML (currently: white chevron-left and white circle-check, used on
the green "Next"/"start" button; plus white refresh, used by both the
Save and Next loading spinners). It follows the exact same fill-swap
convention already used by the hand-authored *_white.svg files in this repo
(e.g. icon_bolt_white.svg / icon_play_white.svg / icon_pause_white.svg).

The white refresh variant was missing entirely (only the base grey
icon_refresh.svg existed), so AppIconGlyphExtensions.ToImageSource's
ImageSource.FromFile("icon_refresh_white.scale-200.png") silently resolved
to a non-existent file and rendered nothing - meaning the spinner's rotation
animation was always running on an invisible image, regardless of any
z-order fix.

Usage:
    python scripts/generate_onboarding_icons.py
"""

from __future__ import annotations

import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
IMAGES_DIR = REPO_ROOT / "Resources" / "Images"

# (base icon file, new variant suffix, fill colour)
VARIANTS: list[tuple[str, str, str]] = [
    ("icon_chevron_left.svg", "_white", "#FFFFFF"),
    ("icon_circle_check.svg", "_white", "#FFFFFF"),
    ("icon_refresh.svg", "_white", "#FFFFFF"),
]

FILL_ATTR_RE = re.compile(r'fill="[^"]*"')


def bake_variant(base_name: str, suffix: str, fill_color: str) -> Path:
    base_path = IMAGES_DIR / base_name
    if not base_path.exists():
        raise FileNotFoundError(f"Base icon not found: {base_path}")

    svg_text = base_path.read_text(encoding="utf-8")
    if not FILL_ATTR_RE.search(svg_text):
        raise ValueError(f"No root fill=\"...\" attribute found in {base_path}")

    new_svg_text = FILL_ATTR_RE.sub(f'fill="{fill_color}"', svg_text, count=1)

    variant_name = base_path.stem + suffix + base_path.suffix
    variant_path = IMAGES_DIR / variant_name
    variant_path.write_text(new_svg_text, encoding="utf-8")
    return variant_path


def main() -> int:
    if not IMAGES_DIR.exists():
        print(f"Images directory not found: {IMAGES_DIR}", file=sys.stderr)
        return 1

    for base_name, suffix, fill_color in VARIANTS:
        variant_path = bake_variant(base_name, suffix, fill_color)
        print(f"Generated {variant_path.relative_to(REPO_ROOT)}")

    print("Done. MAUI's MauiImage build step will rasterize these into PNGs automatically.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
