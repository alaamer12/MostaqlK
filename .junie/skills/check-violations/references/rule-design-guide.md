# Rule Design Guide: Authoring High-Precision Invariant Checks

Creating audit rules that produce excessive false positives leads to alert fatigue and causes developers or agents to ignore warnings. This guide outlines principles for designing rules that are deterministic, maintainable, and low-noise.

---

## 1. Principles of High-Precision, Fast Rule Design

1. **Lightweight Pattern Matching First (Regex & Text):**
   - Start with regex, string matching, or line scanning. It executes in milliseconds, requires zero complex dependencies, and solves 90% of architectural boundary and banned-pattern checks.
2. **AST as a Rare, Targeted Escalation:**
   - Only escalate to an AST parser if regex produces unacceptable false positives due to deep syntax nesting, ambiguous token contexts, or when full type resolution is strictly necessary. Do not add AST complexity prematurely.
3. **Targeted, Scriptable Implementations:**
   - Write checkers as self-contained, scriptable runners:
     - Python scripts (`.py`) for cross-platform file scans, data checks, and vision diffs (when UI exists).
     - Node/TypeScript for JS ecosystems.
     - Single-file C# via PowerShell (`.ps1`) for .NET projects when native C# types or reflection are helpful, avoiding full project builds.
     - Shell/Bash or other lightweight scripts depending on system environment.
4. **Deterministic Context Boundaries:**
   - Define exact boundaries where rules apply. For example, domain error factory rules should only apply inside `Features/` or `Domain/`, while `Infrastructure/` or low-level platform code may legitimately translate third-party SDK exceptions.
5. **Graceful Exceptions & Suppression:**
   - Always provide an intentional, auditable escape hatch (e.g., code comments like `// violation-ignore: RULE_ID [reason]` or `# noqa: RULE_ID`). If a violation must be suppressed, require a documented justification.
6. **Actionable Diagnostics:**
   - Every violation diagnostic must report:
     - **Rule ID:** Unique, stable identifier (e.g., `ERR-001`, `BOUND-004`).
     - **Location:** File path, start line, and start column.
     - **Offending Code:** Exact token or pattern text.
     - **Remediation Advice:** Clear instructions or a replacement snippet on how to comply.

---

## 2. Rule Categorization & Severity Matrix

| Severity | Definition | Examples | Required Action |
|----------|------------|----------|-----------------|
| **Critical** | Compromises data integrity, security, or causes silent unhandled failures. | Swallowed exceptions in transaction loops, SQL injection vectors, circular domain references. | Block merge; immediate remediation required. |
| **High** | Direct architectural boundary breach or contract violation. | Directly invoking `new ErrorResponse()` instead of factory, UI layer directly querying SQLite, unhandled `CancellationToken`. | Must be fixed before release/merge. |
| **Medium** | Deviation from established design patterns without breaking runtime safety. | Non-standard error code naming, missing telemetry attribute tags, sub-optimal query index usage. | Schedule for remediation or address during refactoring. |
| **Low / Info** | Stylistic inconsistency or minor asset optimization opportunity. | Icon metadata discrepancies, non-standard helper naming. | Optional / Informational. |

---

## 3. Fast Rule Engine Lifecycle (Regex vs AST Decision)

```text
Identify Invariant / Anti-Pattern
               │
               ▼
Can Regex / Line Scanning Match It Accurately?
         │                          │
        YES                         NO (e.g. deep nesting, complex types)
         ▼                          ▼
Write Fast Script Checker    Author Targeted Parser (e.g. Roslyn / ast)
(Python / JS / PS1 single-file C#)  │
         │                          │
         └─────────────┬────────────┘
                       │
                       ▼
          Filter In-Scope Files (*.cs, *.py, etc.)
                       │
                       ▼
          Scan Files & Ignore Suppressions
                       │
                       ▼
          Emit File:Line Violations & Exit Code
```

---

## 4. Avoiding Common Pitfalls

- **Do Not Over-Engineer with AST by Default:** Re-building an entire AST parser for simple token/instantiation bans adds build time and dependency headaches. Prefer regex line-scans with comment stripping.
- **Avoid Absolute File Paths:** Diagnostic outputs must emit paths relative to the repository root to ensure reproducibility across CI runners and developer machines.
- **Isolate Environment Differences:** In visual parity or screenshot checks (when auditing UI systems), ignore sub-pixel anti-aliasing variations by applying perceptual hash tolerances or SSIM thresholds rather than strict byte equality.
