# Architectural Rule Specification: <RULE-ID>

**Rule ID:** `<RULE-ID>` (e.g. `ERR-001`, `BOUND-002`, `PARSER-003`)  
**Title:** <Concise Title of the Rule>  
**Severity:** `<Critical | High | Medium | Low>`  
**Verification Partition:** `<Partition A: Fast Regex / Lexical Rule | Partition A: AST (Strict Necessity) | Partition B: Structural Equivalence | Partition C: Visual Parity (UI only) | Partition D: Data Invariant | Partition E: Runtime Concurrency>`
**Applicable Scope:** `<File glob or directory path, e.g. src/Features/**, *.cs>`
**Recommended Implementation:** `<Fast Python Script | Single-File C# via PowerShell | Fast Node/TS Script>`

---

## 1. Architectural Intent & Invariant Statement

### Statement of Invariant
<State clearly what must ALWAYS or NEVER happen in the codebase.>

### Architectural Rationale
<Explain why this rule exists: what failure mode, technical debt, or maintenance issue it prevents.>

---

## 2. Violation Criteria & Pattern Matching

### Banned Anti-Pattern (Violation)
<Describe the specific syntactic construct, structural behavior, or data state that constitutes a breach.>

### Compliant Pattern (Correct Approach)
<Describe how developers or agents must write compliant code.>

---

## 3. Allowed Exceptions & Suppressions

- **Allowed Contexts:** <List files or layers exempt from this rule, e.g. unit tests, mocks, or infrastructure adaptors.>
- **In-Code Suppression Syntax:** `<e.g. // violation-disable-next-line RULE-ID [reason]>`
- **Suppression Requirements:** <Every suppression must be accompanied by a documented technical justification.>

---

## 4. Detection Implementation Specification

- **Detection Approach:** `<Fast Regex / Line Scanner (Preferred) | Targeted AST Parser (Escalation)>`
- **Runner Script:** `<e.g. tools/audit_rules.py | scripts/check-invariants.ps1 | check-rules.js>`
- **Pattern / Regex / Query Criteria:**
  ```text
  <Specify regex pattern, token criteria, or AST node match criteria>
  ```
- **Exit Code Impact:** `<Non-zero (blocks build/commit) | Zero (warning only)>`
