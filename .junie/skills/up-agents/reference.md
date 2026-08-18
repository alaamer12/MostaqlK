# up-agents — Reference

## Sleep formula (all N)

```js
function workerSleepSeconds(i, N, stagger = 60, reviewerDelay = 800) {
  if (i === 1) return 0;
  if (i === N) return Math.floor(reviewerDelay * 0.6);
  return stagger * (2 ** (i - 1) - 1);
}
// reviewer always sleeps reviewerDelay
```

### Precomputed tables

**stagger@60, reviewer@800**

| N | W1 | W2 | W3 | W4 | W5 | Reviewer |
|---|----|----|----|----|-----|----------|
| 2 | 0 | 480 | — | — | — | 800 |
| 3 | 0 | 60 | 480 | — | — | 800 |
| 4 | 0 | 60 | 180 | 480 | — | 800 |
| 5 | 0 | 60 | 180 | 420 | 480 | 800 |

**stagger@60, reviewer@600**

| N | W1 | W2 | W3 | W4 | Reviewer |
|---|----|----|----|----|----------|
| 4 | 0 | 60 | 180 | 360 | 600 |

---

## chekr default split (N=4, plan.md)

Use when user says `up-agents 4 + 1` on this repo without a custom split.

| Worker | Sleep | Owns | Deliverables |
|--------|-------|------|--------------|
| **W1** | 0s | `package.json` (root), `types/`, `packages/helpers/**`, empty shells for utils/core/cli | Full `@chekr/helpers`, `chekr.config.d.ts`, workspace scaffold |
| **W2** | 60s | `packages/utils/**` | Port toolkit utils, no chalk, marker param on ignore handler |
| **W3** | 180s | `packages/core/**` | Engine, config, git module, reporter, `run()` API |
| **W4** | 480s | `packages/cli/**`, `examples/**`, `docs/CONFIG.md`, `docs/CLI.md` | CLI commands, migrate/delete `toolkit/` |
| **Reviewer** | 800s | cross-package fixes only | plan.md §12 DoD, bun test all, E2E `examples/minimal` |

### Forbidden cross-edits

| Path | Owner |
|------|-------|
| `packages/helpers/` | W1 |
| `packages/utils/` | W2 |
| `packages/core/` | W3 |
| `packages/cli/`, `examples/` | W4 |
| `toolkit/` delete | W4 only |
| `plan.md` | orchestrator / user |

### Reviewer DoD (plan.md §12)

1. `chekr.config.js` + `chekr.config.d.ts` working
2. Global + per-step config via `steps[].*`
3. `gitignore` path filters files
4. CLI overrides all config fields
5. `@chekr/helpers` parse/merge helpers exist
6. Only `simple-git` + `ignore` in core deps
7. No chalk
8. `toolkit/` gone; symphony rules in `examples/`
9. `chekr run` E2E on `examples/minimal/`

---

## Splitting arbitrary tasks into N workers

1. Identify **layers** (foundation → domain → integration → surface).
2. Assign **leaf directories** to one worker each.
3. Put **shared contracts** (types, exports) in Worker 1.
4. Put **deletion / migration** in last worker.
5. Put **build + test + review** in reviewer only.

### 3-worker generic template

| Worker | Scope |
|--------|-------|
| W1 | Types, shared libs, project scaffold |
| W2 | Main implementation / business logic |
| W3 | CLI, examples, docs, cleanup |

### 5-worker generic template

| Worker | Scope |
|--------|-------|
| W1 | Scaffold + shared helpers |
| W2 | Data / utils layer |
| W3 | Core engine / services |
| W4 | API / CLI / UI surface |
| W5 | Examples, migration, delete legacy |

---

## Example user invocations

```
up-agents 4 + 1 implement plan.md
```

```
up-agents 3 + 1 @600 refactor packages/core only — see plan.md §8
```

```
up-agents 4 + 1 stagger@90
```

Orchestrator reads `plan.md`, applies chekr default split from this file, launches 5 Task agents.
