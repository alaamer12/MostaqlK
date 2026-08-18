---
name: up-agents
description: >-
  Launch staggered parallel worker subagents plus a delayed reviewer agent.
  Use when the user says up-agents, up agents, up-agents N + 1, spawn workers,
  worker pool with reviewer, or wants parallel implementation with a final QA pass.
---

# up-agents — Staggered Worker Pool + Reviewer

Orchestrate **N parallel worker subagents** with **staggered start delays**, then **1 reviewer subagent** that wakes last to build, test, and review.

Distinct from `/pool-agents` (branching fan-out). `up-agents` is a **linear pipeline**: workers own non-overlapping scopes; the reviewer integrates at the end.

---

## Shorthand grammar

```
up-agents                    → 4 workers + 1 reviewer (defaults)
up-agents 4 + 1              → 4 workers + 1 reviewer (explicit)
up-agents 3 + 1              → 3 workers + 1 reviewer
up-agents 4 + 1 @800         → reviewer sleeps 800s (default)
up-agents 4 + 1 stagger@60   → base stagger 60s between tiers (default)
```

| Token | Meaning |
|-------|---------|
| `N` | Worker count (default **4**) |
| `+ 1` | Always one **reviewer** agent at the end (required notation) |
| `@R` | Reviewer delay in seconds (default **800**) |
| `stagger@S` | Base stagger unit in seconds (default **60**) |
| trailing text | The task / plan to execute (e.g. "implement plan.md") |

**Parse examples:**

| User says | Workers | Reviewer delay |
|-----------|---------|----------------|
| `up-agents` | 4 | 800s |
| `up-agents 4 + 1` | 4 | 800s |
| `up-agents 3 + 1 @600` | 3 | 600s |
| `up-agents 4 + 1 stagger@90 implement plan.md` | 4 | 800s, stagger base 90s |

---

## Roles

| Role | Index | Sleep before work | Responsibility |
|------|-------|-------------------|----------------|
| **Orchestrator** | you | 0 | Split work, launch all agents in one message, merge reviewer report |
| **Worker 1..N** | 1..N | computed (see below) | Implement scoped slice; no cross-scope edits |
| **Reviewer** | N+1 | `@R` (default 800s) | `bun install`, test all packages, E2E, grep audits, fix integration gaps only |

Workers **must not** do reviewer work. Reviewer **must not** rewrite architecture — only wiring, tests, and DoD gaps.

---

## Sleep schedule (mandatory first action per agent)

Compute sleep seconds before each worker starts:

```
sleep(1) = 0
sleep(i) = stagger * (2^(i-1) - 1)     for 1 < i < N
sleep(N) = floor(reviewerDelay * 0.6)    last worker (buffer before reviewer)
sleep(reviewer) = reviewerDelay
```

Defaults: `stagger = 60`, `reviewerDelay = 800`.

**N = 4 (default):**

| Agent | Sleep | First action |
|-------|-------|--------------|
| Worker 1 | 0s | start immediately |
| Worker 2 | 60s | `timeout /t 60 /nobreak` |
| Worker 3 | 180s | `timeout /t 180 /nobreak` |
| Worker 4 | 480s | `timeout /t 480 /nobreak` |
| Reviewer | 800s | `timeout /t 800 /nobreak` |

On non-Windows: `sleep 60` (bash). Prefer CMD on this repo per `AGENTS.md`.

**Rule:** worker must run the sleep command and **must not write files** until sleep completes.

---

## Execution algorithm (orchestrator)

1. **Read** this skill and the task source (`plan.md`, user message, etc.).
2. **Split** work into **N non-overlapping scopes** (file ownership table — no two workers edit the same path).
3. **Assign** dependencies: later workers may read earlier packages; if not ready, stub imports + `// TODO(Wn)`.
4. **Launch all N+1 agents in one message** (multiple `Task` tool calls, `subagent_type: generalPurpose` for writes).
5. Each prompt **starts with** the mandatory block from `AGENTS.md` (subagent delegation section).
6. **Do not** implement worker slices yourself unless a worker fails — then re-run failed scope only.
7. When reviewer returns, **merge** its report into your response to the user.

---

## Worker prompt template

```
=== MANDATORY AGENT PREFERENCES (read and follow) ===
[paste full block from AGENTS.md § Subagent delegation]
=== END PREFERENCES ===

UP-AGENTS ROLE: Worker {i}/{N}
SLEEP: {sleepSeconds}s — FIRST ACTION: run `timeout /t {sleepSeconds} /nobreak` (skip if 0). Do not write files before sleep completes.

TASK SOURCE: {plan.md path or summary}

YOUR SCOPE ONLY (do not edit paths outside this list):
{scoped paths and deliverables}

DEPENDS ON: {prior workers / packages}

DO NOT TOUCH: {explicit forbidden paths}

DONE WHEN:
{checklist}

RETURN FORMAT:
- Summary (3–5 bullets)
- Files created/modified
- Test commands run + results
- Blockers (TODO markers left for reviewer)
```

---

## Reviewer prompt template

```
=== MANDATORY AGENT PREFERENCES (read and follow) ===
[paste full block from AGENTS.md]
=== END PREFERENCES ===

UP-AGENTS ROLE: Reviewer ({N+1}/{N+1})
SLEEP: {reviewerDelay}s — FIRST ACTION: run `timeout /t {reviewerDelay} /nobreak`. Do not run checks before sleep completes.

READ: {DoD source — plan.md §12, or user checklist}

After sleep:
1. bun install at repo root
2. bun test in each package / scope workers created
3. E2E / smoke commands from plan
4. Grep audits (forbidden deps, stale folders, hardcoded markers)
5. Fix ONLY integration gaps blocking DoD — no architecture rewrites

RETURN Review Report:
## Build status: PASS | FAIL
## Tests: X passed, Y failed
## Definition of Done: N/M
## Fixes applied: [list]
## Blockers for human: [list]
## Recommended follow-ups: [list]
```

---

## File ownership rules

- One worker **owns** each path; others are **read-only**.
- Only **Worker 1** creates root `package.json` / workspace config unless plan says otherwise.
- Only **Reviewer** patches cross-package wiring and root scripts after all workers finish.
- Deleting legacy folders (e.g. `toolkit/`) is **last worker** scope, never Worker 1 or 2.

---

## Choosing N

| N | When |
|---|------|
| **4** (default) | Full-stack split: foundation → utils → core → cli/migration |
| **3** | Smaller task: helpers → core → cli |
| **2** | Narrow refactor: implement → review-heavy |
| **5+** | Split one layer further — add reference in [reference.md](reference.md) |

For chekr-specific 4-worker split, see [reference.md](reference.md).

---

## vs `/pool-agents`

| | `up-agents` | `/pool-agents` |
|---|-------------|----------------|
| Shape | N workers + 1 reviewer, linear | `x->2x->4x` branching tree |
| Timing | Staggered sleeps | All depth-1 start together |
| End | Reviewer at `@R` | Root merges branch results |
| Best for | Implementation plans with ordered deps | Exploration, audits, parallel research |

---

## Orchestrator checklist

```
- [ ] Parsed N, reviewer @R, stagger @S from user message
- [ ] Read task plan / DoD
- [ ] Built file-ownership table for N workers
- [ ] Computed sleep table for workers 1..N and reviewer
- [ ] Launched N+1 Task agents in one message
- [ ] Merged reviewer Report into user response
```

---

## Additional resources

- Chekr 4-worker assignment: [reference.md](reference.md)
- Slash command entry: `.cursor/commands/up-agents.md`
- Agent preferences: `AGENTS.md`
- Branching pools: `.cursor/commands/pool-agents.md`
