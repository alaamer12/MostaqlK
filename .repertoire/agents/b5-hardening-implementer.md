# B5 Part A — Notification + Session Hardening (implementer report)

Branch `v3` @ `6a87853` (verified `1.0.4.dev18` / build 23). Skill used: `terminal-ops` (gate runs) + inline reuse of `ActivePipeline`/`ICookieJarProvider` unit patterns per `UNITS.md`. No commit, no tag bump.

## 1. §6 toast source-resolution hardening (DONE)

**Root cause.** `ToastActivator.OnActivated` and `WinAppSdkVariation.OnNotificationInvoked` both resolved the toast's `sourceId` with a silent `SourceId.Default` fallback. A toast for nafezly project `123` that lost its `sourceId` would open **mostaql** project `123` (site-local ids are not globally unique).

**Fix.**
- `ToastActivator.Windows.cs` — restructured `OnActivated`: compute the target route *before* touching the window; if `projectId` is present but `sourceId` is missing/unparsable → `InteractionLogger.Mark("ToastActivator.NavigateFromActivation","Ignored-MissingSourceId", …)` and `return` (window never disturbed). No-projectId (grouped `filter=unread`) → still navigates to the Projects feed (unchanged).
- `WinAppSdkVariation.Windows.cs` — same tightening in `OnNotificationInvoked` (validate before `BeginInvokeOnMainThread`). This handler is the PRIMARY backend (tried first in `WindowsToastSender.Initialize`), so hardening only the WinRt path would have left a real gap.
- Senders now ALWAYS stamp `sourceId` on individual toasts (incl. the default source): `WinAppSdkVariation.BuildIndividualToast` (`AddArgument("sourceId", project.Source.Value)`, was conditional on `!= Default`) and `WinRtVariation.BuildIndividualToastXml` (`sourceParam` unconditional). Grouped toasts unchanged.
- **Route-level default preserved** (not touched): `AppRoutes.ProjectDetails` (omits `&sourceId=` for default) and `ProjectDetailsPage.OnAppearing` (absent → `SourceId.Default`) — external pre-v3 deep links still resolve to the default source. Only the toast-ARGUMENT path tightened.

**Verification vs the brief's claim.** The task said "verify senders always attach sourceId for NEW payloads (WinAppSdk:135-138, WinRt:77-80) — they do per prior audit." **That claim was FALSE at HEAD:** those exact lines were a `if (source != Default)` guard, so mostaql payloads deliberately carried NO sourceId. Making "missing → ignore" safe therefore REQUIRED flipping the senders to always stamp it (done above). See Open Questions.

## 2. Per-source CookieStore (DONE)

**Root cause (surfaced by B4 + a latent DI bug).** `CookieStore` was a single `AddSingleton` injecting one `ISourceDescriptor`. With `AddMostaqlSource()` then `AddNafezlySource()`, MS-DI single-service resolution returns the LAST registration → the one store was bound to the **nafezly** descriptor. So the Settings cookie card wrote/read `cookie_nafezly` even while mostaql was the active source; mostaql sessions were effectively orphaned.

**Fix — factory/provider pattern mirroring `ICookieJarProvider`:**
- New `Services/CookieStoreProvider.cs`: `ICookieStoreProvider` (`For(SourceId)`, `Stores`, `InitializeAllAsync`) + `CookieStoreProvider(IEnumerable<ISourceDescriptor>, ISecretRepository, ICookieJarProvider)` building one cached `CookieStore` per descriptor. `CookieStore` class itself UNCHANGED → each instance uses its own `CookieSecretKey`/`LegacyCookieSecretKey`/jar.
- `MauiProgram.cs`: replaced `AddSingleton<CookieStore>()` with `AddSingleton<ICookieStoreProvider, CookieStoreProvider>()`; startup init now `GetRequiredService<ICookieStoreProvider>().InitializeAllAsync()`.
- `SettingsViewModel.cs`: injects `ICookieStoreProvider`; `ActiveCookieStore => _cookieStores.For(_source)`; all cookie reads/writes go through it. The card copy already followed `_source` (placeholders via `SourceRegistryProvider.DescriptorFor(_source)`), so now the OPERATIONS do too. No XAML change (views bind only to VM properties).
- **Mostaql behavior identical:** the mostaql store = `new CookieStore(secrets, mostaqlDescriptor, jars)` → `cookie_mostaql` key, `mostaql_cookie` legacy migration, `For(mostaql)` jar, same FilePreferences/`app_secrets` paths. This restores the intended pre-nafezly behavior; no key/path/migration changed.
- Contributed section (nafezly POCO) left capability-driven (task says keep as fallback). The shared card already surfaces the real per-source presence; wiring the module VM to the store would add `Sources/Nafezly → Services`/DB coupling and the harness builds that VM with an all-null `EmptyServiceProvider`. Recorded as feasible-but-deferred in §10.

## 3. Tray/Dispatcher live handles (DONE for tray; dispatcher already live)

- `TrayIconService`: removed captured `IPollService`/`DiscoveryQueue` fields; injects `ActivePipeline` and reads `.Poll`/`.Queue` per use — `OnPauseResume`, `OnCheckNow`, `RecomputeAggregatedState` (`_pipeline.Poll.Status`), `OnPollServiceStatusChanged` (`_pipeline.Queue.Count`). `MauiProgram` registration updated to pass `ActivePipeline` + `GlobalAppStatusService`. The raw `StatusChanged` subscription binds the construction-active poll (identical to before for mostaql-at-startup); cross-source live state still rides the `GlobalAppStatusService.All` aggregate, so a morph keeps the icon correct while commands hit the active source. Teardown/dispatcher-marshalling guards live in `GlobalAppStatusService`/`SourcePipelineStatus` and were NOT touched (verbatim).
- `NotificationDispatcher`: **already** holds only `GlobalAppStatusService` (live authority) + toast sender + grouper — it does NOT capture `IPollService`/`DiscoveryQueue`. The brief's "Tray+Dispatcher capture IPollService/DiscoveryQueue" is inaccurate for the dispatcher; no change needed.

## 4. Mobile morph question — evidence-based DEFERRAL (no code)

`design/postmvp/v3-multisource/README.md` is explicitly desktop-only ("Two 1-desktop apps in one frame"). `design/postmvp/mobile/*.html` (dashboard/more/projects/search) contain no source rail/tiles/morph (only `.row-trail` menu chevrons). No mobile mockup defines the morph → per "do NOT invent mobile UI", no mobile host wires `SourceSwitchTiles`. Recorded as `DEFERRED (B5)` in contract §10.

## 5. Gates + docs

- **ParserTests:** `TOTAL: 297 passed, 0 failed / All checks passed.` (baseline held). Changed files are NOT in the harness's curated `<Compile Include>` list, so no regression risk there.
- **DbMigrationTests:** `ALL DB MIGRATION CHECKS PASSED` (incl. nafezly real-band coexistence).
- **Grep gate A (source literals in shared UI/Services/Features/Infrastructure):** zero. **Gate A2 (no `== SourceId.Nafezly/Mostaql` branch):** zero. **Grep gate B (banned inline brand hexes under UI/+Features/):** zero.
- **Contract flips:** §6 Toasts → REAL (B5) for fallback removal, morph-first still PLANNED; §5 CookieStore → REAL (B5, provider, mostaql-identical); §2 Pause/resume → B5 live-handle; §10 settings-section note (blocker removed, module read-out deferred) + §10 mobile-morph `DEFERRED`. Inbox per-source unread left PLANNED (out of scope).
- **UNITS.md:** `CookieStore` updated, new `ICookieStoreProvider`/`CookieStoreProvider` row, `TrayIconService` (ActivePipeline per-use), `ToastActivator` (B5 sourceId-required).
- **CHANGELOG.md:** B5 Part A bullet added. No commit, no tag bump.

## Files touched
- `Infrastructure/Notifications/ToastActivator.Windows.cs`
- `Infrastructure/Notifications/WinAppSdkVariation.Windows.cs`
- `Infrastructure/Notifications/WinRtVariation.Windows.cs`
- `Services/CookieStoreProvider.cs` (NEW)
- `Features/Settings/ViewModels/SettingsViewModel.cs`
- `UI/TrayIcon/TrayIconService.cs`
- `MauiProgram.cs`
- `.repertoire/.steering/v3/backend-ui-contract.md`, `UNITS.md`, `CHANGELOG.md`
- (`Sources/Nafezly/NafezlySource.cs` appears in `git diff --stat` — concurrent edit by another agent, NOT mine; left untouched.)

## Open Questions / Next Actions
1. **Toast payload byte-identity:** individual toasts now always carry `&sourceId=…` including `sourceId=mostaql`. Outcome for mostaql is identical (correct source, correct route), but the raw launch string is no longer byte-identical to pre-v3. If any external tool parsed toast launch strings expecting no sourceId for mostaql, flag it.
2. **§6 morph-first activation** (projection/brand swap before cross-source toast navigation) is still PLANNED — deliberately not folded into parse-failure work. Needs a product decision (it changes theming on every toast click).
3. **§6 Inbox per-source unread** — not in B5 Part A scope; remains PLANNED.
4. **Contributed-session-row live read-out** — now feasible via the provider; deferred to avoid `Sources/Nafezly → Services`/DB coupling. Revisit if a source with `NeedsSessionForFullEnrichment=true` ships.
5. **App build + Windows on-device smoke** owned by parent (toast repaint/morph, tray command routing across a morph, DynamicResource behavior). Not run here per instructions.
