# Polyglot Violation Checking Walkthroughs & Examples

This document demonstrates concrete implementations of invariant and violation checkers across multiple languages and paradigms:
1. **Fast Scriptable Checkers (Recommended Default):**
   - **Python (`.py`):** Fast regex line-scanner checking for banned raw error instantiation and swallowed exceptions.
   - **C# Single-File Script via PowerShell (`.ps1`):** Fast single-file C# runner executing via PowerShell to inspect .NET code contracts without building projects.
   - **Node.js (`.js`):** Lightweight regex checker for front-end/TypeScript rules.
2. **Targeted AST Escalation (Only When Strictly Necessary):**
   - **C# (Roslyn) / Python (`ast`):** When deep syntax trees, nested statements, or ambiguous scopes cannot be solved reliably with regex.
3. **Context-Dependent Structural & Data Audits:** Polyglot input parser resilience test harnesses and SQL invariant checks for projects handling structured inputs or state stores.

---

## 1. Fast Scriptable Pattern Checkers (Lightweight Rules & Regex)

### Example A: Python Single-File Fast Checker (`audit_rules.py`)

**Philosophy:** Fast to run, zero heavy dependencies, covers 90% of checks in milliseconds using regular expressions.

```python
# tools/audit_rules.py
"""
Fast, lightweight checker scanning source files for invariant violations
without the overhead of full AST parsing.
"""
import re
import sys
from pathlib import Path

# Rule: Domain errors must use ErrorFactory, not 'new ErrorResponse(...)' or 'new DomainException(...)'
BANNED_INSTANTIATION = re.compile(r'\bnew\s+(ErrorResponse|DomainException|AppException)\s*\(')

# Rule: Empty catch blocks without comment or logging
EMPTY_CATCH = re.compile(r'catch\s*\([^\)]+\)\s*\{\s*\}')

def check_file(file_path: Path) -> list[str]:
    violations = []
    lines = file_path.read_text(encoding="utf-8", errors="ignore").splitlines()
    
    for idx, line in enumerate(lines, start=1):
        # Ignore comments
        stripped = line.strip()
        if stripped.startswith("//") or stripped.startswith("*"):
            continue

        if match := BANNED_INSTANTIATION.search(line):
            violations.append(
                f"[ERR-001] {file_path}:{idx} Banned instantiation of '{match.group(1)}'. Use 'ErrorFactory' instead."
            )
            
        if EMPTY_CATCH.search(line):
            violations.append(
                f"[ERR-002] {file_path}:{idx} Empty catch block swallows errors silently."
            )

    return violations

def main():
    root = Path(".")
    cs_files = list(root.glob("Features/**/*.cs")) + list(root.glob("Services/**/*.cs"))
    all_violations = []

    for f in cs_files:
        all_violations.extend(check_file(f))

    if all_violations:
        print(f"FAILED: {len(all_violations)} violations found:")
        for v in all_violations:
            print(f"  {v}")
        sys.exit(1)
    else:
        print("PASSED: All fast pattern checks passed.")

if __name__ == "__main__":
    main()
```

---

### Example B: Single-File C# via PowerShell Script (`check-csharp-invariants.ps1`)

**Philosophy:** Check C# code using C# or PowerShell regex in a single scriptable file without needing to compile a full `.csproj`.

```powershell
# scripts/check-csharp-invariants.ps1
# Fast, single-file C# invariant checker executed directly in PowerShell

$ErrorActionPreference = "Stop"
$root = Get-Location

# Scan all C# feature and service files
$files = Get-ChildItem -Path $root -Recurse -Filter "*.cs" | Where-Object { 
    $_.FullName -notmatch '\\(bin|obj|tools|Tests)\\' 
}

$violations = @()

foreach ($file in $files) {
    $lines = Get-Content -Path $file.FullName
    $lineNum = 0

    foreach ($line in $lines) {
        $lineNum++
        $trimmed = $line.Trim()
        if ($trimmed.StartsWith("//") -or $trimmed.StartsWith("*")) { continue }

        # Check 1: Direct Error instantiation
        if ($line -match 'new\s+(ErrorResponse|AppException)\s*\(') {
            $violations += "[ERR-001] $($file.FullName):$lineNum - Direct instantiation of error object. Use ErrorFactory."
        }

        # Check 2: Async methods omitting CancellationToken parameter
        if ($line -match 'public\s+async\s+Task(<[^>]+>)?\s+\w+Async\s*\((?!.*CancellationToken)' -and 
            $line -notmatch 'override') {
            $violations += "[ASYNC-001] $($file.FullName):$lineNum - Async public method missing CancellationToken."
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "FAILED: Found $($violations.Count) violations:" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
    exit 1
} else {
    Write-Host "PASSED: All C# invariant rules verified successfully." -ForegroundColor Green
    exit 0
}
```

---

### Example C: Fast Node.js / JavaScript Scriptable Checker (`check-rules.js`)

**Philosophy:** A zero-dependency single JS file runnable with `node check-rules.js` or `bun check-rules.js`.

```javascript
// scripts/check-rules.js
const fs = require('fs');
const path = require('path');

const BANNED_PATTERNS = [
  { id: 'IMPORT-001', regex: /from\s+['"]\.\.\/\.\.\/Infrastructure/g, msg: 'Features must not directly import Infrastructure.' },
  { id: 'LOG-001', regex: /console\.(log|warn)\(/g, msg: 'Use structured logger instead of raw console output.' }
];

function scanDir(dir) {
  let violations = [];
  for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name);
    if (entry.isDirectory() && entry.name !== 'node_modules' && entry.name !== 'dist') {
      violations = violations.concat(scanDir(full));
    } else if (entry.isFile() && (entry.name.endsWith('.ts') || entry.name.endsWith('.js'))) {
      const content = fs.readFileSync(full, 'utf8');
      const lines = content.split('\n');
      lines.forEach((line, i) => {
        if (line.trim().startsWith('//')) return;
        for (const rule of BANNED_PATTERNS) {
          if (rule.regex.test(line)) {
            violations.push(`[${rule.id}] ${full}:${i + 1} - ${rule.msg}`);
          }
        }
      });
    }
  }
  return violations;
}

const errors = scanDir('./src');
if (errors.length) {
  console.error(`FAILED: ${errors.length} violations found.\n` + errors.join('\n'));
  process.exit(1);
} else {
  console.log('PASSED: Fast rule checks verified.');
}
```

---

## 2. Escalating to AST (Only When Strictly Necessary)

When rules require evaluating nested token hierarchies, resolving inferred types, or differentiating complex multi-line blocks where regex produces unacceptable false positives, escalate to AST.

### Example A: C# Roslyn Analyzer (For Complex Semantic Scopes)

```csharp
// ErrorHandlingAudit/DomainFactoryVisitor.cs
using System;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public sealed class DomainFactoryVisitor : CSharpSyntaxWalker
{
    private readonly string _filePath;

    public DomainFactoryVisitor(string filePath)
    {
        _filePath = filePath;
    }

    public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        string typeName = node.Type.ToString();

        // Anti-pattern: Bypassing domain factory
        if (typeName is "DomainError" or "ErrorResponse" or "AppException")
        {
            var lineSpan = node.GetLocation().GetLineSpan();
            Console.WriteLine($"[VIOLATION: ERR-001] {_filePath}:{lineSpan.StartLinePosition.Line + 1} " +
                              $"Direct instantiation of '{typeName}' is banned. Use 'ErrorFactory' instead.");
        }

        base.VisitObjectCreationExpression(node);
    }
}
```

---

### Example B: Python (`ast` Module) for Nested Exception Blocks

```python
# tools/audit_swallowed_exceptions.py
import ast
import sys
from pathlib import Path

class SwallowedExceptionVisitor(ast.NodeVisitor):
    def __init__(self, filename: str):
        self.filename = filename

    def visit_ExceptHandler(self, node: ast.ExceptHandler):
        # Anti-pattern: bare except or body with only 'pass' or ellipsis '...'
        is_empty = False
        if len(node.body) == 1:
            first_stmt = node.body[0]
            if isinstance(first_stmt, ast.Pass):
                is_empty = True
            elif isinstance(first_stmt, ast.Expr) and isinstance(first_stmt.value, ast.Constant) and first_stmt.value.value is ...:
                is_empty = True

        if is_empty:
            name = node.name or "anonymous"
            ex_type = ast.unparse(node.type) if node.type else "Exception"
            print(f"[VIOLATION: PY-SWALLOW-001] {self.filename}:{node.lineno} "
                  f"Except block for '{ex_type}' as '{name}' silently swallows error.")
        
        self.generic_visit(node)

def audit_file(path: Path):
    tree = ast.parse(path.read_text(encoding="utf-8"), filename=str(path))
    visitor = SwallowedExceptionVisitor(str(path))
    visitor.visit(tree)
```

---

### Example C: TypeScript / JavaScript (AST / Babel Parser) — Enforcing Cancellation Propagation

```typescript
// tools/audit-cancellation-token.ts
import { parse } from "@babel/parser";
import traverse from "@babel/traverse";
import * as fs from "fs";

export function checkCancellationTokens(filePath: string) {
  const code = fs.readFileSync(filePath, "utf-8");
  const ast = parse(code, { sourceType: "module", plugins: ["typescript"] });

  traverse(ast, {
    ClassMethod(path) {
      if (path.node.async && path.node.accessibility === "public") {
        const hasSignalParam = path.node.params.some(
          (param) => param.type === "Identifier" && (param.name === "signal" || param.name === "cancellationToken")
        );

        if (!hasSignalParam) {
          const line = path.node.loc?.start.line ?? 0;
          console.error(
            `[VIOLATION: ASYNC-002] ${filePath}:${line} Public async method '${(path.node.key as any).name}' missing AbortSignal.`
          );
        }
      }
    },
  });
}
```

---

### Example D: Go (`go/parser` & `go/ast`) — Detecting Unchecked Errors

**Rule:** Calls returning `error` in Go must not assign to the blank identifier `_` unless explicitly annotated with `// nolint:errcheck`.

```go
// tools/errcheck/main.go
package main

import (
	"fmt"
	"go/ast"
	"go/parser"
	"go/token"
	"os"
)

func inspectFile(fset *token.FileSet, path string) {
	node, err := parser.ParseFile(fset, path, nil, parser.ParseComments)
	if err != nil {
		return
	}

	ast.Inspect(node, func(n ast.Node) bool {
		assign, ok := n.(*ast.AssignStmt)
		if !ok {
			return true
		}
		// Check for blank identifier assignment: _, err := ... or _ = fn()
		for _, lhs := range assign.Lhs {
			if ident, ok := lhs.(*ast.Ident); ok && ident.Name == "_" {
				pos := fset.Position(assign.Pos())
				fmt.Printf("[VIOLATION: GO-ERR-001] %s:%d Explicitly discarding error return via blank identifier.\n",
					pos.Filename, pos.Line)
			}
		}
		return true
	})
}
```

---

## 2. Structural Equivalence & Parser Resilience Harnesses

### Polyglot Test Fixture Strategy

When verifying that parsers (HTML scrapers, JSON/Protobuf decoders) do not violate structural invariants when markup or API structures mutate:

1. **`canonical_fixture.html`**: Production DOM snapshot.
2. **`renamed_markup.html`**: Classes renamed (simulating CSS obfuscation / Tailwind refactor).
3. **`adversarial_redesign.html`**: Elements re-nested in `<section>`, attributes stripped, non-essential wrappers added.

### Equivalence Test Runner (C# xUnit / Headless Example)

```csharp
// ParserTests/ListingParserEquivalenceTests.cs
using System.IO;
using Xunit;

public class ListingParserEquivalenceTests
{
    [Theory]
    [InlineData("Fixtures/project_current_markup.html")]
    [InlineData("Fixtures/project_renamed_markup.html")]
    [InlineData("Fixtures/project_adversarial_redesign.html")]
    public void Parser_Extracts_Core_Invariants_Across_All_Markup_Variations(string fixturePath)
    {
        // Arrange
        string html = File.ReadAllText(fixturePath);
        var parser = new ListingParser();

        // Act
        var result = parser.Parse(html);

        // Assert: Essential invariants must never fail regardless of markup mutation
        Assert.NotNull(result);
        Assert.NotEmpty(result.Projects);

        foreach (var project in result.Projects)
        {
            Assert.False(string.IsNullOrWhiteSpace(project.Id), "Project ID invariant violated");
            Assert.False(string.IsNullOrWhiteSpace(project.Title), "Project Title invariant violated");
            Assert.True(project.Budget > 0, "Budget calculation invariant violated");
        }
    }
}
```

---

## 3. Data & State Invariant Auditing (SQL / SQLite)

### Python SQLite State Auditor

Audits SQLite databases to ensure business rules and relational invariants have not been violated by concurrent workers:

```python
# tools/db_audit.py
import sqlite3
import sys

def audit_database(db_path: str):
    conn = sqlite3.connect(db_path)
    cursor = conn.cursor()
    violations = []

    # Invariant 1: No projects with negative or zero budget
    cursor.execute("SELECT id, budget FROM Projects WHERE budget <= 0;")
    bad_budgets = cursor.fetchall()
    if bad_budgets:
        violations.append(f"Found {len(bad_budgets)} projects with invalid non-positive budgets.")

    # Invariant 2: No orphan notifications referencing non-existent project IDs
    cursor.execute("""
        SELECT n.id, n.project_id 
        FROM Notifications n 
        LEFT JOIN Projects p ON n.project_id = p.id 
        WHERE p.id IS NULL;
    """)
    orphans = cursor.fetchall()
    if orphans:
        violations.append(f"Found {len(orphans)} orphan notifications referencing missing projects.")

    conn.close()

    if violations:
        print(f"[FAILED] {len(violations)} Data Invariant Breaches:")
        for v in violations:
            print(f"  - {v}")
        sys.exit(1)
    else:
        print("[PASSED] All database state invariants verified.")

if __name__ == "__main__":
    audit_database(sys.argv[1] if len(sys.argv) > 1 else "app.db")
```
