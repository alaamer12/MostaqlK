# site-literal-scatter-auditor

Read-only audit of "one site fact, one home" across the repository at HEAD `84844e7` (branch `v3`, `1.0.4.dev9`).
Nothing was modified; the only file created is this report.

## Goal

Verify the owner's requirement that scattered `https://mostaql.com` literals are gone and every monitored-site
fact lives behind `Core/Sources/ISourceDescriptor` contracts, with concrete site knowledge confined to
`Sources/Mostaql/`. Plan reference: `C:\Users\amrmu\.qoder\plans\fresh-narrows-gull.md` (steps 3/7/8 + the
grep-gate exception list at "Verification" item 4).

## Checks performed

1. Host literal census (`mostaql.com`, case-sensitive and case-insensitive, `.cs`/`.xaml`/`.sql`, tracked +
   untracked, excluding `bin/ obj/ .git _site api docs scratch .repertoire`).
2. Descriptor-function coverage for every site-shaped URL construction (`"https://`, `$"https`, `Uri(`,
   `StartsWith("http`, `"/project`, `"/u/`, `attachments`, `.Domain`).
3. Sibling literals: `مستقل` (excluding `مستقلك`), `ر.س`, `MOSTAQL_COOKIE`, `mostaql_session` (+ `Mostaql_session`
   case variant), deleted types `MostaqlScraper` / `IProjectScraper` (+ `MostaqlLinkButton` / `OpenOnMostaql`).
4. Semantic spot-check of HEAD vs `git show 20d4047:` for `MostaqlSource.BuildAbsoluteUrl`,
   `DetailParser` call sites (610/627), and `StructuralExtractor` attachment auth-sniff (680).
5. Evidence runs: `dotnet build MostaqlK.csproj -f net10.0-windows10.0.19041.0` → 0 warnings / 0 errors;
   `dotnet run --project tools/ParserTests` → 135 passed / 0 failed;
   `dotnet run --project tools/DbMigrationTests` → ALL DB MIGRATION CHECKS PASSED (schema reaches `user_version` 3).

## Files read

`Sources/Mostaql/MostaqlSource.cs`, `MostaqlModule.cs`, `MostaqlFetcher.cs`, `MostaqlSourceServices.cs`,
`Sources/Mostaql/Parsing/DetailParser.cs`, `StructuralExtractor.cs`, `ListingParser.cs`,
`Core/Sources/ISourceDescriptor.cs`, `Core/Sources/SourceId.cs`, `Infrastructure/Database/SqliteConnectionFactory.cs`,
`Infrastructure/Database/DesignDataSeeder.cs`, `Infrastructure/Http/AssetDownloadService.cs`,
`Infrastructure/Http/CookieJar.cs`, `Infrastructure/Http/HttpClientPolicyFactory.cs`,
`Infrastructure/Notifications/NotificationUrlLauncher.cs`, `Services/CookieStore.cs`,
`Features/Settings/ViewModels/SettingsViewModel.cs`, `Features/Projects/Views/Layouts/AboutPage*.xaml(.cs)`,
`Features/Settings/Views/Layouts/SettingsPanel*.xaml`, `UI/PlatformComponents/SourceLinkButton/SourceLinkButton.xaml.cs`,
`tools/ParserTests/Program.cs`, `tools/DbMigrationTests/Program.cs`.

## Verdict

Host-literal scatter: **clean**. One real remaining shared-code site coupling found: the two session-cookie
**placeholders** in the Settings XAML layouts hardcode `Mostaql_session` while the adjacent label is already
descriptor-driven. One coverage gap noted: `ISourceDescriptor` has no site-root/home URL member, so the About-page
launcher links cannot be routed through the descriptor without adding one.

## Violations / findings (file:line)

- `Features/Settings/Views/Layouts/SettingsPanelMobileLayout.xaml:156` — `Placeholder="Mostaql_session..."`.
  Fix: descriptor-derived placeholder text, e.g. add to `SettingsViewModel`
  `public string CookiePlaceholderText => $"{MostaqlK.Core.Sources.SourceRegistryProvider.DescriptorFor(_source)?.SessionCookieName ?? SourceName}..."`
  (or a Latin-labelled variant using `DisplayNameLatin`) and bind `Placeholder="{Binding CookiePlaceholderText}"`.
- `Features/Settings/Views/Layouts/SettingsPanelWindowsLayout.xaml:269` — `Placeholder="الصق قيمة Mostaql_session هنا..."`.
  Fix: same property, bound: `Placeholder="{Binding CookiePastePlaceholderText}"`
  (`$"الصق قيمة {descriptor?.SessionCookieName} هنا..."`).
- Informational (allowed, listed for completeness):
  - `Features/Projects/Views/Layouts/AboutPageMobileLayout.xaml.cs:29` / `AboutPageWindowsLayout.xaml.cs:49` —
    `new Uri("https://mostaql.com")` launcher (plan-sanctioned product identity, deferred to UI phase). To close it
    later, `ISourceDescriptor` needs a site-home member (e.g. `SiteHomeUrl => SiteRoot`); `BuildAbsoluteUrl("")`
    returns `https://mostaql.com/` (trailing slash) so it is **not** byte-identical.
  - `Features/Projects/Views/Layouts/AboutPageMobileLayout.xaml:96` / `AboutPageWindowsLayout.xaml:159` —
    disclaimer copy `Mostaql.com` (capital-M variant; only caught by a case-insensitive grep) +
    `AutomationId="About_MostaqlLink"` (kept on purpose, asserted by `AboutPageTests`) and the private handler name
    `OnMostaqlLinkTapped` in both `.xaml.cs` (line 25 / 45).
  - `Infrastructure/Database/DesignDataSeeder.cs:207,321,326` — dev fixture URLs (plan-sanctioned). Route via
    `SourceRegistryProvider.DescriptorFor(SourceId.Mostaql)?.BuildProjectUrl(id)` if the seeder is ever kept live.
  - `Platforms/Windows/NativeSplashScreen.cs:382` — `منصة متابعة وإشعارات مستقل` splash brand copy (sanctioned).
  - `Infrastructure/Database/SqliteConnectionFactory.cs:184,188,189` and
    `Infrastructure/Database/Migrations/0002_add_source_identity.sql:9,13,14` — `DEFAULT 'mostaql'` (source **id**,
    not host) duplicated outside its home `Core/Sources/SourceId.cs:38`. Frozen migration history; a `const string`
    SQL block cannot interpolate `SourceId.Mostaql.Value`. Note only.
  - `Sources/Mostaql/Parsing/DetailParser.cs:595` — `https://mostaql.com/u/username` in a doc comment (comment-only).
  - `Sources/Mostaql/Parsing/DetailParser.cs:362` — `SiteKeywords = ["مستقل", "Mostaql", "Mostaqlk"]`: a second in-module
    home for the display-name strings (module-internal parse fact, so allowed by the plan). Could read
    `MostaqlSource.Instance.DisplayNameAr` / `DisplayNameLatin`, but it also carries the app's own name `Mostaqlk`,
    which is not descriptor data — decision left to the owner.
  - `tools/ParserTests/Program.cs:20,418,419,485,486,499,509` and `tools/DbMigrationTests/Program.cs:300-302` —
    harness literals (allowed class D).

## Behavior-identity spot-check (HEAD vs `20d4047`)

| Input | Old (inline) | New (`BuildAbsoluteUrl`) | Identical? |
|---|---|---|---|
| `/u/x` | `https://mostaql.com` + `""` + `/u/x` → `https://mostaql.com/u/x` | `SiteRoot` + `""` + `/u/x` | yes |
| `u/x` | `https://mostaql.com` + `"/"` + `u/x` | same | yes |
| `https://x` | unchanged (call site guard `!StartsWith("http")`) | unchanged (guard + method both check) | yes |
| `//x` | `https://mostaql.com//x` | `https://mostaql.com//x` | yes — pre-existing quirk, no drift |
| `""` | unreachable (guarded); would give `https://mostaql.com/` | same | yes |
| `null` | NRE at call site | NRE at call site (`StartsWith`) | same failure mode |
| Auth sniff | `url.Contains("mostaql.com/attachments/")` (ordinal) | `url.Contains($"{MostaqlSource.Instance.Domain}/attachments/")` (same overload, ordinal) | yes |

## Skill used

`.cursor/skills/` does not exist on branch `v3` (verified: `ls .cursor/skills` → no such directory), so the repo's
mandatory-skill rule could not be satisfied literally. This task was a pure read-only audit; no skill-compatible
workflow (code change / command orchestration) was required.
