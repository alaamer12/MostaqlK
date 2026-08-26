"""
Generate rounded-centroid tray icons with status badges from the application logo.

Status Badges:
  - Green  (#22C55E): Processing (Backlog / Processing)
  - Blue   (#3B82F6): Pulling / Polling (Network fetch)
  - Orange (#F97316): Idle (Standing by)
  - Red    (#EF4444): Error (Optional status)
  - None   (No badge): Clean / Base app icon
"""

import os
import sys
import argparse
from pathlib import Path
from PIL import Image, ImageDraw, ImageFilter, ImageOps

# Status badge configurations (User specification: green=processing, blue=pulling, orange=idle)
BADGES = {
    "idle": {
        "color": (249, 115, 22),       # Vivid Orange (#F97316)
        "ring": (255, 255, 255, 240),  # White contrast border
        "description": "Idle state",
    },
    "pulling": {
        "color": (59, 130, 246),       # Vibrant Blue (#3B82F6)
        "ring": (255, 255, 255, 240),  # White contrast border
        "description": "Pulling / Polling state",
    },
    "processing": {
        "color": (34, 197, 94),        # Crisp Green (#22C55E)
        "ring": (255, 255, 255, 240),  # White contrast border
        "description": "Processing state",
    },
    "error": {
        "color": (239, 68, 68),        # Alert Red (#EF4444)
        "ring": (255, 255, 255, 240),  # White contrast border
        "description": "Error state",
    },
    "base": {
        "color": None,
        "ring": None,
        "description": "Base rounded icon without badge",
    }
}

ICO_SIZES = [(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]


def compute_logo_centroid(image: Image.Image, threshold: float = 35.0):
    """
    Computes the bounding box and weighted centroid of the emblem in the logo.
    """
    img_rgb = image.convert("RGB")
    width, height = img_rgb.size
    
    # Sample corner pixels to estimate background color
    corners = [
        img_rgb.getpixel((0, 0)),
        img_rgb.getpixel((width - 1, 0)),
        img_rgb.getpixel((0, height - 1)),
        img_rgb.getpixel((width - 1, height - 1))
    ]
    bg_r = sum(c[0] for c in corners) / 4.0
    bg_g = sum(c[1] for c in corners) / 4.0
    bg_b = sum(c[2] for c in corners) / 4.0

    pixels = img_rgb.load()
    non_bg_x = []
    non_bg_y = []
    weights = []

    for y in range(0, height, 2):
        for x in range(0, width, 2):
            r, g, b = pixels[x, y]
            dist = ((r - bg_r)**2 + (g - bg_g)**2 + (b - bg_b)**2) ** 0.5
            if dist > threshold:
                non_bg_x.append(x)
                non_bg_y.append(y)
                weights.append(dist)

    if not non_bg_x:
        # Fallback to center if no foreground detected
        return (width / 2.0, height / 2.0), (0, 0, width, height)

    total_weight = sum(weights)
    cx = sum(x * w for x, w in zip(non_bg_x, weights)) / total_weight
    cy = sum(y * w for y, w in zip(non_bg_y, weights)) / total_weight

    min_x, max_x = min(non_bg_x), max(non_bg_x)
    min_y, max_y = min(non_bg_y), max(non_bg_y)

    return (cx, cy), (min_x, min_y, max_x, max_y)


def create_rounded_centroid_base(image: Image.Image, target_size: int = 1024, padding_ratio: float = 0.08) -> Image.Image:
    """
    Extracts the emblem centered at its centroid and clips it into a smooth circular mask
    with high-quality supersampling and anti-aliasing.
    """
    img_rgba = image.convert("RGBA")
    (cx, cy), (min_x, min_y, max_x, max_y) = compute_logo_centroid(image)

    content_w = max_x - min_x
    content_h = max_y - min_y
    content_radius = max(content_w, content_h) / 2.0

    # Determine crop bounding box centered on centroid
    crop_radius = content_radius * (1.0 + padding_ratio)
    crop_x1 = cx - crop_radius
    crop_y1 = cy - crop_radius
    crop_x2 = cx + crop_radius
    crop_y2 = cy + crop_radius

    # Work at high resolution for super-sampled anti-aliasing
    super_size = target_size * 2
    canvas = Image.new("RGBA", (int(crop_x2 - crop_x1), int(crop_y2 - crop_y1)), (0, 0, 0, 0))

    # Paste original image onto expanded canvas to safely handle border bounds
    offset_x = int(-crop_x1)
    offset_y = int(-crop_y1)
    temp_canvas = Image.new("RGBA", (int(crop_x2 - crop_x1), int(crop_y2 - crop_y1)), (0, 0, 0, 0))
    temp_canvas.paste(img_rgba, (offset_x, offset_y))

    # Resize to super-sampled square
    scaled = temp_canvas.resize((super_size, super_size), Image.Resampling.LANCZOS)

    # Create supersampled circular mask for anti-aliasing
    mask = Image.new("L", (super_size, super_size), 0)
    draw_mask = ImageDraw.Draw(mask)
    draw_mask.ellipse((0, 0, super_size - 1, super_size - 1), fill=255)

    # Composite circular icon
    rounded_icon = Image.new("RGBA", (super_size, super_size), (0, 0, 0, 0))
    rounded_icon.paste(scaled, (0, 0), mask=mask)

    # Downsample with Lanczos to achieve smooth anti-aliased circular edge
    final_base = rounded_icon.resize((target_size, target_size), Image.Resampling.LANCZOS)
    return final_base


def add_status_badge(base_icon: Image.Image, badge_key: str, badge_scale: float = 0.32) -> Image.Image:
    """
    Overlays a modern circular status badge on the bottom-right corner of the rounded icon.
    """
    badge_info = BADGES.get(badge_key)
    if not badge_info or badge_info["color"] is None:
        return base_icon.copy()

    size = base_icon.size[0]
    # Work at 2x resolution for badge antialiasing
    super_size = size * 2
    canvas = base_icon.resize((super_size, super_size), Image.Resampling.LANCZOS)

    draw = ImageDraw.Draw(canvas, "RGBA")

    # Badge geometry
    badge_radius = int(super_size * (badge_scale / 2.0))
    center_offset = int(super_size * 0.78)
    badge_center = (center_offset, center_offset)

    bx1 = badge_center[0] - badge_radius
    by1 = badge_center[1] - badge_radius
    bx2 = badge_center[0] + badge_radius
    by2 = badge_center[1] + badge_radius

    # 1. Subtle drop shadow for badge depth
    shadow_offset = int(badge_radius * 0.12)
    shadow_margin = int(badge_radius * 0.18)
    shadow_img = Image.new("RGBA", (super_size, super_size), (0, 0, 0, 0))
    shadow_draw = ImageDraw.Draw(shadow_img)
    shadow_draw.ellipse(
        (bx1 - shadow_margin, by1 - shadow_margin + shadow_offset,
         bx2 + shadow_margin, by2 + shadow_margin + shadow_offset),
        fill=(0, 0, 0, 90)
    )
    shadow_img = shadow_img.filter(ImageFilter.GaussianBlur(radius=super_size * 0.015))
    canvas = Image.alpha_composite(canvas, shadow_img)
    draw = ImageDraw.Draw(canvas, "RGBA")

    # 2. Outer contrast ring (white/light border for contrast against dark/light themes)
    ring_width = max(2, int(badge_radius * 0.22))
    draw.ellipse((bx1 - ring_width, by1 - ring_width, bx2 + ring_width, by2 + ring_width), fill=badge_info["ring"])

    # 3. Main badge solid color fill
    color = badge_info["color"]
    fill_rgba = (color[0], color[1], color[2], 255)
    draw.ellipse((bx1, by1, bx2, by2), fill=fill_rgba)

    # 4. Subtle top-half highlight for a modern tactile feel
    highlight_img = Image.new("RGBA", (super_size, super_size), (0, 0, 0, 0))
    hl_draw = ImageDraw.Draw(highlight_img)
    hl_radius_x = int(badge_radius * 0.7)
    hl_radius_y = int(badge_radius * 0.4)
    hl_center_x = badge_center[0]
    hl_center_y = badge_center[1] - int(badge_radius * 0.35)
    hl_draw.ellipse(
        (hl_center_x - hl_radius_x, hl_center_y - hl_radius_y,
         hl_center_x + hl_radius_x, hl_center_y + hl_radius_y),
        fill=(255, 255, 255, 60)
    )
    canvas = Image.alpha_composite(canvas, highlight_img)

    # Downsample back to target size
    return canvas.resize((size, size), Image.Resampling.LANCZOS)


def generate_all_tray_icons(logo_path: str, output_dir: str):
    """
    Generates PNG and multi-resolution ICO files for all states.
    """
    out_path = Path(output_dir)
    out_path.mkdir(parents=True, exist_ok=True)

    print(f"[1/3] Loading source logo: {logo_path}")
    source_logo = Image.open(logo_path)
    print(f"      Dimensions: {source_logo.size}, Mode: {source_logo.mode}")

    print("[2/3] Generating rounded-centroid base icon...")
    base_icon = create_rounded_centroid_base(source_logo, target_size=512)

    print("[3/3] Generating status badge variants (PNG & ICO)...")
    results = []

    for name, info in BADGES.items():
        badged_img = add_status_badge(base_icon, name)

        # 1. Save High-Res PNG (512x512)
        png_path = out_path / f"tray_{name}.png"
        badged_img.save(png_path, format="PNG")

        # 2. Save Standard Tray PNGs (32x32, 16x16)
        png32_path = out_path / f"tray_{name}_32x32.png"
        badged_img.resize((32, 32), Image.Resampling.LANCZOS).save(png32_path, format="PNG")

        png16_path = out_path / f"tray_{name}_16x16.png"
        badged_img.resize((16, 16), Image.Resampling.LANCZOS).save(png16_path, format="PNG")

        # 3. Save Multi-Resolution Windows ICO file
        ico_path = out_path / f"tray_{name}.ico"
        badged_img.save(ico_path, format="ICO", sizes=ICO_SIZES)

        results.append({
            "state": name,
            "description": info["description"],
            "png": str(png_path),
            "ico": str(ico_path)
        })
        print(f"  -> Generated state '{name}': {png_path.name} & {ico_path.name}")

    print(f"\nAll tray icons successfully generated in '{output_dir}'.")
    return results


def main():
    parser = argparse.ArgumentParser(
        description="Generate rounded-centroid tray icons with status badges (Green=Processing, Blue=Pulling, Orange=Idle)."
    )
    parser.add_argument(
        "--logo",
        default="Resources/Images/logo.png",
        help="Path to the source logo PNG (default: Resources/Images/logo.png)"
    )
    parser.add_argument(
        "--out-dir",
        default="temp-tray-icons/generated",
        help="Output directory for generated PNG and ICO files (default: temp-tray-icons/generated)"
    )

    args = parser.parse_args()

    if not os.path.isfile(args.logo):
        print(f"Error: Logo file not found at '{args.logo}'", file=sys.stderr)
        sys.exit(1)

    generate_all_tray_icons(args.logo, args.out_dir)


if __name__ == "__main__":
    main()
