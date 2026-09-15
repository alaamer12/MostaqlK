# Polyglot Specification & Pseudocode: Coupling & Invariant Audit

This document defines the language-agnostic algorithm and polyglot implementation patterns for automated platform coupling audits. Use this specification to build invariant scanners in any ecosystem (Node.js/TypeScript, Python, Shell/Bash, Go, or Rust).

---

## 1. Universal Audit Algorithm (Pseudocode)

```text
ALGORITHM AuditPlatformCoupling(repoRoot, config):
    INPUT:
        repoRoot: string (absolute path to project root)
        config: struct containing:
            ignoredDirectories: list of regex/patterns (e.g., node_modules, bin, obj, target)
            platformFileExtensions: list of suffixes (e.g., .windows.*, .native.*, _windows.go)
            canonicalExemptFiles: list of filenames allowed to inspect platform (e.g., getPlatform.ts, CurrentPlatform.cs)
            directPlatformPatterns: regex matching direct preprocessors or OS identifiers
            indirectPlatformPatterns: regex matching indirect hardware/runtime checks
            leakedApiPatterns: regex matching OS-specific SDKs leaking into shared modules

    OUTPUT:
        violations: list of (file, lineNumber, violationType, snippet)
        status: SUCCESS | FAILURE

    violations = []
    sourceFiles = DiscoverSourceFiles(repoRoot, exclude: config.ignoredDirectories)
    
    FOR EACH file IN sourceFiles:
        // Skip platform-specific compilation leaves (e.g. Card.windows.tsx, X.Windows.cs, x_windows.go)
        IF IsPlatformSpecificFile(file, config.platformFileExtensions):
            CONTINUE
            
        // Skip canonical central platform resolvers
        IF config.canonicalExemptFiles.Contains(file.Name):
            CONTINUE

        lines = ReadLines(file)
        FOR lineIndex FROM 0 TO lines.Length - 1:
            line = lines[lineIndex]

            // 1. Check for Direct Platform Preprocessors / Direct OS Branches
            IF line.Matches(config.directPlatformPatterns):
                violations.Add(file, lineIndex + 1, "DIRECT_CONDITION", line)

            // 2. Check for Indirect Platform Inference (Idioms, Window width checks in groupers, Pointer sniffers)
            IF line.Matches(config.indirectPlatformPatterns):
                violations.Add(file, lineIndex + 1, "INDIRECT_CONDITION", line)

            // 3. Check for Platform SDK Leaks in Shared Folders
            IF line.Matches(config.leakedApiPatterns):
                violations.Add(file, lineIndex + 1, "SDK_LEAK", line)

    IF violations.IsEmpty():
        RETURN (violations, status: SUCCESS)
    ELSE:
        RETURN (violations, status: FAILURE)
```

---

## 2. Multi-Language Invariant Patterns

| Ecosystem | Direct Invariant Regex | Indirect Invariant Regex | Platform Leaf Exclusions |
|---|---|---|---|
| **TypeScript / React Native** | `Platform\.OS\s*===?\|Platform\.select` | `useWindowDimensions\|Dimensions\.get\('window'\)` (in groupers) | `*.ios.tsx`, `*.android.tsx`, `*.native.tsx`, `*.windows.tsx` |
| **Go** | `runtime\.GOOS\s*===?` | `//go:build` inside shared non-leaf packages | `*_windows.go`, `*_darwin.go`, `*_linux.go` |
| **Rust** | `cfg!\(target_os\s*=\s*"..."\)` in shared code | `#[cfg(target_os = "...")]` in domain logic | `*/windows.rs`, `*/unix.rs` |
| **C# / .NET** | `^\s*#if\s+(WINDOWS\|ANDROID\|IOS)` | `DeviceInfo\.Platform\|DeviceInfo\.Idiom\|PointerDeviceType` | `*.Windows.cs`, `_*.Mobile.cs`, `*.Android.cs` |

---

## 3. Polyglot Reference Implementation: Node.js / TypeScript Scanner

```typescript
// scripts/audit-coupling.ts
import fs from 'fs';
import path from 'path';

interface AuditRule {
  name: string;
  pattern: RegExp;
  type: 'DIRECT' | 'INDIRECT' | 'LEAK';
}

const RULES: AuditRule[] = [
  { name: 'Direct OS Check', pattern: /Platform\.OS\s*===?|runtime\.GOOS|#if\s+(WINDOWS|ANDROID)/, type: 'DIRECT' },
  { name: 'Indirect Hardware/Dimension Check', pattern: /useWindowDimensions|DeviceInfo\.Platform/, type: 'INDIRECT' },
  { name: 'Native SDK Leak', pattern: /react-native-haptic-feedback|Windows\.UI\.|Microsoft\.Toolkit/, type: 'LEAK' }
];

const EXEMPT_FILES = new Set(['getPlatform.ts', 'platformSelect.ts', 'CurrentPlatform.cs']);

function scanDirectory(dir: string, fileList: string[] = []): string[] {
  const entries = fs.readdirSync(dir, { withFileTypes: true });
  for (const entry of entries) {
    if (['node_modules', '.git', 'bin', 'obj', 'dist'].includes(entry.name)) continue;
    const fullPath = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      scanDirectory(fullPath, fileList);
    } else if (/\.(ts|tsx|cs|go|rs)$/.test(entry.name)) {
      fileList.push(fullPath);
    }
  }
  return fileList;
}

export function runAudit(rootPath: string): boolean {
  const files = scanDirectory(rootPath);
  let hasErrors = false;

  for (const file of files) {
    const fileName = path.basename(file);
    // Ignore platform leaves (e.g., .native.tsx, .windows.tsx, .Windows.cs)
    if (/\.(native|ios|android|windows)\.[^.]+$/i.test(fileName) || /_(windows|darwin)\.go$/.test(fileName)) {
      continue;
    }
    if (EXEMPT_FILES.has(fileName)) continue;

    const content = fs.readFileSync(file, 'utf-8');
    const lines = content.split('\n');

    lines.forEach((line, idx) => {
      for (const rule of RULES) {
        if (rule.pattern.test(line)) {
          console.error(`[${rule.type}] ${file}:${idx + 1} - ${rule.name}\n    ${line.trim()}`);
          hasErrors = true;
        }
      }
    });
  }

  return !hasErrors;
}
```

---

## 4. Polyglot Reference Implementation: Python Scanner

```python
#!/usr/bin/env python3
# scripts/audit_coupling.py
import os
import re
import sys

DIRECT_PATTERNS = re.compile(r'(Platform\.OS\s*===?|runtime\.GOOS|^\s*#if\s+(WINDOWS|ANDROID|IOS))')
INDIRECT_PATTERNS = re.compile(r'(useWindowDimensions\(|DeviceInfo\.Platform|DeviceInfo\.Idiom)')
EXEMPT_FILES = {'getPlatform.ts', 'platformSelect.ts', 'CurrentPlatform.cs', 'PlatformSelect.cs'}
PLATFORM_SUFFIXES = ('.native.', '.ios.', '.android.', '.windows.', '_windows.go', '_darwin.go', '.Windows.cs', '.Mobile.cs')

def scan_repo(root_dir):
    violations = 0
    for root, dirs, files in os.walk(root_dir):
        dirs[:] = [d for d in dirs if d not in {'node_modules', '.git', 'bin', 'obj', 'dist', 'Platforms'}]
        for file in files:
            if not file.endswith(('.ts', '.tsx', '.cs', '.go', '.rs')):
                continue
            if file in EXEMPT_FILES or any(s in file for s in PLATFORM_SUFFIXES):
                continue
            
            filepath = os.path.join(root, file)
            with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
                for line_num, line in enumerate(f, 1):
                    if DIRECT_PATTERNS.search(line):
                        print(f"DIRECT VIOLATION: {filepath}:{line_num} -> {line.strip()}")
                        violations += 1
                    elif INDIRECT_PATTERNS.search(line):
                        print(f"INDIRECT CONDITION: {filepath}:{line_num} -> {line.strip()}")
                        violations += 1
    return violations

if __name__ == '__main__':
    count = scan_repo('.')
    sys.exit(1 if count > 0 else 0)
```
