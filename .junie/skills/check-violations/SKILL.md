---
name: check-violations
description: Universal, language-agnostic agent skill to audit codebases, architectures, contracts, and visual/data invariants for rule violations using fast scriptable checkers (regex, lightweight rules, scriptable Python/JS/C#), reserving AST only when strictly necessary.
---

# check-violations — Universal Invariant & Architectural Violation Auditing

Systematically audit, detect, and remediate violations of architectural invariants, coding contracts, visual fidelities, and data schemas across any technology stack. This skill abstracts inspection mechanisms—whether static analysis, runtime verification, or visual/structural diffing—into a universal, language-agnostic workflow.

---

## 1. When to Use This Skill

Activate `check-violations` when:
- **Architectural Boundary Enforcement:** Verifying layer isolation (e.g., Domain/Core purity, MVVM/Clean Architecture boundaries, UI decoupled from persistence).
- **Contract & Error Policy Auditing:** Detecting swallowed exceptions, bypassed error factories, unhandled cancellation tokens, or untyped panics/errors.
- **Structural & Parsing Invariant Checks:** Ensuring parsers, serializers, or data ingest pipelines withstand adversarial markup, schema drift, or malformed inputs.
- **Visual & Asset Parity Auditing:** Checking UI screens against design specifications, verifying token usage, or auditing generated icon/font assets.
- **Data & State Integrity Auditing:** Validating database constraints, orphan records, state machine transition invariants, or cache coherence.
- **Pre-Merge / Pre-Commit Sanity:** Running automated audit suites before landing refactors or feature additions.

---

## 2. Core Inspection Philosophy: Fast, Scriptable & Pragmatic

Auditing tools must be **fast, lightweight, and low-friction**. Do not over-engineer checks:

1. **Language Choice follows Target & Scriptability:**
   - The checker does not need to stick to a single language. Choose the most effective, easily scriptable option for the problem (examples):
     - **Python (`.py`)**: Outstanding for quick, standalone scripts scanning directory trees with `re` and `pathlib`, SQLite audits, and computer vision.
     - **Node.js / TypeScript (`.js` / `.ts`)**: Ideal for lightweight regex checks in JavaScript/web repositories and JSON schemas.
     - **Single-File C# via PowerShell (`.ps1`)**: Ideal for .NET projects. C# can be executed as a lightweight single-file script directly inside PowerShell (using `Add-Type` or `dotnet-script`) without creating a full `.csproj` solution.
     - *Note: These are examples—any scriptable language/tooling fitting the target environment (e.g., Bash/Shell, Ruby, Go run, etc.) can be used.*
2. **Lightweight Pattern Rules First, AST Only When Necessary:**
   - **Default to Regex & Lexical Scanning:** Most architectural rules (forbidden symbols, banned instantiation, missing imports, unindexed calls) can be checked rapidly with targeted regular expressions and line scanning.
   - **AST as Last Resort:** Full AST parsing introduces significant complexity and runtime overhead. Use AST strictly when nested scope analysis, semantic type resolution, or complex syntax trees are unavoidable.

---

## 3. Context-Based Verification Partitions

Software systems vary across domains: backend APIs, CLI tools, headless daemons, libraries, and desktop/mobile applications have entirely different surfaces. **Do not assume a project has visual UI, HTML parsing, or a database.**

Instead, activate only the verification partitions that match the system's actual architecture and the context of the audit:

```
┌────────────────────────────────────────────────────────────────────────────┐
│                    CONTEXT-BASED VERIFICATION PARTITIONS                   │
├────────────────────────────────────────────────────────────────────────────┤
│ [Always Available / Core]                                                  │
│ Partition A: Lexical & Pattern Inspection (Default: Regex / Fast Rules)   │
│              - Fast single-file scripts (Python, Node.js, PowerShell / C#) │
│              - Regex / text scans for banned patterns, imports, error codes│
│              - AST / Parsers reserved ONLY when syntactic nesting requires │
├────────────────────────────────────────────────────────────────────────────┤
│ [Context-Dependent: Parsers, Scrapers, Data Pipelines & Serializers]      │
│ Partition B: Structural & Contract Equivalence Testing                     │
│              - Headless test harnesses, golden file tests, input drift     │
│              - Validates resilience against schema drift or malformed input│
├────────────────────────────────────────────────────────────────────────────┤
│ [Context-Dependent: UI, Desktop, Mobile, & Web Frontends]                  │
│ Partition C: Visual & Asset Parity Diffing (Only when UI exists)           │
│              - Scriptable image comparison (OpenCV/Pillow, perceptual diff)│
│              - Checks rendered views vs design specs, icon/token integrity │
├────────────────────────────────────────────────────────────────────────────┤
│ [Context-Dependent: State Stores, Relational DBs & Caches]                 │
│ Partition D: Data Model & State Invariant Auditing                         │
│              - Standalone SQL script runners (Python sqlite3, psql, etc.)  │
│              - Detects orphaned records, invalid states, broken constraints│
├────────────────────────────────────────────────────────────────────────────┤
│ [Context-Dependent: Async Systems, Workers, Daemons & Protocols]           │
│ Partition E: Runtime Concurrency, Protocol & Boundary Probing              │
│              - Scriptable mock probes, rate-limit exhaustion, race checks  │
│              - Probes cancellation cooperation, CLI exits, deadlock hazards│
└────────────────────────────────────────────────────────────────────────────┘
```

---

## 4. End-to-End Execution Workflow

### Phase 1: Invariant & Scope Definition
1. Identify the rule or invariant to verify (e.g., "All errors must originate from domain factories", "DOM parsers must tolerate omitted classes").
2. Determine target files, modules, or subsystems in scope.
3. Select the appropriate verification partition based strictly on the project context (e.g., skip visual diffing if the project is a CLI or backend service; skip database checks if the app is stateless).

### Phase 2: Inspection Tool Selection / Construction
- **Existing Tooling:** If the repository provides inspection scripts (e.g., scripts in `tools/`, headless test harnesses, Python audit scripts), prefer executing existing tools.
- **Fast Script Creation:** If no tool exists, author a fast, standalone, scriptable checker (e.g., a single-file Python script, a Node script, or a single-file C# runner via PowerShell). Start with simple regex/lexical scanning. Do NOT build a heavy AST solution unless regex proves insufficient due to complex syntax nesting.

### Phase 3: Execution & Evidence Collection
1. Run the audit script against the target codebase.
2. Capture concrete evidence: file paths, line numbers, matched offending lines, visual diff metrics, or offending query rows.
3. Categorize findings by severity:
   - **Critical:** Silent data corruption, swallowed exceptions, boundary security bypasses.
   - **High:** Contract violations, missing cancellation propagation, visual layout breakages.
   - **Medium:** Bypassed domain factories, deprecated API usage, unindexed foreign keys.
   - **Low / Informational:** Style drift, non-idiomatic collection returns.

### Phase 4: Triangulation & False-Positive Elimination
1. Verify that flagged violations are genuine breaches and not intentional exceptions or documented edge cases.
2. For regex/pattern matches, verify whether comments or string literals triggered a false match and refine the regex or add quick exclusions.
3. Eliminate false positives before reporting.

### Phase 5: Reporting & Remediation
1. Generate an audit report using the template at `assets/templates/violation-audit-report.md`.
2. Propose concrete remediation diffs adhering to the "Before vs After" hardening pattern.
3. Verify fixes by re-running the audit tool to confirm zero violations remain.

---

## 5. Progressive Disclosure & Supporting Resources

- **`references/invariant-detection-patterns.md`**: Architectural taxonomy of violation checking across context-based verification partitions, emphasizing fast pattern checking over AST.
- **`references/rule-design-guide.md`**: Principles for authoring fast, low-false-positive, deterministic violation rules with regex vs AST guidance.
- **`assets/templates/violation-audit-report.md`**: Standardized Markdown report template for audit findings.
- **`assets/templates/custom-rule-template.md`**: Template for specifying and documenting new architectural rules.
- **`examples/polyglot-violation-checks.md`**: Comparative code implementations demonstrating fast scriptable checkers (Python regex, Node.js text scan, single-file C# via PowerShell) and when AST is justified.
