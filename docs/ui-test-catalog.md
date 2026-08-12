# UI Test Catalog

Enumerates every dynamic-data-bound, clickable, inputtable, pannable/scrollable, animated,
draggable, keyboard/"aria" (Enter-triggered), or focusable element across the 4 MVP mockups
(`.repertoire/design/mvp/{projects,project-details,settings,about}.html`), the shared
`AppSidebar`, and the `RecentNotificationsFlyout`. Every element listed here now carries a
stable `AutomationId` (see the naming convention in `UNITS.md` → Diagnostics), and the
commonsense counterpart state for every explicit control (pause↔resume, save↔reload-verify,
refresh-once↔refresh-again, open↔close, select↔deselect) is called out either as its own row or
as a note under the primary row.

Planned Appium test names below are the target methods for Step 3-6 of
`.junie/plans/appium-ui-test-catalog-and-fixes.md`; they do not exist yet as of this catalog's
authoring (Step 2).

## Shared Sidebar (`AppSidebar.xaml`)

Rendered on all 4 pages (`MainWindowPage`, `ProjectDetailsPage`, `SettingsPanel`, `AboutPage`).

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Projects nav row | `Sidebar_ProjectsButton` | Clickable | `AppSidebar.ProjectsClicked` event → page's `On*ProjectsNavClicked` → `Shell.GoToAsync("//MainWindowPage")` (no-op on `MainWindowPage` itself) | `UI/PlatformComponents/AppSidebar/AppSidebar.xaml:54`, handler `AppSidebar.cs:103` | `SidebarNavigationTests.Click_ProjectsRow_NavigatesToProjects` |
| Advanced search nav row | `Sidebar_AdvancedSearchButton` | Clickable | `AppSidebar.AdvancedSearchClicked` event → `On*AdvancedSearchNavClicked` (TODO: route not implemented yet) | `AppSidebar.xaml:64`, handler `AppSidebar.cs:118` | `SidebarNavigationTests.Click_AdvancedSearchRow_IsWiredButRouteless` |
| Notifications nav row | `Sidebar_NotificationsButton` | Clickable | `AppSidebar.NotificationsClicked` event → `MainWindowPage.OnNotificationsNavClicked` (toggles `NotificationsFlyout.IsVisible`) or other pages' handler (navigates back to `MainWindowPage` first) | `AppSidebar.xaml:74`, handler `AppSidebar.cs:132` | `SidebarNavigationTests.Click_NotificationsRow_OpensFlyout` / `Click_NotificationsRow_Close` |
| Notifications unread badge | *(none — static count label, no gesture)* | Dynamic-data | `AppSidebar.NotificationCount` bindable property, set from `NotificationCenterViewModel.UnreadBadgeCount` | `AppSidebar.xaml:88` | *(covered indirectly by `ProjectsPageTests`)* |
| Settings nav row | `Sidebar_SettingsButton` | Clickable | `AppSidebar.SettingsClicked` event → `On*SettingsNavClicked` → `Shell.GoToAsync("//SettingsPanel")` / `"SettingsPanel"` | `AppSidebar.xaml:93`, handler `AppSidebar.cs:147` | `SidebarNavigationTests.Click_SettingsRow_NavigatesToSettings` |
| About nav row | `Sidebar_AboutButton` | Clickable | `AppSidebar.AboutClicked` event → `On*AboutNavClicked` → `Shell.GoToAsync("//AboutPage")` / `"AboutPage"` (no-op on `AboutPage` itself) | `AppSidebar.xaml:103`, handler `AppSidebar.cs:162` | `SidebarNavigationTests.Click_AboutRow_NavigatesToAbout` |
| "مشاريع مضافة اليوم" stat value | *(none — static label, no gesture)* | Dynamic-data | `AppSidebar.StatValue` bindable property ← `ProjectFeedViewModel.ProjectsAddedTodayText` / `SettingsViewModel.ProjectsAddedTodayCount` ← `IProjectRepository.CountAddedTodayAsync` | `AppSidebar.xaml:128` | *(covered indirectly)* |

## Projects (`MainWindowPage.xaml` — projects.html)

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Search input | `Projects_SearchInput` | Inputtable, keyboard/aria (Enter via debounce), focusable | `ProjectFeedViewModel.SearchCommand` (debounced) → `SearchAsync` → `FtsQueryService.SearchAsync` / `IProjectRepository.GetRecentAsync` | `MainWindowPage.xaml:76-88` | `ProjectsPageTests.Type_SearchInput_Enter_FiltersFeed` |
| Search clear ("x") button | *(inside `SearchInputField`'s built-in clear button — not separately catalogued/owned by this page's XAML)* | Clickable | `ProjectFeedViewModel.ClearSearchCommand` → `ClearSearchAsync` → reloads unfiltered feed | `MainWindowPage.xaml:86` (`ClearCommand` binding) | `ProjectsPageTests.Click_ClearSearch_RestoresFullFeed` |
| Pause/resume pill | `Projects_TogglePollingButton` | Clickable, dynamic-data (label/icon flips) | `ProjectFeedViewModel.TogglePollingCommand` → `TogglePolling` → `IPollService.SetPaused` | `MainWindowPage.xaml:126-149`, VM `ProjectFeedViewModel.cs:205` | `ProjectsPageTests.Click_TogglePolling_Pauses` / `Click_TogglePolling_Resumes` (counterpart) |
| Settings gear button | `Projects_SettingsGearButton` | Clickable | `MainWindowPage.OnGearTapped` → `Shell.GoToAsync("//SettingsPanel")` | `MainWindowPage.xaml:151-166`, handler `MainWindowPage.xaml.cs:51` | `ProjectsPageTests.Click_GearButton_NavigatesToSettings` |
| Retry button (error state) | `Projects_RetryButton` | Clickable | `ProjectFeedViewModel.RefreshCommand` → `RefreshAsync` → `LoadAsync` → `IProjectRepository`/`FtsQueryService` | `MainWindowPage.xaml:213` | `ProjectsPageTests.Click_Retry_OnErrorState_Reloads` |
| Projects feed list | `Projects_ProjectsCollectionView` | Pannable/scrollable, dynamic-data | `ProjectFeedViewModel.Projects` ← `IProjectRepository.GetRecentAsync`/`FtsQueryService.SearchAsync` | `MainWindowPage.xaml:221-229` | `ProjectsPageTests.Scroll_ProjectsFeed_RevealsMoreCards` |
| "تحديد الكل كمقروء" (mark all read) | `Projects_MarkAllReadLabel` | Clickable, dynamic-data | `ProjectFeedViewModel.MarkAllReadCommand` → `MarkAllRead` → `ProjectCardViewModel.MarkAsRead` (per card), updates `UnreadCount` | `MainWindowPage.xaml:247-256` | `ProjectsPageTests.Click_MarkAllRead_ClearsUnreadCount` |
| Refresh `↻` icon | `Projects_RefreshLabel` | Clickable, animated (feedback), dynamic-data | `LastScanStatus.RefreshCommand` → `ProjectFeedViewModel.RefreshCommand` → `RefreshAsync` → `IPollService.RequestCheckNow` + `LoadAsync`; the readout then follows `GlobalAppStatusService.LastScanCompletedAt` | `MainWindowPage.xaml:280-288` (inside the shared `LastScanStatus` unit) | `ProjectsPageTests.Click_Refresh_UpdatesLastScanText` / `Click_Refresh_Twice_NoDoubleFire` (counterpart) |
| "آخر فحص" readout | `Projects_LastScanLabel` (inner label of `Projects_LastScanStatus`) | Dynamic-data | Shared `LastScanStatus` unit, worded by `Core/Formatting/LastScanText` from `GlobalAppStatusService.LastScanCompletedAt` (written by `PollService` every cycle) — it no longer times from the feed's own last database load | `MainWindowPage.xaml:280-288`, unit `UI/PlatformComponents/LastScanStatus/` | *(asserted by `ProjectsPageTests.Click_Refresh_UpdatesLastScanText`)* |
| Live status pill ("مباشر"/"متوقف") | *(none — static label, no gesture)* | Dynamic-data | `ProjectFeedViewModel.LiveStatusText` ← `IsPollingActive` | `MainWindowPage.xaml:117-122` | *(asserted as part of `Click_TogglePolling_*`)* |
| Poll interval / rate-limit labels | *(none — static labels, no gesture)* | Dynamic-data | `ProjectFeedViewModel.PollIntervalText`/`RateLimitText` ← `IPollService.PollIntervalSeconds`/`TokenBucketRateLimiter.Capacity` | `MainWindowPage.xaml:93-106` | *(asserted as part of `SettingsPageTests.Save_PollInterval_ReflectsOnProjectsPage`)* |
| Notifications flyout | `Notifications_Flyout` | Clickable-to-open (via sidebar), dynamic-data | Toggled by `MainWindowPage.OnNotificationsNavClicked`/`OpenNotificationsFlyout`; content from `NotificationCenterViewModel.RecentNotifications` ← `INotificationDispatcher.RecentHistory` | `MainWindowPage.xaml:282-288` | `ProjectsPageTests.Open_NotificationsFlyout_ShowsRecent` / `Close_NotificationsFlyout` (counterpart) |

## Project Card (`ProjectCard.xaml`)

Rendered once per item inside `Projects_ProjectsCollectionView`.

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Whole card (title, description, skills, client, stats, unread dot) | `ProjectCard_Root` | Clickable, dynamic-data (every bound field) | `ProjectCardViewModel.SelectCommand` → `ProjectFeedViewModel.SelectProjectAsync` → `ProjectCardViewModel.MarkAsRead` + `Shell.GoToAsync("ProjectDetailsPage?projectId=...")` | `Features/Projects/Views/ProjectCard.xaml:16-20` | `ProjectsPageTests.Click_ProjectCard_NavigatesToDetails` |

## Project Details (`ProjectDetailsPage.xaml` — project-details.html)

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Back button | `Details_BackButton` | Clickable | `ProjectDetailsPage.OnProjectsNavClicked` → `Shell.GoToAsync("//MainWindowPage")` | `ProjectDetailsPage.xaml:33`, handler `ProjectDetailsPage.xaml.cs:51` | `ProjectDetailsPageTests.Click_Back_ReturnsToProjects` |
| Details scroll view (title/description/skills/attachments/owner card) | `Details_ScrollView` | Pannable/scrollable, dynamic-data | `ProjectDetailsViewModel.Details`/`Skills`/`Attachments` ← `IProjectRepository.GetDetailsAsync` | `ProjectDetailsPage.xaml:57` | `ProjectDetailsPageTests.Scroll_DetailsPage` |
| Attachments list | `Details_AttachmentsList` | Pannable/scrollable, dynamic-data | `ProjectDetailsViewModel.Attachments` (one `AttachmentItemViewModel` per `Asset`) | `ProjectDetailsPage.xaml:91` | `ProjectDetailsPageTests.Scroll_AttachmentsList` |
| Attachment "تحميل" (download/resolve) button | `Details_AttachmentResolveButton` | Clickable, dynamic-data (status text updates) | `AttachmentItemViewModel.ResolveCommand` → `ResolveAsync` → `AssetDownloadService.ResolveAsync` | `ProjectDetailsPage.xaml:97-101`, VM `ProjectDetailsViewModel.cs:39` | `ProjectDetailsPageTests.Click_AttachmentResolve_UpdatesStatusMessage` |
| Error state message | *(covered by `design:LabelWithSubText`, no dedicated id — static per-state text)* | Dynamic-data | `ProjectDetailsViewModel.ErrorMessage`/`HasError` ← `IProjectRepository.GetDetailsAsync` failure or `SetError` | `ProjectDetailsPage.xaml:51-54` | `ProjectDetailsPageTests.Load_InvalidProjectId_ShowsErrorState` |

## Settings (`SettingsPanel.xaml` — settings.html)

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Poll interval input | `Settings_PollIntervalInput` | Inputtable, focusable | `SettingsViewModel.PollIntervalSeconds` → `OnPollIntervalSecondsChanged` → `Preferences.Set` + `IPollService.PollIntervalSeconds` | `SettingsPanel.xaml:74-78` | `SettingsPageTests.Type_PollInterval_PersistsAndAppliesLive` |
| Requests-per-minute input | `Settings_RequestsPerMinuteInput` | Inputtable, focusable | `SettingsViewModel.RequestsPerMinute` → `OnRequestsPerMinuteChanged` → `Preferences.Set` + `TokenBucketRateLimiter.Reconfigure` | `SettingsPanel.xaml:86-90` | `SettingsPageTests.Type_RequestsPerMinute_PersistsAndAppliesLive` |
| "الطلبات الآمنة" checkbox | `Settings_SafeRequestsCheckbox` | Clickable (checkbox), default checked | `SettingsViewModel.SafeRequests` → `OnSafeRequestsChanged` → `Preferences.Set("settings_safe_requests")` + `TokenBucketRateLimiter.Reconfigure(rpm, safeRequests)`; also read at startup in `MauiProgram` | `SettingsPanel.xaml:103-125` | `SettingsPageTests.Toggle_SafeRequests_Off` / `Toggle_SafeRequests_On` (counterpart) |
| Safe-requests (i) hint | `Settings_SafeRequestsInfo` | Hoverable (native tooltip), dynamic-data | `SettingsViewModel.SafeRequestsHintText` (recomputed from `SafeRequests` + `RequestsPerMinute`), shown both as `ToolTipProperties.Text` and as the row's sub-label | `SettingsPanel.xaml:106-113` | `SettingsPageTests.Hover_SafeRequestsInfo_ShowsExplanation` |
| Grouping mode picker | `Settings_GroupingModePicker` | Clickable, dynamic-data, focusable | `SettingsViewModel.GroupingMode` → `OnGroupingModeChanged` → `Preferences.Set` + `NotificationGrouper.Mode` | `SettingsPanel.xaml:98-111` | `SettingsPageTests.Select_GroupingMode_PersistsAndAppliesLive` |
| Grouping threshold input | `Settings_GroupingThresholdInput` | Inputtable, focusable | `SettingsViewModel.GroupingThreshold` → `OnGroupingThresholdChanged` → `Preferences.Set` + `NotificationGrouper.AfterMinutesThreshold`/`AfterCountThreshold` | `SettingsPanel.xaml:119-123` | `SettingsPageTests.Type_GroupingThreshold_PersistsAndAppliesLive` |
| Dark mode toggle | `Settings_DarkModeToggle` | Clickable (toggle) | `SettingsViewModel.IsDarkMode` → `OnIsDarkModeChanged` → `Preferences.Set` + `Application.Current.UserAppTheme` | `SettingsPanel.xaml:129` | `SettingsPageTests.Toggle_DarkMode_On` / `Toggle_DarkMode_Off` (counterpart) |
| Save button | `Settings_SaveButton` | Clickable | `SettingsViewModel.SaveCommand` → `SaveAsync` (confirms already-applied state; fields persist live on change) | `SettingsPanel.xaml:132`, VM `SettingsViewModel.cs:246` | `SettingsPageTests.Click_Save_ThenReload_ValuesPersisted` |
| Validation message | *(none — static label, no gesture)* | Dynamic-data | `SettingsViewModel.ValidationMessage`/`HasValidationError` ← the `OnXChanged` partials' range checks | `SettingsPanel.xaml:58-62` | `SettingsPageTests.Type_InvalidPollInterval_ShowsValidationMessage` |

## About (`AboutPage.xaml` — about.html)

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Facts/roadmap scroll view | `About_ScrollView` | Pannable/scrollable | *(static content — no backend)* | `AboutPage.xaml:35` | `AboutPageTests.Scroll_AboutPage` |
| Mostaqlk footer link | `About_MostaqlLink` | Clickable | `AboutPage.OnMostaqlLinkTapped` → `Launcher.Default.OpenAsync("https://mostaql.com")` | `AboutPage.xaml:136-140`, handler `AboutPage.xaml.cs:16` | `AboutPageTests.Click_MostaqlLink_OpensBrowser` |
| Version pill label | *(none — static label, no gesture)* | Dynamic-data | `AppInfo.Current.VersionString` set in `AboutPage.xaml.cs:13` (`VersionLabel.Text`) | `AboutPage.xaml:49` | *(asserted as part of `AboutPageTests` smoke assertion)* |

## Notifications Flyout (`RecentNotificationsFlyout.xaml`)

Opened from `Sidebar_NotificationsButton` on `MainWindowPage` (see `Notifications_Flyout` above).

| Element | AutomationId | Interaction Kind(s) | Backend it calls | Source file:line | Planned Appium test name |
|---|---|---|---|---|---|
| Notifications list | `Notifications_List` | Pannable/scrollable, dynamic-data | `NotificationCenterViewModel.RecentNotifications` ← `INotificationDispatcher.RecentHistory`/`HistoryChanged` | `RecentNotificationsFlyout.xaml:20` | `ProjectsPageTests.Open_NotificationsFlyout_ListsRecent` |
| Notification row | `Notifications_Row` | Clickable, dynamic-data | `NotificationCenterViewModel.OpenProjectCommand` → `OpenProjectAsync` → `Shell.GoToAsync("ProjectDetailsPage?projectId=...")` | `RecentNotificationsFlyout.xaml:26-31`, VM `NotificationCenterViewModel.cs:57` | `ProjectsPageTests.Click_NotificationRow_NavigatesToDetails` |

## Counts

- Shared Sidebar: 5 clickable rows carrying an `AutomationId` (+ 2 dynamic-data-only labels noted).
- Projects: 11 catalogued interactive/dynamic elements carrying an `AutomationId` (+ 2 dynamic-data-only labels noted).
- Project Card: 1 (`ProjectCard_Root`, template — one instance per feed row).
- Project Details: 4 catalogued elements carrying an `AutomationId`.
- Settings: 8 catalogued elements carrying an `AutomationId` (+ 1 dynamic-data-only label noted).
- About: 2 catalogued elements carrying an `AutomationId`.
- Notifications Flyout: 2 catalogued elements carrying an `AutomationId` (row is a template — one instance per notification).
