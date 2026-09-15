# review-validations

Run all existing validation checkers, verify that zero violations/regressions exist, and proactively expand quality coverage by adding new validation checkers where gaps exist.

---

## Purpose & Distinction from `check-violations`

| Capability | Scope & Focus | Primary Action |
|------------|---------------|----------------|
| **`check-violations` (Skill)** | Architectural audit skill providing detection patterns (lexical/regex, structural, visual, data, concurrency) and guidelines on writing checkers. | Passive / Diagnostic — scans codebase for architectural violations and reports findings. |
| **`review-validations` (Command)** | Active quality assurance workflow. Runs existing validation suites, ensures clean passes, identifies unvalidated invariants, and **implements new validation checkers** to harden the system. | Active / Generative — executes checkers, verifies health, and writes/adds new checkers to eliminate blind spots. |

---

## User Invocation

```text
/review-validations [optional-scope-or-target] [optional flags]
```

### Examples
- `/review-validations` — Full project validation review (runs all existing checkers, audits coverage, suggests/adds missing checkers).
- `/review-validations Features/Projects` — Scoped validation review focused on a specific feature slice or subsystem.
- `/review-validations --add-checkers` — Explicit emphasis on identifying gaps and authoring new validation checkers.
- `/review-validations --run-only` — Quick verification run to assert that existing checkers pass without adding new ones.

---

## The 4-Phase Review Validations Workflow

When `/review-validations` is invoked, follow these four phases strictly:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       REVIEW-VALIDATIONS WORKFLOW                           │
├─────────────────────────────────────────────────────────────────────────────┤
│ Phase 1: Discovery & Inventory                                              │
│          - Discover existing validation scripts, test harnesses, and checks │
│          - Detect project context (CLI, backend, GUI, parser, db, daemons)   │
├─────────────────────────────────────────────────────────────────────────────┤
│ Phase 2: Execution & Zero-Violation Verification                            │
│          - Execute existing validation checkers across relevant partitions  │
│          - Ensure zero failures, crashes, or unhandled violations           │
│          - If violations exist: report or resolve before expanding checkers │
├─────────────────────────────────────────────────────────────────────────────┤
│ Phase 3: Validation Coverage & Gap Analysis                                 │
│          - Audit system invariants against existing checker coverage        │
│          - Identify unvalidated architectural contracts, boundaries & drift │
├─────────────────────────────────────────────────────────────────────────────┤
│ Phase 4: Expansion & Authoring (Adding More Checkers)                       │
│          - Author new lightweight, scriptable validation checkers            │
│          - Prioritize fast regex/pattern scripts; use AST/harness only if    │
│            strictly necessary                                               │
│          - Verify new checkers pass on current code and detect regressions  │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Phase 1: Discovery & Inventory

1. **Locate Existing Validation Tools:**
   - Scan standard audit and test directories (e.g., `tools/`, `scripts/`, `tests/`, `spec/`).
   - Identify existing checking mechanisms:
     - Regex/pattern checkers (`*.py`, `*.js`, `*.ps1`, `*.sh`).
     - Headless parser/contract equivalence runners.
     - Database integrity audit scripts.
     - Visual parity/asset checks (if GUI/frontend).
     - Roslyn/AST analyzers or linters.
2. **Context Determination (Context-First):**
   - Identify the nature of the application:
     - *GUI/Desktop/Mobile:* Visual parity, asset integrity, and UI threading checkers apply.
     - *CLI/Daemons:* Exit code contracts, cancellation tokens, signal handling checkers apply.
     - *Parsers/Scrapers:* Structural drift, adversarial markup/payload fixtures apply.
     - *Data Stores/Databases:* Foreign key, orphan record, and migration invariant checkers apply.
   - Do **not** run or author visual/UI checkers if the project is headless or a backend service.

---

## Phase 2: Execution & Verification (Zero Regressions)

1. **Run Active Checkers:**
   - Execute identified checkers in order of speed (fast regex/scripts first, heavy test suites/visual diffs last).
   - Use non-interactive, single-command execution per repository guidelines (e.g., `cmd /c` on Windows if PowerShell hangs).
2. **Evaluate Output:**
   - Verify that all existing checkers exit with code 0 / clean status.
   - If any violation is found:
     - Log the violation clearly with file path, line number, and rule violation.
     - Address or report the issue so the baseline is clean before adding new checkers.

---

## Phase 3: Coverage & Gap Analysis

Examine recently modified or core architectural components against the verification partitions defined in the `check-violations` skill:

1. **Partition A (Lexical / Code Contracts):**
   - Are there forbidden APIs, raw instantiations bypassing domain factories, unindexed error codes, or swallowed exceptions not covered by a regex rule?
2. **Partition B (Structural / Input Drift):**
   - If parsers/serializers exist, do fixtures test for renamed fields, injected wrapper tags, or missing optional values?
3. **Partition C (Visual & Assets — UI Only):**
   - If UI exists, are newly added icons, typography styles, or layout tokens verified against design specifications?
4. **Partition D (Data & State Invariants):**
   - If SQLite/databases are used, are foreign key constraints, orphan data, and state transitions checked via query scripts?
5. **Partition E (Concurrency & Daemons):**
   - Are background workers, thread pools, or cancellation tokens probed for cooperative shutdown?

---

## Phase 4: Expansion & Authoring (Adding More Checkers)

To ensure enduring quality, add new validation checkers to eliminate discovered gaps:

1. **Language & Scriptability Selection:**
   - Choose the language that is most scriptable and natural for the task:
     - **Python (`.py`)**: Directory scans, text regex, SQLite audits, image comparison.
     - **Node.js / TypeScript (`.js` / `.ts`)**: Fast schema validation and JS/TS repository checks.
     - **Single-File C# via PowerShell (`.ps1`)**: C# type checks or .NET-specific rules without full compilation.
     - **Shell/Bash**: Fast file presence, git hooks, or pipeline validation.
2. **Lightweight Pattern First:**
   - Write deterministic, fast regex scanners.
   - Avoid heavy AST parsing unless complex syntactic nesting or semantic type resolution makes regex insufficient.
3. **Harness Integration:**
   - Place the new checker in `tools/` or `scripts/`.
   - Provide a clean CLI exit code (0 for pass, non-zero for failure).
   - Add documentation or entry to test runners if applicable.
4. **Verification of the New Checker:**
   - Run the new checker against the current codebase: verify it passes (no false positives).
   - Test against a known violation/synthetic mutation: verify it catches the failure (no false negatives).

---

## Report Summary Template

After executing `/review-validations`, output a structured summary:

```markdown
### Review Validations Summary

#### 1. Executed Existing Checkers
- `tools/audit_rules.py`: PASSED (0 violations across 142 files)
- `tools/ParserTests`: PASSED (all golden & adversarial fixtures verified)
- `tools/db_audit.py`: PASSED (0 orphan records, constraints valid)

#### 2. System Health Status
- **Baseline:** Healthy. Zero regressions or active invariant violations detected.

#### 3. Validation Gap Analysis
- Identified missing check: <Description of uncovered invariant or boundary>

#### 4. New Checkers Added
- **Checker:** `tools/check_<new_rule>.py` (or `.ps1`)
  - **Partition:** Partition A (Lexical & Pattern Inspection)
  - **Target:** Enforces <Rule/Invariant description>
  - **Verification:** Verified passing on current tree; validated against synthetic failure.
```
