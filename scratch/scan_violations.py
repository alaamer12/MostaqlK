import os
import re
import json

ROOT_DIR = os.path.abspath(os.path.join(os.path.dirname(__file__), ".."))
TARGET_DIRS = ["Features", "Services", "Infrastructure", "Models", "Core", "UI", "Platforms"]

PATTERNS = {
    "Presentation_Truncation_Formatting": [
        (r'\.Substring\s*\(\s*0\s*,\s*\d+\s*\)', "Direct Substring length truncation"),
        (r'\.Take\s*\(\s*\d+\s*\)', "LINQ Take truncation"),
        (r'\.Substring\s*\(', "String Substring operation"),
        (r'\.Split\s*\(', "String Split operation"),
        (r'string\.Format\s*\(', "string.Format usage"),
    ],
    "Avatar_Initials_AdHoc": [
        (r'\[\s*0\s*\]\.ToString\(\)', "Character extraction [0].ToString() for initials/avatar"),
        (r'\.FirstOrDefault\(\)\.ToString\(\)', "FirstOrDefault().ToString() for initials/avatar"),
        (r'\.Substring\s*\(\s*0\s*,\s*1\s*\)', "Substring(0, 1) for initials/avatar"),
        (r'char\.ToUpper\s*\(', "char.ToUpper for initials"),
    ],
    "Data_Layer_Sanitization_Fallback": [
        (r'\?\?\s*""', 'Silent fallback to empty string ?? ""'),
        (r'\?\?\s*string\.Empty', 'Silent fallback ?? string.Empty'),
        (r'\?\?\s*"غير محدد"', 'Silent fallback ?? "غير محدد"'),
        (r'\?\?\s*"—"', 'Silent fallback ?? "—"'),
        (r'\?\?\s*"-"', 'Silent fallback ?? "-"'),
        (r'\?\?\s*0', 'Silent fallback ?? 0'),
        (r'string\.IsNullOr(WhiteSpace|Empty)\s*\([^)]+\)\s*\?\s*[^:]+\s*:\s*[^;]+', 'Ternary fallback or masking'),
    ],
    "Domain_Status_And_Literal_Leakage": [
        (r'"(مفتوح|مكتمل|قيد التنفيذ|مغلق|جديد|ملغي|مرفوض|قيد المراجعة|بانتظار الموافقة)"', "Hardcoded Arabic project/order status"),
        (r'"(ر\.س|SAR|USD|\$|ريال|دولار)"', "Hardcoded currency literal"),
        (r'"(عرض واحد|عرضان|عروض|عرض|أيام|يوم|دقائق|دقيقة|ساعات|ساعة|ثواني|ثانية|منذ)"', "Hardcoded Arabic relative time / unit"),
    ],
    "Hardcoded_Hex_Color_FromArgb_ViewModels": [
        (r'Color\.FromArgb\s*\(\s*["\']\s*#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})\s*["\']\s*\)', "Hardcoded Color.FromArgb(hex literal) in ViewModels"),
    ],
    "AdHoc_Math_And_Number_Formatting": [
        (r'Math\.(Round|Floor|Ceiling|Clamp)\s*\(', "Math rounding/clamping"),
        (r'\.ToString\s*\(\s*["\'](N\d*|C\d*|F\d*|P\d*|#.*?|0.*?)["\']\s*\)', "Custom numeric format specifier"),
    ],
    "AdHoc_Time_Calculation": [
        (r'DateTime(Offset)?\.(Now|UtcNow)\s*-\s*', "Ad-hoc relative time calculation with subtraction"),
        (r'\.ToString\s*\(\s*["\'](yyyy|dd|MM|HH|hh|tt)[^"\']*["\']\s*\)', "Ad-hoc DateTime ToString formatting"),
    ]
}

# ViewModel-scoped patterns (limits false positives outside presentation contracts).
VM_ONLY_CATEGORIES = {
    "Hardcoded_Hex_Color_FromArgb_ViewModels",
}

def scan():
    findings = []
    file_count = 0
    for t_dir in TARGET_DIRS:
        dir_path = os.path.join(ROOT_DIR, t_dir)
        if not os.path.exists(dir_path):
            continue
        for root, dirs, files in os.walk(dir_path):
            for file in files:
                if file.endswith((".cs", ".xaml.cs", ".xaml")):
                    file_count += 1
                    full_path = os.path.join(root, file)
                    rel_path = os.path.relpath(full_path, ROOT_DIR)
                    rel_path_fwd = rel_path.replace('\\', '/')
                    try:
                        with open(full_path, "r", encoding="utf-8", errors="ignore") as f:
                            lines = f.readlines()
                    except Exception:
                        continue
                    
                    for idx, line in enumerate(lines):
                        stripped = line.strip()
                        if stripped.startswith("//") or stripped.startswith("/*") or stripped.startswith("*"):
                            continue
                        for cat, rules in PATTERNS.items():
                            if cat in VM_ONLY_CATEGORIES and "/ViewModels/" not in rel_path_fwd:
                                continue
                            for pat, desc in rules:
                                for m in re.finditer(pat, line):
                                    findings.append({
                                        "file": rel_path_fwd,
                                        "line": idx + 1,
                                        "category": cat,
                                        "description": desc,
                                        "match": m.group(0),
                                        "code": stripped
                                    })
    print(f"Scanned {file_count} files across {len(TARGET_DIRS)} target directories.")
    print(f"Total candidate matches: {len(findings)}")
    
    # Save formatted JSON and grouped TXT
    with open(os.path.join(ROOT_DIR, "scratch", "detailed_scan.json"), "w", encoding="utf-8") as f:
        json.dump(findings, f, ensure_ascii=False, indent=2)
        
    with open(os.path.join(ROOT_DIR, "scratch", "scan_summary.txt"), "w", encoding="utf-8") as f:
        # Group by file
        by_file = {}
        for item in findings:
            by_file.setdefault(item["file"], []).append(item)
            
        for filepath, items in sorted(by_file.items()):
            f.write(f"\n==================================================\n")
            f.write(f"FILE: {filepath} ({len(items)} matches)\n")
            f.write(f"==================================================\n")
            for it in items:
                f.write(f"  Line {it['line']:<4} [{it['category']}] ({it['description']}) -> Match: `{it['match']}`\n")
                f.write(f"    Code: {it['code']}\n")

if __name__ == "__main__":
    scan()
