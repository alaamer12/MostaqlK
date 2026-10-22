# Step 7 implementation — Settings, cookies, HTTP policy per-source

**Agent:** source-settings-cookies-implementer
**Scope:** Step 7 only of the approved pre-phase plan (`C:\Users\amrmu\.qoder\plans\fresh-narrows-gull.md`).
**Branch:** `v3`. **Skill used:** `senior-refactor` (multi-platform decoupling / zero-coupling) guided the
whole pass (descriptor-driven seams, no shared site literals, no `#if PLATFORM` in bodies); `terminal-ops`
for the build/verification commands.
**State:** working tree only, **uncommitted** (per instructions — parent reviews + commits + bumps the tag).

---

## Goal (restated)

Make settings, session cookies, and HTTP request policy **per-source** so the app is byte-identical today
(single source = mostaql) while a second source can later land as a sibling module with no shared-code
surgery. Every moved literal is seeded with the exact current value.

## What was implemented

### 1. Settings repository with legacy read-through
- **New** `Core/Platform/FilePreferencesSettingsRepository.cs` — implements the Step-1 `ISettingsRepository`.
  Per-source values go into a `FilePreferences` container whose `sharedName` = `descriptor.SettingsContainerId`
  (mostaql → `"mostaql"`). `Get` falls back to the old FLAT global key when the namespaced key is absent
  (read-through, **no migration on read**); `Set` writes only the namespaced key (lazy migrate-on-write).
  `GetGlobal`/`SetGlobal`/`ContainsGlobal` keep the deliberately-global keys (dark mode, startup, polling-active)
  on the flat container exactly as before. Placed in `Core/Platform` next to `FilePreferences`/`AppPaths`
  (it is a storage primitive, not a business service).
- **Changed** `Core/Sources/ISettingsRepository.cs` — added `Contains(SourceId,string)` and `ContainsGlobal(string)`.
  Genuine requirement: the dark-mode seed is a tri-state (unset vs false) that previously used
  `Preferences.ContainsKey`; the fallback-aware `Get` alone can't distinguish "absent" from "set to default".
- Rewired all consumers to `ISettingsRepository` + `SettingsKeys` (deleted the duplicated private `Key*` consts):
  `Features/Settings/ViewModels/SettingsViewModel.cs`, `Features/Projects/ViewModels/ProjectFeedViewModel.cs`,
  `Services/Onboarding/OnboardingStateService.cs`, `Services/Pipeline/SourcePipelineFactory.cs`,
  `Services/Pipeline/SourcePipelineOrchestrator.cs`, `Infrastructure/Database/DesignDataSeeder.cs`,
  `App.xaml.cs` (dark-mode read), `UI/PlatformComponents/AppSidebar/AppSidebar.cs` (dark-mode write, resolved
  via `IPlatformApplication.Current?.Services.GetService<ISettingsRepository>()`, the existing control-DI pattern).

### 2. Per-source cookie storage
- **New** `Infrastructure/Http/CookieParser.cs` — pure, dependency-free static parser (the format logic lifted
  verbatim from the old static `CookieJar`, plus the previously-discarded **Netscape domain column is now
  filtered** subdomain-tolerantly when a domain is supplied). Linked directly by `tools/ParserTests`.
- **Rewrote** `Infrastructure/Http/CookieJar.cs` — now `ICookieJar` + per-source `CookieJar : ICookieJar`
  (instance `Load`, `LastSource`, `SecureProvider`, `DevelopmentFallbacksEnabled`, `ParseForStorage`). Env names
  are `MOSTAQLK_COOKIE_{SOURCE}` / `MOSTAQLK_COOKIE_{SOURCE}_FILE` (generic, derived from `descriptor.Id`), with
  the descriptor-supplied legacy `MOSTAQL_COOKIE`/`MOSTAQL_COOKIE_FILE` honored as fallback — DEBUG-only; Release
  still accepts only the encrypted store. Two on-disk formats, analytics ignore-list, and quote-stripping preserved.
- **New** `Infrastructure/Http/CookieJarProvider.cs` — `ICookieJarProvider`/`CookieJarProvider`: one isolated jar
  per registered descriptor, looked up by `SourceId`.
- **Rewrote** `Services/CookieStore.cs` — per-source; uses `descriptor.CookieSecretKey` (`cookie_mostaql`); on load,
  if that is empty but `descriptor.LegacyCookieSecretKey` (`mostaql_cookie`) holds a value, it is used, rewritten
  under the new key, and the legacy row deleted (seamless, no re-upload). Uploads validated through the jar's
  domain filter. Doc-comment `MostaqlScraper` reference removed.
- **Removed** the now-unused `SecretRepository.MostaqlCookieKey` const (the legacy key moved onto the descriptor).

### 3. Descriptor-driven asset download + HttpClient policy
- **Rewrote** `Infrastructure/Http/AssetDownloadService.cs` — ctor `(HttpClient, SourceRegistry, ICookieJarProvider)`;
  `ResolveAsync(SourceId, Asset, ct)`. The second hardcoded Chrome/120 UA is gone (uses the policy client's UA);
  `Contains("mostaql.com")` → `descriptor.IsSameSiteUrl`; the user-facing "Mostaql"/`MOSTAQL_COOKIE` text is now
  composed from `descriptor.DisplayNameAr`/`Domain` (no site literal in shared source). Callers updated:
  `Services/Pipeline/EnrichmentService.cs` (`projectId.Source`), `ProjectDetailsViewModel`/`AttachmentItemViewModel`
  (`Details.Source`).
- **New** `Infrastructure/Http/HttpClientPolicyFactory.cs` — `IHttpClientPolicyFactory`/`HttpClientPolicyFactory`
  builds each source's `HttpClient` from descriptor `UserAgent`/`AcceptHeader`/`AcceptLanguageHeader`. The Chrome/126
  + Accept + Accept-Language literals and the bot-filter rationale moved from `MauiProgram.cs` into
  `Sources/Mostaql/MostaqlSource.cs`.
- **Changed** `Core/Sources/ISourceDescriptor.cs` + `Sources/Mostaql/MostaqlSource.cs` — added 6 descriptor members
  (documented, seeded with exact current literals): `UserAgent`, `AcceptHeader`, `AcceptLanguageHeader`,
  `LegacyCookieSecretKey`, `LegacyCookieEnvInlineVar`, `LegacyCookieEnvFileVar`.
- **Changed** `Sources/Mostaql/MostaqlFetcher.cs` — takes `ICookieJarProvider`, resolves its own source's jar (no static call).
- **Changed** `MauiProgram.cs` — HttpClient registered through the policy factory for `SourceId.Default`;
  registered `IHttpClientPolicyFactory`, `ICookieJarProvider`, `ISettingsRepository`. Also fixed an **unrelated
  unbalanced-paren typo** the concurrent tray edit left at line 165 (`new TrayIconService(... GlobalAppStatusService>))`)
  which was breaking the whole build; the fix is paren-balance only and preserves their intended third argument.

### 4. Docs
- `UNITS.md` "Secrets & session cookie" section updated: `CookieJar` row changed from "static class" to
  per-source; `CookieStore` row reworded; added rows for `CookieParser`, `ICookieJarProvider`,
  `IHttpClientPolicyFactory`, `AssetDownloadService`, `FilePreferencesSettingsRepository`. Removed the
  stale `MostaqlScraper` reference.
- `tools/ParserTests/Program.cs` + `ParserTests.csproj`: link `CookieParser.cs` (was `CookieJar.cs`) and
  `SourceId.cs` (Models gained a `SourceId` in Step 4 but the harness never linked it — pre-existing break);
  cookie tests now call `CookieParser.ParseFile`; live modes use a harness-local `ResolveHarnessCookie`.
- `tools/DbMigrationTests/Program.cs`: added the 6 new members to its `StubDescriptor`.

## Files touched (absolute paths)

New:
- `F:\Projects\Mobile\C#\MostaqlK\Infrastructure\Http\CookieParser.cs`
- `F:\Projects\Mobile\C#\MostaqlK\Infrastructure\Http\CookieJarProvider.cs`
- `F:\Projects\Mobile\C#\MostaqlK\Infrastructure\Http\HttpClientPolicyFactory.cs`
- `F:\Projects\Mobile\C#\MostaqlK\Core\Platform\FilePreferencesSettingsRepository.cs`

Modified:
- `Core\Sources\ISourceDescriptor.cs`, `Core\Sources\ISettingsRepository.cs`
- `Sources\Mostaql\MostaqlSource.cs`, `Sources\Mostaql\MostaqlFetcher.cs`
- `Infrastructure\Http\CookieJar.cs`, `Infrastructure\Http\AssetDownloadService.cs`
- `Infrastructure\Database\SecretRepository.cs`, `Infrastructure\Database\DesignDataSeeder.cs`
- `Services\CookieStore.cs`, `Services\Pipeline\EnrichmentService.cs`, `Services\Pipeline\SourcePipelineFactory.cs`, `Services\Pipeline\SourcePipelineOrchestrator.cs`, `Services\Onboarding\OnboardingStateService.cs`
- `Features\Settings\ViewModels\SettingsViewModel.cs`, `Features\Projects\ViewModels\ProjectFeedViewModel.cs`, `Features\Projects\ViewModels\ProjectDetailsViewModel.cs`
- `App.xaml.cs`, `MauiProgram.cs`, `UI\PlatformComponents\AppSidebar\AppSidebar.cs`
- `tools\ParserTests\Program.cs`, `tools\ParserTests\ParserTests.csproj`, `tools\DbMigrationTests\Program.cs`
- `UNITS.md`

## Verification (real output)

1. `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → **Build succeeded. 0 Warning(s), 0 Error(s)** (run 3×; re-run after concurrent edits to `ISourceDescriptor.cs`/`SettingsViewModel.cs`).
2. `dotnet run --project tools/DbMigrationTests` → **ALL DB MIGRATION CHECKS PASSED**.
3. `dotnet build tools/ParserTests` → 0 errors / 0 warnings; `dotnet run --project tools/ParserTests` → **TOTAL: 135 passed, 0 failed** (incl. all `TestCookieJarParsing` checks).
4. Grep gate:
   - `"settings_` in `.cs` now only in `Core/Sources/ISettingsRepository.cs` `SettingsKeys` consts (verified by `grep -rn '"settings_' --include=*.cs`).
   - `MOSTAQL_COOKIE` fixed-string appears only in `Sources/Mostaql/MostaqlSource.cs` (descriptor) and `tools/ParserTests` (dev harness) — none in shared code.
   - `مستقل` / `mostaql.com` / `ر.س` remaining in shared dirs are all sanctioned/out-of-Step-7: About-page product links (`AboutPage*.xaml(.cs)`), brand copy (`OnboardingPage.xaml` "مستقلك", `ExitConfirmationBox.cs`, Settings XAML labels) → UI-phase; `DesignDataSeeder` URLs → dev-fixture exception; `ISourceDescriptor.cs:98` + `SettingsViewModel.cs:85` + `BudgetFormatter.cs:9` → doc comments (illustrative, pre-existing, not behavioral). None introduced by this step; the actual behavioral sniffs/literals were removed.
5. Legacy-settings proof (read-only): live `%LOCALAPPDATA%\MostaqlK\Settings\preferences.dat` holds the flat
   container `""` with `settings_query_params`, `settings_is_polling_active`, `settings_max_requests_per_minute`,
   `settings_is_dark_mode` and **no `mostaql` container**. A namespaced read of e.g. `QueryParams` for the mostaql
   container misses → the repository falls through to the flat key and returns `"category=development&sort=latest"`,
   i.e. existing installs keep their values with zero migration; the first write moves it into the `mostaql` container.
   The cookie rewrite path is proven by code inspection (a read-only immutable DB query returned no visible rows
   because the session, if any, lives in the un-checkpointed WAL and its value is DPAPI-encrypted anyway — the DB was
   **not** modified).

## Decisions & rationale
- Added 6 descriptor members rather than keep site literals in shared code (my hard contract: shared dirs must be free of `mostaql.com`/`مستقل`/`ر.س`/`MOSTAQL_COOKIE`); the generic `MOSTAQLK_COOKIE_{SOURCE}` is derived from the source id in shared code (product-identity token only), legacy aliases come from the module.
- Split parsing into a dependency-free `CookieParser` so `tools/ParserTests` still compiles without a DI container or the whole `Core.Sources` set (honoring the plan's "static parse entry point" note; renamed the entry to `CookieParser.ParseFile`).
- Kept `SecretRepository` and made the pre-namespacing cookie key a descriptor fact (`LegacyCookieSecretKey`) so shared infra holds no per-site key name; deleted the old `MostaqlCookieKey` const.
- Orchestrator now applies the persisted poll interval **per source** (was gated to `SourceId.Default`) — identical for single-source, correct for the future multi-source case.
- Added `Contains`/`ContainsGlobal` to `ISettingsRepository` (only genuinely-required interface change).

## Open questions surfaced (not decided)
- **`mostaql_session` literal in `SettingsViewModel.SaveSessionCookieAsync`** (manual cookie-text paste wraps bare input as `mostaql_session=…`). It is a session-cookie-NAME coupling (a Mostaql fact), but lowercase and NOT in the enumerated grep-gate tokens, and outside Step 7's explicit settings/cookie/HTTP sites. Left for behavior-identity; candidate to fold into a descriptor member (e.g. `SessionCookieName`) in a later pass.
- **Intentional UA convergence:** the asset-download request now sends the policy Chrome/126 instead of the old duplicate Chrome/120. This is the deliberate "one policy UA per source" the plan calls for, but it is technically a change in the exact string the site sees for downloads.
- **Single shared `HttpClient` across pipelines:** today only the default source's policy client is registered and injected into `MostaqlFetcher`/`AssetDownloadService`. True per-source `HttpClient`s would come when a second source with different headers needs them (cookie + settings are already per-source; the client is the remaining global). Flagged rather than over-engineered for one source.
- Concurrent agents completed Steps 8/9 renames (e.g. `ProjectDetailsViewModel` no longer contains the `mostaql.com` URL fallback) in the same tree; my edits were kept additive and the final build/harnesses pass with those edits present.
