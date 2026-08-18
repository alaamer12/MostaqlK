---
name: using-agent-skills
description: Discovers and invokes agent skills. Use when starting a session or when you need to discover which skill applies to the current task. This is the meta-skill that governs how all other skills are discovered and invoked.
---

# Using Agent Skills

## Overview

This repo keeps its skills in **`.cursor/skills/`**. Each skill is a directory with a `SKILL.md` (and optional `reference.md`, `references/`, `POWER.md`).

**If you don't know which skill to use, read this file first** — then open the chosen skill's `SKILL.md` and follow it.

---

## Skills catalog (`.cursor/skills/`)

| Skill | Path | Use when |
|-------|------|----------|
| **using-agent-skills** | `.cursor/skills/using-agent-skills/` | You need to pick a skill (this file) |
| **bug-hunting-skill** | `.cursor/skills/bug-hunting-skill/` | Hunting bugs, code audit, `/bug-hunter` subagent — React failure-mode patterns |
| **code-review-and-quality** | `.cursor/skills/code-review-and-quality/` | Reviewing code before merge, quality gates |
| **api-and-interface-design** | `.cursor/skills/api-and-interface-design/` | Designing APIs, tRPC routers, module boundaries, type contracts |
| **frontend-ui-engineering** | `.cursor/skills/frontend-ui-engineering/` | Building/modifying production UI components and layouts |
| **frontend-design** | `.cursor/skills/frontend-design/` | Visual direction, typography, distinctive aesthetics (not generic AI UI) |
| **react-native-best-practices** | `.cursor/skills/react-native-best-practices/` | RN/Expo performance — FPS, TTI, bundle size, re-renders, memory |
| **building-native-ui** | `.cursor/skills/building-native-ui/` | Expo Router native app UI — navigation, tabs, animations, patterns |
| **expo-ui** | `.cursor/skills/expo-ui/` | `@expo/ui` — SwiftUI/Compose native components from React |
| **expo-api-routes** | `.cursor/skills/expo-api-routes/` | API routes in Expo Router with EAS Hosting |
| **up-agents** | `.cursor/skills/up-agents/` | `up-agents N + 1` — staggered worker subagents + delayed reviewer |
| **professional-typing** | `.cursor/skills/professional-typing/` | Semantic types, .d.ts, Zod, validateConfig for configs/APIs |
| **quality-gate** | `.cursor/skills/quality-gate/` | `quality-gate` — lint, typecheck, tests, smoke, ship-ready verify |

---

## Skill discovery

When a task arrives, map it to a skill:

```
Task arrives
    │
    ├── Don't know which skill? ──────────→ using-agent-skills (this file)
    │
    ├── Bug hunt / audit / bug-hunter ────→ bug-hunting-skill
    │
    ├── Code review before merge ─────────→ code-review-and-quality
    │
    ├── API / tRPC / backend contracts ───→ api-and-interface-design
    │
    ├── Web UI implementation ────────────→ frontend-ui-engineering
    │   └── Visual design direction ──────→ frontend-design (also)
    │
    ├── React Native / Expo mobile ───────→ react-native-best-practices
    │   ├── App screens & navigation ─────→ building-native-ui
    │   ├── Native SwiftUI/Compose UI ────→ expo-ui
    │   └── Mobile API routes ────────────→ expo-api-routes
    │
    └── Refactor for native (shared code) → read .repertoire/progress/refactor-for-native.md
                                            + api-and-interface-design
                                            + react-native-best-practices
```

### SNDUK-specific shortcuts

| Task context | Skill(s) |
|--------------|----------|
| `packages/api`, services, tRPC | `api-and-interface-design` |
| `apps/web` components, hooks | `frontend-ui-engineering` + `bug-hunting-skill` |
| React bugs / hooks / stale closures | `bug-hunting-skill` |
| New Expo/RN app work | `building-native-ui` + `react-native-best-practices` |
| `/bug-hunter` subagent | `bug-hunting-skill` (re-read `reference.md` per package) |
| Pool of agents `/pool-agents` | `using-agent-skills` to assign skills per branch |
| `up-agents` / `up-agents 4 + 1` | `up-agents` skill — staggered workers + reviewer |
| `quality-gate` / `/quality-gate` | `quality-gate` skill — lint, typecheck, tests, smoke |
| Professional types / PathLike / Zod config | `professional-typing` |

---

## How to invoke a skill

1. **Read** `.cursor/skills/<skill-name>/SKILL.md` in full.
2. **Read** companion files if the skill points to them:
   - `reference.md`, `POWER.md`, or files under `references/`
3. **Follow** the skill's workflow — skills are processes, not suggestions.
4. **State** which skill(s) you applied when reporting back.

Some skills also have optional `POWER.md` (condensed/onboarding variant) — use when the skill says to.

---

## Rules

1. **Use at least one skill** on every implementation task (see root `AGENTS.md`). Questions / Ask mode are exempt.
2. **Multiple skills can apply** — e.g. `frontend-ui-engineering` + `frontend-design` for a new screen.
3. **When in doubt, start here** — then open the chosen skill.
4. **Subagents too** — delegated agents must read and apply at least one relevant skill.

---

## Core operating behaviors

These apply whenever any skill is active:

### 1. Surface assumptions

Before implementing anything non-trivial:

```
ASSUMPTIONS I'M MAKING:
1. [assumption about requirements]
2. [assumption about architecture]
3. [assumption about scope]
→ Correct me now or I'll proceed with these.
```

### 2. Manage confusion actively

When requirements conflict or are unclear: **stop**, name the confusion, ask — do not guess.

### 3. Push back when warranted

Point out concrete downsides and propose alternatives. Sycophancy is a failure mode.

### 4. Enforce simplicity

Prefer the boring solution. If 100 lines would suffice, 1000 lines is a failure.

### 5. Maintain scope discipline

Touch only what the task requires. No drive-by refactors.

### 6. Verify, don't assume

A task is not done until there is evidence (tests, build, runtime check).

---

## Failure modes to avoid

1. Starting work without reading any skill
2. Making wrong assumptions without checking
3. Plowing ahead when confused
4. Overcomplicating code
5. Skipping verification because "it looks right"
6. Using skills from memory instead of re-reading the file

---

## Quick reference

| Phase | Skill |
|-------|-------|
| Pick a skill | `using-agent-skills` |
| Find bugs | `bug-hunting-skill` |
| Review code | `code-review-and-quality` |
| Design API | `api-and-interface-design` |
| Build web UI | `frontend-ui-engineering` |
| Design visuals | `frontend-design` |
| RN performance | `react-native-best-practices` |
| RN app structure | `building-native-ui` |
| Native UI components | `expo-ui` |
| Expo API routes | `expo-api-routes` |
