
import os
import re
import sys

# Define ranges for Arabic characters and common punctuation/whitespace
# to avoid false positives.
# Arabic: 0600-06FF, 0750-077F, 08A0-08FF, FB50-FDFF, FE70-FEFF
# General Punctuation: 2000-206F
# Mathematical Operators: 2200-22FF
ARABIC_RE = re.compile(r'[\u0600-\u06FF\u0750-\u077F\u08A0-\u08FF\uFB50-\uFDFF\uFE70-\uFEFF\u2000-\u206F\u2200-\u22FF]')

def is_unicode_icon(char):
    code = ord(char)
    # Exclude ASCII (0-127)
    if code <= 127:
        return False
    # Exclude Arabic and common symbols
    if ARABIC_RE.match(char):
        return False
    # Common icon ranges: 
    # Miscellaneous Symbols: 2600-26FF
    # Dingbats: 2700-27BF
    # Private Use Area (FontAwesome): E000-F8FF
    return True

def scan_file(path):
    with open(path, 'r', encoding='utf-8', errors='ignore') as f:
        lines = f.readlines()
    
    found = []
    for i, line in enumerate(lines):
        # Search for characters that might be icons
        for char in line:
            if is_unicode_icon(char):
                found.append((i + 1, char, line.strip()))
                break
    return found

def main():
    root_dir = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    target_extensions = ('.cs', '.xaml')
    
    print(f"Scanning for Unicode icons in {root_dir}...")
    print("-" * 80)
    
    count = 0
    for root, dirs, files in os.walk(root_dir):
        if 'bin' in dirs: dirs.remove('bin')
        if 'obj' in dirs: dirs.remove('obj')
        if '.git' in dirs: dirs.remove('.git')
        
        for file in files:
            if file.endswith(target_extensions):
                path = os.path.join(root, file)
                matches = scan_file(path)
                if matches:
                    print(f"\nFILE: {os.path.relpath(path, root_dir)}")
                    for line_num, char, content in matches:
                        print(f"  Line {line_num}: [{char}] (U+{ord(char):04X}) -> {content}")
                        count += 1
    
    print("-" * 80)
    print(f"Total potential icons found: {count}")

if __name__ == "__main__":
    if sys.stdout.encoding != 'utf-8':
        import io
        sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')
    main()
