# settings-visual-implementer

## Goal
Restyle the Windows Settings page to the v3 mockup design truth
(`.repertoire/design/postmvp/v3-multisource/settings.html`) — continuous page,
`.set-row` vocabulary (label+hint on the start, value+inline-edit-link or pill toggle on the
end, 1px divider), `.section-head` pattern — with **data-driven rows** (descriptor +
capabilities + namespaced prefs, zero source-id branches). Files owned:
`Features/Settings/Views/Layouts/SettingsPanelWindowsLayout.xaml(.cs)` and
`Features/Settings/ViewModels/SettingsViewModel.cs` (+ a local style dictionary inside the
layout). Mobile layout untouched. No animations (`animation-safety.md`). Brand colors via
`{DynamicResource Brand*}` only.

## Actions
- **`Core/Sources/ISettingsRepository.cs`** — added two `SettingsKeys` (shared per-source/global
  key table, allowed file):
  - `EnableSourceNotifications = "settings_enable_source_notifications"` (per-source "تنبيهات هذه المنصة")
  - `RememberLastSource = "settings_remember_last_source"` (global "تذكر آخر منصة مفتوحة")
- **`SettingsViewModel.cs`** — added, without changing any existing persistence semantics:
  - `RememberLastSource` (global bool, default ON, persisted via `SetGlobal`),
    `SourceNotificationsEnabled` (per-source bool, default ON, persisted via namespaced `Set`),
    `ArchiveCount` + `ArchiveCountText`, `IsCookieEditorOpen`/`IsCookieEditorClosed` +
    `OpenCookieEditor`/`CloseCookieEditor` commands.
  - descriptor-driven visibility props (read `SourceRegistryProvider.DescriptorFor(_source)`):
    `ShowSessionEnrichmentRow` (`Capabilities.NeedsSessionForFullEnrichment`),
    `ShowSupervisedTypesRow` (`Capabilities.HasSupervisedFlag`),
    `ShowBudgetCurrencyRow` (active `CurrencySuffix` != default source's),
    `BudgetCurrencyDisplayText`, `SessionEnrichmentHint` (uses descriptor name, never a literal),
    `SessionValueText` (مرتبطة/غير مرتبطة from `HasSessionCookie`),
    `ActiveSourceName`/`ActiveSourceGlyph`.
  - Picker bridge `GroupingModeOptions` + `GroupingModeIndex` for `Settings_GroupingModePicker`.
  - `LoadFromPreferences` seeds the two new prefs + refreshes `GroupingModeIndex`; ctor starts
    `LoadArchiveCountAsync` (through `IProjectRepository.CountTrackedAsync`, existing path).
- **`SettingsPanelWindowsLayout.xaml(.cs)`** — full continuous-page rewrite (see row map below).
  Local keyed styles (`SetLabel/SetHint/SetValue/SetUnit/SetEdit/SetDivider/SetEditor`) reuse
  existing units: `AppEntry`, `AppToggle`, `AppButton`, `AppIcon`, `LastScanStatus`. AppSidebar
  host column kept unchanged. Added `OnViewArchiveClicked` code-behind (navigates to Projects).

## Row → data-source map
| Mockup row | XAML source | Visibility driver | Persistence |
|---|---|---|---|
| فاصل الفحص | `Settings_PollIntervalInput` AppEntry (TwoWay) | always | `Set(_source, PollIntervalSeconds)` (unchanged) |
| حد الطلبات | `Settings_RequestsPerMinuteInput` AppEntry | always | `Set(_source, MaxRequestsPerMinute)` |
| تنبيهات هذه المنصة | `Settings_SourceNotificationsToggle` AppToggle | always | `Set(_source, EnableSourceNotifications)` NEW |
| أنواع المشاريع المتابَعة | value "عادية + مراقبة" | `ShowSupervisedTypesRow` (`HasSupervisedFlag`) | display-only |
| جلسة الإثراء | value + إلغاء الربط (`ClearSessionCookieCommand`) / ربط كوكيز→inline editor (`Settings_SessionCookieEntry`, `Settings_SaveCookieButton`, `Settings_ClearCookieButton`) | `ShowSessionEnrichmentRow` (`NeedsSessionForFullEnrichment`) | existing cookie store path |
| عملة الميزانية | `BudgetCurrencyDisplayText` | `ShowBudgetCurrencyRow` (non-default currency) | display-only |
| آخر فحص | `LastScanStatus` `LabelAutomationId="Settings_LastScanLabel"` ← `GlobalStatus.LastScanCompletedAt` | always | read-only |
| الأرشيف | `ArchiveCountText` + عرض (`Settings_ViewArchiveButton`) | always | read `CountTrackedAsync.Tracked` |
| contributed (تخطي المشاريع المغلقة / module session read-out) | existing `ContributedSections` BindableLayout template (restyled to set-rows, `Settings_ContributedRetryToggle`) | per-source contribution | unchanged |
| تشغيل مع النظام | `Settings_StartupToggle` | `IsStartupVisible` | `SetGlobal(StartupEnabled)` |
| تجميع التنبيهات | `Settings_GroupingToggle` | always | `Set(_source, NotificationGroupingEnabled)` |
| نمط التجميع / حد التجميع | `Settings_GroupingModePicker` / `Settings_GroupingThresholdInput` | always (kept for UI-test ids) | `Set(_source, GroupingMode/GroupingThreshold)` |
| تذكر آخر منصة مفتوحة | `Settings_RememberSourceToggle` | always | `SetGlobal(RememberLastSource)` NEW |
| الوضع الداكن | `Settings_DarkModeToggle` | always (kept for UI-test id) | `SetGlobal(IsDarkMode)` |

## "Remember toggle" pref key
- **Key:** `settings_remember_last_source`, stored as a **global** (non-namespaced) bool via
  `ISettingsRepository.SetGlobal/GetGlobal`, default `true`. VM property:
  `SettingsViewModel.RememberLastSource`. Startup-restore honoring it is **deferred to the
  parent** (App.xaml.cs was NOT touched) — expose-and-persist only.

## Switcher-region handoff
The header renders the active source's **name + tile glyph** (descriptor data) inside a
brand-filled tile — pure caption, not an interactive switcher. The interactive tile switcher
(`SourceSwitchTiles`) is being relocated into `SourceRail` by a parallel agent RIGHT NOW, so I
deliberately did NOT reference `SourceSwitchTiles` (would break on deletion). The AppSidebar host
that already owns the morph tiles today is kept as-is in the rail column. **A placeholder
comment marks where the parent should wire the finished SourceRail tile unit into the header.**
No dependency created on the in-flight SourceRail rewrite.

## Removed / hidden vs. current page (kept bound in the VM; persistence semantics unchanged)
- Dropped the floating-card layout for the continuous set-row page (mockup D11).
- Removed from the Windows page (mockup has no such rows): the "ملاحظة MVP" banner, the "v1/v2
  expanded settings" info banner, and standalone cards for query_params (`Settings_QueryParamsEntry`),
  include_assets (`Settings_IncludeAssetsToggle`), max-concurrent enrichment
  (`Settings_ParallelEnrichmentEntry`), safe_requests, and the close-behavior picker
  (`Settings_CloseBehaviorPicker`). None of these AutomationIds are asserted by `MostaqlK.UITests`;
  the VM props/handlers remain and still load/persist (legacy read-through untouched).
- Dark mode: mockup relocates theme to the rail. Kept a bound row (`Settings_DarkModeToggle`,
  asserted by tests) so functionality + the test id survive; noted as intentionally retained.
- Grouping mode/threshold: not in the mockup عام vocabulary, but `Settings_GroupingModePicker`
  and `Settings_GroupingThresholdInput` ARE asserted by `SettingsPageTests`, so both were kept
  as set-rows to preserve them.

## AutomationId changes
- All six test-asserted ids preserved AND made findable as always-realized controls:
  `Settings_PollIntervalInput`, `Settings_RequestsPerMinuteInput`, `Settings_GroupingThresholdInput`,
  `Settings_GroupingModePicker`, `Settings_DarkModeToggle`, `Settings_SaveButton`. (The prior file
  used `*Entry` names and had no grouping-mode picker / save button, which the tests already could
  not find — this restyle aligns to the asserted names.)
- Kept: `Settings_SessionCookieEntry`, `Settings_SaveCookieButton`, `Settings_ClearCookieButton`,
  `Settings_StartupToggle`, `Settings_GroupingToggle`, `Settings_ContributedRetryToggle`.
- New: `Settings_SourceNotificationsToggle`, `Settings_RememberSourceToggle`,
  `Settings_OpenCookieEditorButton`, `Settings_ViewArchiveButton`, `Settings_LastScanLabel`
  (set via `LastScanStatus.LabelAutomationId` — the dev17 BindableProperty code-setter form).
- Removed (unasserted): `Settings_PollIntervalEntry`, `Settings_RateLimitEntry`,
  `Settings_GroupingThresholdEntry` (renamed to the `*Input` asserted forms),
  `Settings_QueryParamsEntry`, `Settings_IncludeAssetsToggle`, `Settings_ParallelEnrichmentEntry`,
  `Settings_CloseBehaviorPicker` (rows omitted per mockup).

## UNITS.md notes (file NOT edited — W1 holds it)
No new reusable Platform Component was created (rows composed inline from existing units inside
the settings layout + a local keyed style dictionary). Two candidate UNITS notes for W1:
1. A reusable **"SettingsRow" (.set-row) visual** (label+hint / value+edit-link | pill toggle,
   with divider) — currently realized as local keyed styles + inline Grids; if W1 wants to
   promote it, it is a Design-System candidate (`SetLabel/SetHint/SetValue/SetEdit/SetDivider`).
2. `AppToggle` "pill" note: the mockup's 40×24 RTL pill is realized by **reusing the existing
   `AppToggle` (native Switch)** with `OnColor={DynamicResource Brand}` + white thumb — honoring
   the "reuse, never duplicate" rule over the bespoke CSS pill. Flag if a true 40×24 pill unit
   is desired (would be a new unit → new row).

## Descriptor-mapping caveats to confirm (contract-honest, per requirements §10 / guardrail 10)
- **جلسة الإثراء shown for Mostaql, not Nafezly.** The mockup draws the row in the nafezly panel,
  but the descriptors are the truth (behavior over mockup visuals): `MostaqlSource` has
  `NeedsSessionForFullEnrichment: true`, `NafezlySource` `false`. Visibility is gated purely on
  the capability, so the built-in session row renders for the session-gated source (mostaql) today;
  nafezly's own session read-out still arrives via its contributed section. No source-id branch.
- **أنواع المشاريع المتابَعة hidden today.** `HasSupervisedFlag` is `false` on every registered
  descriptor (contract §1/§18 — no source carries it yet), so the row is honestly absent
  (absent = never rendered, never empty). It will appear automatically for any source that
  declares the capability. The value text ("عادية + مراقبة") is static display copy gated by that
  capability; no per-source types pref exists yet.
- **تنبيهات هذه المنصة persisted but not yet consumed.** `EnableSourceNotifications` persists per
  source through the single mutation path; the toast dispatcher does not yet read it. Exposed
  cleanly for the parent to wire dispatch-side gating (mirrors the remember-toggle handoff pattern).

## Verification (gate output)
1. **Windows build:** `dotnet build -f net10.0-windows10.0.19041.0` → **Build succeeded, 0 Warning(s), 0 Error(s)**. No file-lock retries needed on the first pass.
2. **Android build:** default JDK on the machine is 22 (`XA0030` — environment toolchain gate, fires before compilation, NOT a code error). Retried with the supported JDK already installed: `dotnet build -f net10.0-android -p:JavaSdkDirectory="D:\Program Files\Java\jdk-17.0.12"` → **Build succeeded, 0 Warning(s), 0 Error(s)**.
3. **ParserTests:** `TOTAL: 297 passed, 0 failed` — All checks passed.
4. **DbMigrationTests:** `ALL DB MIGRATION CHECKS PASSED` (incl. nafezly real-band coexistence).
5. **Grep gates:** no inline brand hex; no `mostaql`/`nafezly` string literals or compares in the touched files (both `NONE`).

## Foreign / transient notes
- `UNITS.md` changed on disk during the run (another agent holds it) — not edited by me; no
  conflicts. Never touched any forbidden path (`UI/PlatformComponents/**`, `MainWindowWindowsLayout`,
  `AppShell.xaml.cs`, `App.xaml.cs`, `DesignDataSeeder.cs`, `SourceMorph.cs`, `tools/**`,
  `ProjectDetails*`, `Infrastructure/Database/**`, `CHANGELOG.md`, `UNITS.md`). No git commit, no
  version bump.
