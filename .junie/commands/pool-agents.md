# Agent pool (fan-out delegation)

Orchestrate parallel subagents with a branching depth pattern, then execute the user's task.

## User input

The user invoked `/pool-agents` with optional notation and a task:

```
/pool-agents x->2x->4x explore the codebase and document API patterns
```

**Notation** (first token matching `x(->\d*x)+`): defines the pool shape.  
**Task** (everything after the notation): what the pool should accomplish.

If no notation is given, default to **`x->2x->4x`**.

---

## Notation grammar

Pattern: `x->N1x->N2x->N3x->...`

| Token | Meaning |
|-------|---------|
| `x` | Root orchestrator (you). You delegate; you do not do leaf work yourself unless merging results. |
| `2x` | **2** parallel agents at **depth 1** |
| `4x` | **4** parallel agents at **depth 2** (each depth-1 agent spawns **2** children) |
| `8x` | **8** parallel agents at **depth 3** (each depth-2 agent spawns **2** children) |

**Branching factor** between levels = `count[n+1] / count[n]`.

Examples:

| Notation | Depth-1 | Depth-2 | Depth-3 | Total leaf agents |
|----------|---------|---------|---------|-------------------|
| `x->2x` | 2 | — | — | 2 |
| `x->2x->4x` | 2 | 4 | — | 4 |
| `x->3x->9x` | 3 | 9 | — | 9 |
| `x->2x->4x->8x` | 2 | 4 | 8 | 8 |

**Max depth:** 4 worker levels (ignore tokens beyond that). **Never** fan out infinitely.

---

## Execution algorithm

1. **Parse** the notation and extract the task description.
2. **Split the task** into non-overlapping subtasks — one per leaf agent (or per branch if work is naturally hierarchical).
3. **Depth 1:** Launch all depth-1 subagents **in parallel** (one message, multiple Task calls). Each prompt MUST start with the mandatory preferences block from `AGENTS.md`.
4. **Depth 2+:** Each depth-1 subagent prompt MUST include:
   - Its subtask scope
   - Instruction to spawn its children in parallel (same branching factor)
   - The same preferences header block
   - What each child should return
5. **You (root)** wait for depth-1 results, then **merge** into a single coherent response for the user.
6. If a subagent fails, report which branch failed; do not silently drop it.

---

## Subagent prompt template (include in every delegation)

```
=== MANDATORY AGENT PREFERENCES (read and follow) ===
[paste full block from AGENTS.md]
=== END PREFERENCES ===

POOL ROLE: Depth {N}, branch {B} of {total}
NOTATION: {full notation e.g. x->2x->4x}

YOUR SUBTASK:
{specific scoped task}

{if depth < max depth:}
DELEGATION: You MUST spawn {branching_factor} sub-sub-agents in parallel to handle:
- {child subtask 1}
- {child subtask 2}
Each child prompt must include the preferences block above.
Merge child results before returning to parent.

RETURN FORMAT:
- Summary (3–5 bullets)
- Findings / deliverables
- Blockers or uncertainties
```

---

## Rules

- Launch parallel agents in **one message** when possible (multiple Task tool calls).
- Prefer `subagent_type: explore` for read-only research, `generalPurpose` for writes.
- Do **not** create `.cursor/agents/` files unless the user explicitly asks — this command orchestrates existing Task subagents, not permanent subagent definitions.
- Respect `AGENTS.md` shell, bun, configenv, and debugging preferences in every delegation.

---

## Examples

**User:** `/pool-agents x->2x->4x map frontend-backend communication and find mixed concerns`

**You do:**
1. Split into 2 depth-1 branches: (A) communication architecture, (B) mixed-concerns audit
2. Launch 2 parallel Task agents for depth 1
3. Each depth-1 agent spawns 2 depth-2 children (4 total leaves)
4. Merge all results into one report

**User:** `/pool-agents x->3x refactor hooks in apps/web/hooks`

**You do:**
1. Split hooks folder into 3 equal partitions
2. Launch 3 parallel Task agents (no further fan-out)
3. Merge refactor findings
