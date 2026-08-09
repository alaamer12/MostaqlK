# Agent Instructions

Preferences for AI agents working in this repository. Follow these on every task, including when delegating to subagents.

---

## Mandatory context (read first, every conversation)

**At the start of every conversation — regardless of the task — read these files and keep them in mind for all decisions:**

| File | Purpose |
|------|---------|
| [`.repertoire/.steering/base/structure.md`](.repertoire/.steering/base/structure.md) | Directory structure and high-level boundaries across the app. |
| [`.repertoire/.steering/base/product/README.md`](.repertoire/.steering/base/product/README.md) | Base Product docs index — unchanging architecture, data model, and overall design concepts. |
| [`.repertoire/.steering/base/tech/README.md`](.repertoire/.steering/base/tech/README.md) | Base Tech docs index — unchanging conventions for C#, SQL, errors, and UI components. |
| [`.repertoire/.steering/v1/product/README.md`](.repertoire/.steering/v1/product/README.md) | V1 Product docs index — current V1 scope, UX, and configuration. |
| [`.repertoire/.steering/v1/tech/README.md`](.repertoire/.steering/v1/tech/README.md) | V1 Tech docs index — diff-engine, concurrency, workers, and V1-specific tech. |

> **IMPORTANT VERSION CONTEXT:** The current version is **V1**. You MUST read and adhere to `base/` and `v1/` documentation. You MUST IGNORE any documentation inside the `v2/` folder as it is for future features only.

These are the source of truth for project context. Do not contradict them without verifying the codebase first. Each README is a wiki-style index — follow its links to the specific doc that answers your question rather than assuming the README alone is enough.

### Design references

| Location | Purpose |
|----------|---------|
| [`.repertoire/design/mvp/`](.repertoire/design/mvp/) | Current visual design (HTML mockups, e.g. `projects.html`, `project-details.html`) for what is actually shipping in the MVP |
| `.repertoire/design/post-mvp/` | Visual design for anything beyond MVP (v2/v3 features). Use this location for post-MVP design work; do not mix post-MVP mockups into `design/mvp/` |

When implementing or reviewing UI, always check whether the relevant mockup lives under `design/mvp` (in scope now) or belongs under `design/post-mvp` (future scope) before treating it as current spec.

---

## Mandatory skill usage

**Applies to:** main agent, subagents, delegated tasks — any work that changes code, runs audits, or executes a workflow.

**Does NOT apply to:** pure questions, explanations, or **Ask mode** (read-only).

Before starting work:

1. **Use at least ONE skill** from [`.cursor/skills/`](.cursor/skills/).
2. **Read that skill's `SKILL.md` in full** — follow its workflow, not memory.
3. **If unsure which skill fits** → read [`.cursor/skills/using-agent-skills/SKILL.md`](.cursor/skills/using-agent-skills/SKILL.md) first, pick from the catalog, then read the chosen skill.
4. **State which skill(s) you used** when reporting results.

| Task hint | Skill |
|-----------|-------|
| Pick a skill | `using-agent-skills` |
| Bug hunt / `/bug-hunter` | `bug-hunting-skill` |
| Code review | `code-review-and-quality` |
| API / tRPC design | `api-and-interface-design` |
| Web UI work | `frontend-ui-engineering` (+ `frontend-design` for visuals) |
| React Native / Expo | `react-native-best-practices`, `building-native-ui`, `expo-ui` |
| Junie CLI / Orchestration | `unit-agent` (orchestrate CLI subtasks) |
| Parallel Workers / Review | `up-agents` (staggered worker pool + reviewer) |
| Multi-agent research | `pool-agents` (via command or notation) |
| PowerShell / Scripting | `powershell-master` |
| CLI execution / Repo checks | `terminal-ops` |

**Usage Note:** Choose skills wisely based on the complexity and scope of the task. For sequential or CLI-heavy tasks, prefer `unit-agent`. For massive parallel implementations, use `up-agents`. Use `pool-agents` for exploration and audits. Use `powershell-master` for Windows/cross-platform scripting and `terminal-ops` for verified repository operations.

Full catalog: `.cursor/skills/using-agent-skills/SKILL.md`

---

## Shell & commands

- **Prefer CMD over PowerShell.** PowerShell commands often hang or behave poorly in this environment. Use `cmd /c "..."` for simple one-off commands when needed.
- **Prefer one command at a time.** Do not chain with `&&`, `||`, or pipes unless unavoidable.
- **Complex or multi-step commands:** write a script under [`scratch/`](scratch/), run it, then **delete it when finished**.
- **Package manager:** always use **bun** and **bunx** (not npm, bunx, or yarn) unless there is a strong reason not to.

---

## Environment & configuration

- **Do not edit `.env` files directly.**
- Add or change env vars through:
  - **T3 env** setup in the relevant app/package
  - **configenv** in [`packages/shared/src/configenv/`](packages/shared/src/configenv/) (schemas, loaders, per-app configs)

See [`packages/shared/src/configenv/README.md`](packages/shared/src/configenv/README.md) for the pattern.

---

## Shared blocks & filters

Use these from `@kit/shared` for conditional rendering and filter combinators:

| Import | Use for |
|--------|---------|
| `@kit/shared/blocks` | Conditional/list rendering — `If`, `Maybe`, `For`, `Repeat`, `Compose` |
| `@kit/shared/filters` | Filter combinators — `all_`, `any_`, `partition`, etc. (barrel import only) |

**Filters:** always import from `@kit/shared/filters` — no sub-path imports (`/combinators`, `/utils`, `/types`).

---

## Debugging

- **Try multiple approaches.** If the code looks correct, do not stop at one failed attempt.
- **Escalate systematically:**
  1. Write a **debug SQL file** in `scratch/` and query the database directly to confirm data exists.
  2. If the DB returns expected data, write a **debug script** that calls the existing **service layer** to see if the bug is in the service (hotspot) vs UI/routing.
  3. Keep **hypothesizing and narrowing** until the root cause is found.
- **Scratch debug files:** read configuration via **`process.env`** (never hardcode secrets or connection strings).
- **Clean up:** remove scratch debug files when done.

---

## Agent pool notation (fan-out delegation)

When the user says **"pool of agents"** or uses notation like **`x->2x->4x`**, run a branching delegation tree — do not ask them to re-explain.

### Shorthand

| User says | Meaning |
|-----------|---------|
| `pool of agents x->2x->4x` | 2 parallel subagents; each spawns 2 more (= 4 leaves) |
| `pool of agents x->3x` | 3 parallel subagents, no further fan-out |
| `pool of agents x->2x->4x->8x` | 2 → 4 → 8 over three worker depths |
| `/pool-agents x->2x->4x {task}` | Same as above via slash command (see `.cursor/commands/pool-agents.md`) |

### Grammar

`x->N1x->N2x->...` — token after `->` is the **agent count at that depth**:

- `x` = you (orchestrator; merge results, do not do all leaf work yourself)
- `2x` = 2 agents at depth 1
- `4x` = 4 agents at depth 2 (each depth-1 agent spawned 2 children)

Branching factor = `count[n+1] / count[n]`. Max **4 worker depths**. Never infinite.

### How to execute

1. Parse notation + task
2. Split task into scoped subtasks (one per leaf)
3. Launch depth-1 subagents **in parallel** (multiple Task calls in one message)
4. Each subagent that has a deeper level MUST spawn its children in parallel and merge before returning
5. Root merges all branch results into one response

Full algorithm: [`.cursor/commands/pool-agents.md`](.cursor/commands/pool-agents.md)

---

## Subagent delegation

When spawning a subagent (Task tool or any delegated work), the **parent (master) agent** is responsible for the following, before and after the subagent runs:

1. **Naming.** The master agent assigns every subagent a name based on its role/responsibility (e.g. `pipeline-diff-reviewer`, `ui-navigation-implementer`), not a generic label. This name is used consistently for the handle, the report file, and any status updates about that subagent.
2. **Full context up front.** Subagents cannot ask follow-up questions and cannot see the master's conversation. The master MUST give the subagent complete, self-contained details in the task prompt: the exact goal, the relevant files/paths already identified, any decisions already made (e.g. naming conventions, chosen approach), and the exact output expected back. Do not assume the subagent will infer missing context.
3. **Mandatory doc list.** The master MUST explicitly list, inside the subagent's prompt, every steering doc the subagent needs for its specific task (not just the general list below) — e.g. if the task touches the pipeline, name `worker-pool-and-rate-limiter.md` and `diff-engine.md` explicitly; if it touches UI, name `cross-platform-ui-conventions.md` and the relevant `design/mvp/` mockup. The subagent must read and adhere to these docs perfectly, not just the mandatory-context block below.
4. **Report file on completion.** When a subagent finishes its work, it MUST create (or the master must create on its behalf, if the subagent's tools don't allow it) a markdown report at `.repertoire/agents/<agent-name>.md`, summarizing: what it was asked to do, what it did, files touched, decisions made, and verification performed. This is in addition to (not instead of) the normal final result returned to the master.

**Paste the block below as the first thing in every subagent prompt** so the subagent inherits the baseline rules:

```
=== MANDATORY AGENT PREFERENCES (read and follow) ===

1. CONTEXT — Read at conversation start and consider for every decision:
   - Current version is **V1**. Ignore all `v2/` folder documentation.
   - .repertoire/.steering/base/product/README.md & .repertoire/.steering/base/tech/README.md
   - .repertoire/.steering/v1/product/README.md & .repertoire/.steering/v1/tech/README.md
   - Design mockups: .repertoire/design/mvp/ for in-scope MVP UI, .repertoire/design/post-mvp/ for future-scope UI
   - PLUS any task-specific docs the master agent lists below in "TASK-SPECIFIC DOCS" — read those in full and adhere to them exactly.

2. SKILLS — Use at least ONE skill from .cursor/skills/ (read its SKILL.md in full). Unsure? Start with .cursor/skills/using-agent-skills/SKILL.md. Not required for pure questions / Ask mode.

3. SHELL — Prefer CMD over PowerShell (PowerShell hangs). One command at a time; no && or || chaining unless unavoidable. Complex/pipelined commands → script in scratch/, delete when done.

4. TOOLING — Use bun and bunx (not npm/bunx/yarn).

5. ENV — Do not edit .env files directly. Update T3 env + packages/shared/src/configenv instead.

6. DEBUG — Try multiple approaches. If code looks correct: debug SQL in scratch/ → test via existing services → hypothesize until root cause. Use process.env in scratch debug files. Delete scratch files when finished.

7. REPORT — When you finish, write a summary report to .repertoire/agents/<your-agent-name>.md (goal, actions taken, files touched, decisions made, verification), in addition to your normal final result.

TASK-SPECIFIC DOCS: <master agent fills in the exact doc paths relevant to this subagent's task here>

=== END PREFERENCES — task follows below ===
```
