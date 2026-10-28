# Agent Instructions

Preferences for AI agents working in this repository. Follow these on every task, including when delegating to subagents.

> **Reconciled with the filesystem on 2026-10-28.** Every path in this file was checked to exist, and the
> sections that had been copied from a different (TypeScript monorepo) project were rewritten: the skills
> pointer (`.cursor/skills/` → `~/.qoder/skills/`), the toolchain (bun/npm → .NET + Python), the
> configuration surface (`packages/shared/src/configenv`, `.env` → neither exists here), the design path
> (`post-mvp/` → `postmvp/`), the version guidance (v1-only → base+v1 shipped, v3 active, v2 future), and
> `@kit/shared` blocks/filters → this repo's own primitives. **If a path in this file does not exist, the
> filesystem wins — fix this file and say so, rather than following the stale instruction.**

---

## Mandatory context (read first, every conversation)

**At the start of every conversation — regardless of the task — read these files and keep them in mind for all decisions:**

| File | Purpose |
| ------ | --------- |
| [`.repertoire/.steering/base/structure.md`](.repertoire/.steering/base/structure.md) | Directory structure and high-level boundaries across the app. |
| [`.repertoire/.steering/base/product/README.md`](.repertoire/.steering/base/product/README.md) | Base Product docs index — unchanging architecture, data model, and overall design concepts. |
| [`.repertoire/.steering/base/tech/README.md`](.repertoire/.steering/base/tech/README.md) | Base Tech docs index — unchanging conventions for C#, SQL, errors, and UI components. |
| [`.repertoire/.steering/v1/product/README.md`](.repertoire/.steering/v1/product/README.md) | V1 Product docs index — current V1 scope, UX, and configuration. |
| [`.repertoire/.steering/v1/tech/README.md`](.repertoire/.steering/v1/tech/README.md) | V1 Tech docs index — diff-engine, concurrency, workers, and V1-specific tech. |
| [`.repertoire/.steering/v3/backend-ui-contract.md`](.repertoire/.steering/v3/backend-ui-contract.md) | The active wave's contract: what the backend may hand the UI, the settings bands/`SettingsKeys` authority, `PlatformEnablement`, and the isolability axes. Read for anything touching sources, settings or pipelines. |
| [`.repertoire/.steering/v3/animation-safety.md`](.repertoire/.steering/v3/animation-safety.md) | Binding animation rules 1–6, including why no tick or animation callback may throw into WinUI (`TickerGuard`) — each rule is a crash class with dump evidence behind it. |
| [`.repertoire/.steering/v1/product/configuration-reference.md`](.repertoire/.steering/v1/product/configuration-reference.md) | The settings table with its **storage** column and the "who owns a setting's scope" rulings — the readable mirror of `SettingsCatalog.Builtin`, checked by `ParserTests`. |

> **VERSION CONTEXT (reconciled with the filesystem, 2026-10-28 — verify before contradicting):**
>
> * **`base/` + `v1/` are the shipped product.** Read them for any work on the app as it runs today.
> * **`v3/` is the wave in progress** — multi-source (`v3/product/multi-source-requirements.md`), the
>   backend/UI contract (`v3/backend-ui-contract.md`), and the binding animation rules
>   (`v3/animation-safety.md`). Anything touching sources, settings, pipelines or animation is governed
>   by `v3/`; ignore it and you will re-litigate settled decisions or reintroduce a crash class that is
>   written down there with dump evidence.
> * **`v2/` is future scope** (personalization engine, search-and-filtering, roadmap-future). Ignore it
>   unless the task explicitly is v2.
>
> When a `v3` doc and a `v1` doc disagree, `v3` wins for the feature it covers, and the disagreement is
> worth reporting rather than silently picking one.

These are the source of truth for project context. Do not contradict them without verifying the codebase first. Each README is a wiki-style index — follow its links to the specific doc that answers your question rather than assuming the README alone is enough.

---

## Versioning & per-version data

- **Format:** `MAJOR.MINOR.PATCH.TAG{n}` (e.g. `1.0.4.dev1`). The base comes from `MostaqlK.csproj`; `ApplicationDisplayVersion` and `InformationalVersion` carry the full tag.
- **Every change that needs a rebuild increments the tag** (`dev1` → `dev2` → …) and `ApplicationVersion` (build code). Do not change the base version unless asked.
- **Per-version logs, channel-scoped app data:** on desktop, app data (Data, Settings, Cache, bundle-cache, pending-crashes) lives in the channel root — `%LocalAppData%\MostaqlK\` for stable, `%LocalAppData%\MostaqlK.Canary\` for canary — so every build of the same channel sees the same database and settings while the two channels stay isolated. Logs and crash files (`log\`, `crash.dmp`, `crash.log`) live under `<channel-root>\<version-tag>\` (`AppPaths.VersionDirectory`), so each build keeps its own evidence. Windows crash dumps stay in WER's `%LocalAppData%\CrashDumps`.
- **Retention:** `AppPaths.PurgeOldLogs` applies the 7-day retention to the current build's `log\` folder and to previous version folders (`LogDirectoryPurger.PruneSiblingVersions`), and never touches the shared root data.
- **Migration:** on the first launch of a new tag, `AppPaths` copies missing Data and Settings files from the newest older version folder, leaving the old folder untouched.

---

## Branching & release channels

- **`canary` is the integration branch.** All new work — features, fixes, refactors, docs — lands on `canary` first. Do not commit directly to `main`.
- **`main` is the stable branch.** It advances only by merging `canary` (owner-approved promotion). After promotion both branches share the same code baseline; the difference is release-channel behavior, not divergent code.
- **CI per branch:** push to `canary` publishes a GitHub prerelease tagged `v{ApplicationDisplayVersion}-canary` (e.g. `v1.0.4.dev43-canary`); push to `main` publishes a stable release tagged `v{Version}` (clean SemVer, e.g. `v1.0.4`). The release script derives both from `MostaqlK.csproj`.
- **Channel isolation:** the canary build appends `.Canary` to every persistent OS identity (data root, named mutexes, AppInstance key, AUMID, toast COM CLSID, HKCU Run value, tray temp dir), so stable and canary binaries run side by side. Single source: `Core/Platform/ChannelIdentity.cs`; CI passes `-p:Channel=canary`, which stamps `CHANNEL_CANARY`.
- **Versioning by branch:** on `canary` keep incrementing `dev{N}` and `ApplicationVersion`; stable tags never carry the `dev` counter (the release script uses `<Version>` on `main`).
- **Database:** canary carries the newest schema. An older `main` build cannot open a canary DB; promote `canary` → `main` before expecting schema parity.

---

## Units — read before any implementation or planning

**[`UNITS.md`](docs/UNITS.md)** catalogs every existing UI builder block (`AppButton`, `AppCard`, `NavigationControl`, `ShimmerBox`, `TrayIconService`, etc.) grouped by mechanism (Platform Components, Platform Concepts, Design System, Tray Icon).

**BEFORE any implementation or planning work**, you MUST:

1. **Read `docs/UNITS.md` in full** and check whether an existing unit already covers (or partially covers) what the task needs.
2. **Make the best use of existing units** — reuse/extend them instead of creating parallel/duplicate components, unless there's a clear, stated reason not to.
3. **If the feature introduces a new unit** (a new reusable Platform Component, Platform Concept, Design System primitive, etc.), you MUST **update `docs/UNITS.md`** with a new row/entry for it (mechanism, base type, purpose, status) as part of that same task — not as a separate follow-up.

This applies to the main agent and to every subagent doing implementation/planning work.

### Design references

| Location | Purpose |
|----------|---------|
| [`.repertoire/design/mvp/`](.repertoire/design/mvp/) | The shipped design: `projects.html`, `project-details.html`, `settings.html`, `about.html`, `onboarding.html` |
| [`.repertoire/design/postmvp/`](.repertoire/design/postmvp/) | Beyond-MVP design, **including the wave in progress**: `postmvp/v3-multisource/` (`projects.html`, `project-details.html`, `settings.html`, `settings-shapes.html`, `README.md`) is the current spec for the multi-source app, and `postmvp/mobile/` is mobile scope. `advanced-search.html` is v2 scope. |

> **Trap, verified on disk:** `settings.html` exists in **three** places — `design/mvp/`, `design/postmvp/`,
> and `design/postmvp/v3-multisource/` — and they are not the same page. Name the full path when reading
> or changing a mockup; the settings work of dev52-dev59 landed in `postmvp/v3-multisource/settings.html`.

When implementing or reviewing UI, check which folder the mockup actually lives in before treating it as
current spec: `design/mvp` is what ships today, `design/postmvp/v3-multisource` is what is being built
now, and the rest of `postmvp` is planned scope.

---

## Mandatory skill usage

**Applies to:** main agent, subagents, delegated tasks — any work that changes code, runs audits, or executes a workflow.

**Does NOT apply to:** pure questions, explanations, or **Ask mode** (read-only).

Before starting work:

1. **Use at least ONE skill.** User skills live in `~/.qoder/skills/<name>/SKILL.md` (there is no `.cursor/skills/` in this repo). The CLI's own available-skills list is authoritative for what is loadable right now, including built-ins that are not files in that folder.
2. **Read that skill's `SKILL.md` in full** — follow its workflow, not memory.
3. **If unsure which skill fits** → `find-skills` (discover), or the table below.
4. **State which skill(s) you used** when reporting results.

| Task hint | Skill (verified present) |
| ----------- | ------- |
| The user hands over a goal without every technical detail | `dtb` (decide the *how*, keep their *what*) |
| Finish the work unsupervised ("I want to sleep") | `iwts` |
| Write/rewrite a postmortem, audit, spec or comprehensive account | `document` |
| Mockup-vs-rendered-screen parity, convergence loops | `visual-parity-testing` |
| Interactive HTML design prototypes | `prototype`, `artcraft` |
| Capture the running app window / a mockup / a site as images | `capture` |
| Fetch framework docs to read offline | `scrape` |
| UI/design critique, polish, or a frontend pass | `anti-slop` |
| "Build it myself, or is there a proven library?" | `take-a-breath` |
| Comparing candidate stacks/libraries for the user to choose | `techstack` |
| Authoring/refactoring agent skills themselves | `polyglot` |
| Delegating a scoped job to a subagent (brief quality) | `subagent` |
| Strip project/personal identifiers before sharing an artifact | `revoke` |
| Autonomous-loop scheduling, `/verify`, `/run`, memory review, security scan | the CLI's `loop`, `verify`, `run`, `remember`, `security-scan` built-ins |

Repo-specific knowledge that is *not* a skill lives in [`docs/skill/`](docs/skill/)
(`error-handling-audit.md`, `maui_phone_development_parity.md`) — read those when the task is an error-
handling audit or a phone/desktop parity pass. There is no
`.cursor/skills/using-agent-skills/`, `version-bumping`, `tray-inspection`, `check-violations`,
`bug-hunting-skill`, `up-agents`, `pool-agents`, `powershell-master`, `terminal-ops`, `senior-refactor`
or `orthogonal-scout-*` skill in this project; earlier versions of this file listed them and they were
copied from another repository. Version bumps, tray work and gate runs are covered by **Quality gates**
below and by `MostaqlK.csproj` + `CHANGELOG.md` convention.

**Usage Note:** pick by the shape of the job, not by how impressive the workflow sounds — `document` for
a write-up, `visual-parity-testing` for a convergence loop, `dtb`/`iwts` when the user has delegated the
*how*, and no skill at all for a pure question. Long parallel fan-outs are the CLI's own agent/Workflow
tooling (a Workflow run needs the user's explicit opt-in), not a skill.

---

## Quality gates

- **One command:** `powershell -File tools\quality-gate.ps1` runs ParserTests, CrashPathTests and a repo-wide `dotnet format --verify-no-changes` (read-only — it never reformats files). Flags: `-Quick` (both suites only, ~12s), `-Build` (Windows canary + stable), `-Android`, `-Smoke` (coexistence smoke, publishes so minutes), `-FormatChanged` (scope the style check to the working diff). Exit 0 only if every selected step passed.
- **Every build enforces code style:** `EnforceCodeStyleInBuild` is on in `MostaqlK.csproj`, so an `.editorconfig` violation surfaces as a build warning and the gate fails on it. `IDE0051`/`IDE0052` stay `suggestion` on purpose — one TFM cannot see the members another platform's partial uses, so "unused" here means "wrong platform", not "dead code".
- **Hooks are opt-in and are the owner's to enable:** `git config core.hooksPath .githooks` turns on `.githooks/pre-commit` (the two headless suites) and `.githooks/pre-push` (adds the repo-wide format). Agents must not run that `git config` themselves.
- **CI:** the `quality` job runs both headless suites on every push/PR. The release jobs intentionally do **not** yet declare `needs: quality` — turning it on changes what can ship, which is an owner decision recorded in the workflow file.
- **Adding a new invariant?** Put the check in `tools/ParserTests` as a source scan with a `scanned >= N` floor, so a scan that matches nothing cannot pass silently (that vacuous-green failure has happened here once), and state the rule itself in the steering doc that owns the topic.

## Shell & commands

- The agent shell in this environment is Git Bash; the repo's own scripted tooling is **PowerShell** (`tools/*.ps1`, `scripts/*.ps1`, and CI runs `pwsh`). Both work — the failure modes are specific and listed below.
- **Run scripts with `-File`, not inline `-Command`:** `powershell -NoProfile -ExecutionPolicy Bypass -File tools/quality-gate.ps1`. Inline `"...$x..."` lets bash expand `$x` first, and the script then sees a mangled command.
- **Keep `.ps1` files ASCII.** Windows PowerShell 5.1 reads a BOM-less UTF-8 script as ANSI, so an em-dash becomes `”` — a *string delimiter* — and the file fails to parse partway through the run. `tools/coexistence-smoke.ps1` and `tools/quality-gate.ps1` are ASCII for exactly this reason.
- **One command at a time.** Avoid `&&` / `||` chains unless unavoidable, so a failure names the step that failed.
- **Long steps belong in the background:** `dotnet format` on this project is ~90 s, an android build ~130 s, a publish ~2 min, the coexistence smoke several minutes. Write output to a log and read it, rather than waiting on a silent pipe (a silent download looks exactly like a hang).
- **Multi-step probes:** write a throwaway script under [`scratch/`](scratch/), run it, **delete it when finished**. `scratch/` is deliberately out of git (`.git/info/exclude`) — do not force-add it, and do not treat anything there as durable.
- **Toolchain:** .NET SDK + `dotnet` (build / run / publish / format), MAUI workloads (`dotnet workload install maui-windows|maui-android`), and Python for the `tools/*.py` capture/compare/db helpers (`pip install -r tools/requirements.txt`). **There is no JavaScript toolchain in this repo** — no `package.json`, no bun/npm/yarn; that instruction was copied from another project.

## Environment & configuration

- **No `.env` files exist here** — do not create them, and there is no `packages/shared/src/configenv` (also from the other project). Configuration is owned per surface:

| Surface | Where it lives | Owner in code |
|----------|-----------------|----------------|
| User settings | `preferences.dat` in the channel root: JSON **containers** keyed `""` (system/flat), `"mostaql"`, `"nafezly"` (per-platform) | `ISettingsRepository`; `Core/Sources/ScopedSettings.cs` routes container by `SettingsDescriptor.Scope`; every persisted name is in `SettingsKeys` (`Core/Sources/ISettingsRepository.cs`) |
| Project data | `mostaqlk.db` (SQLite, `PRAGMA user_version` migrations) | `Infrastructure/Database/` + `Migrations/` |
| Session cookies | Encrypted rows in the DB, per-descriptor `CookieSecretKey` | `Services/CookieStore.cs`, `Services/CookieStoreProvider.cs` |
| Start-with-system | HKCU `…\CurrentVersion\Run`, channel-isolated value name | `Services/StartupService.cs` + `Platforms/Windows/StartupService.Windows.cs` (the row in Settings asks *this* service, not the stored bool) |
| Channel identity | Compiled in via `-p:Channel=canary\|stable`; every persistent OS identity derives from one file | `Core/Platform/ChannelIdentity.cs` — never hand-write a `.Canary` suffix, mutex name, AUMID or CLSID |

- **Never paste, log, or commit a cookie value, a secret, or a user's database row.** Describe the shape of the data if a bug report needs it.
- **Opt-in diagnostic env vars** (all off in normal launches; mechanism notes in `docs/postmoterm/animation-tick-failfast-2026-10-28.md` §6): `MOSTAQLK_DIAG_COM=1` logs every first-chance exception with its live stack, `MOSTAQLK_DIAG_NET=1` logs every outbound site request (the witness for "a disabled platform makes zero requests"), `MOSTAQLK_NO_WATCHDOG=1` suppresses the watchdog spawn.

## Shared primitives — reuse instead of inventing

This is one C# MAUI project, not a monorepo, so there are no `@kit/*` imports to reach for. The equivalents:

| Need | Use |
|------|-----|
| An operation that can fail | `Core/Result.cs` (`Result<T>`) with `Core/DomainError.cs` — errors are **values**, not exceptions; if a `Result.IsError` is observed and not propagated, report it through `InteractionLogger.Failure` (that sink exists because dropped errors were invisible once) |
| A reusable UI block or behavior | an existing unit from [`docs/UNITS.md`](docs/UNITS.md) (`AppButton`, `AppCard`, `SettingsRow`, `PipelineRadar`, `LastScanStatus`, `TickerGuard`, `MotionPreferences`, …) — reuse/extend it, and add a row the same task if it is genuinely new |
| Anything about a site/platform | `Core/Sources/` — `SourceId`, `ISourceDescriptor` (+ `SourceCapabilities`), `SourceRegistry`, `PlatformEnablement`, `ScopedSettings`, `NotificationMute`, `SourceCadence`. Shared code must **not** name a platform; `ParserTests` enforces it |
| Per-frame work on the UI thread | `TickerGuard.Run(...)` (nothing may throw into WinUI) and honour `MotionPreferences.IsReducedMotionRequested` |
| Splitting platform behavior | the isolability axes — `Core/Layers/LayerResolver` + per-variant files (`*.Windows.cs` / `*.Android.cs`, `*.Canary.cs` / `*.Stable.cs`), governed by `.repertoire/.steering/v3/backend-ui-contract.md` |

## Debugging

- **Try multiple approaches.** If the code looks correct, do not stop at one failed attempt.
- **Escalate in this order** (each step has a real artifact to read):
  1. **Logs, not guesses.** `%LocalAppData%\MostaqlK[.Canary]\<version-tag>\log\<date>\` holds `interaction-log.txt` (every `MARK`/`FAULT`/`NET` line, pid-tagged), `startup-debug.log` (birth certificate), `heartbeat.log` (`finalizePending`, GC generations, handles, threads), and `crash.log` for faults that were reported.
  2. **Confirm the data.** Query the channel's `mostaqlk.db` read-only — `python -c "import sqlite3,os; …uri=file:...?mode=ro"` — or run `dotnet run --project tools/DbMigrationTests` for schema/upgrade behaviour. Never open the DB write-mode from an ad-hoc script.
  3. **Isolate the layer.** Drive the existing service/pipeline from a scratch console or `tools/ParserTests`-style harness to see whether the hotspot is the service or the UI/routing, then hypothesize until the root cause is named — a symptom fix is not a root cause.
  4. **Native crashes.** `0xC000027B` / `0xC0000409` deaths leave only a WER dump in `%LocalAppData%\CrashDumps`. The whole procedure — hand-parsing the minidump, following the stowed-exception pointer, and resolving symbols with `dbghelp`/`symsrv` without a debugger — is written up in `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` §12. `tools/enable-full-dumps.ps1` (with `-Restore`) is the switch for when a mini dump genuinely is not enough.
- **Scratch debug files:** read configuration from the same surfaces above (channel root paths, `AppPaths`), never hardcode a secret or a connection string, and delete the file when finished.

---

## Agent pool notation (fan-out delegation)

When the user says **"pool of agents"** or uses notation like **`x->2x->4x`**, run a branching delegation tree — do not ask them to re-explain.

### Shorthand

| User says | Meaning |
| ----------- | --------- |
| `pool of agents x->2x->4x` | 2 parallel subagents; each spawns 2 more (= 4 leaves) |
| `pool of agents x->3x` | 3 parallel subagents, no further fan-out |
| `pool of agents x->2x->4x->8x` | 2 → 4 → 8 over three worker depths |
| `/pool-agents x->2x->4x {task}` | Same as above, requested as a slash-style phrase (there is no `pool-agents` command file in this repo — the notation itself is the instruction) |

### Grammar

`x->N1x->N2x->...` — token after `->` is the **agent count at that depth**:

- `x` = you (orchestrator; merge results, do not do all leaf work yourself)
- `2x` = 2 agents at depth 1
- `4x` = 4 agents at depth 2 (each depth-1 agent spawned 2 children)

Branching factor = `count[n+1] / count[n]`. Max **4 worker depths**. Never infinite.

### How to execute

1. Parse notation + task
2. Split task into scoped subtasks (one per leaf)
3. Launch depth-1 subagents **in parallel** (multiple `Agent` calls in one message)
4. Each subagent that has a deeper level MUST spawn its children in parallel and merge before returning
5. Root merges all branch results into one response

A fan-out this size is expensive; if the CLI requires explicit opt-in for `Workflow` runs, ask before
using it and use plain parallel `Agent` calls instead. There is no `pool-agents` command file — the
notation above is the whole algorithm.

---

## Subagent delegation

When spawning a subagent (`Agent` tool or any delegated work), the **parent (master) agent** is responsible for the following, before and after the subagent runs:

1. **Naming.** The master agent assigns every subagent a name based on its role/responsibility (e.g. `pipeline-diff-reviewer`, `ui-navigation-implementer`), not a generic label. This name is used consistently for the handle, the report file, and any status updates about that subagent.
2. **Full context up front.** Subagents cannot ask follow-up questions and cannot see the master's conversation. The master MUST give the subagent complete, self-contained details in the task prompt: the exact goal, the relevant files/paths already identified, any decisions already made (e.g. naming conventions, chosen approach), and the exact output expected back. Do not assume the subagent will infer missing context.
3. **Mandatory doc list.** The master MUST explicitly list, inside the subagent's prompt, every steering doc the subagent needs for its specific task (not just the general list below) — e.g. if the task touches the pipeline, name `worker-pool-and-rate-limiter.md` and `diff-engine.md` explicitly; if it touches UI, name `cross-platform-ui-conventions.md` and the relevant `design/mvp/` mockup. The subagent must read and adhere to these docs perfectly, not just the mandatory-context block below.
4. **Units check.** The master MUST instruct the subagent to read [`UNITS.md`](docs/UNITS.md) before any implementation/planning and to reuse existing units where possible; if the subagent's task introduces a new unit, the master must require the subagent (or do it itself) to add an entry for it in `docs/UNITS.md`.
5. **Report file on completion.** When a subagent finishes its work, it MUST create (or the master must create on its behalf, if the subagent's tools don't allow it) a markdown report at `.repertoire/agents/<agent-name>.md`, summarizing: what it was asked to do, what it did, files touched, decisions made, and verification performed. This is in addition to (not instead of) the normal final result returned to the master.

**Paste the block below as the first thing in every subagent prompt** so the subagent inherits the baseline rules:

```text
=== MANDATORY AGENT PREFERENCES (read and follow) ===

1. CONTEXT — Read at conversation start and consider for every decision:
   - `base/` + `v1/` are the SHIPPED product. `v3/` is the wave in progress (multi-source,
     .repertoire/.steering/v3/backend-ui-contract.md, v3/animation-safety.md) and wins for the feature
     it covers. `v2/` is future scope — ignore unless the task explicitly is v2.
   - .repertoire/.steering/base/product/README.md & .repertoire/.steering/base/tech/README.md
   - .repertoire/.steering/v1/product/README.md & .repertoire/.steering/v1/tech/README.md
   - Design mockups: .repertoire/design/mvp/ (shipped) and .repertoire/design/postmvp/ — the v3 wave
     lives in postmvp/v3-multisource/ (watch out: settings.html exists in three places, name the path)
   - PLUS any task-specific docs the master agent lists below in "TASK-SPECIFIC DOCS" — read those in full and adhere to them exactly.

2. SKILLS — Use at least ONE skill from ~/.qoder/skills/<name>/SKILL.md (read it in full; the CLI's available-skills list is authoritative). Common here: dtb, document, visual-parity-testing, capture, anti-slop, subagent. Not required for pure questions / read-only work.

3. SHELL — One command at a time; no && or || chaining unless unavoidable. Run .ps1 with `powershell -NoProfile -ExecutionPolicy Bypass -File <script>` (never inline -Command with $vars), and keep new .ps1 files ASCII — PS 5.1 reads BOM-less UTF-8 as ANSI and an em-dash breaks the parse. Long steps (build, publish, dotnet format, smoke) → background + read the log. Multi-step probes → script in scratch/, delete when done.

4. TOOLING — .NET SDK + dotnet (build/run/publish/format), MAUI workloads, Python for tools/*.py. There is NO JavaScript toolchain here: no package.json, no bun/npm/yarn.

5. CONFIG — No .env files and no configenv in this repo. Settings are preferences.dat containers in the channel root ("" = system/flat, "<source>" = per-platform) behind ISettingsRepository + ScopedSettings + SettingsKeys; data is mostaqlk.db; cookies are encrypted DB rows; channel identity comes only from Core/Platform/ChannelIdentity.cs. Never paste, log, or commit a cookie, secret, or user row.

6. DEBUG — Try multiple approaches. Read the channel logs first (interaction-log.txt / startup-debug.log / heartbeat.log, all pid-tagged); confirm data with a read-only SQLite query or tools/DbMigrationTests; then drive the service layer to locate the hotspot. Native crash dumps: docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md. Delete scratch files when finished.

7. REPORT — When you finish, write a summary report to .repertoire/agents/<your-agent-name>.md (goal, actions taken, files touched, decisions made, verification), in addition to your normal final result.

8. UNITS — Before any implementation or planning, read docs/UNITS.md in full and make the best use of existing units (reuse/extend instead of duplicating). If this task introduces a new reusable unit, update docs/UNITS.md with a new entry for it as part of this task.

9. GATES — Work is not done until `powershell -File tools\quality-gate.ps1` passes (add -Build / -Android for the builds). Never let an animation/timer callback throw: route per-frame work through TickerGuard. Shared code must not name a platform; ParserTests will fail the build if it does.

TASK-SPECIFIC DOCS: <master agent fills in the exact doc paths relevant to this subagent's task here>

=== END PREFERENCES — task follows below ===
```
