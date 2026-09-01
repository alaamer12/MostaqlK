# treasure-findings Reference & Practical Guide

## Practical Walkthrough: The Codebase Violation Hunt

This walkthrough demonstrates how the `treasure-findings` pattern was executed to discover layer-mixing and Single-Ground violations across MostaqlK.

---

### Step 1: Defining 2 Orthogonal Routes

| Scout Role | Route Archetype | Methodology | Target Discoveries |
|---|---|---|---|
| **`script-violation-scanner`** | **Automated Scanner & AST Heuristics** | Writes a specialized Python AST/regex scanner in `scratch/scan_violations.py` to scan all 211 codebase files for `.Substring()`, `.Trim()`, `Color.FromArgb("#...")`, math casts, and status string literals. Directly inspects candidate matches to dismiss false positives. | Exact line numbers, high-volume string slicing, regex mismatches, literal token bypasses. |
| **`codebase-deep-swipe-auditor`** | **Manual Architectural Swipe** | Manually traces data flow from `Infrastructure/Http/Parsers` &rarr; `Infrastructure/Database` &rarr; `Core/Domain` &rarr; `Features/*/ViewModels` &rarr; `Layouts/*.xaml`. Inspects inverted dependencies (`Core` importing `Infrastructure`), SQLite temporal string persistence (`publish_time_text`), and cross-platform UI parity gaps. | Inverted architecture boundaries, database write locks, N+1 query patterns, silent XAML binding failures. |

---

### Step 2: Isolated Dispatch Prompts

#### Scout 1 Dispatch (Automated Scanner):
```markdown
=== MANDATORY AGENT PREFERENCES (read and follow) ===
1. CONTEXT: Read .repertoire/.steering/base/ & docs/single-ground-architecture-blueprint.md & bugfree.txt.
2. SCOPE: Automated Scanner & AST Heuristics.
3. INSTRUCTIONS:
   - Develop scratch/scan_violations.py to scan all C# and XAML files for ad-hoc string slicing, hardcoded colors in ViewModels, raw DTO bindings, and status switches.
   - Inspect all candidate hits directly in source files to reduce false positives.
   - Deliver findings report to `.repertoire/agents/script-violation-scanner.md`.
=== END PREFERENCES ===
```

#### Scout 2 Dispatch (Manual Deep Swipe):
```markdown
=== MANDATORY AGENT PREFERENCES (read and follow) ===
1. CONTEXT: Read .repertoire/.steering/base/ & docs/single-ground-architecture-blueprint.md & bugfree.txt.
2. SCOPE: Manual Architectural & Data Lifecycle Swipe.
3. INSTRUCTIONS:
   - Perform a manual layer-by-layer audit across Models, Infrastructure, Core, Services, and Features.
   - Trace data flow, database storage lifecycle (temporal string persistence), inverted dependencies, and cross-platform parity gaps.
   - Deliver findings report to `.repertoire/agents/codebase-deep-swipe-auditor.md`.
=== END PREFERENCES ===
```

---

### Step 3: Triangulation & Anomaly Matrix

| Finding | Found by Scanner | Found by Deep Swipe | Classification | Master Decision |
|---|:---:|:---:|---|---|
| **Arabic "ال" Definite Article Stripping** (`ProjectCardViewModel`) | Yes | Yes | **Consensus (Slam-Dunk)** | Critical UI Logic Bleed &rarr; Extract to `Core/Formatting/ArabicNameFormatter.cs`. |
| **SQLite `publish_time_text` Temporal Rewriting** (`PublishedTimeUpdateService`) | No (Semantic / Service logic) | Yes | **Solitary Treasure (Architectural)** | Deep Storage Contamination &rarr; Drop presentation columns, delete background service, compute dynamic relative time in VM. |
| **Duplicated Status Hex Colors** (`#ECFDF5`, `#FEF2F2`) | Yes | Yes | **Consensus** | Duplicated Micro-Decisions &rarr; Centralize in `UI/DesignSystem/Badges/EnrichmentBadgeStyle.cs`. |
| **Inverted Dependency: `Core/` &rarr; `Infrastructure/`** | No | Yes | **Solitary Treasure (Architectural)** | Clean Architecture Breach &rarr; Move string normalization to `Core/Utilities/StringNormalization.cs`. |
| **Silent Binding Failure** (`{Binding PostedRelative}`) | No | Yes | **Solitary Treasure (XAML)** | Silent Runtime Defect &rarr; Wrap items in `NotificationItemViewModel`. |
| **Notification Truncation Duplication** (`.Substring(0, 197)`) | Yes | No | **Solitary Treasure (Scanner)** | Code Duplication &rarr; Extract to `Core/Formatting/TextTruncator.cs`. |

---

### Step 4: Synthesized Master Output Template

```markdown
# Comprehensive Architectural & Single-Ground Findings

## 1. Executive Summary
- Total Candidate Sites Scanned: 148
- Confirmed Distinct Violations: 14
- High-Impact Architectural Breaches: 5

## 2. Methodology & Orthogonal Coverage
- Route 1 (Automated AST/Regex Scanner): 100% coverage across 211 files for syntax patterns.
- Route 2 (Deep Architectural Swipe): Comprehensive lifecycle tracing across 6 core architectural layers.

## 3. Consolidated Findings Matrix
[Itemized list with Severity, File:Line, Root Cause, and Single-Ground Remedy]

## 4. Phased Remediation Roadmap
- Phase 1: Core Foundation & Inverted Dependency Cleanup
- Phase 2: SQLite Schema & Storage Layer Purification
- Phase 3: ViewModel & Presentation Layer Modernization
- Phase 4: Layout & Cross-Platform Parity
- Phase 5: Verification & Zero-Regression Desktop Build
```
