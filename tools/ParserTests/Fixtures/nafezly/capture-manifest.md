# Nafezly Capture Manifest (A2 — DOM truth for parser-profile derivation)

Captured 2026-10-22 (UTC) for plan `fresh-narrows-gull.md` §A2 and requirements §8.3
(`.repertoire/.steering/v3/product/multi-source-requirements.md`).
All excerpts below are **verbatim quotes** from the captured `.html` files in this folder.

## Method

- Skill consulted: `capture` (`C:\Users\amrmu\.qoder\skills\capture\SKILL.md` + `references/dom.md`).
  Its raw-HTML modes are `dom` (headless Playwright — forbidden by §8.3 "Playwright/WebView is NOT
  needed and must not be added"), `website` (full asset mirror — wrong shape), and `design`
  (curl-based but single-site analysis dumps, not per-URL fixtures). None fits the fixture contract,
  so the task-sanctioned fallback was used: **plain HTTPS GET via curl 8.10.1** with the app's real
  browser UA (exact `MostaqlSource.UserAgent` string), plus `Accept`/`Accept-Language` headers,
  `--compressed`, forced UTF-8. No headless browser was needed — first probe returned HTTP 200,
  `Content-Type: text/html; charset=UTF-8`, 46 KB.
- UA: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36`
- Headers: `Accept: text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8`,
  `Accept-Language: ar,en-US;q=0.9,en;q=0.8`
- **Rate-limit incident (recorded truth):** the first burst of 8 back-to-back fetches got
  `HTTP 429` on `detail-in-progress` + `detail-extra` — Cloudflare error **1015** ("rate limited").
  After a 90 s cooldown + 40 s inter-request pacing + `--retry 4 --retry-delay 40 --retry-all-errors`,
  both re-fetched as 200. Operational lesson for A3: keep well under 8 req/min sustained with
  backoff on 429 (§8.3 cadence 60 s / 8 rpm is conservative and correct).
- **detail-in-progress slug recovery:** the owner-supplied URL string for id 56515 returned
  Cloudflare 429 on attempt 1 (true status masked). Per task instructions, id 56515 was located in
  the listing: not on page 1 (ids 56609→56580) nor page 2 (56579→56544) — found on **page 3**
  (auxiliary probe `listing-page3-probe.html` kept in this folder as evidence). The real URL differs
  from the owner's transcription:
  - owner: `…56515-…ويوب-تعمل-بورتفوليو-بالاي-…` (`تعمل`, `بالاي`)
  - real: `…56515-…ويوب-لعمل-بورتفوليو-بالai-…` (`لعمل`, `بالai`)
  The 404-vs-200 behaviour of a wrong slug was not independently provable (masked by the 1015);
  the recovered URL is what `detail-in-progress.html` contains. Whether slug mismatch 404s or
  redirects by id alone is **unverified**.
- `detail-open.html` and `detail-extra.html` owner URLs were byte-exact hits (56573's URL was
  additionally confirmed identical in `listing-page2.html` row markup).

## Capture set

| Fixture | URL | Status | Bytes | sha256 (12) |
|---|---|---|---|---|
| `listing-page1.html` | `…/projects?page=1` | 200 | 320,204 | `3692893143ca` |
| `listing-page2.html` | `…/projects?page=2` | 200 | 322,604 | `da7c69a45eb1` |
| `listing-filtered.html` | `…?specialize=…&skills=photoshop,illustrator&pricing=100,10000&page=1` | 200 | 324,684 | `23fce6ba265d` |
| `detail-open.html` (project **56540**) | `…/project/56540-…Cisco-Packet-Tracer…` | 200 | 489,119 | `18ab89c29ae4` |
| `detail-in-progress.html` (project **56515**) | recovered URL via listing page 3 | 200 | 366,818 | `75950ec01e54` |
| `detail-extra.html` (project **56573**) | `…/project/56573-…ازرار-تفاعلية…` | 200 | 315,277 | `a3b60bce88b7` |
| `listing-page3-probe.html` (auxiliary) | `…/projects?page=3` | 200 | 320,891 | `81fc7ed5dd4d` |

All ≥ 315 KB (well above the 5 KB error-page threshold). One `.meta.json` per file with
url / url_effective / method / UTC timestamp / status / byte size / sha256.

> ⚠️ **Fixture names vs reality (important):** the states the owner assumed are shifted. Measured
> from the pages themselves: `detail-open.html` (56540) is **تحت التنفيذ**, `detail-in-progress.html`
> (56515) is **مكتمل**, `detail-extra.html` (56573) is **مفتوح**. Filenames were kept as the task
> mandates them; A3 tests must key on the true state vocabulary below, not the file names.
> The three pages incidentally cover exactly the three live states — that is the useful truth.

---

## 1. Detail page analysis

### 1.1 Page shell

Every detail page: `<html lang="ar" dir="rtl" >`, `<meta charset="utf-8">`.
Project title heading (full Arabic title, independent of URL slug):

```html
<h1 style="display: block;font-size: 17px;color: var(--bg-color-0) ;line-height: 1.5" class="kufi font-md-4  mb-0">
                                        تنفيذ مختبر شبكة باستخدام Cisco Packet Tracer وتسليم ملف المشروع
                                    </h1>
```

### 1.2 JSON-LD `JobPosting` (highest-signal machine-readable block, `<head>`)

Verbatim from `detail-open.html` (56540):

```json
{
    "@context": "https://schema.org",
    "@type": "JobPosting",
    "title": "تنفيذ مختبر شبكة باستخدام Cisco Packet Tracer وتسليم ملف المشروع",
    "applicantLocationRequirements": { "@type": "Country", "name": "EG" },
    "baseSalary": { "@type": "MonetaryAmount", "currency": "USD",
        "value": { "@type": "QuantitativeValue", "value": 8.33, "unitText": "HOUR" } },
    "directApply": "true",
    "salaryCurrency": "USD",
    "datePosted": "
    2026 - 10 - 22 ",
    "validThrough": "
    2026 - 11 - 05 ",
    "description": "…full project brief…",
    "employmentType": "CONTRACTOR",
    "industry": "برمجة، تطوير المواقع والتطبيقات",
    "jobLocationType": "TELECOMMUTE",
    "occupationalCategory": "برمجة، تطوير المواقع والتطبيقات",
    "skills": "إدارة الشبكات,Cisco,نظام اسم النطاقات",
    "workHours": "3",
    "hiringOrganization": { "@type": "Organization", "name": "Nafezly", … }
}
```

Notes: `datePosted`/`validThrough` contain embedded newline + spaces around the digits (`"\n    2026 - 10 - 22 "`) —
normalize before parsing. `workHours` is actually **days** (3 days ↔ meta panel "المدة المتاحة 3 أيام";
56515: workHours 1 ↔ "1 أيام"; 56573: workHours 7 ↔ "7 أيام"). `baseSalary.value` is a derived hourly
rate (budget ÷ days ÷ 8?), NOT the site's budget display — use the meta panel for budget.
A `BreadcrumbList` LD follows, position 3 being the specialize category label + href
`https://nafezly.com/projects?specialize=development`.

### 1.3 Meta panel — بطاقة المشروع (label→value rows, right column)

Container header:

```html
<div class="col-12 px-0 main-nafez-box-styles   py-0 rounded border-0">
  …
  <div style="color: var(--bg-color-0);font-size: 17px" class="mb-1"> بطاقة المشروع </div>
```

Each fact is a sibling row `<div class="col-12 row" style="padding:4px 5px">` with a label div
(`text-right … font-size: 13px`) and a value div (`… font-1`). Exact labels observed, in DOM order:
`حالة المشروع`, `تاريخ النشر`, `المدة المتاحة`, `الميزانية`, `عدد المتقدمين`, then a divider and the
`صاحب المشروع` / `منفذ المشروع` person blocks.

### 1.4 State vocabulary (حالة المشروع) — exact strings + badge markup

The value cell holds one span whose inline `background` hex encodes the state:

```html
<span style="background:#007bff; color: #fff; padding: 0px 7px 1px!important; display: inline-block;position: relative ;font-size: 10px;border-radius:2px;line-height: 1.6;" class=" p-1">
                                             تحت التنفيذ </span>          <!-- detail-open.html (56540) -->

<span style="background:#2a2a2a; …same shape…">                                             مكتمل </span>   <!-- detail-in-progress.html (56515) -->

<span style="background:#28a745; …same shape…">                                             مفتوح </span>   <!-- detail-extra.html (56573) -->
```

Observed vocabulary (all 3 fixtures + listings): **`مفتوح`** (open, `#28a745`), **`تحت التنفيذ`**
(in-progress, `#007bff`), **`مكتمل`** (completed, `#2a2a2a`). **`مغلق` (closed) was NOT observed
anywhere** — do not encode it from guesswork; capture a closed project later if it exists.
Note the value text has leading whitespace and a trailing space before `</span>` — trim required.

### 1.5 Budget (الميزانية) — verbatim display strings

```html
<div class="col-6 col-md-5 col-lg-6 col-xl-7 px-0 text-right p-1 font-1">
    10 - 25 $
</div>
```

- 56540: `10 - 25 $` · 56515: `10 - 25 $` · 56573: `50 - 100 $`
- Format: `{min} - {max} $` — literal space-hyphen-space separator, **trailing `$` with a preceding
  space**, currency-suffix (matches descriptor plan `CurrencySuffix = "$"`). Sits inside a `dir="rtl"`
  page → must be rendered LTR-isolated (contract §8.3 already demands this).
- Filtered listing shows the band ladder also used by `pricing`: `100 - 250 $`, `250 - 500 $`.

### 1.6 Proposals count

- Detail label is **`عدد المتقدمين`** (not العروض المقدمة): values verbatim `72` (56540), `43` (56515), `30` (56573) as plain text in the font-1 value div.
- The `العروض المقدمة` section header renders **server-side with the offers already inline**: the number of `<div class="col-12 offer-order …" data-score="…">` blocks **exactly equals عدد المتقدمين** (72 / 43 / 30 counted in the fixtures). Offers are NOT lazy-loaded for anonymous users.
- Offer row (verbatim, detail-in-progress): `<div class="col-12 offer-order \n        " style="border-bottom: 1px solid var(--bg-main-bg);padding: 21px 15px;" data-score="38.297872340426">` — `data-score` is a float sort score.
- Section header + sort dropdown markup:

```html
العروض المقدمة <span class="fas fa-spinner-third fa-spin  mr-2 loading-offer-spin fast-spin" style="display: none;"></span>
…
<button … id="offer-sort-list-btn-text" …> تلقائي </button>
<span class="dropdown-item …" data-sort="NEWEST">الأحدث</span>
<span class="dropdown-item …" data-sort="OLDEST">الأقدم</span>
```

### 1.7 Publish time (تاريخ النشر) + duration (المدة المتاحة)

Relative-text only in the meta panel (verbatim): `منذ 23 ساعة` (56540), `منذ يوم` (56515),
`منذ 11 ساعة` (56573). Duration: `3 أيام`, `1 أيام`, `7 أيام`. Pattern vocabulary:
`منذ {N} ساعة|ساعتين|أيام|يوم|أسبوع…`, `منذ يوم` (no "1"), `{N} أيام`. No absolute timestamp in the
panel; the only absolute date is the oddly-whitespaced LD `datePosted`.

### 1.8 Skills block (body)

Section card header ` مهارات مطلوبة` (leading space), then one anchor per skill:

```html
<div style="font-size: 17px;color:var(--bg-color-0)" class="mb-1"> مهارات مطلوبة</div>
…
<a href="https://nafezly.com/projects/skill/network-administration" class="mx-1 d-inline-block btn font-small rounded-pill py-1 my-1 tag-class"><span class="far fa-tags pl-1"></span> إدارة الشبكات</a>
<a href="https://nafezly.com/projects/skill/cisco" class="…tag-class"><span class="far fa-tags pl-1"></span> Cisco</a>
<a href="https://nafezly.com/projects/skill/dns" class="…tag-class"><span class="far fa-tags pl-1"></span> نظام اسم النطاقات</a>
```

Selector: `a[href^="https://nafezly.com/projects/skill/"]` (class `tag-class`), label = text after the
`far fa-tags` span (leading space). Same skill CSV also in LD `skills`.

### 1.9 Poster / executor blocks (owner & freelancer)

Inside بطاقة المشروع, section labels `<span class="px-1" style="padding:4px 5px;font-size:12px">صاحب المشروع</span>` and `منفذ المشروع`.
Person row verbatim (owner of 56540):

```html
<div class="col-12 px-2 row d-flex align-items-center justify-content-between text-truncate" style="flex-wrap: nowrap;">
    <div class="d-flex align-items-center" style="width:calc(100% - 96px)">
        <a href="javascript:void(0)" role="button" class="js-profile-app-gate d-inline-block" data-profile-role="owner"  aria-label="user"     >
        <img src="https://nafezly-production.fra1.cdn.digitaloceanspaces.com/uploads/avatars/small/1792346086_6ad507e6e4368.webp" style="width: 70px;…" alt="Anas Osama">
    </a>
        <div class="d-inline-block px-1" style="font-size:15px">
            <a href="javascript:void(0)" role="button" class="js-profile-app-gate" style="color: inherit;" data-profile-role="owner">
        Anas Osama
    </a>
            <div class="d-block mt-1" style="font-size:10px;opacity: 0.7;">
                <span class="d-inline-block">
                    <span class="fas fa-map-marker-alt mb-1  pl-0 pl-md-1 "></span> مصر
                </span>
            </div>
        </div>
    </div>
</div>
```

Observed values: 56540 owner `Anas Osama` / مصر, executor `Abdelrahman Moneer` (executor sub-line is
profession, not country: `<span class="fas fa-suitcase …"></span> مهندس حاسبات ونظم`).
56515 owner `دليل الخير` / مصر, executor `عبدالرحمن الزرقانى`.
56573 (open) owner `Saudisader Sader` / السعودية — **no `منفذ المشروع` block at all** ⇒ the executor
section is present only when the project has been awarded (تحت التنفيذ / مكتمل). Model as nullable.

- **Profile URLs do not exist in HTML**: every person anchor is `href="javascript:void(0)"` +
  `class="js-profile-app-gate"` + `data-profile-role="owner|freelancer"`, opening an overlay whose copy is
  `حساب المنفذ متاح على التطبيق` / `حساب صاحب المشروع متاح على التطبيق` (gate div `#nafezly-profile-gate`).
- **Name**: anchor text (also mirrored in `img alt`). **Avatar**: `img src` on
  `nafezly-production.fra1.cdn.digitaloceanspaces.com/uploads/avatars/small/…webp|jpg`.
- **Rating**: only as star glyphs inside offer rows — 5 × `<span class="fas fa-star" style="color: var(--bg-color-4);font-size:9px;"></span>` (empty counterpart not observed in these fixtures; filled-count = stars with `fa-star`, no numeric value in HTML). No numeric rating string anywhere.
- **Successful-projects count: ABSENT** from anonymous HTML (searched `نجح`, `أكمل`, `منجز`, `مشروع ناجح`, `fa-briefcase`, `سجل` — zero hits). It is app-gated. The contract's "executor info" for nafezly web = name + avatar (+ profession line for freelancer) ONLY; rating = star-count approximation from offers at best. A3 must not invent this field.

### 1.10 Supervised flag — ABSENT (recorded as truth)

No `مشرف` / `مراقب` / `إشراف` / `موثوق` badge, text, or per-row marker exists on any captured detail
or listing page (the only `مشرف` hits are the category "أدوات مشرفي المواقع من جوجل"; the only `موثوق`
hit is inside a project description). A JS handler for `.nafezly-filter[data-filter="verified"]` exists
in the listing page, but **the checkbox itself is not server-rendered for anonymous sessions** — so a
"verified/supervised" facet is session-gated. §8.3's "supervised flag" is therefore unobservable
anonymously → capabilities flag false / nullable, never hardcode a selector for it.

### 1.11 Attachments — ABSENT (recorded as truth)

No `المرفقات` / `مرفق` section on any of the 3 detail pages. The word `attachment` appears 3× per page,
all inside the offer-submit modal (`data-attachment-uploader-container-selector=""`) and the
fileuploader extension whitelist JS (`'pdf','png',…`). Conclusion: project briefs have no public
attachment list; attachments are an offer-submission (bidding) concept requiring a session. Parser:
attachments = absent-by-default.

### 1.12 `سعر الخدمة` (mentioned in §8.3)

Not a project field — it is the price line of recommended **service cards** (`…/service/{id}-{slug}`):

```html
<span style="font-size: 12px;white-space: nowrap;" class="d-inline-block">
    سعر الخدمة <span style="font-weight:bold;font-size: 15px;color:#138d50">25$</span>
</span>
```

Card container `id="service-{n}"`, class `service-card`. Price format here is `{n}$` (no space).

---

## 2. Listing grammar

- Header: `<h2 …>المشاريع المعروضة</h2>` — **no result-count text anywhere** (searched نتيجة/إجمالي/found/results). Pagination is prev/next only (no numbers, no total).
- **Row container (exact class string)**, 20 rows per page:

```html
<div class="col-12 main-nafez-box-styles p-3 p-lg-4 mb-lg-3 mb-3 project-box" style="border-radius:5px;…">
```

- **Link / id**: inside row, `div.d-block.text-truncate… > a.text-truncate[href]`; href always absolute:
  `https://nafezly.com/project/{id}-{slug}` → extraction regex `^https://nafezly\.com/project/(\d+)-`.
  Title text = full Arabic title (≠ slug), e.g.
  `<a href="https://nafezly.com/project/56609-…" …> مطلوب مسوّق بالعمولة لبيع شرائح الإنترنت الإلكترونية (eSIM) في السع...</a>` (leading space, `...` truncation).
- **Snippet**: `<h3 class="naskh font-1 m-0 col-12 col-lg-10 px-0" style="…line-height: 1.8;overflow: hidden;height: 48px"> … إنس...</h3>`
- **Owner row (desktop-only)**: wrapper `col-12 px-0 row align-items-center … d-lg-flex d-none`; 40px avatar `img` (`alt` = name) in `a.js-profile-app-gate[data-profile-role=owner]`, name anchor, country `<span class="fas fa-map-marker-alt …"></span> السعودية`.
- **Meta column** `col-12 col-lg-3 p-0`, each fact a `<span class="d-inline-block kufi " style="font-size: 12px;color: #777777">` + icon span:
  - budget — `fal fa-usd-circle` → `10 - 25 $` (same string as detail; wrapper `d-none d-lg-block` — desktop only)
  - duration — `fal fa-business-time` → `2 أيام`
  - proposals — `fal fa-ballot` → `2 عروض` (pattern `{n} عروض`, suffix not singularized in observed rows)
  - publish — `fal fa-clock` → `منذ ساعة`
  - country (mobile fallback) — `fal fa-map-marker-alt`, wrapper `d-inline-block d-lg-none`
  - state — `fas fa-check-circle` + text, colored inline: `مفتوح` `color:#28a745`, `مكتمل` `color:#2a2a2a`, `تحت التنفيذ` `color:#007bff` (all three observed on rows; `مغلق` never).
- **Order**: newest-first by id (p1: 56609→56580, p2: 56579→56544, p3 contains 56515).
- **Pagination controls** (verbatim, page1):

```html
<nav>
  <ul class="pagination">
    <li class="page-item disabled" aria-disabled="true"><span class="page-link">&laquo; السابق</span></li>
    <li class="page-item"><a class="page-link" href="https://nafezly.com/projects?page=2" rel="next">التالي &raquo;</a></li>
  </ul>
</nav>
```

  page2 differs: `السابق` becomes an active `<a … href="…projects?page=1" rel="prev">` and `التالي` → `page=3`. Next href on filtered page **preserves the full query**: `href="https://nafezly.com/projects?specialize=business%2Cdevelopment%2C…%2Cother&amp;skills=photoshop%2Cillustrator&amp;pricing=100%2C10000&amp;page=2"` (commas encoded `%2C`, params reordered to specialize→skills→pricing→page).

## 3. Filter effect (`listing-filtered.html`)

- Row grammar is **identical** (same `project-box` class, 20 rows). No active-filter chips, no
  filter summary banner, no empty state (results existed; `لا توجد`/no-result markers count = 0).
- Observable differences vs plain page1:
  1. every sidebar specialize checkbox carries `checked` (7 hits, verbatim: `<input type="checkbox"  id="business" name="titles[]" data-ui="" data-filter="specialize" value="business" class="nafezly-filter" checked aria-label="بحث" />`);
  2. skills `select2` options carry `selected=""` (`<option value="photoshop"  selected=""  data-extra-search=…>`);
  3. pagination next-href preserves params (see above);
  4. budgets shifted to `100 - 250 $` / `250 - 500 $` — consistent with `pricing=100,10000` acting on the low end of the band;
  5. result ids jump to older/newer-matching set (56437, 54866, 53136, …) — filtering ignores strict id order.
- **The seven `specialize` slugs confirmed against the page's own labels** (sidebar group heading context + each `<label … for="{slug}">`):
  `business` → `أعمال وخدمات استشارية وإدارية` · `development` → `برمجة، تطوير المواقع والتطبيقات` ·
  `design` → `تصميم وأعمال فنية وإبداعية` · `marketing` → `تسويق الكتروني ومبيعات` ·
  `writing-translation` → `كتابة، صناعة محتوى، ترجمة ولغات` · `training` → `تدريب، تعليم ومساعدة عن بعد` · `other` → `أمور أخرى`.
- Facet mechanics (JS, verbatim): `.nafezly-filter[data-filter="specialize"]:checked … setGetParam($(this).data('filter'),titles.join())` → comma-joined values in the query. `pricing` facet: `<input … class="js-range-slider nafezly-transition pricing" … data-filter="pricing">` inside a `dir="ltr"` container under heading `الميزانية`. `skills` facet: `<select class="select2 … nafezly-filter" multiple … data-filter="skills">` under heading `المهارات`, options carry `value` slug + `data-extra-search` JSON `{title: '{slug} {ar} {en} {slug}'}`. JS also wires an `online` and (unrendered) `verified` facet.

## 4. Encoding check

All 7 files: HTTP `charset=UTF-8`, `<html lang="ar" dir="rtl" >`, `<meta charset="utf-8">`.
Zero mojibake markers (`Ã` count = 0, U+FFFD = 0) in every fixture. Arabic renders correctly
throughout; the pricing-slider container is the only `dir="ltr"` island in listings. Budget strings
are RTL-page LTR runs (`10 - 25 $`) — confirms contract's dir=ltr isolation requirement.

## 5. Vocabulary to encode into `NafezlyParseProfile` (handoff for A3)

State texts → enum:
- `مفتوح` → open (green `#28a745`) · `تحت التنفيذ` → in-progress (blue `#007bff`) · ` مكتمل ` → completed (dark `#2a2a2a`) — **trim whitespace**; `مغلق` unobserved, session-gated `verified` facet unobservable.

Meta-panel labels (`بطاقة المشروع` row pairs): `حالة المشروع`, `تاريخ النشر`, `المدة المتاحة`, `الميزانية`, `عدد المتقدمين`, `صاحب المشروع`, `منفذ المشروع`. Person sub-lines: country icon `fa-map-marker-alt`, freelancer profession icon `fa-suitcase`.

Detail selectors: title `h1.kufi`; LD blocks `script[type=application/ld+json]` `"@type": "JobPosting"` (+ `BreadcrumbList`); skills `a[href^="https://nafezly.com/projects/skill/"]` under ` مهارات مطلوبة`; offers `#project-load-offers .offers-list div.offer-order[data-score]`; offer-sort options `تلقائي/الأحدث/الأقدم`; profile-gate anchors `a.js-profile-app-gate[data-profile-role=owner|freelancer]` (names from anchor text / `img alt`; avatars on `nafezly-production.fra1.cdn.digitaloceanspaces.com/uploads/avatars/…`; **no rating number, no executor success-count, no attachments, no supervised flag in anonymous HTML** — all nullable/absent-by-default).

Budget: display `{min} - {max} $` (spaces around `-`, space before `$`, suffix currency `$`); service-card price `{n}$` under label `سعر الخدمة` (separate shape). Publish/duration relative vocabulary: `منذ {n} ساعة`, `منذ يوم`, `منذ {n} أيام`, `{n} أيام`. Proposals row text: `{n} عروض`.

Listing selectors: row `div.project-box` (full class `col-12 main-nafez-box-styles p-3 p-lg-4 mb-lg-3 mb-3 project-box`), id `^https://nafezly\.com/project/(\d+)-`, title/snippet `a.text-truncate` + `h3.naskh`, meta icons `fa-usd-circle|fa-business-time|fa-ballot|fa-clock|fa-map-marker-alt|fa-check-circle`, header `المشاريع المعروضة`, 20 rows/page, pagination `ul.pagination li.page-item a.page-link[rel=prev|next]` labels `« السابق` / `التالي »` (HTML entities `&laquo;`/`&raquo;`), no result count.

Query facets (descriptor data, not UI constants): `specialize` (7 slugs + Arabic labels in §3), `skills` (comma slugs), `pricing=min,max` (band floor), `page=N` (newest-first). Param order in echoed URLs: `specialize,skills,pricing,page`, commas `%2C`-encoded.

Rate-limit truth: burst > ~6 req/min from one client → Cloudflare 429/1015; honor `Retry-After`-style backoff ≥ 40 s; descriptor defaults 60 s / 8 rpm are appropriate.
