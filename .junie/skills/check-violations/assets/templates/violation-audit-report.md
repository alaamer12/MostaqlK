# Violation Audit Report: <Target Subsystem / Scope>

**Date:** <YYYY-MM-DD>  
**Auditor / Agent:** <Agent Name / Model>  
**Target Scope:** `<Target Path or Module>`  
**Audit Partitions Invoked:** <Partition A: Lexical/Regex | Partition B: Structural/Drift | Partition C: Visual (UI only) | Partition D: Data/State | Partition E: Runtime/Concurrency>
**Status:** <PASSED | FAILED | WARNINGS>

---

### 1. Executive Summary

- **Total Files / Artifacts Scanned:** <Count>
- **Total Invariants Checked:** <Count>
- **Violations Identified:** <Count> (Critical: <C>, High: <H>, Medium: <M>, Low: <L>)
- **Verdict:** <Brief narrative summarizing the state of compliance and immediate remediation priority.>

---

### 2. Violations Summary Matrix

| Rule ID | Severity | File / Location | Description | Status |
|---------|----------|-----------------|-------------|--------|
| `<RULE-001>` | `Critical` | `path/to/file.ext:42` | <Short description of breach> | Open |
| `<RULE-002>` | `High` | `path/to/file.ext:88` | <Short description of breach> | Open |

---

### 3. Detailed Itemized Findings

#### Finding 1: [<RULE-ID>] <Rule Title>
- **Severity:** `<Critical | High | Medium | Low>`
- **Location:** `<file-path>:<line>:<col>`
- **Breached Invariant:** <Formal statement of the architectural invariant or contract breached.>
- **Offending Code Snippet:**
```<lang>
// Snippet of violating code
```
- **Failure Impact:** <Explain what can fail at runtime, in data integrity, or across maintenance.>
- **Remediation Diff:**
```<lang>
// Recommended compliant replacement
```

---

### 4. False-Positive Analysis & Dismissed Candidates

| Candidate Location | Rule ID | Reason for Dismissal / Valid Exception |
|--------------------|---------|----------------------------------------|
| `path/to/fixture.ext:12` | `<RULE-001>` | Legitimate test fixture exercising boundary exception handling. |

---

### 5. Recommended Next Steps

1. <Immediate action item 1>
2. <Action item 2>
3. <Re-run verification command>
