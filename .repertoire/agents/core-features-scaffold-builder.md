# core-features-scaffold-builder — Report

## Goal
Scaffold `Core/`, `Models/`, `Services/` (+ `Services/Pipeline`, `DiffEngine`, `WorkerPool`),
`Infrastructure/` (`Http`, `Database`, `Notifications`), and `Features/` (Projects, Notifications,
Settings — MVVM with CommunityToolkit.Mvvm) per the V1 architecture in
`system-components.md`/`structure.md`, wire DI in `MauiProgram.cs`, register routes in
`AppShell.xaml`, and build the project.

## Actions taken
- Read `structure.md`, `system-components.md`, and the csproj before starting.
- Created `Core/`: `Result.cs`, `DomainError.cs`, `ErrorAttributes.cs` (`ErrorCode`, `ErrorCategory`,
  `NeitherContract`, `ErrorModule` attributes), `ErrorCodeRegistry.cs`, `Domain/BatchResult.cs`,
  `Domain/ItemFailure.cs`.
- Created `Models/`: `ProjectSummary.cs`, `ProjectDetails.cs`, `Owner.cs`, `ProjectSkill.cs`,
  `Asset.cs`, `EnrichmentStatus.cs`.
- Created `Services/`: `INotificationDispatcher`/`NotificationDispatcher`, `NotificationGrouper`
  (grouping enum `EndOfMinute`/`AfterMinutes`/`AfterCount`).
- Created `Services/Pipeline/`: `IPollService`/`PollService`, `PollErrors.cs`, `InFlightTracker.cs`,
  `DiscoveryQueue.cs` (Channel-backed), `TokenBucketRateLimiter.cs`, `IEnrichmentService`/
  `EnrichmentService`, `HttpErrors.cs`; `DiffEngine/`: `DiffEngine.cs`, `DiffResult.cs`,
  `IKnownStateProvider.cs`, `SqliteCommittedProvider.cs`, `InFlightSetProvider.cs`,
  `DiffErrors.cs`; `WorkerPool/`: `WorkerPool.cs`, `EnrichmentWorker.cs`.
- Created `Infrastructure/Http/`: `IProjectScraper`/`MostaqlScraper`, `HttpErrors.cs`,
  `Parsers/ListingParser.cs`, `Parsers/DetailParser.cs`, `Parsers/ParseException.cs`.
- Created `Infrastructure/Database/`: `SqliteConnectionFactory.cs` (uses `Microsoft.Data.Sqlite`),
  `DatabaseSchemaException.cs`, `DatabaseErrors.cs`, `Migrations/README.md`,
  `IProjectRepository`/`ProjectRepository`, `IOwnerRepository`/`OwnerRepository`,
  `IAssetRepository`/`AssetRepository`, `SearchIndex/FtsQueryService.cs`,
  `SearchIndex/FtsSchema.sql` (commented FTS5 DDL).
- Created `Infrastructure/Notifications/`: `WindowsToastSender.cs`, `NotificationErrors.cs`.
- Added NuGet packages `Microsoft.Data.Sqlite` (10.0.10) and `CommunityToolkit.Mvvm` (8.4.2) via
  `dotnet add package`.
- Created `Features/Projects/ViewModels/`: `ProjectFeedViewModel.cs`, `ProjectCardViewModel.cs`,
  `StatusBarViewModel.cs` (all `ObservableObject` + `[ObservableProperty]`/`[RelayCommand]`).
- Created `Features/Projects/Views/`: `MainWindowPage.xaml`(+.cs) — RTL sidebar nav
  (المشاريع/البحث المتقدم/التنبيهات/الإعدادات/حول التطبيق) + `CollectionView` project feed;
  `ProjectCard.xaml`(+.cs) — unread/read card using `MostaqlK.UI.PlatformComponents.AppCard`
  and a `DataTrigger` for bold-when-unread (avoided a nonexistent converter resource);
  `AboutPage.xaml`(+.cs) — minimal title/version/body placeholder.
- Created `Features/Notifications/Views/RecentNotificationsFlyout.xaml`(+.cs) and
  `Features/Notifications/ViewModels/NotificationCenterViewModel.cs`.
- Created `Features/Settings/Views/SettingsPanel.xaml`(+.cs) — poll interval/rate, grouping
  mode/threshold, dark-mode `Switch` (plain `Switch`, not `AppToggle`, to keep this file
  independently compilable), "مشاريع مضافة اليوم" stat card; `Features/Settings/ViewModels/SettingsViewModel.cs`.
- Updated `AppShell.xaml` to register `MainWindowPage` (new default first `ShellContent`),
  `SettingsPanel`, and `AboutPage` routes, set `FlowDirection="RightToLeft"`, and **kept** the
  original template `MainPage` route (`Route="MainPage"`) unchanged/untouched for compatibility.
- Updated `MauiProgram.cs` with all requested DI registrations (`IPollService`→`PollService`,
  `IEnrichmentService`→`EnrichmentService`, `IProjectScraper`→`MostaqlScraper`,
  `IProjectRepository`/`IOwnerRepository`/`IAssetRepository`, `INotificationDispatcher`, plus
  pipeline singletons `InFlightTracker`, `DiscoveryQueue`, `TokenBucketRateLimiter`, `DiffEngine`,
  `WorkerPool`, and the Views/ViewModels created above).

## Key architectural decisions
- **CommunityToolkit.Mvvm**: added and used throughout (`ObservableObject`, `[ObservableProperty]`,
  `[RelayCommand]`) since it was absent from the csproj beforehand.
- **About page location**: placed under `Features/Projects/Views/AboutPage.xaml` (not a new
  `Features/About` folder) since it's a single static page reachable only from the Projects
  sidebar nav, not an independent feature slice — documented inline in the XAML comment.
- **MainPage handling**: left the MAUI-template `MainPage.xaml`/`.xaml.cs` completely untouched;
  `AppShell.xaml` now lists `MainWindowPage` as the first `ShellContent` (so it opens by default)
  while keeping `MainPage`'s own route registered for backward compatibility.
- Did **not** touch `UI/`, `Resources/Styles/`, or `Platforms/Windows/Styles/` as instructed.

## Verification
- Ran `dotnet restore` then `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` from
  the project root: **build succeeded with 0 errors**. Only warnings: `NU1903` (known SQLitePCLRaw
  advisory, pre-existing transitive risk of the SQLite package) and `MVVMTK0045` (AOT/WinRT
  source-gen advisory on `[ObservableProperty]` fields — informational, no action required for
  this scaffold).
- `MostaqlK.UI.PlatformComponents.AppCard`/`AppButton` referenced from `ProjectCard.xaml` and
  `MainWindowPage.xaml` resolved successfully, confirming the parallel UI-components agent's work
  was present and integrated cleanly at build time.

## Integration notes / TODOs
- All pipeline/repository/parser method bodies are stubs (`NotImplementedException` or `TODO`
  comments) as instructed — no real scraping/parsing/persistence/notification logic yet.
- `SettingsPanel.xaml` uses a plain `Switch` for dark mode rather than `AppToggle`; swap once that
  component's API is confirmed stable.
- `Picker.SelectedItem` in `SettingsPanel.xaml` is bound to the `NotificationGroupingMode` enum
  against a `string[]` items source — compiles fine (runtime-only concern), but will need a
  converter or enum-aware picker once real settings persistence is implemented.
