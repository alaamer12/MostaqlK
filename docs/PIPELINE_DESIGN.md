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
*   **UX Implementation**: The radar is integrated into the application footer, providing a high-tech "engine room" heartbeat that assures the user the system is actively and safely processing data.

#### 4. Technical Architecture
*   **Services**: `PollService`, `WorkerPool`, and `EnrichmentWorker` now report live metrics to a centralized `GlobalAppStatusService`.
*   **UI Component**: `PipelineRadar` uses `Microsoft.Maui.Graphics` for performant, hardware-accelerated circular drawing and animations.
*   **Data Integrity**: The combination of the persistent backlog and the `InFlightTracker` snapshot ensures that the pipeline is both resilient to crashes and safe from race conditions.

#### 5. Animation System
The radar features a sophisticated, state-driven animation system implemented in .NET MAUI:
*   **Discovery Pulse**: When a new project is discovered, a particle animation originates from the outer ring and travels to the queue ring.
*   **Transition to Worker**: When a worker picks up a project from the queue, a particle travels from the middle ring to the specific inner worker segment.
*   **Worker States**: Workers transition through `Processing` (pulsing glow), `Completed` (completion pulse), and `Error` (red glow) states.
*   **Performance**: The system uses MAUI's `Animation` API to interpolate properties in the `PipelineRadarDrawable` at 60fps, invalidating only the `GraphicsView` when necessary.
