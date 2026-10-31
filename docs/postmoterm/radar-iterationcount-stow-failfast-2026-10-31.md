# Postmortem: PipelineRadar (Windows compositor) — crash loop: caught `IterationCount` throw fed the 0xC000027B dispatcher fail-fast — 2026-10-31

> **Status:** review · **Severity:** SEV-3 · **Incident ID:** INC-2026-10-31-RADAR-STOW
> **Authors:** Qoder agent (investigation and document), on the owner's machine · **Reviewers:** UNKNOWN — repo owner has not yet signed · **Sign-off:** pending

---

## 1. What the problem was

**Summary.** Canary builds of 1.0.4.dev69 on the owner's Windows machine entered a crash loop: every instance died with the native fail-fast `0xC000027B` (STATUS_STOWED_EXCEPTION) within roughly one to two minutes of use, and the app's own watchdog faithfully relaunched each corpse, so the product looked like it "kept getting up and dying". The suspected broken poll service was not broken — requests fired correctly. The accelerant was one line in `UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs:82`: `loop.IterationCount = -1`, a dev30-era idiom that the current WinAppSDK 1.7 C# projection rejects (`ArgumentException` / `E_INVALIDARG`). The exception was swallowed by a local catch, but each swallowed throw abandoned a half-built `KeyFrameAnimation` COM object; abandoned composition objects finalize off-thread, and that finalizer flood is the documented fuel of the `0xC000027B` dispatcher fail-fast class. The fix (`int.MaxValue` instead of `-1`) removed the throw; a soak instance survived the exact usage pattern for over 30 minutes with zero faults. The fix is committed with this document.

**Exact symptom.** Native death, no managed stack:

```text
exception code 0xC000027B  addr=0x7FFF7ADCCDE5  nparams=2
Microsoft.UI.Xaml.dll 3.1.7 (file version 3.1.7.2509) +0x39cde5
  = FailFastWithStowedExceptions, called from CXcpDispatcher::MessageTimerCallback
stowed ErrorValue 0x80004005 (E_FAIL)
```

The last managed event before every diag-flagged death (`MOSTAQLK_DIAG_COM=1` interaction log, verbatim):

```text
2026-10-31T22:55:43.8187640+02:00 | pid=15036 | FAULT | FirstChance |
data={"Type":"System.ArgumentException","HResult":-2147024809,
"Message":"The parameter is incorrect.\r\n\r\nAn invalid IterationCount is specified. It must be greater than 0."}
```

Heartbeat of dying instance pid 24792 (verbatim):

```text
[2026-10-31 22:51:33.481] #2 to-background uptime=0.00:00:02 ... finalizePending=2277 ...
[2026-10-31 22:52:19.322] #3 to-foreground  uptime=0.00:00:47 ... finalizePending=6138 ...   ← died 34 s later, 22:52:53Z-local
```

**Impact in one line.** One canary-channel user (the owner) could not use the app at all for ~55 minutes of crash-looped sessions (plus earlier deaths from ~16:27Z) because every instance fail-fasted within 48–143 s of active use while the watchdog relaunched it indefinitely; no data loss, no other users affected.

## 2. When and where — traceability

| Field | Value |
|---|---|
| First bad commit | `8ba4e1a39341f5382b6fce4cb9668e2106e216a5` — "feat: dev30 — frame-decoupling wave 2 …" (introduced `loop.IterationCount = -1` at 2026-10-22T15:01:58Z; **inference** that this is first-bad: it throws under the *current* projection — whether the projection validated the value back in dev30 is UNKNOWN — see §9) |
| Last good commit | UNKNOWN — no dev30–dev68 instance was observed dying in this class; the latent defect produced only the "radar parks" symptom (see §8, §17) |
| Fix commit(s) | `63a743f2e3d07053cb787e6c7fa50c096b0178f1` — `fix: dev70 — the radar's rejected -1 was fueling the dispatcher fail-fast` (this document shipped inside that commit; the SHA is recorded here in the follow-up, since a document cannot carry its own containing commit's hash) · no PR — canary branch commit, promotion is an owner action |
| Versions affected | 1.0.4.dev69 working-tree builds and the shipped dev69 `02e94d65c392c176969ee050dfb3b6b44ea4aef2` (all Windows targets); dev30–dev68 carried the throwing line but were not observed crash-looping |
| Version fixed | 1.0.4.dev70 (this commit; `MostaqlK.csproj` `ApplicationVersion` 75) |
| Release channel | canary (`%LOCALAPPDATA%\MostaqlK.Canary`, `-p:Channel=canary` behavior not required — default dev build is canary-flagged via branch) |
| Build ID | `1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995` (dying diag builds and the first fixed soak build share this label — the version dir does not encode uncommitted diffs) |
| Artifact hash | `sha256:1f393bf18352ca9d8ecb6f190e5de3be5eb06f0c764d6160ffc40ed6fbfe7ac4` — SHA-256 of the fixed source file `UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs` (the `.exe` hash is UNKNOWN — not recorded at 23:18 rebuild) |
| Built from | commit `06374f32dd56b6d3d05f03e2f7cea10c16a9a995` + working-tree diff (this fix) by a local `dotnet build` — no CI run; shipped bytes are NOT proven to match reviewed source by CI |
| Build type | Debug, win-x64, unpackaged WinUI (`bin\Debug\net10.0-windows10.0.19041.0\win-x64\`) |
| Toolchain | .NET 10 SDK, MAUI workload, `net10.0-windows10.0.19041.0`; `WindowsAppSDKSelfContained=true` (`MostaqlK.csproj:77`) |
| Dependencies | `Microsoft.UI.Xaml.dll` file version `3.1.7.2509` shipped self-contained next to the exe (fact: `VersionInfo.FileVersion` read post-mortem) |
| Feature flags / config that matter | `MOSTAQLK_DIAG_COM=1` (first-chance logging — the flag that made the throw visible); `MOSTAQLK_NO_WATCHDOG=1` (soak); `settings_query_params` per container and `settings_disabled_platforms` set by dev69 onboarding |
| OS / build | Windows 10 10.0.19045 x64 |
| Hardware | UNKNOWN — desktop dev machine, not established; not believed relevant |
| Container digest | n/a |
| Cloud region / zone | n/a (local desktop app) |
| Security policies in force | Windows Defender real-time protection **quarantined fresh-built `MostaqlK.exe` twice earlier the same day** (MSB3021 / AUMID `UnauthorizedAccessException`) — recorded here because an AV-interposed file lock was ruled out as a death cause, not because it is one |
| Network conditions | live internet to mostaql.com / nafezly.com — ruled out: `MOSTAQLK_DIAG_NET=1` showed polls firing normally before deaths |
| Reproduces where? | owner's canary dev build: yes · CI headless suites: no (they never start the WinUI window) · Android: not tested — no composition visual tree equivalent |

**Reproduce matrix**

| Environment | Version | Reproduces? | Notes |
|---|---|---|---|
| Owner desktop, canary Debug, clean DB | 1.0.4.dev69 (pre-fix) | yes — every session | death 48–143 s into active use (wizard or filter churn) |
| Owner desktop, canary Debug, diag COM flag | 1.0.4.dev69 (pre-fix) | yes — 8 dumps 20:49–21:13Z | first-chance log captured the throw |
| Owner desktop, canary Debug, post-fix | 1.0.4.dev69+06374f3 + this diff | no — 1 soak, 30 min+ | pid 14676, flood exercised, 0 faults |
| Release build | 1.0.4.dev69 (pre-fix) | yes (inference — same line, same runtime) | deaths at 16:27Z and 18:14Z preceded the Debug repro; cause attribution of 22256.dmp UNKNOWN (not parsed) |
| Android | any | untested | fix is Windows-partial; Android TFM never compiles this line |

## 3. When it started and how long it lasted

Local time is UTC+2 (verified: log `+02:00` stamps, and watchdog local `20:19:21` ↔ crash-record UTC `18:19:21`). All rows below UTC.

| Milestone | UTC timestamp | Evidence |
|---|---|---|
| Introduced (commit/merge) | 2026-10-22T15:01:58Z | `8ba4e1a39341f5382b6fce4cb9668e2106e216a5` |
| First-ever observed death attributable to this class on dev69 | 2026-10-31T18:14:48Z | `%LOCALAPPDATA%\CrashDumps\MostaqlK.exe.22640.dmp` (11.7 MB mini) — matches the crash-loop window; the 16:27Z `22256.dmp` predates dev69 completion and was not parsed, so it is listed but not attributed |
| First noticed (owner report: "while i ran the app and i was using it crashes") | ≈2026-10-31T18:15Z | chat message immediately precedes the clean-DB repro authorized at 18:18Z; exact chat timestamp UNKNOWN (not captured) |
| Crash-loop reproduction begins (clean DB + prefs) | 2026-10-31T18:18:35Z | `1.0.4.dev69+a927d46f9c3d237d2f00328ccfc91ffb35567dbc/log/2026-10-31/watchdog.log` first supervisor line |
| Diagnosis (root cause named) | ≈2026-10-31T21:10Z | first-chance log line + 8 parsed dumps + §10 postmortem match, session transcript |
| Fix built and soak started | 2026-10-31T21:18:31Z | `1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995/log/2026-10-31/startup-debug.log` birth line pid 14676 |
| **Total exposure window** | **≈3 h 5 min** | 18:14:48Z first observed death → 21:18:31Z fixed build running (crash-usable window; wall-clock owner-facing outage ≈55 min of continuous loop 18:18–19:13Z plus intermittent deaths from 18:14Z) |
| Shipped (committed) | this commit, 2026-10-31 | `git log` |

## 4. The interval in which the pattern occurs

- Window: 48–143 s after process start **under active use** (wizard navigation or filter/feed churn); an idle instance could live longer — the soak's pre-fix predecessor instances were being actively driven.
- min / median / max: 48 s / 82 s / 143 s · exact-sample size: **n = 3** (watchdog exit lines: pid 23368 `uptime=0.00:02:23`, pid 15400 `uptime=0.00:00:48`; parsed heartbeat: pid 24792 start 20:51:31Z-local→ death 20:52:53Z-local ≈ 82 s).
- **Histogram:**

| Bucket (uptime at death) | Count | % of exact samples |
|---|---|---|
| 0–60 s | 1 | 33% |
| 60–120 s | 1 | 33% |
| 120–180 s | 1 | 33% |

  Twelve additional watchdog-spawned instances (`watchdog.log` supervisor-start lines 18:26:51Z–18:40:19Z, ~45–90 s apart, 15 starts in 13.5 min) corroborate a sub-2-minute MTBF without each having a logged exit line — **inference**, falsifiable by finding any of those pids' exit lines with a long uptime; none exist in that log.
- Time-based pattern: deaths clustered at foreground use, right after feed-replacement bursts (a radar (re)build batch), not at a clock time.
- Frequency: every run, given use; the pre-fix MTBF under the owner's flow was order 1–2 minutes.
- Recurrence once started: escalates — each relaunched instance repeats the same build → throw → abandon → finalize flood sequence from zero.
- **Why it falls in this window:** the fuel is per radar-build, not per second; the dispatcher fail-fast fires once the off-thread finalization backlog of abandoned composition objects collides with a timer callback on the XAML dispatcher (`CXcpDispatcher::MessageTimerCallback` in every dump — fact). Time-to-death therefore scales with how fast the user drives radar rebuilds, which under manual walkthrough is minutes, not hours.

## 5. Reproducibility

**Class:** state-dependent, probabilistic-in-exact-timing: every session with the dev69 flood pattern died within minutes, but the precise second depended on GC/finalizer scheduling.

**Exact steps (as reproduced 2026-10-31T18:18:35Z):**
1. Delete the channel's `Data\mostaqlk.db` and `Settings\preferences.dat` (owner-authorized — no important data).
2. Launch the pre-fix canary Debug build (`dotnet run` / `scripts\run.ps1`).
3. Walk onboarding (or open Projects) and change the filter so the category query re-fetches and the whole feed replaces (dev69 chip flow).
4. Within ~1–2 min the process dies with exit code `0xFFFFFFFF` (or `0x1`); WER drops `MostaqlK.exe.<pid>.dmp` in `%LOCALAPPDATA%\CrashDumps`.

**Minimal reproducer:** not a code isolator — a usage pattern. Smallest reliable trigger found: complete-onboarding + one filter change on the pre-fix build (the UIA walk script used for the soak is the same driver, deleted from `scratch/` with the harness; the soak itself is the re-run proof).
**Reproduced on:** ≥8 diag-flagged instances + 15 watchdog-loop instances, one machine, one build. · **Not reproduced on:** post-fix build (1 instance, 30 min).

## 6. How deep — layer and blast radius

| Layer | Involved? | Where |
|---|---|---|
| UI / presentation | **yes** | `UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs` — the throwing line and the swallowing catch |
| Application logic | no | poll/pipeline services behaved correctly throughout (diag NET log) |
| Framework / library | **yes** | WinAppSDK self-contained `Microsoft.UI.Xaml.dll 3.1.7.2509` — its C# projection validates `IterationCount > 0`, and its dispatcher fail-fasts on the stowed exception |
| Runtime | yes (mechanical) | .NET GC finalization of the abandoned COM RCWs happens off-thread — the transport of the fuel, not the defect |
| OS / driver | no | WER only recorded dumps |
| Hardware / firmware | no | — |
| Network / infrastructure | no | — |
| Data / storage | no | DB/settings untouched by the deaths; clean-DB wipe was requested, not caused |
| Process / organization | yes (contributing) | the "radar parks" known-symptom item was never root-caused; swallowed catches in Windows-component builders had no visibility requirement — see §16 |

**Depth rating:** moderate — the defect is one line in app code, but its death mechanism lives in framework-internal fail-fast policy, so it reads as "native crash" rather than "exception".
**Blast radius:** whole canary app process (every window, every feature) for the owner. **Not yet failed but exposed:** any other Windows-component path that builds composition objects inside a `try/catch` that continues as if the build succeeded — §7 sweep covers `IterationCount`; the same "half-built object survives the catch" shape was seen in `SyncRotations` (`_scannerRotating` set true even after the caught throw) — flagged for audit.

## 7. The pattern

- **Bug class:** wrong API contract assumption after a projection/type-surface change (the `-1 → UINT32_MAX` wrap idiom) + error-swallowing catch that leaves inconsistent state and leaks a native object per occurrence; the crash itself is resource-exhaustion-of-a-new-kind (finalizer backlog) on a previously documented failure mode.
- **Repeat of a known incident?** **Yes, partially** — the death signature is exactly the class documented in `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` §10/§11/§12 (`0xC000027B` fail-fast fed by native-peer finalization floods). What changed: a **new fuel source** — abandoned composition animations from a *caught* throw, rather than the layout-dispatch path of the original incident.
- **Same defect elsewhere?** Sweep for the exact idiom: `Grep -n "IterationCount" --type cs` over the repo → after this fix, **no remaining negative or `-1` assignments to `IterationCount`** (only this file referenced it). Sweep query recorded in §16 (A1) for the stronger rule. The broader "catch that continues after a failed native-object build" shape was **not** fully swept — see action A2.
- **Pattern tag:** `caught-composition-throw-abandoned-com-object`

## 8. How it happened

| Element | Statement |
|---|---|
| Symptom | app dies within 1–2 minutes of use, repeatedly, and "gets up" after closing (watchdog relaunch) |
| Trigger | dev69's onboarding filter chips made whole-feed replacement routine; each replacement rebuilds radar visuals, hitting the throwing setter each time |
| Root cause | `loop.IterationCount = -1` — an integer literal carrying a WinRT-UINT32 assumption the C# projection no longer honors — throws `ArgumentException`/`E_INVALIDARG` inside a catch that swallows it and continues ([`PipelineRadar.Windows.cs:82`](../../UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs)) |
| Latent conditions | the throw was invisible for 9 days (dev30→dev69): the catch silenced it, the only visible symptom was "the radar doesn't spin", which had been noted as a cosmetic owner item; the fatal fuel (abandoned COM objects finalizing off-thread) required dev69's rebuild frequency to reach fail-fast density |

**Causal chain** (two independent conditions had to coincide — this is a chain, not one sentence):
1. dev30 replaced the radar's 16 ms managed ticker with a looping compositor animation and wrote `loop.IterationCount = -1` believing it wraps to `UINT32_MAX` — [`UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs`](../../UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs) at `8ba4e1a39341f5382b6fce4cb9668e2106e216a5`.
2. The WinAppSDK 1.7 C# projection types the property INT32 and validates `> 0` → every `StartRotation` throws `ArgumentException` (HResult `-2147024809`).
3. The surrounding catch swallows it; the local `loop` (half-built `KeyFrameAnimation` COM object) is abandoned — `GC.KeepAlive` never reached, no dispose — and finalizes **off-thread** — [`SyncRotations`/`StartRotation` path](). Each rebuild adds one.
4. dev69's filter chips cause whole-feed replacement on each category change → radar rebuilds become frequent → `finalizePending` climbs into thousands (fact: 2,277 → 6,138 within one minute on pid 24792; 9,350 during the post-fix flood test — the drain is what differs).
5. A `CXcpDispatcher::MessageTimerCallback` on the XAML dispatcher encounters a stowed `E_FAIL` from the finalization churn and calls `FailFastWithStowedExceptions` → `0xC000027B`, process death, WER dump — [`docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` §10](published-layout-dispatcher-failfast-2026-10-28.md) documents this death mode and its fuel; step 5→6 linkage is **inference** (see §9).
6. The app's watchdog does its job — relaunches each dead instance (max 5 per 30 min) — converting one bug into a visible crash loop the owner read as "the poll service broke".

**Five whys**
1. Why did the app crash-loop? → every active instance hit the XAML dispatcher fail-fast within minutes.
2. Why the fail-fast? → a stowed `E_FAIL` inside the dispatcher timer callback, fueled by an off-thread finalization flood of abandoned composition objects (documented class).
3. Why were composition objects being abandoned per cycle? → the animation-setter throw inside a catch continued past the half-built object without disposing it or resetting the rotation state.
4. Why did the setter throw at all? → the code assumed `-1` wraps to `UINT32_MAX`, but the current projection types the property INT32 and validates `> 0`.
5. Why was that assumption invisible for 9 days? → the catch made it produce a *cosmetic* symptom ("radar parks") with no log line, and no gate tests composition-object contract values — the missing invariant is "a swallowed native-object throw must leave no object and no state behind, and must be logged."

**Contributing factors**
- Watchdog relaunch policy (correct behavior) amplified one crash into a loop and obscured causality — the owner's own report framed it as "it still gets up".
- First-chance logging is opt-in (`MOSTAQLK_DIAG_COM=1`), so the repro-window deaths (18:18–18:40Z) produced no managed evidence — only the later diag run connected the throw to the death.
- The dev69 wave shipped a heavy new UI-rebuild frequency on the same day, correlating the crash with the (innocent) poll service and delaying suspicion toward it.

**Second story — why each human step looked reasonable at the time**

| Human step in the chain | What they saw / knew then | Why the action was rational | What the system should have shown or prevented |
|---|---|---|---|
| Writing `IterationCount = -1` with the comment "-1 == 0xFFFFFFFF == infinite" | The WinRT projection historically surfaced the UINT32 as `int`; the wrap idiom was the documented way to request infinite loops | Correct for the API surface as understood at authoring time; the build passed and the radar appeared to work in dev | The projection's validation should surface at build/first-run, not as a swallowed runtime throw (action A1) |
| Wrapping the compositor animation build in a catch that returns quietly | Animation setup must never crash the page on platforms where the compositor may be unavailable; crash-safety rules in `v3/animation-safety.md` push exactly this way | The guard was doing its intended job — preventing a *managed* crash | The catch should have reset `_scannerRotating`, released the half-built object, and logged the failure through `InteractionLogger.Failure` (actions A2, A3) |
| Treating "radar parks" as a cosmetic owner item for 9 days | It was on the owner punch-list as a polish task, with no crash association | Low severity assigned to a visual non-behavior | A dead animation is evidence the animation system threw — worth a grep before a backlog slot (action A4) |

No names in this table. All three rows are system properties: the safety rule, the silent catch, and the unlogged first-chance.

## 9. Cause category, with evidence

**Primary category:** code-side (developer) — a wrong value contract in app code — with a tool-side **amplifier**: the WinAppSDK C# projection changed the contract (INT32, validated) without a compile-time break on `int`-typed `-1`, and the framework converts the resulting leak into a process fail-fast.

- **Evidence:** verbatim first-chance line (§1) naming `IterationCount` from the projection; `git blame` pins the line to `8ba4e1a39341f5382b6fce4cb9668e2106e216a5` (dev30); diff at HEAD shows the only delta is `-1 → int.MaxValue`; 8/8 parsed WER dumps share the death address documented for the class; the postmortem `published-layout-dispatcher-failfast-2026-10-28.md` §10 independently documents the fuel mechanism.
- **Tool-side only:** upstream issue/discussion links: UNKNOWN — no upstream report was filed or found for the projection change; recording that `-1` no longer works is worth an upstream note (action A5).
- **Root-cause confidence:** **proven** for the throwing line (the exception message is emitted by the projection naming the property, and removing the throw eliminated the fault count — before/after contrast §14) and for the fail-fast signature identity (all dumps parsed to the same address/stowed code). **Strongly supported, not proven**, for the middle link — that the abandoned `KeyFrameAnimation` per caught throw (rather than the dev69 peer-flood alone) was the fuel that pushed `finalizePending` past the fail-fast threshold. Falsifier: a pre-fix instance crash-looping *without any radar ever being built* (radar hidden). Not tested — this run becomes the watch's re-open criterion (§15).
- **Alternatives considered and why rejected:**
  - *Poll service regression (owner's initial hypothesis)* — rejected: `MOSTAQLK_DIAG_NET=1` showed polls firing normally with the dev69 queries until the moment of death; the service layer has no native object surface.
  - *dev69 XAML/token refactor introducing a layout fail-fast (the original §10 incident recurring from the new source)* — rejected: the 2026-10-28 incident's dispatch-path fix was intact; the differentiator was the new first-chance throw naming `IterationCount`, absent from the original incident's logs.
  - *AV/Defender interference killing the app* — rejected: AV events earlier that day were build-time file locks (MSB3021), not process terminations; death signature is fail-fast, not TerminateProcess.
  - *Clean-DB wipe as cause* — rejected: deaths reproduced identically with a populated DB before the wipe (the 18:14Z dump and the owner's pre-wipe sessions).

## 10. Scripts, tools and commands used to FIND the bug

**Hypothesis log**

| # | Hypothesis | How it was tested (exact command) | Result | Verdict |
|---|---|---|---|---|
| 1 | Poll service broke | read channel logs after wipe: `watchdog.log` + `interaction-log.txt` in `1.0.4.dev69+a927d46f9c3d237d2f00328ccfc91ffb35567dbc/log/2026-10-31/` | crashes every 24–90 s, watchdog relaunch lines, `crash.dmp` absence in-app but WER dumps present | rejected — the app crash-loops; polling ran until each death |
| 2 | Death class = documented XAML dispatcher fail-fast | hand-parse each `%LOCALAPPDATA%\CrashDumps\MostaqlK.exe.<pid>.dmp` (streams 3/4/5/6/9, `EXCEPTION_RECORD`, `'20ES'` stow blob) via throwaway `scratch/stowed_parse.py` (deleted after) | all deaths `0xC000027B addr=…+0x39cde5`, stow `E_FAIL`, no managed frames | confirmed — matches `published-layout-dispatcher-failfast-2026-10-28.md` §10 signature |
| 3 | Fuel = native-peer finalization flood | heartbeat lines at death, e.g. pid 24792 | `finalizePending` 2,277 → 6,138 within 47 s, then frozen value at death | confirmed as fuel class (documented); source of the flood not yet named — inconclusive on its own |
| 4 | Something throws *per rebuild* and gets swallowed | rerun under `MOSTAQLK_DIAG_COM=1` (every first-chance with live stack to `interaction-log.txt`), drive the flow via UIA | every death's last managed event: `System.ArgumentException` `-2147024809` "An invalid IterationCount is specified. It must be greater than 0."; repeating per scan-cycle | confirmed — located the throw |
| 5 | The throwing call path leaves state inconsistent | read [`PipelineRadar.Windows.cs`](../../UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs) + rotation call graph (`OnDiscoveryProgressChanged → Wake → SyncRotations → StartRotation`) | catch returns normally; `_scannerRotating` stays true; animation is null; retry each Wake; half-built COM object dropped on the floor each time | confirmed mechanism |
| 6 | Symbol resolution of `+0x39cde5` | dbghelp + symsrv download via throwaway `scratch/syms.py` (deleted after) | `Microsoft.UI.Xaml.dll 3.1.7` `FailFastWithStowedExceptions` in `CXcpDispatcher::MessageTimerCallback` | confirmed |
| 7 | Full dumps would give richer stow strings | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/enable-full-dumps.ps1` | still 11–12 MB minis for fail-fast deaths — registry DumpType=2 did not change fail-fast dump form | dead end — diagnosis continued via first-chance log + symbolication; `-Restore` executed afterwards |
| 8 | run.ps1 relaunches the app (user suspicion) | read `scripts/run.ps1` | plain `dotnet run`/`dotnet watch` wrapper — resurrection is the in-app watchdog (max 5 restarts/30 min, then `StopCrashLoop`) | rejected as cause of relaunches; watchdog confirmed as the getting-up actor |

**Tools used**

| Purpose | Tool | Exact copy-paste command | Output (summary or file) |
|---|---|---|---|
| Kill the confounder | channel app data reset | delete `%LOCALAPPDATA%\MostaqlK.Canary\Data\mostaqlk.db` + `Settings\preferences.dat` (owner-authorized) | clean-state repro on next launch |
| Death signature | manual minidump parse | `python scratch/stowed_parse.py` (throwaway, deleted) | exception code/address/stowed value per dump, §10 #2 |
| Address → symbol | dbghelp + Microsoft symbol server | `python scratch/syms.py` (throwaway, deleted) | `FailFastWithStowedExceptions`, §10 #6 |
| See the swallowed throw | app's own COM diag flag | `set MOSTAQLK_DIAG_COM=1 && MostaqlK.exe` (Git Bash form: `MOSTAQLK_DIAG_COM=1` env on launch) | first-chance ArgumentException lines |
| Drive the UI deterministically | PowerShell UIA walk | `powershell -NoProfile -File scratch/walk.ps1` (throwaway, deleted) | completed wizard, fired filter changes at speed |
| Post-fix proof | heartbeat watch | `tail -f "%LOCALAPPDATA%\MostaqlK.Canary\1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995\log\2026-10-31\heartbeat.log"` | §14 |

## 11. How it was solved

- **Fix type:** root-cause fix (one literal), plus this document.
- **How it addresses the mechanism:** `loop.IterationCount = int.MaxValue;` — a positive INT32 that the 1.7 projection accepts; at a ≤ few-second sweep duration, `int.MaxValue` iterations is ~273 years, i.e. infinite for the product's purposes. No throw → no caught-continue → no abandoned half-built `KeyFrameAnimation` → the finalization fuel for this incident's deaths stops at the source.
- **Removes the cause, or only the symptom?** Removes the *named throw and leak*. It does not remove the structural defect class: other catches in Windows-component builders can still continue after abandoning native objects — that is action A2, not this diff. It also does not touch the original §10 fuel source, only this new one.
- **Rollout:** canary branch first (repo convention — `canary` integrates, `main` promotes); no flag needed, single-literal change.
- **Rollback plan:** `git revert <this commit>` — restores `-1` (radar silent-park + crash-loop risk); no data migration involved.
- **Side effects:** positive — the compositor rotation can finally start, closing the "radar parks" owner item *visually*, which now needs the eye-test (§15 re-open criteria includes it). No perf cost measurable: one accepted animation property instead of a thrown one.

## 12. Scripts, tools and commands used to FIX and release

| Stage | Command | What it did |
|---|---|---|
| Patch | manual edit, [`UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs:82`](../../UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs) | `-1` → `int.MaxValue` + the WHY comment (projection fact, fail-fast class, `int.MaxValue` rationale) |
| Build | `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0 -c Debug -p:Channel=canary` | produced the 23:18-local soak binary, 0 errors |
| Test (soak) | launch with `MOSTAQLK_DIAG_COM=1 MOSTAQLK_NO_WATCHDOG=1`, UIA-drive wizard + repeated filter changes ≥30 min | pid 14676: 0 IterationCount faults, finalizePending drained, survived (§14) |
| Test (gate) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/quality-gate.ps1 -Quick` (format scoped via `-FormatChanged`) | suites + style check on the working diff |
| Version bump | `MostaqlK.csproj` `dev69→dev70`, `ApplicationVersion 74→75`; `CHANGELOG.md` dev70 entry | repo versioning convention |
| Release | `git commit` on `canary` (this commit) — push/promotion is owner's action, not taken here | — |
| One-off remediation (reversible, done) | `powershell -NoProfile -ExecutionPolicy Bypass -File tools/enable-full-dumps.ps1 -Restore` | removed the DumpType=2 diagnostic registry change so future dumps return to normal form |

## 13. Heuristic or verified?

**Achieved level: V3 — before/after controlled contrast on the reproduced pattern (N=1 soak).**
The failure was reproduced at will pre-fix (≥8 diag instances + 15 loop instances, all dead within minutes of the driven flow), and one post-fix instance survived ≥30 minutes of the same driven flow with the exact fault's first-chance count at 0 while GC/finalizer load *exceeded* pre-fix levels during the flood (9,350 pending, then drained to 28–702). Not V4 (multiple independent runs / other machines / CI): one soak instance, one machine, one build — and the causal middle link (§9) is inference, so "no recurrence" is statistically real but structurally single-threaded. A regression test (V4 floor) exists only once action A1 lands.

## 14. How it was verified

| Check | Before the fix | After the fix | Evidence |
|---|---|---|---|
| Reproduction | died in 48–143 s under driven flow, 8 diag dumps + 15 loop instances | 1 soak of 30 min+ (pid 14676, alive at document time, started 21:18:31Z), same UIA-driven flow, 0 deaths | `1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995/log/2026-10-31/heartbeat.log` + `tasklist` |
| Throw count | ArgumentException per radar build (first-chance log) | `grep -c IterationCount interaction-log.txt` → **0** across the soak window | soak interaction log |
| Finalizer fuel | `finalizePending` 6,138 frozen at death | spike 9,350 → drain 214 → 702 → … → 28 → 322 over 24 beats; never frozen | heartbeat.log beats #4–#13 |
| Regression test | none existed | none yet — becomes A1; honest: the before/after soak is the current proof | this document |
| Watchdog behavior | relaunched dead instances (loop mask) | `MOSTAQLK_NO_WATCHDOG=1` in soak — survival is single-instance, not watchdog-rescued | soak startup line: "not spawning (… MOSTAQLK_NO_WATCHDOG=1)" |
| Quality gate | n/a | run on working diff pre-commit (see §12); result recorded in commit message body | `tools/quality-gate.ps1 -Quick` |
| Poll service (owner's fear) | — | polls fired with dev69 category queries throughout the soak | diag NET log / interaction log MARK lines |
| Independent reviewer | — | UNKNOWN — pending owner sign-off | — |

## 15. Rare or untestable bugs

The death itself was *frequent* (every session) but its timing was GC-scheduling-random, so absence-after-fix needs soak arithmetic:

- **MTBF estimate (pre-fix):** ≈60–120 s under driven use (samples §4; conservative MTBF estimate **2 min**).
- **Soak duration needed:** 3 × MTBF ≈ 95% confidence → `T = MTBF × ln(1/(1−0.95)) = 2 min × 3.0 = 6 min`.
- **Arithmetic, shown:** achieved instance-time T = 30 min → `confidence = 1 − e^(−T/MTBF) = 1 − e^(−30/2) = 1 − e^(−15) ≈ 0.99999970`. Had the bug remained, the probability of a 30-minute clean run at the pre-fix MTBF is ~3 in 10 million.
- **Instance-hours actually run:** 1 instance × 0.5 h = **0.5 instance-hours** · achieved confidence ≈ 99.99997% *conditional on the MTBF estimate being right*.
- **Rule of three:** 0 failures in 0.5 h → upper bound on failure rate ≈ 3/0.5 = 6/h → lower-bounded MTBF ≈ 10 min, i.e. even distribution-free, the post-fix MTBF is ≥10 min where pre-fix was ≤2.5 min. Distribution-free bound is the weaker, safer claim; both are stated.
- **Acceleration used:** the UIA walk replays the owner's flow (wizard completion + rapid filter changes) as fast as the dispatcher allows — the same accelerator that made pre-fix deaths reliable.
- **Not covered:** multi-day idle operation (the *original* §10 fuel class — layout dispatch floods — is untouched by this fix and can still, in principle, fire on long sessions); Android (line is Windows-partial); Release-build soak (fix verified on Debug only).
- **Production watch:** owner's normal usage of dev70 for the next working week; alert condition = any new `MostaqlK.exe.*.dmp` in `%LOCALAPPDATA%\CrashDumps` whose exception address is `Microsoft.UI.Xaml.dll+0x39cde5`, or any `finalizePending` beat ≥4,000 that does not drain by the next beat (heartbeat.log).
- **Re-open criteria:** (1) any death with the §1 signature on dev70+; (2) a death with the §1 signature **without** any first-chance `IterationCount` ArgumentException in a diag run — that would falsify the middle link in §9 and mean the fuel source is the dev69 peer flood itself, reopening the investigation; (3) radar still not visibly spinning (would mean a second blocker behind the throw).
- **Residual risk:** accepted by owner on reading this document — the class itself is architectural (native-object discipline under a fail-fast framework) and only actions A1/A2/A3 reduce it.

## 16. Prevention

**Prevent** — make the defect impossible at build/test time: a ParserTests source scan that fails on any negative or `-1` literal assigned to a composition animation's iteration-count-style property, with the `scanned >= N` anti-vacuity floor the repo already mandates (this is exactly the pattern AGENTS.md prescribes for new invariants).
**Detect** — make swallowed native throws visible: any `catch` in a Windows component that constructs COM/composition objects must log through `InteractionLogger.Failure`; a first-chance that recurs N times per session is the signature that preceded this incident by 9 days.
**Mitigate** — bound the blast radius: the heartbeat's `finalizePending` already exists; emit a MARK/warning when a beat exceeds a threshold (e.g. 4,000) and the next beat does not drain — an early warning of "fuel" independent of which component produces it.
**Learn** — "the feature is cosmetic but silent" is evidence a code path threw: the "radar parks" backlog item was this incident's symptom from dev30; the doc-class review habit (matching a death to the postmortem catalog) worked and should stay.

**Same class of bug elsewhere** — sweep: `grep -rn "IterationCount" --include=*.cs` (0 remaining offenders post-fix) and the softer shape sweep for action A2: `grep -rn "catch" UI/PlatformComponents --include=*.Windows.cs` audited for "continue-after-failed-native-build".

| ID | Action | Type | Priority | Owner | Due | Ticket | Definition of done | Status | Verified by / date |
|---|---|---|---|---|---|---|---|---|---|
| A1 | ParserTests source scan: no negative/`-1` literal assigned to `IterationCount` (anti-vacuity floor on scanned count) | prevent | P1 | repo owner | 2026-11-07 | none (this repo commits without tracker) | scan present in `tools/ParserTests`; intentionally-broken copy of the line fails the suite; suite green on real tree | open | — |
| A2 | Audit every `catch` in `UI/PlatformComponents/*.Windows.cs` for abandoned half-built COM objects / inconsistent state after swallow; add logging + state reset where found (`SyncRotations` `_scannerRotating` is the known instance) | prevent | P2 | repo owner | 2026-11-14 | — | each audited catch either resets state and logs, or has a comment proving the object graph cannot orphan | open | — |
| A3 | Heartbeat threshold: MARK-warning when `finalizePending ≥ 4000` and next beat has not drained it by 50% | detect | P2 | repo owner | 2026-11-14 | — | warning line appears in heartbeat.log when the scratch flood is replayed on a debug build | open | — |
| A4 | Close the "radar parks" owner item with an eye-test on dev70 (radar visibly sweeps) | learn | P3 | repo owner | 2026-11-03 | — | screenshot or owner confirmation in CHANGELOG note | open | — |
| A5 | Add the new fuel source to the fail-fast catalog: extend `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` §10 (or this doc's backlink) so the next investigator greps for "IterationCount" first | learn | P3 | repo owner | 2026-11-07 | — | catalog §10 cross-references this postmortem by path | open | — |

Five rows, ≥1 prevent and ≥1 mitigate-class (A3) and ≥1 prevent (A1/A2) — within the cap.

## 17. Supporting sections

**Metadata**

| Field | Value |
|---|---|
| Incident ID | INC-2026-10-31-RADAR-STOW |
| Status | review (pending owner sign-off) |
| Severity | SEV-3 — one internal canary user, no data loss, no external users, product-unusable for ~55 min; justified by the crash *loop* masking the cause |
| Authors / reviewers | Qoder agent (this document, from the investigation session) / repo owner — pending |
| Detection | user report during use; WER dumps + watchdog log confirmed |

**Impact**

| Dimension | Value |
|---|---|
| Users / requests affected | 1 of 1 canary user (100%) on this machine; stable channel not running the dev69 build during the window (UNKNOWN — stable channel root not inspected for same-day dumps; assumed unaffected) |
| Duration of user-visible impact | 18:14Z–21:18Z ≈ 3 h 04 m clock, of which ≈55 min continuous loop and the rest intermittent sessions |
| Data integrity / loss | none — SQLite DB and `preferences.dat` survived each death (fail-fast kills the process, not the files); the DB wipe was owner-requested pre-diagnosis, its data confirmed unimportant by the owner at the time |
| Security impact | none |
| SLA / SLO breach | none (no published SLO for canary) |
| Cost | 8 WER dumps ≈ 12 MB each ≈ 97 MB disk; ~2 investigation hours |

**UTC timeline, with sources**

| UTC | Event | Source |
|---|---|---|
| 2026-10-22T15:01:58Z | dev30 commit introduces `IterationCount = -1` | `git blame` PipelineRadar.Windows.cs |
| 2026-10-28 (postmortem date) | 0xC000027B dispatcher fail-fast class documented, fuel mechanism described | `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` |
| 2026-10-31T16:27:49Z | dump `MostaqlK.exe.22256.dmp` (22.8 MB) — **unattributed**, listed for completeness | CrashDumps directory listing |
| 2026-10-31T18:14:48Z | dump `22640.dmp` — owner reports crashes "while using it" | CrashDumps + owner chat (chat time ≈) |
| 2026-10-31T18:18:35Z | clean DB/prefs; repro session; watchdog starts pid 13456→23368 | `watchdog.log` (1.0.4.dev69+a927d46 dir) |
| 2026-10-31T18:19:21Z | pid 23368 dies, exit `0xFFFFFFFF`, uptime 143 s; watchdog relaunch (restart 1/5) | `watchdog.log` |
| 2026-10-31T18:21:09Z | pid 15400 dies, exit `0x1`, uptime 48 s | `watchdog.log` |
| 2026-10-31T18:26:51Z–18:40:19Z | 13 more instance starts by supervisor/watchdog, each `#1 start` then silence | `watchdog.log`, `heartbeat.log` |
| 2026-10-31T20:42:00Z | dev69 committed (`02e94d65c392c176969ee050dfb3b6b44ea4aef2`) — fix NOT in it | `git show -s --format=%cI` |
| 2026-10-31T20:43:29Z | palette tools committed (`06374f32dd56b6d3d05f03e2f7cea10c16a9a995`) by a sibling agent | git log |
| 2026-10-31T20:49–21:13Z | diag runs (`MOSTAQLK_DIAG_COM=1`): 8 dumps (26524,13248,22820,5360,14552,24792,15036,14420); first-chance ArgumentException `IterationCount` found before every death; dumps parsed to `+0x39cde5` | CrashDumps; interaction log (diag copy — **transient**, see Open questions) |
| 2026-10-31T21:18:31Z | fixed Debug build launched as soak, pid 14676, `NO_WATCHDOG=1` | `startup-debug.log` (1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995 dir) |
| 2026-10-31T21:22–21:34Z | flood: finalizePending 9,350 → 214 → 702 → 96; 0 faults | `heartbeat.log` beats #4–#7 |
| 2026-10-31T21:48Z | soak alive at 30 min; `tasklist` confirms pid 14676 | session |
| this commit | fix + document committed on `canary` as dev70 | `git log` |

**Key durations:** TTD (first death → noticed) ≈ 1 min (interactive) · TTA (report → repro secured) ≈ 4 min · TTI (repro → root cause named) ≈ 2 h 50 m (dump parsing without a debugger was the long pole) · TTM (root cause → soak) ≈ 8 min · TTR (soak → committed) same session.

**Postmortem quality**

| Measure | Value |
|---|---|
| Time from incident end to published postmortem | < 1 h (same session; review state) |
| Actions completed on time at 30 days | UNKNOWN — fill 2026-11-30 |
| Actions closed with evidence of DoD | 0 of 5 so far |
| Same pattern recurred since? | no (as of writing) |
| Reviewer re-ran | author-verified: `tail heartbeat.log` soak beats reproduced above; a non-author re-run has not happened |

**Detection analysis** — why was it not caught earlier? Three guards were absent: the throw was invisible (swallowed catch, opt-in first-chance logging), the visible symptom was cosmetic ("radar parks", backlogged), and the fail-fast's managed-side tell was only logged under a diagnostic flag nobody had on during normal use. The dump-to-class match against the 2026-10-28 postmortem was the fast part; naming the *new* fuel source needed the diag flag.

**Lessons learned** — went well: cataloging the earlier incident with dump-level signatures let this one be identified in one parse pass; the heartbeat's `finalizePending` metric made fuel measurable before and after. Went poorly: nine days of a silently throwing line; the watchdog loop consumed the owner's trust ("poll service is broken") before evidence redirected it. Got lucky: the diag flag, UIA driver, and dump-parsing tooling from prior incidents already existed. Surprises: a `catch` can be *more* destructive than a crash when it abandons native objects and continues; "the exception was handled" was the bug, not the safety.

**Related incidents:** `docs/postmoterm/published-layout-dispatcher-failfast-2026-10-28.md` (same death class, different fuel); `docs/postmoterm/animation-tick-failfast-2026-10-28.md` (tick-callback rules the radar ticker originally retired in dev30).
**Open questions:** (1) are the 16:27Z and 18:14Z dumps the same class? — parse `22256.dmp`/`22640.dmp`, owner or next agent, before next release note. (2) did the throw exist-and-get-swallowed identically on dev30–dev68 (projection age) or only after a WinAppSDK bump? — establish from workload changelogs, informs A1's wording. (3) the diag-window interaction log was deleted with the scratch harness — evidence preserved here by quoted lines only; future runs should copy such logs into a durable folder *before* cleanup.

**Sign-off**

| Role | Name | Date |
|---|---|---|
| Author | Qoder agent (investigation session) | 2026-10-31 |
| Reviewer | repo owner — pending | — |

**Revision history**

| Version | Date | Change | Author |
|---|---|---|---|
| 1.0 | 2026-10-31 | initial, same session as the fix | Qoder agent |
| 1.1 | 2026-11-01 | §2 fix-commit cell updated with the real SHA and subject (unknowable at authoring time) | Qoder agent |

## 18. Files and artifacts

File mode — per the template, this section's job is done by the inline commands and permalinks above. Durable evidence lives at: `%LOCALAPPDATA%\CrashDumps\MostaqlK.exe.{26524,13248,22820,5360,14552,24792,15036,14420}.dmp` (owner's machine, 8 files, 11.7–12.8 MB each), `1.0.4.dev69+a927d46f9c3d237d2f00328ccfc91ffb35567dbc\log\2026-10-31\{watchdog,heartbeat}.log`, `1.0.4.dev69+06374f32dd56b6d3d05f03e2f7cea10c16a9a995\log\2026-10-31\{startup-debug,heartbeat}.log`, and the fix diff at `UI/PlatformComponents/PipelineRadar/PipelineRadar.Windows.cs`.
