# up-agents (staggered workers + reviewer)

Launch **N worker subagents** with staggered sleeps, plus **1 reviewer** that starts last.

**Read and follow:** `.cursor/skills/up-agents/SKILL.md` (full algorithm).

## User input

```
/up-agents 4 + 1 implement plan.md
```

**Grammar:**

```
/up-agents [N] + 1 [@R] [stagger@S] {task}
```

| Part | Default |
|------|---------|
| `N` workers | 4 |
| `+ 1` reviewer | required |
| `@R` reviewer delay (seconds) | 800 |
| `stagger@S` base stagger (seconds) | 60 |
| `{task}` | everything after notation |

## Examples

```
/up-agents implement plan.md
/up-agents 4 + 1
/up-agents 3 + 1 @600
/up-agents 4 + 1 stagger@90 migrate toolkit per plan.md
/up-agents 5 + 1 @600          # remediation: audit, cli, biome, heavy tests
/up-agents as needed            # agent picks 4+1 or 5+1 from repo state
```

## What you do

1. Read `.cursor/skills/up-agents/SKILL.md`
2. Parse N, @R, stagger@S, and task from user input
3. Split task into N non-overlapping scopes (chekr defaults in `reference.md`)
4. Launch **N+1 Task agents in one message** with sleep instructions
5. Merge reviewer Report into final response

## vs pool-agents

- **`/up-agents`** — linear workers + delayed reviewer (implementation)
- **`/pool-agents`** — branching fan-out (exploration/audit)
