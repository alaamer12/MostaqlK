### Pipeline Persistence and Visualization Design (V1)

#### 1. The Problem: Memory Volatility
The application's project discovery pipeline was originally designed as a two-tier in-memory system:
*   **Tier 1 (PollService)**: Discovers new IDs and puts them in a memory queue.
*   **Tier 2 (EnrichmentWorkers)**: Consumes the queue and fetches full details.

**The Issue**: If the application is closed while projects are in the queue or being processed, that state is lost. While a new scan on startup "re-discovers" these projects, any project that has moved off the first page of the Mostaql feed during the downtime is effectively lost forever (or until a deep scan is performed).

#### 2. The Internal Solution: Persistent Backlog
To ensure zero data loss, we have implemented a **Persistent Backlog** using a dedicated SQLite table:

*   **`discovery_backlog` Table**: A lightweight ledger that stores `project_id` and `discovered_at`.
*   **Discovery Phase**: When the `PollService` finds a new ID, it performs an `INSERT OR IGNORE` into the `discovery_backlog` table *before* enqueuing it in memory.
*   **Completion Phase**: When an `EnrichmentWorker` successfully saves a project to the database, it `DELETE`s the ID from the `discovery_backlog`.
*   **Recovery Phase**: On startup, the `WorkerPool` queries the backlog table and re-hydrates the in-memory `DiscoveryQueue` with any interrupted projects.
*   **Auto-Cleanup**: A 30-day automatic pruning policy ensures that stale or un-enrichable projects (e.g., deleted on source) do not bloat the database indefinitely.

#### 3. The External Solution: Lighthouse Radar
To provide professional, real-time feedback on this background machinery, we introduced the **Lighthouse Radar** UI component.

*   **Visual Representation**:
    *   **Outer Ring (Blue)**: Represents the **Discovery Tier**. Pulses and fills as the scraper scans the Mostaql feed.
    *   **Middle Ring (Amber)**: Represents the **Backlog Pressure**. Grows thicker as the persistent queue fills up, showing how much work is pending.
    *   **Inner Ring (Green)**: Represents **Enrichment Activity**. The ring is divided into segments corresponding to worker slots; segments throb as workers fetch data.
    *   **Radial Sweep (White Needle)**: Represents the **Snapshot/Diff Engine**. A rotating radar sweep that pulses when the `DiffEngine` takes a point-in-time snapshot to synchronize state and prevent duplicates.
*   **UX Implementation**: The radar lives in the **pipeline dashboard panel** (`PipelineDashboardPanel`), a collapsible column opposite the nav sidebar: the dial on top, then discovery/queue summary cards, the three worker rows, and a drill-in block that follows the dial's selection. It was originally a 56dp dial in the application footer, but at that size it was easy to miss, and because the radar parks its ticker and fades its scanner once the pipeline settles, it appeared to disappear altogether when idle. The panel keeps every figure permanently readable (and collapses to a ~40dp status rail that still shows worker states and backlog utilisation), so the "engine room" heartbeat is legible whether or not anything is currently moving. The boundary between the feed and the panel is user-pannable via `SplitterHandle`, with a minimum width per section.

#### 4. Technical Architecture
*   **Services**: `PollService`, `WorkerPool`, and `EnrichmentWorker` now report live metrics to a centralized `GlobalAppStatusService`.
*   **UI Component**: `PipelineRadar` uses `Microsoft.Maui.Graphics` for performant, hardware-accelerated circular drawing and animations.
*   **Data Integrity**: The combination of the persistent backlog and the `InFlightTracker` snapshot ensures that the pipeline is both resilient to crashes and safe from race conditions.

#### 5. Animation System
The radar is **one state-driven pipeline visualization**, not a collection of unrelated timers. The
data model decides the motion: `ProjectDiscovered → QueueEnter → WorkerAssignment → WorkerProcessing
→ WorkerCompletion → ProjectExit`.

*   **Structure (3 files)**:
    *   `RadarPipelineState` — a pure (MAUI-free) model holding every animated value: scanner progress, queue utilisation + numeric readout, per-worker intensity/breath/highlight, pooled pulses, and the project *tokens* with their pipeline stage. `Advance(dt)` moves everything one frame and reports whether anything is still moving.
    *   `PipelineRadarDrawable` (`IDrawable`) — renders that state and nothing else; no timers, no per-frame allocations (cached colours/dash patterns, pooled objects).
    *   `PipelineRadar` (`ContentView`) — hosts **one** ticker (a single committed MAUI `Animation`) which advances the state and calls `GraphicsView.Invalidate()`. It parks itself once the state has settled, so an idle pipeline costs no frames.
*   **Interruptible by construction**: pipeline events only change *targets*. Values approach their target with a critically damped spring, so `25 → 26 → 30` arriving mid-flight simply redirects — the radar is never reset, and travelling tokens re-base from their current position.
*   **Discovery event**: scanner head → detection pulse → token appears on the outer ring → token travels into its queue slot → *then* the queue arc and number grow. The motion explains why the value changed instead of the number jumping on its own. Simultaneous discoveries are staggered by 50ms.
*   **Queue tier**: the arc length is backlog utilisation (`QueueCount / QueueCapacity`), always interpolated. Queued tokens keep their identity: when the ordering changes they slide to their new slot rather than being removed and recreated.
*   **Worker tier**: `Idle` (static, low intensity), `Processing` (brighter, soft glow, restrained breathing with a per-worker phase offset plus a highlight travelling through the segment), `Completed`/`Error` (brief brightness increase → outward completion pulse → back to idle). A finished project exits the radar and is only released from the collection *after* its exit animation.
*   **Interaction**: pointer hit-testing per ring drives fast (~150ms) hover emphasis plus a data panel for the discovery / queue / worker tooltips (interpolated figures, 180ms fade + small translation, interruptible via `CancellationTokenSource`). Clicking a worker focuses it: the other workers quieten and a connector is drawn toward its project, showing `queue item → worker N`.
*   **Accessibility**: `MotionPreferences` reads the OS reduce-animations setting; when set, the scanner, breathing and pulses stop and travel becomes a fade — every state change is still communicated.
*   **Telemetry**: `PollService` reports scan start/stop, scan interval and per-id discoveries; `EnrichmentWorker` reports the exact queue→worker handover plus completion/error and the live backlog count. `GlobalAppStatusService.Workers[]` carries the figures the tooltips show (current project, processing time, completed count, success rate, oldest/average queue wait).
