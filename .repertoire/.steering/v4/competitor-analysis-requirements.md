# V4 — Competitor analysis and personalized winnability

> **Status: requirements draft, 2026-11-03.** Decisions in §8 are agreed; items in §9 are not settled and
> must not be implemented on assumption. This document is the v4 counterpart to
> [`../v3/backend-ui-contract.md`](../v3/backend-ui-contract.md) and inherits every constraint in §5.R6.
> **§2 is the full investigation trail** (artifacts, scripts, commands, who established what);
> **Appendix A** is the sequence of events, **Appendix B** the alternatives rejected, **Appendix C** the
> corrections — including the ones against my own earlier claims.
>
> **Evidence labels used throughout:**
> **PROVEN** — verified by direct read this session · **CITED** — from a read-only sweep with a file:line
> pointer, not personally re-checked · **OWNER** — stated by the project owner, unverified ·
> **INFERRED** — reasoned from evidence, unproven · **UNKNOWN** — nobody has established it.

---

## 1. What v4 is

v3 taught the app to watch two platforms and to know every **project** on them. v4 makes it know the
**people** — the freelancers competing for those projects, and the app's own user among them — and
turns that knowledge into a per-project answer to the question a freelancer actually asks:

> *Can I win this one?*

Three surfaces, one split, and the split is load-bearing:

| Surface | Subject | May it depend on who is looking? |
|---|---|---|
| `performer-profile.html` | **about him** — one performer's file, neutral | **No.** Ruled explicitly: a performer page cannot have special treatment based on the viewer's profile. |
| `bidders.html` | **about us** — me versus the field on one project | **Yes.** `?profile=true` is its personalization switch. |
| `profile.html` | **about me** — the input surface for every personalized number | N/A — it *is* the profile. |

The deliverable of v4 is not a page; it is a **winnability index on every open project**, fed by my
profile and by the resolved competitor files, with the competitor surfaces as its explanation.

---

## 2. The investigation trail

### 2.1 The prior session (2026-11-01) — how the 10 performers and Peter were found

**Reconstructed from surviving artifacts, and one limit stated up front:** the scripts that ran the
10-performer pass were deleted afterwards, per the repo's own scratch rule
(*"write a throwaway script under `scratch/`, run it, delete it when finished"* — `AGENTS.md`).
`scratch/__pycache__/` contains only `dump_trace.cpython-311.pyc`, which belongs to the crash-dump work,
not the scrape. **So the exact command lines of that pass are not recoverable** — everything below is
what the artifacts prove, not what was typed.

| Artifact | Bytes | Modified | What it proves |
|---|---|---|---|
| `scratch/nafezly_extract.txt` | 251 | Nov 1 14:17 | Pass 1 resolved 10 usernames → `SAVED scratch/nafezly_profiles.json` |
| `scratch/nafezly_profiles.json` | 21,291 | Nov 1 14:17 | Pass 1 output: identity + skills only |
| `scratch/nafezly_extract2.txt` | 241 | Nov 1 14:21 | Pass 2 re-ran the same 10 → `SAVED` |
| `scratch/nf_run3.txt` | 902 | Nov 1 14:21 | Pass 2 per-performer counts (below) → `nafezly_profiles_full.json` |
| `scratch/nafezly_profiles_full.json` | 59,832 | Nov 1 14:21 | The record set this whole design rests on |
| `scratch/profile_sample.html` | — | Nov 1 | Raw profile markup — the bio-selector ground truth |
| `scratch/offers_sample.html` | 133,956 | Nov 1 | Raw `/u/<user>/offers` markup — proved relative times exist, budgets do not |
| `scratch/nafezly-freelancers` | 211,969 | Nov 1 | A capture of `https://nafezly.com/freelancers` («المنفذين | نفذلي»), **24 unique `/u/` slugs on the page** — this is where **Peter\_\_tharwat** was found |
| `scratch/peter.html` | 128,760 | Nov 1 14:33 | Peter's profile page — the canon field set |
| `scratch/peter_projects.html` | 170,872 | Nov 1 14:35 | Peter's projects/offers history |
| `scratch/nf_bidders.py` | 993 | Nov 1 19:38 | **The only surviving scraper script** — the bidder-identity probe |

**The 10 performers, verbatim from `nf_run3.txt`** (`portfolio | services | offers | skills`):

```text
ahmed_dahy9          Ahmed Dahy       portfolio 4 | services 0 | offers 6 | skills 11
ahmedfathallah1      Ahmed Fathallah  portfolio 3 | services 0 | offers 4 | skills 2
Ahmedhegazii22       Ahmed Hegazy     portfolio 3 | services 0 | offers 6 | skills 5
EngAmira_Ehab        Amira Ehab       portfolio 4 | services 0 | offers 6 | skills 11
hadeer_osama         Hadeer Osama     portfolio 3 | services 0 | offers 6 | skills 9
hanin_hussien        Hanaa Hussien    portfolio 3 | services 0 | offers 5 | skills 1
HazemAhmed12         Hazem Ahmed      portfolio 4 | services 0 | offers 6 | skills 9
Kamel_Mohamed277     Kamel Mohamed    portfolio 3 | services 0 | offers 5 | skills 10
Karim_Elkhouly       Karim Elkholy    portfolio 3 | services 0 | offers 2 | skills 1
khaled_yasserr       Khaled Yasser    portfolio 3 | services 0 | offers 4 | skills 16
SAVED scratch/nafezly_profiles_full.json
```

Two facts fall out of that log that the summary in the owner's brief did not state: **`services 0` on
every single row** (the empty-`/services` finding is not a sample quirk), and the **slug forms are wildly
inconsistent** (`hanin_hussien` for "Hanaa Hussien", `Ahmedhegazii22` for "Ahmed Hegazy",
`Karim_Elkhouly` for "Karim Elkholy") — which is the concrete reason R2/R3 key identity on the numeric
avatar id rather than the slug or the name.

**How Peter was selected.** He is not one of the 10. He was picked off the `/freelancers` listing capture,
where his card carries `class="fas verified-nafezly-badge fa-check-circle" data-content="موثوق من منصة
نفذلي"` — a platform-verified account. That badge is the real-world anchor for the canon's «متميز» axis,
and it is **the one canon axis that is a site badge rather than a scrapeable count**. He also appears twice
inside the real fixture `tools/ParserTests/Fixtures/nafezly/detail-open.html`, but **as a `/service/…` link
near a `laravel` tag, not as an `offer-order` bidder row** — so Peter lists services while all 10 sampled
performers list none. That asymmetry is exactly why he reads as the mature file.

**Credential note.** `scratch/nafezly-freelancers` contains a `<meta name="csrf-token">`. It is
credential-shaped; the value is not reproduced anywhere in this document and must not be.

**The surviving script, verbatim** — `scratch/nf_bidders.py`. It is the bidder-identity probe, and it is
the reason §4.2 is stated as fact rather than hope:

```python
import urllib.request, re, html

UA = {'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 \
(KHTML, like Gecko) Chrome/130.0 Safari/537.36'}

def get(u):
    return urllib.request.urlopen(urllib.request.Request(u, headers=UA), timeout=25)\
        .read().decode('utf-8', 'replace')

d = get('https://nafezly.com/project/57334-%D8%A5%D9%86%D8%B4%D8%A7%D8%A1-Cv-%D8%A7%D8%AD%D8%AA%D8%B1%D8%A7%D9%81%D9%8A')
# context around each /u/ link
for m in re.finditer(r'href=["\']https?://nafezly\.com/u/([^"\']+)["\']', d):
    start = max(0, m.start() - 200)
    chunk = d[start:m.end() + 600]
    txt = html.unescape(re.sub(r'<[^>]+>', ' ', chunk))
    txt = re.sub(r'\s+', ' ', txt).strip()
    print('== /u/%s ==' % m.group(1)); print(txt[:300]); print()

# section headings present on page
for h in re.findall(r'<h[1-4][^>]*>(.*?)</h[1-4]>', d, re.S):
    t = re.sub(r'<[^>]+>', '', h); t = re.sub(r'\s+', ' ', html.unescape(t)).strip()
    if t: print('H:', t[:90])
```

Two things it establishes: the method (plain `urllib`, a Chrome/130 UA, no browser, no session), and
**the project it probed is `57334-إنشاء-Cv-احترافي` — a real CV-automation project.** That matters for
§9.3(2): the bidders investigation was actually run against the CV project that `project-details.html`
and `performer-profile.html` both depict, so `bidders.html`'s «مخزن خشب» header is the anomaly, not the
anchor.

**The owner's method statement** (from the original brief, not independently verified): 4 pages per
performer (`/u/<user>`, `/portfolio`, `/services`, `/offers`), 40 requests total at 2.5 s spacing, plain
HTTP, no Cloudflare block, everything server-rendered; performers were taken from the app's own DB
(ongoing/completed Nafezly projects), and 18/18 name→profile matches succeeded. **OWNER.**

### 2.2 This session (2026-11-03) — commands run and what each established

| Command (abridged where noted) | Purpose | What it established |
|---|---|---|
| `git status` / `git log --oneline -15` / `git diff --cached --stat` | State of the tree | Two staged mockups, nothing else; branch `canary` at dev85 |
| `gh auth status` | Test the owner's "token expired" theory | **Token valid** (`repo`, `workflow`, `read:org` scopes) — the earlier empty output was `gh run view --log`, not auth |
| `gh run list --limit 8` / `--limit 100 --json …` | CI health | 39 failures / 5 successes; last green **2026-08-26** |
| `gh run view 41462555558` | Which jobs failed | `quality` ParserTests + `Build & Release` release step |
| `gh api repos/…/actions/jobs/125335037689/logs` | Actual failure text | `TOTAL: 614 passed, 2 failed` with both FAIL lines quoted verbatim in §9.5 |
| `gh api repos/…/actions/jobs/125335037393/logs` | Release failure text | `Cannot validate argument on parameter 'Type'. The argument "-Type" does not belong to the set "Portable,Directory"` |
| `gh release list` | Blast radius | Newest release v1.0.3, 2026-08-26 — nothing published since |
| `git log -S'releaseScriptArgs' -- scripts/github-release.ps1` | When it broke | `d237999` (2026-09-02) |
| Existence loop over every path `AGENTS.md` declares | Pointer audit | All 8 steering docs exist; `Migrations/` and `Core/Sources/CookieSecretKey` do not (§9.4) |
| `find . -name "*.cs" … \| wc -l` | Scope | 379 source files |
| `python - <<'PY'` over `nafezly_profiles_full.json` (3 passes) | Field + quirk audit | Keys per record; **50 offers, 1 with budget, 0 with posted**; stats-key union (5 keys); per-bio quirks: prefix on 2, entities on 4, trailing chip dump on **all 10** |
| `grep`/python over `scratch/offers_sample.html` | Does the data exist? | `منذ` 9 hits, `يوم` 4, `ساعة` 3 → **times exist, the scraper missed them**; `دولار` 0, `ميزانية` 0, `$<digit>` 0 → **budget genuinely absent** |
| `powershell -File scripts/build.ps1 -Platform Windows -Channel canary` | Reproduce the owner's 3 errors | `Build succeeded. 0 Warning(s) 0 Error(s)` in 52.6 s |
| `powershell -File scripts/release-windows.ps1 -Type Portable -Arch x64 -Channel canary` | Reproduce win-x64 + prove the splat bug | `Release successful!`, 133.35 MB exe — so CI's failure is the caller's array splat only |
| Playwright `scratch/pp_mode_check.py` | DOM state of both URL modes | DEFAULT: `mePillVisible false`, threat `خطورة`, back → `project-details.html`. PROFILE: all true, threat `خطورته ضدك`, back → `bidders.html?profile=true` |
| Playwright `scratch/pp_shot.py` (light + dark) | Visual verification | `tailwindLoaded True`, `fontFamily Tajawal`, `brandToken #FFA200`, 9 head-to-head rows, 4 gap rows; 4 PNGs |
| `for id in …; do grep -c "id=\"$id\"" bidders.html; done` | Is the approved mode real? | All 10 profile-mode targets present exactly once |
| `git restore --worktree -- performer-profile.html README.md` | Undo my edits | Verified after: 0 traces of `mode-switch`/`me-pill`/`headtohead`/`skillgap`, README 132 lines, `bidders.html` 508 lines, nothing else modified |
| `sed`/`grep` on `NafezlyDetailParser.cs:154-158,390-428`, `SourceCapabilities.cs`, `SqliteConnectionFactory.cs:318-330` | Re-check the agents' load-bearing citations before publishing them | Offer-row count is the only bidder use; owner rating/completed stay null **by an explicit code comment**; `HasSupervisedFlag: false` on both; owner columns exist |
| `grep -o 'data-score=…' / 'data-type_id=…' / 'avatars/small/[0-9]*_' detail-open.html` | Bidder-row reality | `data-score="38.297872340426"`, `data-type_id="422278"`, avatar ids `318530_…`, `332228_…`, `307293_…`; **72** `offer-order` blocks; **150** `js-profile-app-gate` anchors |
| `grep -rl "data-bid-item" tools/ParserTests/Fixtures/` | Mostaql count proof | **Empty** — no fixture anywhere contains it |
| python context dump of `scratch/nafezly-freelancers` | How Peter was found | 24 unique slugs; Peter's card carries the `verified-nafezly-badge` with «موثوق من منصة نفذلي» |

### 2.3 Delegated read-only sweeps

Six static-only sub-agents were dispatched (no build, no run, no writes): `doc-chain-reader`,
`core-code-sweeper`, `gates-and-tests-inventorier` during the project examination; then
`identity-surface`, `cookie-plumbing`, `bidder-visibility` and `field-matrix` for v4. Their findings are
cited throughout with file:line. **Claims I re-verified personally** are the ones in §2.2's last four
rows plus the ParserTests/CI pair; **claims taken on their citations alone** include the Mostaql
owner-stat parse path (`DetailParser.cs:187-198`), the `CookieParser.cs:97-107` expiry-column drop, and
`AssetDownloadService.cs:52-56` — all plausible, all worth a second look before code depends on them.
Findings from the sweeps that were *not* independently confirmed are marked **CITED** rather than
**PROVEN**.

### 2.4 The Mostaql investigation (2026-11-04) — live, with the owner's session

The owner handed over `cookies.txt` (repo root, gitignored at `.gitignore:9`, **never read into any
output**) and asked for the same treatment Nafezly got: freelancers, the user profile, what a cookie
unlocks, and the full data set. Twenty GETs total, 7–8 s apart, no POST, no mutation, no download of
attachments. Evidence lives as raw HTML in `scratch/mostaql/` (21 pages, 3.2 MB, excluded via
`.git/info/exclude:9`), which is the agreed evidence route from §9.1.

| Command | What it established |
|---|---|
| `python scratch/mq_probe.py` | `GET /projects` **anonymous** → 200, 177,580 B, `Set-Cookie: XSRF-TOKEN, mostaqlweb`. **The site's session cookie is `mostaqlweb`; `MostaqlSource.cs:84` declares `mostaql_session`.** 50 project links, exactly **one** `/u/` link, `data-bid-item` 0 (a listing has no bid rows), 53 login-prompt hits. |
| `python scratch/mq_probe.py routes_anon.txt --gap 6` | `/project/{id}-{slug}` anonymous → 200, 271,265 B, **`data-bid-item` ×28** with `?bid={id}` deep links. `/u/{username}` anonymous → **HTTP 202, 1,983 B, `window.gokuProps` = AWS WAF JavaScript challenge**. `/freelance` is the marketing page, not a catalog. `/portfolios` anonymous → 200, 157,307 B, **23 real `/u/` usernames + 35 `/portfolio/` links**, pagination `?limit=36&newness=quarter&page=2`. `/projects/programming` → **404** (category slugs are a fixed vocabulary, not free text). |
| `python scratch/mq_probe.py routes_auth2.txt --auth --gap 8` | The session half, all 200: `/dashboard/bids` 256,047 B «العروض الخاصة بي»; `/project/…?bid=…` 333,488 B (62 KB fatter than anonymous, same 28 bid rows); `/freelancers` 283,776 B with **75 `/u/` links**; `/freelancers/full-stack-developer` 296,257 B; `/dashboard/projects`, `/notifications`, `/account/membership` all real pages. **`/u/{username}` with a session is still 202 + `gokuProps`** — the WAF does not care about the session. |
| `python scratch/mq_probe.py routes_anon2.txt --gap 7` | **`/freelancers` and `/freelancers/{slug}` work anonymously** (198,767 B / 209,969 B, 75 `/u/` links each) — performer discovery needs no session. A real bid project fetched anonymously (115,303 B, 4 bid rows). |
| `python scratch/mq_probe.py routes_anon3.txt --gap 7` | `/dashboard/bids` **anonymous** → 200 with **2,219 B of `accounts.hsoub.com` SPA shell** («You need to enable JavaScript to run this app»). `/freelancers?skills=laravel` anonymous → filters genuinely (25 users, only 3 overlap with the unfiltered page; "laravel" 12× vs 1×). `/portfolio/{id}-{slug}` anonymous → 200, 76,730 B. |
| `scratch/mq_read.py`, `mq_rows.py`, `mq_bids_table.py`, `mq_fields.py` | Local extractors over the saved HTML: route families from every `href`, form `action`s, `data-*` inventories, per-row markup, and the `/dashboard/bids` row table. They are what turned "a big page" into the field lists in §4.6. |

Two things this pass settled by construction rather than by asking: the **`/freelancers` filter form is
the platform's own field dictionary** (`keyword`, `specialities[]`, `titles`, `profile[job_title_id]`,
`skills`, `freelancer__rating`, `verified`, `connected`, `bid_me`, `deal_with`), and the **inline
`Mostaql.headerConfig` script block is a route manifest** (`/dashboard/bids`, `/dashboard/projects`,
`/dashboard/bookmarks`, `/account/profile`, `/payments`, `/invoices`, `/orgs`, `/project/create`,
`/freelancers`, `/ajax/*`, `/api/v1/paypal/accounts`). Both were read out of anonymous HTML, so they are
available to the scraper today with no session.

### 2.5 The second live pass (2026-11-05) — what it overturned

The owner handed a second credential file (`nafezly.com_cookies.txt`, Netscape format, session cookie
`nfthly_session` + a long-lived `remember_web_…`) and asked for the unproven things to be proven, with a
JavaScript-capable browser where a plain client was blocked. ~40 requests at 13 s pacing, GET-only, no 429.
Evidence: `scratch/mostaql/q1_*.html`, `scratch/nafezly/nf_*.html`, `scratch/mostaql/pw_u_*.html|json|png`.

**Two of this document's recorded conclusions are now wrong, and they are the load-bearing ones.**

1. **D33 / C11 — Mostaql `/u/` is NOT unreachable.** Playwright/Chromium passes the AWS WAF challenge: the
   first response is the 202 challenge page, it self-solves, and **2.5 s later the real profile arrives**
   (236,860 B with a session; **129,316 B with no cookies at all**, same profile fields — the session only
   adds the notification/messaging tray). An `aws-waf-token` is minted in the process. So the blocker was
   never privilege, it was **JS execution**. The freelancer profile field set, which §9.1 recorded as
   unknown, is now known and richer than the catalog:
   `إكمال المشاريع` 100.00% · `التسليم بالموعد` 100.00% · `إعادة التوظيف` 0.00% · `نجاح التواصلات` 20.00% ·
   `متوسط سرعة الرد` «ساعة و 49 دقيقة» · `المشاريع المكتملة` 2 · `التقييمات` 2 · `تاريخ التسجيل`
   20 سبتمبر 2024 · `آخر تواجد` «منذ دقيقتين» · توثيقات (بريد/هاتف/هوية) · أوسمة · portfolio ids ·
   project links · a five-axis review rubric (الاحترافية/التواصل/الجودة/الخبرة/التسليم) — **and no prices**.
   What the app can do with this is an open architecture question (§9.2 item 7), because MAUI's HttpClient
   is not a JS engine.
2. **The winning price exists only for deals you WON, on both sources — and it was never findable on a
   lost project.** Previous passes checked *open* projects; this one fetched genuinely awarded ones.
   On two lost Mostaql projects, **51 bid rows across both pages contain zero money tokens and zero award
   vocabulary** (no «تمت الترسية/الفائز/تم توظيف/بمبلغ»); the only money on the page is the budget, which
   the site itself labels `data-type="project-budget_range"`. The one «المبلغ $420.00» found sits inside
   **my own** `data-bid-item` row. On Nafezly, 35 offer rows on a completed project carry no price field at
   all — prices appear only where a bidder typed them into prose.
   **What is now proven available instead:** the agreed amount on a deal I won. Nafezly states it in the
   session feed — «وتم إضافة المبلغ المتفق عليه بينكما : 20 $ في رصيدك». Mostaql exposes it as a payout
   ledger at `/payments` (a surface never fetched before): «الربح من إكمال مشروع …» with `+$24.00`,
   `+$36.00`, `+$100.00`, and a page-config constant `projectFees: 20`; dividing each payout by 0.8
   reproduces three of my recorded bids exactly. `/invoices` is wallet balances only.
   Consequence for the score model: **§9.1's "unknowable" was half right** — the price a *competitor* won
   at is unknowable on both sources, but *my* deal price is knowable, with a 20% fee to undo on Mostaql.

**A disclosure the investigating agent made, recorded here rather than buried.** One console command
mis-assumed the Netscape column order and echoed **Nafezly cookie values to the terminal** before being
corrected. Nothing was written to disk, to a script, or into any file in this repo; the values went to the
session transcript only. It is nonetheless a breach of the rule this document sets in §9.1, and the
`remember_web_…` token behind it is valid to 2027 — the owner should decide whether to rotate it.

---

## 3. Scraping mechanics that are now settled fact

| Item | Finding | Status |
|---|---|---|
| Display name → profile resolution | Search `https://nafezly.com/freelancers?key=<Name+With+Plus>&page=1`; first card is usually the performer. 18/18 matched. Names may be truncated (`"Ahmed Elsayed Taw."`) — strip punctuation and retry. | OWNER, consistent with fixtures |
| Slug ≠ person | `ahmed_elgendy15` = "Ahmed Elsayed Taw."; `hanin_hussien` = "Hanaa Hussien". JSON-LD `name` is the source of truth. | OWNER + PROVEN in fixtures |
| Gallery location | Lives at `/portfolio`, not on the profile page (3–4 items each; URL-encoded titles decodable). | OWNER |
| `/services` | Empty for all 10 performers («لا توجد خدمات»). Performers sell via bids. | PROVEN in JSON |
| `/offers` row contents | title, status, project URL, relative time («منذ 14 ساعة», «منذ يوم», «منذ يومين»). **No budget** (`دولار` 0 hits, `ميزانية` 0 hits, no `$<digit>`), **no completion date**. | PROVEN |
| Statuses | `مكتمل` / `مغلق` / `مستبعد` / `ملغي` / `بإنتظار الموافقة`. One record also carried an unmapped `?` status. | PROVEN |
| Win/loss record without a session | `مكتمل` = won, `مستبعد` = lost, `مغلق`/`ملغي` = neither; joined to the project's own detail page (which *does* carry the budget band) this yields **win rate by band with no session at all**. | INFERRED, high confidence |
| Bid amounts | Not present on `/offers` rows nor on anonymous project offer rows. Whether a session reveals my price and the winning price: **UNKNOWN**. | §9.1 |
| Co-bidder slugs are free | My own `/u/<me>/offers` page exposes other freelancers' `/u/` slugs unmasked — `Marwa_Abdeltwab`, `2mostafaelsayed`, `zeyadwael2`, `Nour_Alaa_Sheir`, `SALAH10` at `scratch/offers_sample.html:1625-1809`. The scraper **discarded** these anchors. | PROVEN |
| Bio quirks to clean | (a) `لم يكتب نبذة شخصية` prefix glued onto real text (Hazem, Khaled); (b) HTML entities `&amp;`, `&#039;` (4 of 10); (c) **all 10** bios end with a comma-joined skill-chip dump appended to the section text. | PROVEN |
| Bio source | Normal on-page section content («نبذة عني»), **not** `meta[name=description]`. | OWNER correction, accepted |
| Rate ceiling | 8 back-to-back fetches produced `HTTP 429` / Cloudflare **1015**; recovery needed 90 s cooldown + 40 s pacing. Recorded operational lesson: *"keep well under 8 req/min sustained with backoff on 429."* | PROVEN (`tools/ParserTests/Fixtures/nafezly/capture-manifest.md:20-24`) |
| **Mostaql bot filter is AWS WAF, not Cloudflare — only on `/u/`, and passable with a one-time token** | `GET /u/{username}` from a plain client returns **HTTP 202 with a 1,983-byte JS challenge** (`window.gokuProps`) both anonymously and with a live session. Not a Cloudflare 429 class problem, and not permanent: mint `aws-waf-token` once in any JS engine, carry it, and the page serves normally (236 KB with a session, 129 KB without — same fields). Every other route (`/projects`, `/project/…`, `/freelancers`, `/portfolios`, `/dashboard/*`) serves full HTML to a plain client. Port spec **R1.10**, mechanism **§4.8** | PROVEN 2026-11-04, **corrected 2026-11-05** (`scratch/mostaql/u_owner.html`, `pw_u_YElmelegy_{auth,noauth}.html`) |
| **Mostaql's real session cookie is `mostaqlweb`** | `Set-Cookie` on every anonymous fetch: `mostaqlweb`, `XSRF-TOKEN` (Laravel), plus `AWSALB`/`AWSALBCORS` stickiness. `MostaqlSource.cs:84` declares `mostaql_session`, and `SettingsViewModel.cs:140,143` uses that name to wrap a **bare pasted value** into `name=value` — so a user who pastes their `mostaqlweb` value gets it stored under a name the site ignores, and every session-gated fetch silently degrades to the logged-out shell. | PROVEN (fetch) + CITED (`SettingsViewModel.cs:125-143`) |
| **A dead Mostaql session is HTTP 200** | Session-gated routes answer **200 with a 2,219-byte `accounts.hsoub.com` SPA shell** whose only text is «حسابات حسوب — جميع منتجات حسوب في مكان واحد» and "You need to enable JavaScript to run this app". No 401, no 302, no HTML redirect. Status code alone cannot detect expiry — this is the Mostaql instance of the Nafezly "a dead cookie looks like a successful scrape" class. | PROVEN (`scratch/mostaql/anon_my_bids.html`) |
| Session-alive probe URL | `GET https://mostaql.com/dashboard/bids`. Alive = ~200 KB + `<title>العروض الخاصة بي | مستقل</title>`. Dead = ~2 KB + `accounts.hsoub.com`. Cheap, side-effect-free, and the answer is unambiguous by byte size. This resolves the §9.1 "the probe needs a known session-only URL" question. | PROVEN 2026-11-04 |
| Login is hsoub SSO | Mostaql authenticates through `accounts.hsoub.com`; the `mostaqlweb` cookie is issued afterwards. Consequence: cookie expiry is owned by the SSO, not by mostaql.com, so "re-link the session" must send the user to the SSO, and the `?reread=fail` copy in `profile.html` should not promise a mostaql.com login. | PROVEN (shell body) + INFERRED (expiry ownership) |
| Mostaql rate behaviour observed | 20 GETs at 7–8 s spacing produced no 429, no challenge (outside `/u/`), no throttled body. The descriptor's 2 req/min (`MostaqlSource.cs:38`) is conservative but the real ceiling is **still unproven** — nothing here should be read as "safe at 8/min". | OBSERVED, not stress-tested |

---

## 4. Field-availability matrix — what each source can actually feed a score with

Provenance levels: **real-fixture** (verbatim in a curl-captured `Fixtures/nafezly/*.html` with
`.meta.json`) · **synthetic-fixture** (hand-authored `Fixtures/project_*.html`, which admit it at
`project_current_markup.html:2-9`) · **code-only** (parser or column exists, nothing exercises it).

### 4.1 Proven and already parsed — usable today

| Item | Nafezly | Mostaql | Where |
|---|---|---|---|
| Bid / proposal **count** | **real-fixture** (72/43/30 offer rows == عدد المتقدمين) | **real-page as of 2026-11-04** — `data-bid-item` ×28 on `/project/1290674-…` and ×4 on `/project/1271465-…`, anonymously, each row carrying its own server bid id. **This unblocks D28.** | `NafezlyDetailParser.cs:155-156` → `projects.proposal_count` |
| Project skills | **real-fixture** (JSON-LD `skills`, `/projects/skill/` anchors) | **real-page** — `ul.skills` + `/skills/{slug}` anchors on the fetched detail page | `project_skills` table |
| Budget (display string) | **real-fixture** `10 - 25 $` | **real-page, and it is USD**: `الميزانية $100.00 - $250.00`. The descriptor's `CurrencySuffix => "ر.س"` (`MostaqlSource.cs:40`) is **wrong** and is appended to the card and detail budget (`ProjectCardViewModel.cs:237`, `ProjectDetailsViewModel.cs:238`), so the app currently labels dollars as riyals | `projects.budget TEXT`; **no currency column** |
| Delivery duration | **real-fixture** (`المدة المتاحة`, LD `workHours`) | **real-page** — `مدة التنفيذ 5 أيام` on the project page; and per-bid on `/dashboard/bids` | `projects.delivery_days` |
| Status | **real-fixture** | **real-page** — `حالة المشروع مُغلق` on the detail page; five bid states on `/dashboard/bids` | `projects.status` |
| Posted time | **relative only** (`منذ…`) — LD `datePosted` exists and is **not parsed** | **relative on public pages** («منذ شهرين»), but **absolute on the session page**: `/dashboard/bids` rows carry `<time datetime="2026-11-02 11:09:04">` | `publish_time_number/text`; real times only app-side `discovered_at`/`enriched_at` |
| Executor (awarded freelancer), name only | **real-fixture**, null on open projects | `HasExecutorInfo:false` → always null | `NafezlyDetailParser.cs:438`, `projects.executor_name` |
| Owner name / avatar / hashed id | **real-fixture** (`OwnerId = StableHash(name)`) | name + profile_url | `NafezlyDetailParser.cs:390-427` |

### 4.2 Present in the HTML, **never parsed** — new work, no model or table exists

- **Bidder identities per project.** Each `div.col-12.offer-order` carries display name
  (`Ahmed Waleed`), avatar URL whose numeric prefix is the user id (`318530_…`), server offer id
  (`data-type_id="422278"`), a sort score (`data-score="38.29…"`) and the offer's opening text. The
  parser counts these rows and throws the rest away. **No `Offer`/`Bid`/`Bidder` class or table exists.**
- **Category.** LD `industry` / `occupationalCategory` and a breadcrumb `specialize` are in the page;
  not extracted; **no column**.
- **Absolute posted date.** LD `datePosted` present, not parsed.
- **Offer preview text.** Present, unparsed.

### 4.3 Session-gated on Nafezly (manifest §1.9–§1.11)

Owner profile URLs, **owner rating**, **owner completed-projects count**, executor success count,
numeric rating, verified/supervised facet, attachments.

### 4.4 Mostaql-only — **now proven on a real page** (was: synthetic markup only)

Measured 2026-11-04 on two live project detail pages fetched **anonymously**
(`scratch/mostaql/proj_detail.html`, `anon_proj_1271465.html`), inside the `div.profile_card` block:

| Field | Real page shows | Definition the site itself gives |
|---|---|---|
| `hiring_rate_percent` | `معدل التوظيف 50.00%` / `0.00%`, bucketed by `label-rating-excellent` / `bg-poor` | «نسبة المشاريع العامة التي وظف مستقلين عليها إلى المشاريع التي قام بفتحها» |
| `open_projects_count` | `المشاريع المفتوحة 1` / `0` | «عدد المشاريع المفتوحة بنفس الوقت وتتضمن هذا المشروع» |
| `in_progress_projects_count` | `مشاريع قيد التنفيذ 0` | «عدد المشاريع قيد التنفيذ التي يعمل عليها مع المستقلين» |
| `ongoing_communications_count` | `التواصلات الجارية 0` / `2` | «عدد الرسائل التي يجريها صاحب المشروع بجميع مشاريعه الحالية» |
| `registered_at` | `تاريخ التسجيل 13 يناير 2015` — an **absolute** date, on a public page | — |
| owner display name | `صاحب المشروع Hassan D.` (masked form) | — |

Still **not** proven on a real page: `completed_projects_count` (absent from the owner card on both
pages) and owner `profile_url` — **neither real page contains a `/u/` link in the owner card**, so the
`ProfileUrlPathSegment "/u/"` extraction (`MostaqlSelectors.cs:36`, `DetailParser.cs:557`) fires only on
some layouts. Consequence for the data model: on Mostaql the client has **no stable join key** — a masked
display name and a hire rate that cannot be tied to an account.

> **This reverses the Nafezly asymmetry.** Client quality is *session-gated* on Nafezly (§4.3, §4.5) and
> **free and anonymous** on Mostaql. The D-factors are therefore per-source by construction, not by
> tuning — see D34.

### 4.5 Dead or false right now

| Item | Reality |
|---|---|
| `owners.rating` | **Null in every real scrape on both sources.** Neither parser assigns it; `OwnerRepository.cs:57` persists `DBNull`. The only non-null rating in the repo is a `DesignDataSeeder.cs:385` mock. |
| Supervised / escrow flag | `HasSupervisedFlag: false` on **both** sources (`NafezlySource.cs:62`, `MostaqlSource.cs:46`), never true. **The «مشرف عليه» shown in `projects.html` is design fiction.** |
| Stored absolute site time | Neither source stores it. |
| Category | No column, no parse, on either source. |

> **Correction of record.** An earlier pass of this same discussion claimed client-quality factors were
> "free, already in the `owners` table". That was **wrong for Nafezly** — the columns exist but the
> parser leaves them null, and the code says so in a comment (`NafezlyDetailParser.cs:425`:
> *"rating / completed-projects-count are app-gated → stay null"*). Client quality is therefore a
> **session-gated** factor on Nafezly and a **synthetic-proven** factor on Mostaql, not a free one.

### 4.6 Mostaql field availability, measured 2026-11-04

Everything below is read off live HTML in `scratch/mostaql/`, not off fixtures and not off the parser.

**Routes, by what a plain HTTP client can do with them**

| Route | Anonymous | With session | Notes |
|---|---|---|---|
| `/projects`, `/projects/{category-slug}` | **yes** (177 KB) | yes | category slugs are a fixed vocabulary; `/projects/programming` is a 404 |
| `/project/{id}-{slug}` | **yes** (115–271 KB) | yes, +62 KB | bid rows, budget range, owner card, skills |
| `/project/…?bid={bidId}` | yes | yes | deep link to one bid row |
| `/project/…?deal_id={dealId}` | yes | yes | the private/awarded variant of the same link |
| `/freelancers` | **yes** (198 KB, 25 cards) | yes (283 KB) | **performer catalog, no session needed** |
| `/freelancers/{job-title-slug}` | **yes** | yes | taxonomy: `graphic-designer`, `full-stack-developer`, `ai-engineer`, … |
| `/portfolios`, `/portfolio/{id}-{slug}` | **yes** (157 KB / 76 KB) | yes | portfolio catalog + public work pages |
| `/u/{username}` | **NO — AWS WAF** | **NO — AWS WAF** | HTTP 202, 1,983 B, `gokuProps`. The session does not help. |
| `/dashboard/bids` | login shell (2.2 KB, 200 OK) | **yes** (256 KB) | my bids, with amounts |
| `/dashboard/projects`, `/dashboard/bookmarks`, `/account/profile`, `/account/membership`, `/payments`, `/invoices`, `/notifications`, `/messages`, `/orgs`, `/project/create`, `/u/{me}/portfolio` | login shell | yes | session-gated |
| `/ajax/report/bid/{id}`, `/ajax/report/user/{id}`, `/ajax/bookmark/user/{name}`, `/ajax/skills/predict`, `/ajax/jobTitles/predict`, `/bid` (POST) | partly rendered | yes | **`/ajax/report/user/{id}` is the only place a numeric Mostaql user id appears — session-only** |

**Bid rows on a project page — anonymous** (`div[data-bid-item]`, 28 on one project, 4 on another)

`data-bid-item="{server bid id}"` · `id="bid{id}"` · `data-context="default"` · bidder display name
(first name + initial, masked) · bidder **job title** · bidder **rating rendered as filled
`rating-star` icons** (a count, not a decimal) · `profile-verification-badge` · `img-thumbnail
bid-avatar` · the bid's own text · `/ajax/report/bid/{id}`. **No amount, and the session does not add
one to other people's rows.**

**`/dashboard/bids` — session only, 25 rows/page (`tr.bid-row`)**

| Field | Markup | Note |
|---|---|---|
| **my bid amount** | `<i class="fa fa-money"></i><span dir="rtl">$200.00</span>` | **Proven to be mine, not the budget**: the same project's public page states a *range* `الميزانية $100.00 - $250.00`; the my-bids row states the single `$200.00`. |
| outcome, five states | `label label-prj-pending / -processing / -completed / -closed / -lost` | `prj-lost` is the rejected case the v4 brief asked for; it is a first-class state here, not an inference |
| absolute timestamp | `<time datetime="2026-11-02 11:09:04">` | the only absolute site-side time found on either source |
| my committed duration | `مدة التنفيذ 5 أيام` | separate from the project's stated duration |
| delivery countdown | `باق على التسليم يوم` | live deal rows only |
| join key | `?bid={bidId}` (public) or `?deal_id={dealId}` (private/awarded) | **the `?bid=` id equals the `data-bid-item` id on the project page** — my row and the competitor rows are the same object |
| project + client | title, `/project/…` href, client masked display name | |
| my full bid text | `div.text-wrapper-div.project__brief` | the proposal I wrote |

**`/freelancers` card — anonymous** (this is the Mostaql performer field set, deferred question §2)

| Field | Source in markup |
|---|---|
| canonical username | `href="https://mostaql.com/u/{username}"` (unmasked, in every link) |
| display name | `<bdi>` inside that link — masked form only («Joseph O.») |
| job title + taxonomy | `fa-briefcase` → `/freelancers/{slug}` |
| **rating** | count of `i.fa-star.clr-amber.rating-star` inside `div.freelancers__item-rating` |
| **project completion rate** | `66.67`, tooltip: «معدل اكمال المشاريع — نسبة المشاريع التي تم توظيف المستقل عليها وقام بتسليمها إلى إجمالي المشاريع التي استلمها» |
| **presence** | `i.usercard__indicator-top.fa-circle.clr-green`, `title="متصل الآن"` |
| avatar | `img.uavatar--medium` 64×64 |
| full bio | `div.freelancer__brief` |
| invite / bookmark / report actions | `دعوة للعمل`, `/ajax/bookmark/user/{name}`, `/ajax/report/user/{id}` (session) |
| server-side filters, all anonymous | `keyword`, `specialities[]`, `titles`, `profile[job_title_id]`, `skills`, `freelancer__rating`, `verified`, `connected`, `bid_me`, `deal_with` |

**What Mostaql cannot give at all — and what it only hides from the shipped client**

- another bidder's **amount**, and the **winning price** (the session adds 62 KB to the project page and
  still no amount appears in anyone's row);
- a freelancer's **profile page from the shipped client** — `HttpClient` alone receives the 202 challenge forever. **This is no longer a data limit, only a plumbing gap:** with a minted `aws-waf-token` the page serves, and its field set is enumerated in **§4.8** with a reference extractor the owner already wrote. Until R1.10 lands the catalog remains the performer source — and it is already *better* than nothing in one respect: it carries the completion rate and live presence, which Nafezly's does not;
- a numeric user id **anonymously** (session-only), and no stable client id at all;
- supervised/escrow status — `HasSupervisedFlag:false` confirmed by absence on both sources.

**Identity keys, per source, as they now stand**

| | Nafezly | Mostaql |
|---|---|---|
| freelancer key | numeric id from the avatar URL prefix (anonymous) | **username** (anonymous); numeric id only with a session |
| bid key | `data-type_id` | `data-bid-item` == `?bid=` |
| client key | `StableHash(masked name)` | masked name only — no id, no profile URL on real pages |

### 4.7 Selector reachability — the data is on the page, the shipped parser cannot reach it

Every selector string in `MostaqlSelectors.cs:14-53` was counted against the six real pages
(`scratch/mostaql/selector_audit.txt`). Most hold. Four do not, and they are the ones v4 wanted:

| Selector | Declared as | Real markup | Consequence today |
|---|---|---|---|
| `OwnerStatRowClassSubstring` | `justify-between` (`:23`) | **0 hits on every page.** The real owner stats sit in `dsp--f-space-between` inside `table.table-meta` / `.meta_items` | `HtmlStructureEngine.cs:169` selects rows by that substring, so **hire rate, open, in-progress, ongoing-communications and registration date all resolve to null in production** while sitting visibly in the HTML (`DetailParser.cs:187-198`) |
| `OwnerUserIdAttribute` | `data-user-id` (`:26`) | **0 hits on every page** | `DetailParser.cs:607-610` finds no id, so `OwnerId` is always `StableHash(masked name)` — two clients who share a masked display name collide, and there is no way to fix it from the page |
| `SkillHrefSubstrings` | `/skills/`, `skill=`, `/tag/` (`:34`) | **0 hits on both detail pages** (`/skills/` exists only on listing/catalog pages) | project skills on a detail page come from `ul.skills` / `.skills__item` **text**, not from hrefs; the href path is dead where it matters |
| `AttachmentFileTypeAttribute` / `AttachmentBadgeClassSubstring` | `data-file-type`, `ext-file` (`:41-43`) | **0 hits**, although `attachment` itself appears twice | the attachment sniff cannot fire, which is consistent with `NeedsSessionForFullEnrichment: true` being untested rather than proven |

Working as declared, on real markup: `project-meta-panel`, `meta-container`, `meta-row`, `meta-label`,
`meta-value`, `profile_card`, `data-bid-item` (28 and 4), `projectDetailsTab`, `text-wrapper-div`,
`project-row` (25), `project__meta`, `project__brief`, `hsoub-file-signature-icon` (25 — the listing's
proposal-count icon, so `fa-users` at `:47` is a dead *fallback*, not a dead primary).

**The label map is the part that is right.** `MostaqlParseProfile.cs:24-47` keys on Arabic labels, and
those labels are exactly what the real pages print: `الميزانية`, `مدة التنفيذ`, `حالة المشروع`,
`تاريخ النشر`, `صاحب المشروع`, `معدل التوظيف`, `المشاريع المفتوحة`, `مشاريع قيد التنفيذ`,
`التواصلات الجارية`, `تاريخ التسجيل`. Two labels the map expects exist **nowhere**: `عدد العروض` and
`المشروع منشور منذ` — which is why the parser counts bid rows instead of reading a count, and why the
proposal count was never believed (§4.1, now unblocked).

So the honest summary of "how to get Mostaql's data": **anonymous HTML already contains everything the
score needs except my bid amounts; the blocker is not privilege, it is four selector strings.**

### 4.8 Mostaql freelancer profile field set — prior art, and how to reach it

The gap §4.6 and §9.1 both recorded is closed twice over: the 2026-11-05 crawl saw the fields on a live
page once an AWS WAF token was held, and the owner **already has a working extractor and a schema for
them** — `F:\Projects\Languages\Python\WorkingOnIt\mostaql_f_scraper`, a separate project, run at
profile-directory scale (its `dump/server.log` shows a `Fetching Profiles` bar with `total=36332`). The
dump files themselves are Git-LFS pointers, so the field list below comes from the **code**, not from the
data, and is graded accordingly.

**Schema** (`src/models.py:93-139`) — `ProfileStats`: `rating`, `reviews_count`, `completion_rate`,
`ontime_delivery_rate`, `rehire_rate`, `communication_success_rate`, `employment_rate`,
`total_completed_projects`, `active_projects`, `received_projects`, `financial_deals`, `response_time`
plus `avg_response_time_minutes` (normalized, default `1440` = unknown), `last_seen`, `last_active`,
`member_since`, `registration_date`. `ProfileDetails`: `name`, `profile_url`, `avatar_url`, `category`,
`title`, `location`, `bio`, `skills[]`, `skills_count`, `skills_str`, `portfolio_count`, `verifications[]`,
`badges[]`, `stats`, `metadata`, `rank`, `scraped_at`.

**Discipline layer** (`src/models.py:69-90`, `docs/SCRAPING_DISCIPLINE_FRAMEWORK.md`) — every field is a
`FieldMeta{confidence 0..1, raw, issues[], type, formatted}`, and the profile carries `parse_signals`,
`outlier_fields` and `schema_version`. That is per-field provenance already implemented, the same
instinct as this document's PROVEN / CITED / INFERRED grading — worth stealing, not just citing.

**The WAF mechanism** (`src/services/waf.py`, used at `src/services/network.py:256-300`):

1. A headless Chromium opens **one** `/u/` URL — the comment at `waf.py:22` is explicit that the bootstrap
   target must be a profile route, because that is where the challenge script runs.
2. It waits for `aws-waf-token` to appear in the context cookies (up to ~5 s; observed length 314).
3. The browser **closes**. The cookie set is then handed to ordinary async HTTP sessions
   (`apply_to_session`, `:60-68`).
4. The token is cached and reused for 300 s (`:76`), behind an `asyncio.Lock` so concurrent workers
   bootstrap once, with a 10 s cooldown re-check (`:82`) and a kill switch (`MOSTAQL_WAF_ENABLED`).
5. **A `202` is treated as a signal, not an error** (`network.py:291-300`): it increments a challenge
   counter, calls `ensure_cookies(force_refresh=True)`, and retries. The log shows that loop working —
   **30 challenge events against 8 token acquisitions**, i.e. roughly four profiles per bootstrap.

So the wall is not a per-request browser. It is **one bootstrap every few minutes, then plain HTTP** —
which is what makes the profile fields affordable at all, and what removes the cost objection in R4.13.

**What it does not buy:** rate headroom. The same log records `http_429=20` against `html_ok=162` in one
run, so Mostaql throttles too; the token gets past the JS test, not the quota. And the anti-detection
steps the Python uses (`--disable-blink-features=AutomationControlled`, and `add_init_script` deleting
`navigator.webdriver`, `waf.py:149-161`) are exactly the parts that must be re-verified on any other
engine — see D53.

---

---

## 5. Requirements

### R1 — Profile acquisition (per source)

1. **R1.1** Profile is **per-source**: a Nafezly file and a Mostaql file, each scraped and scored on its
   own site. A thin global layer holds only self-declared things (interests, target price).
2. **R1.2** `/u/<user>` is public **on Nafezly**. The **public path needs no session there** and reuses the
   extractor already proven for performers — one parser, two uses.
   **Corrected 2026-11-04 for Mostaql:** `/u/<user>` is behind an AWS WAF JavaScript challenge (HTTP 202,
   1,983 B) *even with a live session*, so it is not a usable acquisition path at all. Mostaql's own-profile
   and performer data come from **`/freelancers`** (anonymous, 25 cards/page, filterable) and
   **`/dashboard/bids`** (session) instead — see §4.6. One parser per source, not one parser for both.
3. **R1.3** Session cookies are captured through the **existing** Settings `CookieEditor`
   (`SettingsShape.CookieEditor`, row `session-cookie`, `SettingsCatalog.Builtin.cs:260-280`), stored in
   `app_secrets` per source via `SecretProtector`. **No second secret-capture surface.** The profile page
   shows session state and links to that row.
   **Prerequisite found 2026-11-04:** `MostaqlSource.cs:84` names the session cookie `mostaql_session`,
   the site issues **`mostaqlweb`** (`Set-Cookie` on every response), and `SettingsViewModel.cs:140,143`
   wraps a bare pasted value under the declared name — so the current capture path can store a real
   session under a name mostaql.com ignores. Fix the descriptor before relying on R1.3.
4. **R1.4** Session failure is detected by **probing a known session-only URL**, not by inferring from
   missing fields. Justification: today **a dead cookie looks exactly like a successful scrape** — no 401
   handling anywhere, `HttpClient` follows 3xx silently, the login-redirect HTML sniff exists only on the
   attachment path (`AssetDownloadService.cs:52-56,145-154`), and `CookieParser.cs:97-107` **discards
   column 5 (expiry)**.
   **Resolved 2026-11-04 — the Mostaql probe URL is `GET /dashboard/bids`.** Alive = ~200 KB with
   `<title>العروض الخاصة بي | مستقل</title>`. Dead = **HTTP 200** with ~2.2 KB of `accounts.hsoub.com` SPA
   shell. Status code is identical either way, so the test must be size or title, never `IsSuccess`.
5. **R1.5** Manual entry is the fallback and covers **profile facts only** (username, skills, interests,
   target band, availability, usual speed). History-dependent features stay locked, never guessed.
6. **R1.6** Scraped and manual values **cannot disagree by construction**: the scraper parses raw HTML
   into the same derived shape the form produces. Manual entry skips the parsing, nothing else.
7. **R1.7** `profile.html` is **primarily an entry form**, exposing acquisition states through
   force-params in the existing convention of `settings-shapes.html`:
   `?state=connect|probe-failed|scraped|incomplete|manual|stale`, plus `?source=` and `?theme=`.
8. **R1.8** Read-back = **completeness against the canon** +, per field, **which score factor it feeds**.
9. **R1.9** Voice stays **«حقائق — لا توصيات»**, including on my own page. No advice anywhere.

10. **R1.10 — Mostaql `/u/` acquisition: mint the WAF token once, then fetch with `HttpClient`.** The
    pattern is proven at directory scale in the owner's own `mostaql_f_scraper` (§4.8); v4 ports its shape,
    not its stack.

    **Contract.** One `IWafTokenProvider` per source, owned by the HTTP layer:
    `Task<IReadOnlyDictionary<string,string>> GetCookiesAsync(bool forceRefresh, CancellationToken)` plus
    `bool HasToken`. Nothing else in the app may know that a browser was involved.

    **Bootstrap engine = the platform's own embedded web view, never a shipped browser.** Windows:
    `WebView2` (already first-class in MAUI through BlazorWebView) with
    `CoreWebView2EnvironmentOptions.AdditionalBrowserArguments` carrying
    `--disable-blink-features=AutomationControlled`; `--headless=new` is omitted — it is unnecessary and it
    changes the fingerprint. Cookies read from `CoreWebView2.CookieManager`, watching for `aws-waf-token`.
    Android: `Microsoft.Maui.Controls.WebView` + `Android.Webkit.CookieManager`. Rejected outright:
    Playwright/.NET as an app dependency (a ~150 MB browser download for a desktop utility), and computing
    the token in a managed JS engine (Jint/ClearScript) — AWS rotates that script, so the port would break
    on their schedule, not ours.

    **Rules carried over from the reference, each with a reason:**
    - the bootstrap target **must be a `/u/` route** — that is where the challenge script runs; a listing
      page will not mint the token (`waf.py:22-23`);
    - **one bootstrap at a time**: single-flight with a re-check after acquiring the lock (`waf.py:79-83`),
      so N queued enrichments never start N web views;
    - **reuse window 300 s, 10 s cooldown, refresh lazily** (`waf.py:76-82`) — the TTL is assumed, not
      measured, and the next rule is what makes that safe;
    - **HTTP 202 is a signal, not a failure.** Count it, force-refresh, retry once (`network.py:291-300`).
      Today `MostaqlFetcher` would hand a 202 upstream as a successful-but-empty page — the same
      "dead cookie looks like a successful scrape" class R1.4 exists to prevent, now with a second trigger;
    - **kill switch** mirroring `MOSTAQL_WAF_ENABLED`, and counters (`challenges`, `bootstraps`) reported
      through `InteractionLogger` the way the crash funnel reports everything else — a silent token loop is
      undebuggable;
    - the token is a cookie, therefore **secret material**: never logged, never in a diagnostic file, never
      written into a snapshot row (§9.1 applies unchanged).

    **Prerequisite spike, not yet run** — the only thing between this and implementation: does an engine
    that still advertises `navigator.webdriver` pass the challenge at all? The reference hides it two ways
    (`waf.py:149-161`), and WebView2's fingerprint is not headless Chromium's. Spike: fetch one `/u/` page
    headless **without** the stealth patches, then the same through WebView2; record status, bytes, and
    whether `aws-waf-token` appeared. Two to four requests. If WebView2 fails where headless Chromium
    succeeds, R1.10 is blocked and the catalog stays the performer source.

    **Scope note:** the token buys passage, **not quota** — the reference log shows `http_429=20` against
    `html_ok=162`. `TokenBucketRateLimiter` and `DefaultRequestsPerMinute` still govern (§3, D29).

### R2 — Profile data model

1. **R2.1** Skills and interests become **structured records per source**. `SettingsKeys.QueryParams`
   is demoted to a **derived output** of `OnboardingQueryComposer`, no longer the storage format.
2. **R2.2** **One model, two writers**: onboarding step 5 writes it on first run, `profile.html` edits it
   later.
3. **R2.3** Storage: **separate tables per role** — `user_profile`, `performers` (competitor files),
   `user_bids`.
4. **R2.4** Canon = **Peter's actual field set** (`Peter__tharwat`). The six axes
   (ملف، أعمال، توثيق، مشاريع، خدمات، متميز) are **display grouping only**, not a second source of truth.
5. **R2.5** **The canon drives the form**: every empty canon slot becomes a row to fill, so completing the
   form visibly raises اكتمال.
6. **R2.6** Incompleteness is stored as **a key plus the exact list of missing fields** — a *maturity*
   indicator, never a quality verdict.
7. **R2.7** Migration required: `PRAGMA user_version` 5 → 6 (`SqliteConnectionFactory.cs:18` is
   `CurrentSchemaVersion = 5`; the v3 contract doc still says 4 and is stale).
8. **R2.8 — every cookie crawl is stored as a snapshot; a failed crawl never replaces a good one.**
    Owner's ruling, 2026-11-04: *"we store all data we have get from each cookies-crawling in a table, so
    if a crawling failed we use the last one passed."*
    New table `profile_snapshots`, one row per attempt, **append-only**:

    | Column | Type | Rule |
    |---|---|---|
    | `id` | INTEGER PK AUTOINCREMENT | the snapshot number the UI quotes («لقطة #14») |
    | `source` | TEXT NOT NULL | `SourceId` value — snapshots are per-source, never merged (R1.1) |
    | `crawled_at` | TEXT NOT NULL | ISO8601 UTC, `datetime('now')` default, same convention as every other table |
    | `ok` | INTEGER NOT NULL | 1 = usable, 0 = failed attempt. **Only `ok=1` rows are ever read back into the profile** |
    | `fields_json` | TEXT | the parsed field set, in the same derived shape manual entry produces (R1.6). **NULL when `ok=0`** |
    | `error_kind` | TEXT | `session_expired` \| `waf_challenge` \| `not_found` \| `network` \| `parse_empty`. NULL when `ok=1` |
    | `http_status` | INTEGER | what the site answered — recorded because it is **not** the signal (§3: a dead session answers 200) |
    | `body_bytes` | INTEGER | the discriminator that actually works: ~2 KB = login shell, ~200 KB = data |
    | `pages_read` | INTEGER | how many of the four pages in the read completed — drives the progress UI honestly |

    Read path: `SELECT * FROM profile_snapshots WHERE source = ? AND ok = 1 ORDER BY crawled_at DESC LIMIT 1`.
    Write path: the crawl **inserts** its own row and nothing else; the profile projection is rebuilt only
    from `ok=1` rows. A failed attempt therefore cannot blank, half-update, or poison a profile — it is
    evidence about the attempt, not about the user.
    Three consequences that are the point of doing it this way:
    1. the page must **name the snapshot it is showing** (age + number), because the data on screen may be
       days old — `profile.html` does this in its failure page («آخر لقطة ناجحة #14 — قبل ٣ ساعات»);
    2. the score's freshness input (F2) becomes a real column instead of a guess;
    3. `waf_challenge` is a first-class `error_kind`, so Mostaql's `/u/` block is recorded as a known
       outcome rather than mis-reported as an empty profile (D33).
    Retention: keep the last 20 per source — rows are small, and the history is what makes a repeated
    failure diagnosable. Purging follows the existing `AppPaths.PurgeOldLogs` precedent, never the shared
    data root.

### R3 — Competitor and bidder data model

1. **R3.1** Store **full offer rows including the preview text as-is** — no extra fetch per offer, no
   truncation policy beyond what the site already gives.
2. **R3.2** Identity key = the **numeric user id** embedded in the avatar URL prefix; display name is
   stored as a label only; slug resolution stays lazy via the freelancers search.
3. **R3.3** Collisions keep **both ids** and promote the **data-rich, most-recently-seen** account as
   canon. Dependency worth noting: `آخر تواجد` lives on the performer *profile* page, not on the offer
   row, so a collision can only be resolved **after** both identities are resolved to profiles.
4. **R3.4** A new per-project bidder table is required; none exists today (§4.2).
5. **R3.5** Bidder file pulls stay **on demand** (the rule already drawn in `bidders.html:272`:
   «الملفات تُسحب عند الضغط على إعادة تحليل»), while bidder **identity rows** are captured with the
   detail-page fetch that already happens.

### R4 — Winnability score

1. **R4.1** The number is a **relative index (0–100), not a probability**. We may not claim "48% chance".
2. **R4.2** Meaning: **chance of winning**, not expected value or "worth bidding".
3. **R4.3** Weights are **hand-authored, never statistically fitted**, and **per source** — the two
   sources are not identical and must not share one weight set.
4. **R4.4** Missing factors **renormalise away**; the surface shows **coverage** (how many of the model's
   factors actually contributed).
5. **R4.5** **One competition sub-score** internally composed of depth, rival-strength distribution and
   named-rival presence, then entering the total once. Rationale: those three are the same underlying
   fact three times; independent terms would punish a project three times for one strong rival.
6. **R4.6** **No hard gates inside the arithmetic** — every bad fact is a weighted term.
   *Consequence not yet resolved:* see §9.2 (a project already awarded or closed would still show a
   score). The proposed reconciliation — gate by **visibility** rather than by value — is unanswered.
7. **R4.7** Weight **values** are deferred to a later experiment (§9.2). "Experiment" is still undefined.
8. **R4.8** Sorting by the score **must remain a user action**. The feed carries a binding ordering
   invariant — *a live feed must not change the rank of rows already on screen without user intent*
   (`docs/postmoterm/feed-vanishing-project-cards-2026-11-01.md` §16 A5, the dev80 fix). Auto-ranking by
   score reopens exactly that bug class.
9. **R4.9** Mostaql's pill **does not ship in v1** until one real Mostaql page proves its bid count is
   extractable. This **supersedes** the earlier "both sources" ruling for the pill specifically.
10. **R4.10** Request budget: v4 **intends to push past the 8 req/min ceiling** to keep data fresh.
    Accepted risk: Cloudflare 1015 is IP-level, so an overshoot degrades the listing poll and enrichment
    for every source sharing that IP, not just v4. Mitigation is a requirement, not a preference —
    a kill switch and backoff, and the overshoot must not be the default.
11. **R4.11 — staleness decay on time-sensitive factors** (owner ruled the *intent* — "a small decaying
    factor, logarithmic, never a hard factor, still a chance after 14 days" — and delegated the exact
    mathematics here).

    **Chosen:** `f(t) = 1 / (1 + λ · ln(1 + t/H))`, with `H = 7 days` and `λ = 0.9`, multiplying **only the
    time-sensitive factors** — the competition sub-score, bidder counts, project state, price gaps. Static
    inputs (your skills, target band, availability, portfolio) are not decayed: a month-old snapshot of
    your own skills is still your skills.

    | age | 1 d | 3 d | 7 d | 14 d | 30 d | 90 d | 1 y |
    |---|---|---|---|---|---|---|---|
    | f(t) | 0.92 | 0.76 | 0.62 | 0.50 | 0.40 | 0.30 | 0.16 |

    Why this form: it is logarithmic as the owner chose, **strictly decreasing forever** (so age always
    costs something — no flat region where a 90-day snapshot looks like a 30-day one), and **bounded away
    from zero** without any clamp, floor, or piecewise branch — which is exactly "even after 14 days the
    user still has a chance". Half the weight is gone at two weeks; a year old still contributes a sixth.

    Rejected, with the reason: the raw `1 − λ·ln(1+t/H)` **clamped at 0.25** (hits its floor at ~30 days and
    then stops responding to age — the dishonesty the floor was supposed to avoid, and it needs a branch);
    **exponential** `2^(−t/τ)` (with a 7-day half-life a month-old field is worth 5.8% — contradicts the
    owner's stated intent, and a longer τ abandons the week as the reference); **power-law** `(1+t/H)^−p`
    (never zeroes, but at p=0.6 decays harder than the log the owner tested and preferred).

    Constants live where the weights live (R4.12 / D45: plain constants in the source module) and are named
    `DecayHorizonDays = 7`, `DecayLambda = 0.9` so a successor can see they are tunable, not derived.
12. **R4.12 — the decay is derived at read time, never stored.** The score is recomputed **on app open and
    on any data change** (crawl result, profile edit, snapshot write); the multiplier is calculated from
    `crawled_at` at that moment. Consequence the owner accepted: an app left open for days shows a value
    frozen at the last recompute — there is no timer, so nothing visibly decays while the window sits
    open. Reversal is one added tick; the stored shape does not change either way.
13. **R4.13 — rival resolution is a batch, and failure is stated.** Every recompute resolves **all**
    bidders of a project as one batch; there is no partial-coverage mode and no "13 not analysed yet"
    affordance (it is removed from `bidders.html`). If the batch fails, the unresolved rivals contribute
    **nothing** — the remaining factors renormalise per R4.4, coverage is displayed, and the surface says
    the resolution failed rather than showing a number that quietly ignored the field.
    Cost, measured not guessed: at the proven safe pacing (Nafezly answers 8 back-to-back fetches with
    Cloudflare 1015), a 25-bidder project is **≥3 minutes of queue time per project**, so batch resolution
    rides the existing enrichment queue and its rate limiter (`TokenBucketRateLimiter`,
    `SourcePipelineFactory.cs:51`) — never inline with a recompute, and a project's score may legitimately
    arrive minutes after its card does.
    **Amended 2026-11-05 by §4.8 and D53:** that cost was priced against a browser *per profile*. It is
    not — one token bootstrap every few minutes, then ordinary GETs, so resolving rivals on Mostaql costs
    the same as resolving anything else. What the token does **not** buy is quota: the prior scraper's own
    log shows `http_429=20` against `html_ok=162`, so the rate limiter still governs.

### R5 — Surfaces

1. **R5.1** Score renders as a **pill on the feed card** (number + band colour) with a **factor breakdown
   on the project details page**.
2. **R5.2** Colour caution: v3 reserves green/red/amber for **state** (enriched / failed / pending) under
   the rule *"source colour is identity, state colour is truth"*. Winnability bands must **not** reuse
   those hues; tint on the brand scale instead.
3. **R5.3** With **no profile, feed cards are unchanged** — v4 only *adds* to existing components.
4. **R5.4** New rail entry: **avatar at the top of the existing rail** (photo, initials fallback, — the rail sits on the reading-start edge, D41)
   dot when no profile, `.tip` hover per rail convention). Fallback if disliked: avatar **replaces** the
   brand mark. The rail currently has **no** avatar/account slot
   (`UI/PlatformComponents/SourceRail/SourceRail.xaml`: brand `:62-69`, source tiles `:76`, fixed sections
   `:83-157`, per-source `ContributionNavHost` `:162`, bell/theme `:166-216`).
5. **R5.5** `performer-profile.html` gets **no** profile-dependent mode (see §1). The five
   `performer-profile.html?profile=true` hrefs in `bidders.html` (`:286,295,304,322,331`) are therefore
   **wrong and should become plain links**.
6. **R5.6** `bidders.html` keeps its own `?profile=true` mode — already implemented and verified
   (all 10 DOM targets present; script at `:475-493`).

### R6 — Constraints inherited from the codebase (not negotiable in v4)

1. Shared code must not name a platform — `ParserTests` enforces it.
2. Errors are values (`Result<T>` + `DomainError`); an observed `IsError` must be propagated or reported
   through `InteractionLogger.Failure`.
3. Nothing may throw into WinUI from a tick or animation callback — `TickerGuard`; honour
   `MotionPreferences.IsReducedMotionRequested`.
4. A new reusable UI unit gets a `docs/UNITS.md` row **in the same task**.
5. Every persisted setting name lives in `SettingsKeys`; scope is owned by `SettingsDescriptor.Scope`
   through `ScopedSettings`.
6. Channel-isolated OS identities derive only from `Core/Platform/ChannelIdentity.cs`.
7. Never paste, log or commit a cookie, secret or user row.
8. `powershell -File tools\quality-gate.ps1` must pass; a new invariant goes into `tools/ParserTests`
   with a `scanned >= N` anti-vacuity floor.
9. Capability gating rule: **absent = the section is never rendered, never rendered empty**
   (`SourceCapabilities.cs:5-6`, enforced at `DetailSchemaRenderer.cs:142-148`,
   `ProjectDetailsViewModel.cs:457-459`, `SettingsCatalog.cs:145-159`).

---

## 6. Factor inventory

Weights are placeholders pending §9.2. "Evidence" uses §4's provenance levels.

### A — Competition (composed into one sub-score per R4.5)

| # | Factor | Shape | Evidence |
|---|---|---|---|
| A1 | Bidder count now | log, saturating (0→1 and 1→2 move it; 60→70 must not) | Nafezly real-fixture; Mostaql code-only |
| A2 | Bidder growth rate vs time since we saw it | derivative | INFERRED — needs a count-history table, not new parsing |
| A3 | Rival strength distribution (expected max, not a count) | sub-score core | Requires the §4.2 parse |
| A4 | Canon-tier rival present | dominant step | Requires §4.2 parse + lazy file pull |
| A5 | Head-to-head: this rival beat me before | multiplier | Requires co-bidder rows retained over time |
| A6 | Rivals currently paused/unavailable | subtracts from effective depth | Profile stat, real-fixture |

### B — Fit

| # | Factor | Shape | Evidence |
|---|---|---|---|
| B1 | Skill overlap me ↔ project requirements | ratio | real-fixture (`project_skills`) |
| B2 | Category fit vs my history | banded | **Not available** — §4.5 |
| B3 | Portfolio match | small positive | real-fixture (`/portfolio`) |
| B4 | Budget vs my accepted band | **two-sided** — below my floor is not good | display string only, no currency column |
| B5 | Deadline vs my usual delivery | saturating | my delivery dates **UNKNOWN** |
| B6 | Language / country mismatch | small | real-fixture (all 10 were مصر) |

### C — Me

| # | Factor | Shape | Evidence |
|---|---|---|---|
| C1 | Win rate **in this band** | shrink toward prior at small n | INFERRED, computable today (§3) |
| C2 | Completion % | mild positive | real-fixture |
| C3 | Rating vs band median | mild positive | real-fixture |
| C4 | Availability now | term (R4.6 forbids a gate) | session or manual |
| C5 | Concurrent load | negative when saturated | **UNKNOWN** for self |
| C6 | Account age / seniority | decaying importance | real-fixture |
| C7 | My profile completeness vs canon | trust proxy | R2.4–R2.6 |

### D — Client

| # | Factor | Shape | Evidence |
|---|---|---|---|
| D1 | Hiring rate (do they ever award) | strong negative | Nafezly **session-gated**; Mostaql synthetic |
| D2 | Open vs completed projects (black-hole ratio) | negative | Mostaql synthetic only |
| D3 | Client rating | mild | **null in every real scrape, both sources** |
| D4 | Supervised / escrow | — | **false on both sources; does not exist** |
| D5 | Age with no award | negative | relative time only |

### E — Project

| # | Factor | Shape | Evidence |
|---|---|---|---|
| E1 | Budget vs category median | abandonment risk | blocked by B2 (no category) |
| E2 | Spec richness | inverse of bid count | attachments session-gated on Nafezly |
| E3 | Time left to submit | urgency | needs absolute dates, §4.5 |

### F — Properties of the answer, not factors

| # | Item | Note |
|---|---|---|
| F1 | **Coverage** — how many factors contributed | Required by R4.4; without it a 3-factor Mostaql number sits beside a 9-factor Nafezly one |
| F2 | Freshness of A1/A2 | Ties to the R4.10 overshoot decision |
| F3 | **Self-selection bias** | My win rate comes only from projects I chose to bid on, biasing it upward against projects I'd skip. **No weighting scheme fixes this.** A professional model states it; the details-page breakdown should carry it as a caveat. |

---

## 7. Mockup deliverables and order

1. **First: `projects.html` trial** — avatar entry at the top of the rail (photo / initials /
   no-profile dot, `.tip` on hover) **plus** score pills on existing cards, toggled by `?profile=true` so
   the default render stays byte-identical. Working-tree edit only, no commit, verified by rendering both
   states.
2. **Then: `profile.html`** — the entry form with its R1.7 states.
3. **Cleanup on `bidders.html`** — drop the `?profile=true` param from the five performer links (R5.5).
4. ~~**`v3-multisource/README.md`** — both new pages are still absent from its Files table~~ — resolved
   by D50: the v4 pages and their Files table live in `v4-analysis/README.md`, and v3's README is back to
   describing only what v3 ships.
5. **Reverted this session, deliberately:** a `?profile=true` treatment *was* built into
   `performer-profile.html` (a `#me-pill`, two hidden sections, a mode-switch script) and was
   **reverted via `git restore` from the index** after the owner ruled that the performer page must not
   depend on the viewer. Verified: 0 traces remain, `README.md` back to 132 lines, `bidders.html`
   untouched at 508 lines.
6. **Session-key artwork, placed 2026-11-04** — `Resources/Images/Analysis/` (the glob at
   `MostaqlK.csproj:94` needs no project edit; the folder carries its own `PROMPT.txt` with both prompts and
   the mapping). The metaphor has two states and each image belongs to one:
   **`analysis_key_offer.png`** (upright, holding the card *and* the key, gate shut but glowing) is the
   **invitation**, rendered on `profile.html`'s connect state `?cookies=false` above the paste box;
   **`analysis_key_blocked.png`** (leaning, key reaching the lock, brow set) is the **dead end**, rendered on
   the read-failure page `?reread=fail` **in place of** the progress ring.
   The rule that decided it: *the face carries the obstacle, the scene carries the reassurance.* A calm man
   on a failure page gives no reason to act, and a distressed man on a recoverable failure implies lost data
   — which R2.8 explicitly promises has not happened. Progress keeps a dial; a dead end gets a scene; one
   state never carries both focal elements.
   Not applied, because both are visible changes to surfaces the owner approved: reusing the offer image for
   onboarding step 5 (the one step `Onboarding/PROMPT.txt` records as having no image), and a thumbnail
   beside the `session-cookie` row in `settings.html`.

---

## 8. Decision log

| # | Decision | Status | Why |
|---|---|---|---|
| D1 | Profile is **per-source**, thin global layer for self-declared data | DECIDED | Matches "each source is a full identity, not a filter" |
| D2 | `profile.html` is **primarily an entry form** | DECIDED | Owner rejected the dashboard/states options |
| D3 | Acquisition states exposed via **force-params** | DECIDED | Mirrors `settings-shapes.html` |
| D4 | Canon = **Peter's field set**; 6 axes are grouping only | DECIDED | One scrapeable list; keeps the drawn radar honest |
| D5 | **Canon drives the form** | DECIDED | Completing the form raises اكتمال |
| D6 | Skills/interests become **structured**; `QueryParams` derived | DECIDED | String-matching a search query cannot feed a score |
| D7 | **One model, two writers** (onboarding + profile page) | DECIDED | No duplicated truth |
| D8 | **Separate tables per role** | DECIDED | Rejected the shared `performers`+`is_self` option |
| D9 | Session captured via **existing Settings CookieEditor** | DECIDED | No second secret surface |
| D10 | Failure detected by **probing a session-only URL** | DECIDED | A dead cookie is otherwise indistinguishable from an empty profile |
| D11 | Manual fallback = **profile facts only** | DECIDED | Typing aggregates would poison every downstream verdict |
| D12 | Scraped vs manual **cannot disagree** — one derived shape | DECIDED | Owner's ruling; supersedes any precedence rule |
| D13 | Read-back = completeness + what each field feeds | DECIDED | Explains why a field is asked |
| D14 | **Facts only, no advice**, on my own page too | DECIDED | One voice app-wide |
| D15 | Score is a **relative index**, never a probability | DECIDED | Hand weights cannot earn a % sign |
| D16 | Score means **chance of winning** | DECIDED | Rejected expected value |
| D17 | Weights **hand-authored, per source, never fitted** | DECIDED | — |
| D18 | **Renormalise over available factors + show coverage** | DECIDED | Neutral-for-missing clusters everything at the middle |
| D19 | **One competition sub-score** | DECIDED | Prevents triple-counting |
| D20 | **No hard gates in the arithmetic** | DECIDED | But see §9.2 — the consequence is unresolved |
| D21 | Feed **pill + details breakdown**; sorting stays a user action | DECIDED | A5 ordering invariant |
| D22 | Avatar **top of the existing rail**; fallback = replace brand mark | DECIDED (trial) — **the side it names is superseded by D41**; the avatar's position *within* the rail is unchanged | "let's try … then if I did not like" |
| D23 | Avatar shows **photo, initials fallback, dot when no profile** | DECIDED | — |
| D24 | **No profile → cards unchanged**; v4 only adds | DECIDED | Owner's rule |
| D25 | Bidder rows stored as **full offer rows incl. preview text as-is** | DECIDED | Rejected count-only and lazy |
| D26 | Identity key = **numeric avatar-prefix user id** | DECIDED | Immune to name truncation |
| D27 | Collisions: **keep both ids**, data-rich + last-seen wins | DECIDED | "handle gracefully" made concrete |
| D28 | Mostaql pill **held** until bid count is proven | **UNBLOCKED 2026-11-04** | `data-bid-item` ×28 and ×4 on two live project pages, fetched anonymously — the count is real. The hold can now be lifted deliberately, not by default |
| D29 | v4 **pushes past the 8 req/min ceiling** | DECIDED, risk accepted | See R4.10 mitigation requirement |
| D30 | **projects.html trial first**, then profile.html | DECIDED | — |
| D31 | Pill = **number + band colour** | DECIDED | With the R5.2 state-colour caution |
| D32 | `profile.html` facts row = **six site-read values only**. No locked placeholder card | DECIDED — **the owner ruled this twice** | Bid amounts never appear as a card, and the string «قيمة عروضك» appears nowhere on the page; the numbers live only in the session section |
| D33 | Mostaql **performer data comes from `/freelancers`**, never from `/u/` | DECIDED by evidence — **the evidence was wrong: see C12.** The ruling survives only as a cost decision, now reopened as §9.2 item 7 | `/u/` was 202 + `gokuProps` to `HttpClient`; a JS-capable client passes it in ~2.5 s, anonymously, with a dozen fields the catalog lacks |
| D34 | Factor availability is **per-source by construction**, not by tuning | DECIDED by evidence | Client quality (hire rate, open/in-progress counts, registration date) is **anonymous on Mostaql** and **session-gated on Nafezly** — the same D-factor has different privilege requirements per source, so the coverage figure (F1) is mandatory, not decorative |
| D35 | **My bid amount is obtainable; nobody else's is** — **amended 2026-11-05: the price I agreed on a deal I WON is obtainable on both sources** | DECIDED by evidence, then corrected by **C13** | My own price: Mostaql `/dashboard/bids`, Nafezly via the deal feed. A winner's price on a project I lost: absent from 51 Mostaql + 35 Nafezly bid rows. A winner's price on a project I won: Nafezly states it («المبلغ المتفق عليه بينكما»), Mostaql derives it from `/payments` ÷ 0.8 under `projectFees: 20`. The chart is re-based accordingly in **D51** |
| D36 | Mostaql win/loss uses the site's **five states** (`prj-pending/processing/completed/closed/lost`), no inference | DECIDED by evidence | Nafezly still infers won/lost from offer statuses (§3); Mostaql states `lost` outright, so the two sources reach the same factor by different routes |
| D37 | Identity keys: **Mostaql freelancer = username** (numeric id is session-only); **Mostaql client = no stable key** | DECIDED by evidence | `/ajax/report/user/{id}` renders only with a session; the owner card on real pages carries no `/u/` link at all. D26's numeric-id rule holds for Nafezly, not for anonymous Mostaql |
| D38 | Mostaql's missing owner stats are a **selector defect, not a privilege wall** — fix them before gating any factor behind the session | DECIDED by evidence | `justify-between` (`MostaqlSelectors.cs:23`) has **zero** occurrences on any real page while the stats themselves are there (§4.7). Gating hire rate behind a session on Mostaql would be designing around a bug |
| D39 | Crawl results are **append-only snapshots**; the profile always reads the newest `ok=1` row and a failed crawl overwrites nothing | DECIDED — **owner's ruling** | R2.8. Required by §3: a dead session answers HTTP 200, so success is decided by what arrived, not by a status code — and until the selector defects in §4.7 are fixed, a Mostaql "success" can legitimately carry NULLs that must not replace real values from an earlier crawl |
| D40 | The read page is **settled**: variant A (gate + read log), converted to the app's own **page register** — full-bleed bands, sticky section head, hairlines, actions in the footer row. No evidence block, no per-row explanation line | DECIDED — **owner ruled, after comparing three treatments in a throwaway sheet that is now deleted** | The evidence strip («ما ردّ به الموقع») and the dead row's explanation were removed on instruction so the three actions sit above the fold: measured 874 px against a 950 px viewport, page height 801. What the removal costs — the raw `HTTP 200 · 2.2 KB · accounts.hsoub.com` proof — survives in §3 and in the probe requirement R1.4, not on the screen |
| D41 | **App chrome geometry**: icon rail on the physical **right** (reading-start), pipeline/radar panel on the **left**, feed column between — ruled as the look for the whole app | DECIDED — **owner reversed the original left-rail ruling after seeing it rendered** | `projects.html` used an LTR wrapper to force the rail left while `profile.html` already had it right, so the two disagreed. **Verified against shipped code:** `MainWindowWindowsLayout.xaml:15` is `FlowDirection="RightToLeft"` with tracks `[0]=panel 252 · [1]=splitter 8 · [2]=feed · [3]=SourceRail` — in RTL that is rail **physical-left**, panel **physical-right**, i.e. the app is the mirror of the new look. Matching it re-indexes `SourceRail Grid.Column` in three Windows layouts, the header's `Column="0" ColumnSpan="3"`, and the code-behind that keeps cols 0/1 in step with the panel's live width. **Scope now all four v4 surfaces** (2026-11-05): `profile.html` already had the rail right, the LTR wrapper was removed from `bidders.html` and `performer-profile.html`, `projects.html` took the flip plus the panel re-order — measured rail 1376–1440 and `main` 0–1376 in every page, and the logical hairlines survived the flip unedited (1px solid, facing content, on all four). Mechanics and reversal boundary: `design/postmvp/v4-analysis/README.md` → *Chrome geometry*. **Note on the v3 doc:** `v3/product/multi-source-requirements.md` rule 3 was restored to its shipped wording by D50 and still records physical-left as the approved exception — that is correct for what ships today, and v4 supersedes it here rather than in the v3 file |
| D42 | **Staleness decay**: `f(t) = 1/(1 + 0.9·ln(1 + t/7d))` on time-sensitive factors only, derived at read time, recomputed on open + data change | DECIDED — **owner set the intent and delegated the mathematics** | R4.11/R4.12. Chosen over clamped-log, exponential and power-law for the reasons in R4.11; the constants are tunable, not derived |
| D43 | **Rivals are batch-resolved**; unresolved contribute nothing, the failure is stated, coverage is shown; the «13 متقدماً لم يُحلَّل ملفهم بعد» affordance is removed from `bidders.html` | DECIDED — **owner's ruling** | R4.13. Carries a measured cost: ≥3 min of queue per 25-bidder project at safe pacing, so it rides the enrichment queue, never the UI |
| D44 | **Weights are plain constants** in the source module — no declarative table, no `ParserTests` floor, not user-editable, not a shipped JSON | DECIDED — **owner's choice over the gated option** | The gate-less option was chosen knowingly; the repo's own history (a scan that passed while matching nothing) is the risk accepted here |
| D45 | On an **awarded or closed** project the pill keeps showing the chance, **dimmed** | DECIDED — **owner's choice** | Rejected "replace with the outcome"; R4.6 stays partially open on how the dimmed state is worded |
| D46 | **Mostaql pill: ON**, with coverage displayed | DECIDED — **lifts D28's hold** | Bid count proven on live pages (§2.4); coverage display becomes load-bearing immediately because Mostaql scores rest on fewer factors |
| D47 | **«مشرف عليه» removed** from `projects.html` / `project-details.html` | DECIDED — **owner's ruling** | `HasSupervisedFlag` is false on both sources and the marker appears in no captured page; a design-fiction capability is not a placeholder |
| D48 | **Real captured pages become fixtures** (`Fixtures/mostaql/` + `.meta.json`, values scrubbed) | DECIDED — **owner's ruling** | Closes the §4.7 blind spot where the parser was validated against markup authored to match it |
| D49 | **The four descriptor defects are folded into v4**, not fixed on the shipped baseline | DECIDED — **owner's ruling** | `mostaqlweb`, `nfthly_session`, `CurrencySuffix` USD, `OwnerFallbackMeta` fabrication (§9.5) become v4 requirements, with the versioning move in D50 |
| D50 | **Version restructure**: this whole body of work is copied into v4 (`.steering/v4/`, `design/postmvp/v4-analysis/`), and **v3 is restored** to its shipped state | DECIDED — **owner's ruling** | Files that never existed in v3 (`profile.html`, `bidders.html`, `performer-profile.html`) move rather than copy; `projects.html` edits are split out and the v3 original restored. **EXECUTED 2026-11-05** — see §9.2 item 8 and the changelog |
| D51 | **The winning-price chart is re-based, not dropped**: it becomes *my bid vs the agreed deal price on won projects*, with Mostaql's 20% fee undone (`payout / 0.8`) | DECIDED by evidence + agent judgment under D42's delegation | §2.5. A competitor's winning price is confirmed absent on both sources across 51+35 bid rows; my own deal price is now proven on both. The «الفائز» series as drawn cannot survive |
| D53 | **Port the token-bootstrap pattern, not a browser pipeline**: mint `aws-waf-token` once with the platform's *embedded* web view, hand it to the ordinary `HttpClient`, re-bootstrap reactively on any 202 | PROPOSED — **pending a spike, not yet approved** | §4.8. Windows: `WebView2` (already first-class in MAUI through BlazorWebView) with `AdditionalBrowserArguments` for the automation flag and cookies read via `CookieMonitor`; Android: the same shape with `WebView` + `CookieManager.GetCookieAsync`. Rejected: shipping Playwright/.NET as an app dependency (a ~150 MB browser download for a desktop utility) and computing the token in a managed JS engine (Jint/ClearScript) — AWS rotates that script, so the port would break on their schedule, not ours. **The spike must answer:** the token's real TTL (the Python code assumes 300 s and lets 202 correct it), whether the token is route-scoped or site-wide, whether WebView2 is fingerprinted differently, and the `navigator.webdriver` difference. ToS posture is the owner's standing decision, already exercised at directory scale | | D52 | **A manually uploaded avatar does not move اكتمال**, and the image file must join the `AppPaths` first-launch copy set | DECIDED by agent judgment under delegation; owner may reverse | The five axes measure profile maturity against the canon, and no axis is a photo — inventing one would move the ring on a field nothing else scores. Without the copy-set change, a version bump silently loses the user's photo, which is the failure mode `AppPaths` exists to prevent |
| R1 | `?profile=true` on `performer-profile.html` | **REVERTED** | Built, rendered, verified — then ruled out: the performer page must not depend on the viewer |
| C1 | "Client quality is free, already in `owners`" | **CORRECTED** | False for Nafezly; see §4.5 correction of record. **Now true for Mostaql** — see §4.4, measured 2026-11-04 |
| C9 | Mostaql budgets are in **ريال** (`CurrencySuffix => "ر.س"`, `MostaqlSource.cs:40`) | **CORRECTED** | Real pages read `الميزانية $100.00 - $250.00` — USD. The suffix is appended to the card and detail budget (`ProjectCardViewModel.cs:237`, `ProjectDetailsViewModel.cs:238`), so the shipped UI labels dollars as riyals |
| C10 | Mostaql owner meta «السعودية • عضو منذ 2021» (`OwnerFallbackMeta`, `MostaqlSource.cs:42`) | **CORRECTED — it is invented data** | Rendered by `ProjectCardViewModel.cs:194` whenever the parser has no owner meta. The real page carries an actual `تاريخ التسجيل 13 يناير 2015` and no country at all. A fabricated country+year presented as fact violates the same «حقائق — لا توصيات / لا مخمَّنًا» rule R1.5 sets for the profile page |

---

## 9. Not settled

### 9.1 Deferred by the owner — **both answered from evidence, 2026-11-04**

1. **Authenticated route inventory + bid amounts.** **ANSWERED** — full route table in §4.6, bid-amount
   verdict in D35. The owner's condition was *"never ask for or accept a cookie value"*; it held: the
   session was read from `cookies.txt` by the probe script and attached to the request header directly,
   no value was ever printed, logged, copied into a document, or committed (`cookies.txt` is gitignored at
   `.gitignore:9`). What the evidence actually settled it on was **saved authenticated HTML in
   `scratch/mostaql/`**, exactly the route §9.1 predicted.
   The one part still open: **the winning price is not obtainable** — not on the project page, not with my
   session, not on `/dashboard/bids`. v4 must model "what the project sold for" as unknowable on both
   sources, rather than as a Nafezly-only gap.
2. **Mostaql freelancer profile field set.** **ANSWERED, with a twist the owner's phrasing did not
   predict**: the profile page is unreachable (AWS WAF, D33), but the **`/freelancers` catalog card** is
   anonymous and carries a field set that is *not* a subset of Nafezly's — it adds the project completion
   rate and live presence («متصل الآن»). *"almost has everything nafezly has but little less"* is true of
   the pages and false of the data.

### 9.2 Asked, not answered — these block implementation

**Answered in the grilling rounds of 2026-11-05** (each row now points at its decision; nothing below is
open unless it says so):

| was | now |
|---|---|
| 1. Unresolved rivals | **D43 / R4.13** — batch-resolve all, unresolved contribute nothing, failure stated, coverage shown |
| 2. Where weights live and how correctness is enforced | **D44** — plain constants in the source module, no gate. Accepted knowingly |
| 3. What "experiment" means | **folded into D44/R4.7** — with constants chosen and no gate, the experiment is deferred rather than defined; R4.7 still says "deferred" and that is now honest |
| 4. Pill on awarded/closed projects | **D45** — keep the chance, dimmed. **Still open:** the wording of the dimmed state (R4.6 remains internally incomplete until the label exists) |
| 5. Small-sample handling | **unresolved by choice** — "experiment later" became D44, so shrinkage-vs-raw-rate is still unmade |
| 6. How old is too old for a snapshot | **D42 / R4.11-R4.12** — logarithmic decay, never a hard cutoff, derived at read time |

**Newly open, and these do block:**

7. ~~**Does the app adopt a JS-capable fetch path?**~~ — **RESOLVED 2026-11-05 by prior art: no
   per-request browser is needed.** The owner's own `mostaql_f_scraper` already solves this with a
   **one-time token bootstrap**: headless Chromium opens a single `/u/` route, extracts `aws-waf-token`,
   closes, and every later request is plain HTTP carrying that cookie, refreshed reactively whenever a 202
   comes back (30 challenges against 8 bootstraps in its log). Mechanism and field set: **§4.8**; the port
   is **D53**. One risk stays unverified until a spike runs — whether WebView2 / Android WebView pass the
   same challenge with `navigator.webdriver` hidden. Until then, Mostaql profile fields stay out of the
   score model and the catalog remains the performer source.

8. ~~**Executing D50**~~ — **DONE 2026-11-05.** `design/postmvp/v4-analysis/` now holds `profile.html`,
   `bidders.html`, `performer-profile.html`, `projects.html` and its own `README.md`; `v3-multisource/` and
   `.steering/v3/product/multi-source-requirements.md` are byte-identical to `HEAD` again, and the three
   v4-native pages were removed from v3 rather than left duplicated. Links to surfaces v4 has not edited
   yet (`settings.html`, `project-details.html`, `settings-shapes.html`, `onboarding.html`) point at
   `../v3-multisource/…` instead of copying them.
9. **The dimmed-pill wording** (from D45) and **the small-sample rule** (from 5) are the last two things
   R4 cannot be implemented without.

### 9.3 Contradictions inside the already-approved mockups

1. **Peter is not in `bidders.html`.** It ranks Andrew Emil 9.1, غدير دواس 8.3, Ahmed Gomaa 7.8 and never
   lists Peter, yet its CTA is «ملف المنافس الأول» → `performer-profile.html`, which is Peter. Either the
   label is wrong or Peter belongs in the table.
2. **The competitor flow describes two different projects.** `project-details.html` and
   `performer-profile.html` are anchored to «أتمتة تحسين السير الذاتية (CV) · 10–25$ · 5 أيام»;
   `bidders.html` is anchored to «برنامج لإدارة مخزن خشب · 10–25$ · 7 أيام · 25 عرضًا» — and its
   «5/7 أيام» speed math depends on that. **Evidence pointing at the fix:** the surviving bidder probe
   (`scratch/nf_bidders.py`) was run against real project `57334-إنشاء-Cv-احترافي`, i.e. the CV project.
   So `bidders.html`'s wood-store header is the anomaly, not the anchor. **INFERRED, high confidence.**
3. **Five dead `?profile=true` links** (R5.5).
4. **`projects.html` shows «مشرف عليه»** for a capability that is `false` on both sources (§4.5).
5. **All numbers in both mockups are illustrative**, not the 10 real profiles — the README says so, and
   no real performer name appears in either file.

### 9.4 Housekeeping

1. *(the README half is resolved by D50)* — `v4-analysis/README.md` lists the four v4 surfaces.
   **Still open:** no `docs/UNITS.md` rows exist for the units v4 implies (competitor table row, threat
   pill, bid-economics bubble chart, outcomes bar, score pill, profile form, the read-page log row).
2. `AGENTS.md` still declares *"v3 is the wave in progress"*; a v4 steering folder now exists.
3. `.repertoire/.steering/v3/backend-ui-contract.md:100` still says `user_version=4`; code says 5.
4. `configuration-reference.md` contradicts itself: `settings_notification_grouping_enabled` false at
   `:44` vs true at `:77`; `max_concurrent_detail_fetches` "2–3" at `:17` vs `2` at `:76`.
5. Several broken pointers inside the doc chain (`v1/*/README.md` `../base/` depth,
   `v1/product/search-and-filtering.md`, `fresh-narrows-gull.md` cited by the v3 contract, stale paths in
   `base/structure.md`).

### 9.5 Out of scope for v4, found on this pass

CI has been red on **every** run since 2026-08-26 (39 failures / 5 successes in the last 100) and **no
release has published since v1.0.3**, for three mechanical reasons, each confirmed against the actual
Actions log: the `quality` job runs on `ubuntu-latest` while two ParserTests checks assume Windows paths
(`tools/ParserTests/Program.cs:1761,1764` backslash allowlist keys vs `Path.GetRelativePath`;
`:3506` asserts `C:\temp\hero.png → file:///C:/temp/hero.png`) — `TOTAL: 614 passed, 2 failed`; and
`scripts/github-release.ps1:101-106` splats an **array**, so the literal `"-Type"` binds positionally to
a `ValidateSet("Portable","Directory")` parameter. `CrashPathTests` never executes in CI at all. The
release pipeline itself is fine — the same call with named parameters publishes successfully.

**Three Mostaql descriptor defects, found by the 2026-11-04 live pass** — none of them is a v4 feature
bug, all three corrupt what the shipped app shows or stores today, and none is fixed here:

1. `MostaqlSource.cs:84` `SessionCookieName => "mostaql_session"`; the site issues **`mostaqlweb`**.
   `SettingsViewModel.cs:140,143` uses the declared name to wrap a bare pasted value, so the paste path
   can store a live session under a name mostaql.com ignores — and the failure is silent, because a dead
   session answers **200** (§3).
2. `MostaqlSource.cs:40` `CurrencySuffix => "ر.س"`; real budgets are USD (`$100.00 - $250.00`), and the
   suffix is appended on the card and in details (`ProjectCardViewModel.cs:237`,
   `ProjectDetailsViewModel.cs:238`). See correction **C9**.
3. `MostaqlSource.cs:42` `OwnerFallbackMeta => "السعودية • عضو منذ 2021"` is rendered verbatim by
   `ProjectCardViewModel.cs:194` whenever parsing yields no owner meta — **fabricated country and join
   year shown as fact**, while the real page carries a genuine `تاريخ التسجيل 13 يناير 2015` and no
   country at all. See correction **C10**.

A fourth item is a testing gap rather than a defect: every Mostaql assertion in
`tools/ParserTests/Fixtures/` is against hand-authored markup (`project_current_markup.html:2-9` admits
it). The 21 real pages in `scratch/mostaql/` are the first real Mostaql evidence this repo has held;
promoting two or three of them into fixtures would move §4.1 and §4.4 from *measured in a scratch
directory* to *enforced by the gate*. That is a change to what the gate proves, so it is the owner's
call, not a cleanup.

---

## 10. What this document does not claim

Nothing here was **executed against the app or the live site**. No MAUI build of v4 code exists, no
parser for bidder rows has been written, no score has been computed. Every "proven" claim is proven
about **markup and code as written**, not about runtime behaviour. The score model, its weights and its
coverage display are design decisions awaiting both §9.1 and §9.2 — and until the experiment in §9.2(3)
is defined, no weight in §6 is measured, only argued.

---

## 11. Implementation prerequisites — read before planning

The design is settled except for this list, ordered by what blocks what.

1. **The WebView2 / `navigator.webdriver` spike** (R1.10). Unblocks Mostaql profile fields, and with them
   the richest honest version of the competition factor on that source. Nothing else waits on it — the
   catalog path is already specified and works anonymously.
2. **The four descriptor defects** (D49, §9.5, C9/C10/C14): `mostaqlweb`, `nfthly_session`,
   `CurrencySuffix` → USD, and deleting `OwnerFallbackMeta`'s invented country/year. Prerequisites in the
   literal sense — no session-gated feature can be built on a cookie stored under a name the site ignores.
3. **The selector defects** (§4.7, D38): `justify-between`, `data-user-id`, skill hrefs on detail pages,
   attachment attributes. Until they are fixed, a Mostaql "success" legitimately carries NULLs that the
   snapshot rule (D39) must not let overwrite good values.
4. **Real fixtures first** (D48): promote 2–3 captured Mostaql pages into
   `tools/ParserTests/Fixtures/mostaql/` with `.meta.json`, values scrubbed, *before* touching the parser —
   otherwise the gate keeps proving markup that was authored to match it.
5. **Schema work**: `profile_snapshots` (R2.8), the per-project bidder table (R3.4), structured
   skills/interests (R2.1), `PRAGMA user_version` 5 → 6 (R2.7). The avatar file must join the `AppPaths`
   first-launch copy set (D52) or a version bump silently loses the user's photo.
6. **The chrome swap in real code** (D41): `SourceRail` re-indexed in three Windows layouts, the header's
   `ColumnSpan`, and the code-behind that keeps cols 0/1 in step with the panel's live width. The mockups
   already show the target.
7. **Two wording decisions R4 cannot ship without**: the dimmed-pill label on awarded/closed projects
   (D45), and the small-sample rule for the client-quality factor (deferred twice, still open).
8. **The owner's standing call on ToS posture.** The token pattern has already been exercised at directory
   scale in the owner's own tooling; v4 extends it inside a shipped product. That is a decision made once,
   and it should be made deliberately rather than inherited by whoever implements.

---

## Appendix A — Sequence of events

| When | Event | What it changed |
|---|---|---|
| 2026-11-01 | Owner's session scrapes 10 performers (two passes) + Peter, from `/freelancers`; saves raw markup samples | The entire evidence base in §2.1 and §3 |
| 2026-11-01 → 11-02 | `performer-profile.html` (Nov 1 16:26) and `bidders.html` (Nov 2 06:27) authored, **staged, never committed** | The approved surfaces in §1 and §7 |
| 2026-11-02 14:30Z | Last push (`e5f71ae`, dev85 docs) → CI fails again | Third consecutive month of red; see §9.5 |
| 2026-11-03 | `exmaine` pass over the repo | Found: CI red since 2026-08-26 for three mechanical causes; broken doc-chain pointers; the `LayerResolver` claim in `AGENTS.md` is false in production; 4 settings keys bypass `SettingsKeys`; the TickerGuard scan misses re-committing animations |
| 2026-11-03 | Owner hands over the v4 brief + the original "?profile=true" request | Scope moves from audit to design |
| 2026-11-03 | **Misread of "do the task"** → I built a `?profile=true` mode into `performer-profile.html` (a `#me-pill`, two hidden sections, a mode-switch script) and edited the wave README, rendered and verified both states, then **reverted both files from the index** on instruction | Recorded as reversal **R1** in §8; produced the three-surface rule in §1 |
| 2026-11-03 | Owner redirects to `profile.html` and names two acquisition paths (cookies / manual, manual as fallback) | The whole of R1 |
| 2026-11-03 | Bidder-visibility sweep | **Bidder identities are anonymously scrapeable and the app discards them** (§4.2) — v4's premise becomes implementable |
| 2026-11-03 | Field-availability sweep | **My own client-quality claim was wrong** (§4.5 correction of record); Mostaql's bid count is unproven → D28 |
| 2026-11-03 | Eight grilling rounds on the design tree | Decisions D1–D31; five questions left unanswered (§9.2); two deferred by the owner (§9.1) |
| 2026-11-03 | This document written, then expanded on instruction to carry the full trail | — |
| 2026-11-04 | Owner twice rejects the locked «قيمة عروضك» card; the facts row is restored to the six site-read values and the portrait to the cards-beside-avatar shape | Decision **D32**; the session-only numbers keep their single home in the session section |
| 2026-11-04 | **Mostaql live investigation** — 20 GETs, 7–8 s apart, anonymous + the owner's session, evidence saved to `scratch/mostaql/` | §2.4, §3 (five new rows), §4.4 rewritten, §4.6 and §4.7 added; **D33–D38**, **C9**, **C10**; both §9.1 deferrals answered; three descriptor defects logged in §9.5 |
| 2026-11-04 | Owner rules: the read gets **a page of its own** (banner only for success, and only briefly), and **every crawl is snapshotted** so a failure falls back to the last good one | **R2.8**, **D39**, open question §9.2(6); `profile.html` rebuilt accordingly — full-page busy/fail with per-page progress, the HTTP-200-login-shell evidence block, and the snapshot the screen is actually showing |
| 2026-11-04 | Read page taken through three treatments, settled on the gate + read-log split, then converted to the page register and trimmed; the comparison sheet was deleted on instruction | **D40**; the evidence block and the dead row's explanation line left the screen (they remain in §3 and R1.4); actions now above the fold at 950 px |
| 2026-11-04 | **Chrome reversed**: icon rail moved to the physical right and the radar/pipeline panel to the left in `projects.html`; the owner saw it rendered and ruled it the app's new look | **D41**; ruled in `design/postmvp/v4-analysis/README.md` → *Chrome geometry* (it lived under the v3 name until D50 ran), and the physical-left exception retired in `v3/product/multi-source-requirements.md` rule 3. **Verified gap**: the shipped shell is still the mirror image (`MainWindowWindowsLayout.xaml:42-47`), and closing it re-indexes three layouts plus the panel-width code-behind |
| 2026-11-05 | **Second live pass** with both owners' session files and a JS-capable browser (~40 GETs, 13 s pacing) | **C12, C13, C14** and **D51/D52**; D33's factual basis overturned (`/u/` opens for a browser engine, anonymously, in ~2.5 s — a dozen fields the catalog lacks); D35 half-corrected (a lost project's winning price is absent from 86 bid rows across both sites, but *my* agreed price on won deals is explicit on both, Mostaql via `/payments` and a 20% fee constant). New blocking question: §9.2 item 7, whether the app adopts a JS-capable fetch path |
| 2026-11-05 | **Security event**: one command in the investigating agent mis-read the Netscape column order and echoed Nafezly cookie **values to the terminal** before being corrected. Nothing was written to disk, to a script, or into any repo file | Recorded rather than buried. The repo's own protection was already tightened that day: `cookies.txt` was staged, then `git restore --staged` + `.gitignore` `*_cookies.txt` — the file is untracked and cannot be swept into a commit. The `remember_web_…` token is valid to 2027; **rotation is the owner's call** |
| 2026-11-05 | **The `/u/` question closed as a design matter**: §3 and §4.6 corrected, the port specified as **R1.10** (single-flight token provider over WebView2 / Android WebView, 202-as-signal, kill switch, counters, token treated as secret material), remaining work ordered as **§11 Implementation prerequisites** | One unverified prerequisite blocks nothing else: whether an engine that still advertises `navigator.webdriver` passes the challenge. Playwright-as-dependency and managed-JS token computation are rejected in writing so nobody re-proposes them |
| 2026-11-05 | **Prior art found**: the owner's separate `mostaql_f_scraper` already defeats the Mostaql WAF with a one-time token bootstrap, and ships a full profile schema plus per-field confidence metadata | New **§4.8**; **§9.2 item 7 resolved** (no per-request browser); **R4.13 cost amended**; **D53** proposed — port the pattern onto WebView2 / Android WebView and hand the token to `HttpClient`, pending a spike on fingerprinting and TTL. Read from code and `dump/server.log`; the dump itself is Git-LFS pointers, so no profile data was inspected |
| 2026-11-05 | **D50 executed — v4 declared the active wave.** The v4 mockups moved to `design/postmvp/v4-analysis/`; `v3-multisource/` and `.steering/v3/product/multi-source-requirements.md` were restored to `HEAD`; the three v4-native pages were deleted from v3 rather than duplicated; `AGENTS.md`'s version block and subagent brief now read `v4 > v3 > v1 > base` with v3 as shipped | New `.steering/v4/README.md` index; `v4-analysis/README.md` states the copy-on-first-edit rule so a v4 change never edits a v3 file. `profile-variants.html` removed. **Self-caught defect:** inserting D42-D52 had overwritten the D41 row; it was restored the same day — a reminder that a table edit whose `old_string` is a neighbour row silently deletes it |
| 2026-11-05 | **Grilling rounds 1-3** on the unsettled set; the owner delegated the remaining decisions | **D42-D52** and **R4.11-R4.13**; §9.2 rewritten so six old questions now point at decisions and three genuinely new ones are named. Decay mathematics chosen under delegation: `f(t) = 1/(1 + 0.9·ln(1 + t/7d))` on time-sensitive factors only |

## Appendix B — Alternatives considered and rejected

Recorded because a design doc that lists only the winners hides the trade-offs.

| Rejected option | Why it lost |
|---|---|
| `profile.html` as an analysis dashboard, or as one page of acquisition states | Owner chose **entry form primarily**; states are still exposed, via force-params (R1.7) |
| One global merged profile across both sources | Merging two sites' skill taxonomies invents a mapping nobody owns; contradicts "each source is a full identity" |
| Claiming the score is a probability | Hand-authored weights cannot earn a `%` sign (D15) |
| Neutral 0.5 for missing factors | Unknowns would cluster every thin project at the middle and stop the ranking separating anything (D18) |
| Expected value ("worth bidding") instead of chance of winning | Two meanings in one number; owner chose odds (D16) |
| Hard gates that zero the score | Owner chose all-terms (D20) — **the consequence is still open, §9.2(4)** |
| Auto-ranking the feed by score | Reopens the A5 ordering invariant that the dev80 vanishing-cards fix established (D21) |
| Letting the user type aggregates (win counts, average bid) as the manual fallback | Self-reported numbers would silently corrupt every downstream verdict (D11) |
| Accepting a `cookies.txt` upload | You then cannot tell the user *which* cookie is missing when the session fails (D9) |
| Capturing the session on the profile page itself | Duplicates a secret-capture surface that already exists in Settings |
| A shared weight table with per-source overrides | Shared code must not name a platform — the override table is exactly that shape (D17, per-source) |
| Merging colliding identities on name + country | A wrong merge can make Peter appear on a project he never bid on, and crushes that score (D27) |
| Independent competition terms (count, strength, named rival) | Triple-counts one fact; a thin-but-strong field would read as crowded (D19) |
| Fitting weights to my own outcomes, or to the scraped population | Owner chose hand-authored, never fitted; and population fitting has no ground truth for who won most projects (D17) |
| Deriving "who I compete with" from the co-bidder slugs on my own `/offers` page | Owner chose project bidder rows only. **Note: this leaves a proven, free signal unused** (§3, `offers_sample.html:1625-1809`) — recorded as a decision, not an oversight |
| Avatar replacing the brand mark at the top of the rail | Held as the fallback if the first trial is disliked (D22) |
| Flipping the whole rail to the physical right (RTL-native) | Not taken this pass; still `AGENTS.md`/README open question 1 |

## Appendix C — Corrections issued during this work

| # | Wrong claim | Corrected to | Source of the correction |
|---|---|---|---|
| C1 | The bio comes from `meta[name=description]` | It is normal on-page section content («نبذة عني»); re-extract from the section and re-check the quirks against that | **Owner** — now reflected in R1.6 and in the mockup label «نص الملف الأصلي» |
| C2 | Bidding-behaviour analytics were absent from the analysis | Added as a requirement; feasibility established: **pace yes, budget no, delivery time not derivable** from `/offers` | **Owner**, then verified in §3 |
| C3 | A performer page may carry a viewer-dependent mode | It may not — the performer page is about him, period; personalization belongs on the us-surfaces | **Owner** → the three-surface split in §1 and reversal R1 |
| C4 | "Do the task" licensed editing files | Edits were made, then reverted; the working rule is now: document and grill, act only on an explicit go | **Owner** |
| C5 | Client-quality factors are "free, already in the `owners` table" | **False for Nafezly** — those columns are null in every real scrape by an explicit parser comment; client quality is session-gated there and synthetic-proven on Mostaql | **My error**, caught by the field-matrix sweep and re-verified by direct read (§4.5) |
| C6 | 50 offers lack dates because the site withholds them | Dates **are** on the page (`منذ 14 ساعة` etc.); the scraper's selector missed them. Budget is genuinely absent — a different conclusion for each field | Direct grep of `scratch/offers_sample.html` |
| C7 | The GitHub token was expired | It is valid; the empty output was `gh run view --log`, and `gh api …/logs` returned the failure text fine | `gh auth status` |
| C8 | v3's `LayerResolver` governs the platform split (stated in `AGENTS.md`) | `Resolve<T>` has **zero app callers** — only `tools/ParserTests`; the real split is `.Windows.cs` partials + `#if` + csproj `Channel` | `exmaine` sweep, **CITED not re-verified** |
| C9 | Mostaql budgets are in riyal, per `CurrencySuffix => "ر.س"` | They are **USD** — real pages print `الميزانية $100.00 - $250.00`, and the declared suffix is appended to that string in the UI | Direct read of `scratch/mostaql/proj_detail.html`, `anon_proj_1271465.html` (2026-11-04) |
| C10 | `OwnerFallbackMeta` «السعودية • عضو منذ 2021» is a fallback describing the client | It is **invented data rendered as fact** whenever the parser returns no owner meta. The real page carries `تاريخ التسجيل 13 يناير 2015` and no country; the reason the parser returns nothing is the `justify-between` selector defect (§4.7) | Direct read of the real owner card + `ProjectCardViewModel.cs:194` (2026-11-04) |
| C11 | Mostaql freelancer profiles need a session, or are simply "less" than Nafezly's | `/u/` is blocked by **AWS WAF at HTTP 202 for everyone**, session or not; the data is instead **anonymous on `/freelancers`**, where it includes two fields Nafezly does not publish (completion rate, live presence) | `scratch/mostaql/u_owner.html`, `mq_u_freelancer.html`, `anon_freelancers.html` (2026-11-04) — **SUPERSEDED next day by C12** |
| C12 | Mostaql `/u/` is unreachable from the app, so the catalog is the only performer source | **Wrong.** A JS-capable client passes the WAF in ~2.5 s, **anonymously**, and the profile carries fields the catalog does not: completion / on-time / rehire / communication-success rates, average response time, completed count, review count and a five-axis rubric, registration date, last seen, verifications, badges. No prices | `scratch/mostaql/pw_u_YElmelegy_{auth,noauth}.html` (2026-11-05). D33 survives as a decision about what to build, not as a fact about what is reachable — see §9.2 item 7 |
| C13 | The winning price is unknowable on both sources (the basis of D35 and of §9.1's open half) | **Half wrong.** Unknowable for projects I *lost* — confirmed across 51 Mostaql and 35 Nafezly bid rows on genuinely awarded projects: zero money tokens, zero award vocabulary, the only figure being the labelled `project-budget_range`. For projects I **won** it is explicit: Nafezly «وتم إضافة المبلغ المتفق عليه بينكما : 20 $ في رصيدك»; Mostaql `/payments` payouts under a `projectFees: 20` constant, where payout ÷ 0.8 reproduced three of my recorded bids exactly | `scratch/nafezly/nf_proj_awarded.html`, `nf_my_offers.html`, `scratch/mostaql/q1_lost_*.html`, `q1_payments.html` (2026-11-05) → D51 |
| C14 | Nafezly names no session cookie, so its paste flow must take a whole export | The site issues **`nfthly_session`** (plus a `remember_web_…` token valid to 2027). A name exists to declare; `NafezlySource.cs:95` returning `null` is a gap in our descriptor, not a property of the site | Field names from `nafezly.com_cookies.txt`, read without printing values (2026-11-05) → D49 |
