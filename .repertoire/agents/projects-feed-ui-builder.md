# projects-feed-ui-builder — Step 6 report

## Goal
Implement Step 6 of the MostaqlK V1 plan: make the Projects feed and a new Project Details page
fully functional against real data, with a new debounced-search component hierarchy.

## Files created
- `UI/PlatformComponents/DebouncedEntry/DebouncedEntry.cs`
- `UI/PlatformComponents/SearchInputField/SearchInputField.cs`
- `UI/PlatformComponents/SearchInputField/SearchInputField.Windows.cs`
- `Infrastructure/Http/AssetDownloadService.cs` (`AttachmentStatus`, `AttachmentResolution`, `AssetDownloadService`)
- `Features/Projects/ViewModels/ProjectDetailsViewModel.cs` (`ProjectDetailsViewModel` + `AttachmentItemViewModel`)
- `Features/Projects/Views/ProjectDetailsPage.xaml` / `.xaml.cs`
- `.repertoire/agents/projects-feed-ui-builder.md` (this report)

## Files modified
- `Features/Projects/ViewModels/ProjectFeedViewModel.cs` — real `LoadAsync`/`RefreshAsync`/`SearchAsync`/
  `ClearSearchAsync`/`SelectProjectAsync`, `SearchQuery`, `IsLoading`/`IsEmpty`/`HasError`/`ErrorMessage`/
  `ShowFeed`, queries `IProjectRepository.GetRecentAsync` or `FtsQueryService.SearchAsync`.
- `Features/Projects/ViewModels/ProjectCardViewModel.cs` — added `SelectCommand` (delegates to the
  feed's select handler via constructor callback), `NotifyPropertyChangedFor(IsUnread)`.
- `Features/Projects/ViewModels/StatusBarViewModel.cs` — reads `TokenBucketRateLimiter.AvailableTokens`
  via a 1s `Timer` poll and injects `IPollService` (read-only, no pipeline changes).
- `Features/Projects/Views/MainWindowPage.xaml` / `.xaml.cs` — `SearchInputField` bound to
  `SearchQuery`/`SearchCommand`/`ClearSearchCommand`; four feed states (`ShimmerBox` loading,
  `LabelWithSubText` error/empty, `CollectionView` success); sidebar + feed recomposed through
  `NavigationControl.Build` in the code-behind.
- `Features/Projects/Views/ProjectCard.xaml` — `AppCard.IsUnread` binding, tap gesture → `SelectCommand`.
- `UI/PlatformConcepts/NavigationControl.cs` — added `Build(navRail, content)` for real Windows
  side-panel composition from caller-supplied content (previously an empty static `Grid`).
- `UI/DesignSystem/ShimmerBox.cs` — real sweeping shimmer overlay animation (was an empty stub).
- `UI/DesignSystem/LabelWithSubText.cs` — real composed `Text`/`SubText` labels with row-hiding (was an empty stub).
- `Services/Pipeline/TokenBucketRateLimiter.cs` — added read-only `AvailableTokens` property (lazy refill-on-read, no new background timer/event plumbing added to the limiter itself).
- `AppShell.xaml` / `.xaml.cs` — registered `ProjectDetailsPage` as a detail-only route via `Routing.RegisterRoute` (not a `ShellContent` tab).
- `MauiProgram.cs` — DI registrations for `FtsQueryService`, `AssetDownloadService`, `ProjectDetailsViewModel`, `ProjectDetailsPage`.
- `UNITS.md` — added `DebouncedEntry`/`SearchInputField` rows; flipped `AppButton`, `AppCard`, `AppEntry`,
  `NavigationControl`, `ShimmerBox`, `LabelWithSubText` to `Implemented` (only units actually wired into
  real UI this task; `AppToggle`/`TruncatingLabel`/`DesignTokens` left as `Scaffold` — not touched).

## Key implementation decisions
- **Debounce mechanism**: `CancellationTokenSource` restart-on-keystroke in `DebouncedEntry` — every
  `TextChanged` cancels the previous pending fire and starts a fresh `Task.Delay`, keeping it simple and
  cancel-safe without a `System.Timers.Timer`.
- **AssetDownloadService cookie handling**: mirrors `attachment_downloader.py` exactly — cookie read only
  from `MOSTAQL_COOKIE` / `MOSTAQL_COOKIE_FILE` env vars, never hardcoded, no login flow implemented;
  HTML-sniffs the response body to detect a rejected/expired session (`AuthFailed`) vs a real file
  (`Downloaded`, saved under `FileSystem.CacheDirectory/attachments`).
- **Navigation wiring**: `ProjectFeedViewModel.SelectProjectAsync` marks the card read and calls
  `Shell.Current.GoToAsync("ProjectDetailsPage?projectId={id}")`; `ProjectDetailsPage` uses
  `[QueryProperty(nameof(ProjectId), "projectId")]` to receive it and loads via
  `ProjectDetailsViewModel.LoadAsync(long)`.
- **NavigationControl**: extended from an empty static `Grid` factory to a real `Build(navRail, content)`
  composition helper; `MainWindowPage`'s code-behind reparents its XAML-declared nav rail and feed content
  into the real Windows side-panel layout at construction time (XAML only allows one root `Content`
  element, so both are declared under a temporary root `Grid` and immediately detached/recomposed).
- **StatusBarViewModel rate-budget indicator**: kept intentionally minimal per the task guidance — a 1s
  `Timer` polling `TokenBucketRateLimiter.AvailableTokens` (a small new read-only property added to the
  limiter), no event-based plumbing added to the pipeline services themselves.

## Not implemented / deferred
- `ProjectCardViewModel.MarkAsRead` does not yet persist the read state to SQLite — `IProjectRepository`
  has no `UpdateReadStateAsync` method; left a `TODO` since adding new repository methods was out of the
  explicitly listed scope for this task. The UI still reflects the read state immediately in-memory.
- `AppToggle`, `TruncatingLabel`, and `DesignTokens` were left as `Scaffold` in `UNITS.md` — not used by
  the feed/details work in this task, so left untouched per the "only flip units you actually touched" rule.

## Verification
- `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` — **succeeded, 0 errors** (only
  pre-existing/expected warnings: `NU1903` SQLitePCLRaw advisory, `MVVMTK0045` AOT/WinRT notices for
  `[ObservableProperty]` fields — consistent with the rest of the existing codebase's ViewModels).
