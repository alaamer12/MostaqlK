
"""
MostaqlK Icon Manager Tool
==========================

This script automates the process of adding new icons to the project. 
It can download SVGs from FontAwesome's CDN and "bake" them into the specific 
color variants required by the design system.

USAGE:
------
1. Find the FontAwesome icon name (e.g., 'stopwatch', 'filter').
2. Add it to the `ICONS_TO_INSTALL` list below with its required variants.
3. Run: `python tools/manage_icons.py`
4. Update the C# code as described in the "C# UPDATES" section below.

C# UPDATES:
-----------
After adding a new icon, you MUST update two files:

1. UI/PlatformComponents/AppIcon/AppIconGlyph.cs
   - Add a new enum member (e.g., `Stopwatch`).
   - Add a doc comment with the semantic use and the FontAwesome name for traceability.

2. UI/PlatformComponents/AppIcon/AppIconGlyphExtensions.cs
   - Update `ToImageBaseName`: map your new enum member to the base filename (e.g., `AppIconGlyph.Stopwatch => "icon_stopwatch"`).
   - Update `ActiveVariantIcons` if the icon needs a blue "_active" state in the sidebar.
   - Update `ToColourVariant` if the icon needs conceptual colors (Indigo, Violet, etc.) based on its TextColor.

DESIGN SYSTEM COLORS:
---------------------
- Default (Inactive): #475569 (Slate 600)
- Active (Blue):     #2563EB (Blue 600)
- Poll (Indigo):     #6366F1
- Query (Violet):    #8B5CF6
- Assets (Orange):   #F97316
- Grouping (Teal):   #14B8A6
- Rate (Pink):       #EC4899
"""

import os
import re
import urllib.request
import sys

# Configuration
BASE_IMAGE_DIR = r'Resources\Images'
FA_VERSION = '6.7.2'
CDN_BASE_URL = f'https://cdn.jsdelivr.net/npm/@fortawesome/fontawesome-free@{FA_VERSION}/svgs'

# List of icons to ensure are installed and baked
# Format: (fa_name, style['solid'|'regular'|'brands'], project_base_name, [list_of_variants])
# Variants: 'active', 'indigo', 'violet', 'orange', 'teal', 'pink'
ICONS_TO_INSTALL = [
    ('stopwatch', 'solid', 'icon_stopwatch', ['indigo']),
    ('filter', 'solid', 'icon_filter', ['violet']),
    ('paperclip', 'solid', 'icon_paperclip', ['orange']),
    ('layer-group', 'solid', 'icon_layer_group', ['teal']),
    ('gauge-high', 'solid', 'icon_gauge_high', ['pink']),
    ('circle-question', 'regular', 'icon_circle_question', []),
    ('upload', 'solid', 'icon_upload', []),
    ('pen-to-square', 'solid', 'icon_edit', ['active']),
    ('rotate-right', 'solid', 'icon_refresh', []),
    ('play', 'solid', 'icon_play', ['white']),
    ('pause', 'solid', 'icon_pause', ['white']),
    ('chevron-right', 'solid', 'icon_chevron_right', []),
    ('chevron-left', 'solid', 'icon_chevron_left', []),
    ('xmark', 'solid', 'icon_close', []),
]

COLOR_MAP = {
    'default': '#475569',
    'active':  '#2563EB',
    'indigo':  '#6366F1',
    'violet':  '#8B5CF6',
    'orange':  '#F97316',
    'teal':    '#14B8A6',
    'pink':    '#EC4899',
    'white':   '#FFFFFF',
}

def download_svg(fa_name, style):
    url = f"{CDN_BASE_URL}/{style}/{fa_name}.svg"
    try:
        print(f"Downloading {fa_name} ({style})...")
        with urllib.request.urlopen(url) as response:
            return response.read().decode('utf-8')
    except Exception as e:
        print(f"Error downloading {fa_name}: {e}")
        return None

def process_svg(content, fill_color):
    # Remove existing fill if any
    content = re.sub(r'fill="[^"]*"', '', content)
    # Insert new fill in the <svg tag
    if fill_color:
        content = re.sub(r'<svg', f'<svg fill="{fill_color}"', content)
    return content

def main():
    if not os.path.exists(BASE_IMAGE_DIR):
        print(f"Error: Directory {BASE_IMAGE_DIR} not found.")
        sys.exit(1)

    for fa_name, style, base_name, variants in ICONS_TO_INSTALL:
        # 1. Download/Get source
        svg_content = download_svg(fa_name, style)
        if not svg_content:
            continue

        # 2. Bake base version (gray)
        base_path = os.path.join(BASE_IMAGE_DIR, f"{base_name}.svg")
        with open(base_path, 'w', encoding='utf-8') as f:
            f.write(process_svg(svg_content, COLOR_MAP['default']))
        print(f"  Created base: {base_name}.svg")

        # 3. Bake variants
        for var in variants:
            if var not in COLOR_MAP:
                print(f"  Warning: Unknown variant '{var}'")
                continue
            
            var_filename = f"{base_name}_{var}.svg"
            var_path = os.path.join(BASE_IMAGE_DIR, var_filename)
            with open(var_path, 'w', encoding='utf-8') as f:
                f.write(process_svg(svg_content, COLOR_MAP[var]))
            print(f"  Created variant: {var_filename}")

    print("\nIcon management complete.")
    print("Don't forget to update AppIconGlyph.cs and AppIconGlyphExtensions.cs if you added new icons!")

if __name__ == "__main__":
    main()
