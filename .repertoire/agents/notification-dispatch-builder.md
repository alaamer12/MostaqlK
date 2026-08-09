# notification-dispatch-builder

## Goal
Implement Step 5 of the MostaqlK V1 plan: real Windows toast notification dispatch with grouping logic, replacing stub bodies in the Notification System (`Services/NotificationGrouper.cs`, `Services/NotificationDispatcher.cs`, `Infrastructure/Notifications/WindowsToastSender.cs`), without touching `EnrichmentWorker`'s existing call site.

## What was done

### Files modified
- `Services/NotificationGrouper.cs` — implemented real buffer/flush mechanics:
  - Added an `Enabled` property (mirrors `notification_grouping_enabled`, default `false`). While disabled, every `Add()` flushes immediately as an individual item — matching "every new project gets its own detailed toast" default behavior from `configuration-reference.md`.
  - `end_of_minute`: on first buffered item, schedules a `System.Threading.Timer` to fire at the next UTC clock-minute boundary, then flushes everything accumulated.
  - `after_minutes`: schedules a timer for `AfterMinutesThreshold` minutes from the first item in the batch.
  - `after_count`: flushes eagerly and synchronously inside `Add()` once `_pending.Count >= AfterCountThreshold` (no timer needed); a 5-minute safety-net timer still runs in case the threshold is never reached, so nothing is buffered forever.
  - Exposed `OnFlush` (`Action<IReadOnlyList<ProjectSummary>>`) event fired whenever a batch is ready — single-item bypass is handled by `WindowsToastSender` at render time (batch size 1 always renders as an individual toast, 2+ as a grouped one), so the grouper itself doesn't need special-case logic for it.
  - `Mode` / `AfterMinutesThreshold` / `AfterCountThreshold` / `Enabled` are all plain mutable properties (no constructor binding), per the requirement that a later `SettingsViewModel` can reconfigure the grouper live.
  - Implements `IDisposable` to clean up any pending timer.
- `Services/NotificationDispatcher.cs` — wired to the grouper: constructor subscribes to `OnFlush`; `NotifyNewProjectsAsync` feeds every project into `_grouper.Add(...)` and returns `Result<bool>.Ok(true)` immediately (buffering/delivery is inherently asynchronous once grouping is enabled); `HandleFlush` fire-and-forgets `WindowsToastSender.SendAsync` for the flushed batch.
- `Infrastructure/Notifications/WindowsToastSender.cs` — real implementation using `Microsoft.Windows.AppNotifications.AppNotificationManager` + `AppNotificationBuilder` (Windows App SDK). Registers the notification manager once (thread-safe lazy `Register()`), builds an individual toast (title + owner/time/proposal-count subtitle) when the batch has exactly 1 item, or a grouped toast ("يوجد N مشاريع جديدة — تفقدها هنا" + up to 2 titles, staying within the 3-text-element toast content limit) for 2+. Both toast kinds attach launch arguments (`projectId`/`url` or `filter=unread`) with a `TODO` marking the deep-link hook point for a later UI/routing step. Native API failures are caught and mapped to `NotificationErrors.ToastDeliveryFailed` → `Result<T>.Err`.

### Files not touched (as required)
- `Services/Pipeline/WorkerPool/EnrichmentWorker.cs` — confirmed its call site (`_notificationDispatcher.NotifyNewProjectsAsync(new List<ProjectSummary> { ToSummary(details) }, cancellationToken)`) matches `INotificationDispatcher.NotifyNewProjectsAsync(IReadOnlyList<ProjectSummary>, CancellationToken)` exactly — no interface/signature changes were needed.
- `UI/`, `Features/`, `Infrastructure/Http/`, `Infrastructure/Database/`, `Services/Pipeline/` — untouched.

## Toast API chosen and why
`Microsoft.Windows.AppNotifications.AppNotificationManager` (Windows App SDK) — the modern, native toast API for WinUI3 apps. Chosen over the older `Microsoft.Toolkit.Uwp.Notifications` / classic `Windows.UI.Notifications.ToastNotificationManager` because:
- MostaqlK's Windows head is already a `MauiWinUIApplication` (`Platforms/Windows/App.xaml.cs`), so the Windows App SDK types are already available transitively through `Microsoft.Maui.Controls` for the `net10.0-windows10.0.19041.0` target — confirmed by a clean baseline build with **no new package reference needed** in the main project.
- It's the API Microsoft recommends going forward for WinUI3/Windows App SDK apps (vs. the UWP-era API), and integrates with the same `AppNotificationBuilder` fluent API used throughout this codebase's style.

## Verification performed
- Baseline build (`dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0`) confirmed **0 errors** before any changes.
- After implementation, rebuilt the same target — **0 errors**, only pre-existing/unrelated warnings (obsolete `Frame` usage in generated XAML, nullable-reference warnings in HTTP parsers, `MVVMTK0045` AOT warnings on `[ObservableProperty]` fields, `NU1903` SQLite advisory).
- End-to-end manual verification: created a throwaway standalone console project under `scratch/NotificationVerify/` (compiled the real `WindowsToastSender.cs`/`NotificationErrors.cs`/`ProjectSummary.cs`/`Result.cs`/`DomainError.cs` sources, referencing `Microsoft.WindowsAppSDK` directly) that constructed fake `ProjectSummary` instances and called `WindowsToastSender.SendAsync` for both an individual project and a 3-item batch.
  - First run surfaced a real bug: the grouped toast exceeded `AppNotificationBuilder`'s 3-text-element cap (`"The parameter is incorrect. Maximum number of text elements added"`), which was fixed by capping the grouped toast to at most 2 listed project titles alongside the header line.
  - Second run: both `Individual toast: IsOk=True` and `Grouped toast: IsOk=True` — real native toast notifications were shown successfully, not just "didn't throw."
  - Deleted the `scratch/NotificationVerify/` throwaway project afterward per repo convention; it does not remain in the tree.

## Build status
**Succeeded**, 0 errors, on `net10.0-windows10.0.19041.0`.

## Notes / follow-ups for later steps
- Toast click deep-linking is stubbed with `TODO` comments (arguments are attached to the notification but not yet consumed) — this is explicitly a `Features/UI` routing concern for a later step, per the issue's own scope boundary.
- `NotificationGrouper.Enabled`/`Mode`/thresholds default to the spec's stated defaults (`Enabled = false`, `Mode = EndOfMinute`, `AfterMinutesThreshold = 1`, `AfterCountThreshold = 5`) but are not yet wired to any persisted configuration — that's `SettingsViewModel`'s job in a later step, as noted in the issue.
