"""DetailParser: field combinator over structural extraction and inference fallbacks.

Behavioral-parity port of ``Infrastructure/Http/Parsers/DetailParser.cs`` (plan
§3.C.2 + §4.1 traps 2/3/4/11/12/13/15/16/17/22). Attachment extraction is
DROPPED per the scope lock (refactor-python-plan.md §2): the C# Parse body's
``ExtractAttachments`` block (cs:227-237) has no Python counterpart and
``ProjectDetails`` carries no attachments field.

ENTITY RULE (parity decision, plan §4.1 trap 9): HtmlAgilityPack keeps HTML
entities encoded in node text, so C# wraps reads in
``Normalize(HtmlEntity.DeEntitize(node.InnerText))``. lxml.html already decodes
entities at parse time and ``text_content()`` fuses adjacent text nodes exactly
like HAP InnerText; every such C# read maps to plain ``normalize(...)``
/``text_content()`` here — NO extra ``html.unescape`` (double-decoding would
corrupt payloads containing literal ``&amp;amp;``-style text). The same applies
to the page-wide gate text (cs:164) which becomes ``normalize_label(root.text_content())``.

Inference is a lazily-computed-once-per-page fallback (cs:96, cs:129); it is
invoked through :func:`_infer_fields_once`, which imports the sibling-owned
``mostaql.scraping.parsers.inference`` module lazily (contract §8:
``InferenceEngine.infer_fields(root) -> InferFieldsResult`` with
``fields: dict[str, InferredField]``, ``InferredField(value, confidence,
strategy)``; a missing field key is treated as ``(None, 0.0)``).
"""

import re
from dataclasses import replace
from datetime import UTC, datetime
from importlib import import_module

from lxml import etree  # type: ignore[import-untyped]
from lxml.html import HtmlElement, document_fromstring  # type: ignore[import-untyped]

from mostaql.models.enrichment_status import EnrichmentStatus
from mostaql.models.field_resolution import FieldMismatch, FieldResolution
from mostaql.models.owner import Owner
from mostaql.models.project_details import ProjectDetails
from mostaql.models.project_skill import ProjectSkill
from mostaql.scraping.parsers.errors import raise_empty_html, raise_missing_title
from mostaql.scraping.parsers.structural import (
    _by_exact_token,
    _by_token_contains,
    extract_meta_fields,
    find_owner_card,
    label_driven_extract,
    normalize_multiline,
)
from mostaql.text.normalization import (
    LABEL_TRIM_CHARS,
    normalize,
    normalize_label,
    to_ascii_digits,
)
from mostaql.text.proposals import parse_proposals
from mostaql.text.relative_time import parse_relative_number

__all__ = ["DetailParser"]

# ---------------------------------------------------------------------------
# Frozen tables (copied verbatim from DetailParser.cs:19-70)
# ---------------------------------------------------------------------------

#: Arabic label -> inference field key (cs:19-42).
_LABEL_TO_FIELD: dict[str, str] = {
    "حالة المشروع": "project_status",
    "تاريخ النشر": "published_date",
    "الميزانية": "budget",
    "مدة التنفيذ": "duration",
    "تاريخ التسجيل": "registration_date",
    "معدل التوظيف": "hire_rate",
    "نسبة التوظيف": "hire_rate",
    "المشاريع المفتوحة": "open_projects_count",
    "مشاريع مفتوحة": "open_projects_count",
    "مشاريع قيد التنفيذ": "in_progress_count",
    "المشاريع قيد التنفيذ": "in_progress_count",
    "مشاريع منجزة": "completed_projects_count",
    "مشاريع مكتملة": "completed_projects_count",
    "المشاريع المكتملة": "completed_projects_count",
    "المشاريع المنجزة": "completed_projects_count",
    "التواصلات الجارية": "ongoing_conversations",
    "بدأ تنفيذه منذ": "started_since",
    "تاريخ الصفقة": "deal_date",
    "موعد التسليم": "delivery_date",
    "عدد العروض": "proposal_count",
    "عدد المقترحات": "proposal_count",
}

#: Field -> ALL synonym labels, preserving first-occurrence order of the label
#: table above (C# FieldToLabels grouping, cs:54-57): the label-presence check
#: must accept ANY known synonym, not just whichever happened to group first.
_FIELD_TO_LABELS: dict[str, tuple[str, ...]] = {}
for _label, _field in _LABEL_TO_FIELD.items():
    _FIELD_TO_LABELS[_field] = (*_FIELD_TO_LABELS.get(_field, ()), _label)

_COMPLETED_ONLY_FIELDS = frozenset(
    {"started_since", "deal_date", "delivery_date", "completed_projects_count"}
)
_COMPLETED_STATUS_TEXT = "مكتمل"

_NUMERIC_FIELDS = frozenset(
    {
        "hire_rate",
        "budget",
        "duration",
        "open_projects_count",
        "in_progress_count",
        "completed_projects_count",
        "ongoing_conversations",
        "proposal_count",
    }
)

#: Placeholder markers count as VALID resolutions in SanityOk (trap §4.1-12);
#: they are nulled afterwards while provenance survives.
_NOT_CALCULATED_MARKERS = ("لم يحسب بعد", "غير محدد", "N/A", "لا يوجد")
_ARABIC_DIGIT_RE = re.compile("[٠١٢٣٤٥٦٧٨٩]")

_SITE_SUFFIX_SEPARATORS = (" - ", " | ", " – ")  # noqa: RUF001 (verbatim C# cs:361)
_SITE_KEYWORDS = ("مستقل", "Mostaql", "Mostaqlk")

_OWNER_NAME_XPATHS = (
    ".//h5[contains(@class, 'name')]",
    ".//h3[contains(@class, 'name')]",
    ".//a[contains(@href, '/u/')]",
    ".//h5",
    ".//h3",
)

_DETAILS_AREA_XPATHS = (
    "//*[@id='projectDetailsTab']",
    "//div[contains(@class, 'project-details')]",
    "//article",
)

_TRIM_CHARS = "".join(LABEL_TRIM_CHARS)
_INT32_MAX = 2**31 - 1

_PERCENT_NUMBER_RE = re.compile(r"\d+(?:[.,]\d+)?")  # cs:722-723
_GROUPED_INT_RE = re.compile(r"\d{1,3}(?:[.,]\d{3})+|\d+")  # cs:772-773

_PROFILE_BASE_URL = "https://mostaql.com"


def _first(nodes: list[HtmlElement]) -> HtmlElement | None:
    """SelectSingleNode equivalent: first node of an xpath result, or None."""
    return nodes[0] if nodes else None


def _coalesce(*candidates: HtmlElement | None) -> HtmlElement | None:
    """First non-None element (lxml elements are falsy when childless — never
    use ``or`` chaining for them)."""
    for candidate in candidates:
        if candidate is not None:
            return candidate
    return None


def _details_area(root: HtmlElement) -> HtmlElement:
    """Scoped details container falling back to the whole root (cs:577-579,
    cs:618-621, cs:671-674 — every call site ultimately uses ``?? root``)."""
    for xpath in _DETAILS_AREA_XPATHS:
        area = _first(root.xpath(xpath))
        if area is not None:
            return area
    return root


def _infer_fields_once(root: HtmlElement) -> dict[str, tuple[str | None, float]]:
    """Lazily run InferenceEngine ONCE per page (cs:96/cs:129).

    Returns ``{field: (value, confidence)}``; missing keys mean no candidates
    and resolve to ``(None, 0.0)`` at the call site. The sibling-owned module
    is imported lazily via importlib/getattr because it lands concurrently;
    the runtime call shape matches the frozen contract exactly
    (``InferenceEngine.infer_fields(root) -> InferFieldsResult``).
    """
    inference_module = import_module("mostaql.scraping.parsers.inference")
    # Dynamic attribute access is deliberate: the module lands concurrently and
    # a static name would couple this file's import health to its timing.
    engine = getattr(inference_module, "InferenceEngine")  # noqa: B009
    result = engine.infer_fields(root)
    raw_fields = getattr(result, "fields")  # noqa: B009
    return {key: (entry.value, entry.confidence) for key, entry in raw_fields.items()}


# ---------------------------------------------------------------------------
# Sanity / agreement primitives (cs:273-296, cs:299-300, cs:312-321)
# ---------------------------------------------------------------------------


def _is_placeholder(value: str | None) -> bool:
    """Mirrors pipeline.py's _is_placeholder (cs:299-300)."""
    return value is not None and any(marker in value for marker in _NOT_CALCULATED_MARKERS)


def _sanity_ok(field: str, value: str | None) -> bool:
    """Cheap type-shape gate on the structural fast-path value (cs:273-296).

    Null/empty fail; a recognized placeholder is a VALID (nullable) resolution,
    NOT a failure; numeric fields must contain some digit (ASCII or
    Arabic-Indic); dates/status/free text accept any non-empty value.
    """
    if value is None:
        return False
    trimmed = value.strip()
    if len(trimmed) == 0:
        return False
    if _is_placeholder(trimmed):
        return True
    has_digit = any(ch.isdigit() for ch in trimmed) or _ARABIC_DIGIT_RE.search(trimmed) is not None
    if field in _NUMERIC_FIELDS:
        return has_digit
    return True


def _values_agree(a: str | None, b: str | None) -> bool:
    """Trim-equality or ordinal containment either direction; null on either
    side counts as agreement (trap §4.1-15) (cs:312-321)."""
    if a is None or b is None:
        return True
    a_norm = a.strip()
    b_norm = b.strip()
    return a_norm == b_norm or b_norm in a_norm or a_norm in b_norm


def _count_bid_items(root: HtmlElement) -> int:
    """Deterministic bid-row count via ``data-bid-item`` attributes (cs:308-309)."""
    return len(root.xpath("//*[@data-bid-item]"))


# ---------------------------------------------------------------------------
# Field combinator (cs:92-157)
# ---------------------------------------------------------------------------


def _structural_value(
    structural: dict[str, str],
    label_driven: dict[str, str],
    labels: tuple[str, ...],
) -> str | None:
    """Preferred structural value across synonym labels (cs:104-113).

    Structural wins per label; label-driven DOM adjacency is the second lookup
    under the same normalized key. First label yielding any entry wins.
    """
    for label in labels:
        label_key = normalize_label(label)
        if label_key in structural:
            return structural[label_key]
        if label_key in label_driven:
            return label_driven[label_key]
    return None


def _inference_pick(
    inferred: dict[str, tuple[str | None, float]], field: str
) -> tuple[str | None, float, str]:
    """Missing field key resolves to (None, 0.0, "none") per the §8 contract."""
    entry = inferred.get(field)
    if entry is None:
        return (None, 0.0, "none")
    value = entry[0]
    source = "inference" if value is not None else "none"
    return (value, entry[1], source)


def _cross_validate(
    field: str,
    s_val: str | None,
    s_ok: bool,
    value: str | None,
    inferred: dict[str, tuple[str | None, float]] | None,
    mismatches: list[FieldMismatch],
) -> str | None:
    """Record structural/inference disagreement when both sides exist (cs:138-149).

    Inference overrides ONLY on failed sanity; a trusted structural fast path
    keeps its value while the mismatch stays observable.
    """
    if inferred is None or s_val is None:
        return value
    entry = inferred.get(field)
    inf_val = entry[0] if entry is not None else None
    if inf_val is not None and not _values_agree(s_val, inf_val):
        mismatches.append(FieldMismatch(field, s_val, inf_val))
        if not s_ok:
            value = inf_val
    return value


def _resolve_fields(
    root: HtmlElement, structural: dict[str, str], label_driven: dict[str, str]
) -> tuple[dict[str, FieldResolution], list[FieldMismatch]]:
    """Structural-first / inference-fallback combinator per field (cs:100-157)."""
    fields: dict[str, FieldResolution] = {}
    mismatches: list[FieldMismatch] = []
    inferred: dict[str, tuple[str | None, float]] | None = None

    for field, labels in _FIELD_TO_LABELS.items():
        s_val = _structural_value(structural, label_driven, labels)
        s_ok = _sanity_ok(field, s_val)
        if s_ok:
            value: str | None = s_val
            source = "structural"
            confidence = 1.0
        else:
            # Lazily computed ONCE per page (cs:129); reused by later fields.
            if inferred is None:
                inferred = _infer_fields_once(root)
            value, confidence, source = _inference_pick(inferred, field)

        value = _cross_validate(field, s_val, s_ok, value, inferred, mismatches)
        if _is_placeholder(value):
            value = None
        fields[field] = FieldResolution(value, source, confidence)

    return fields, mismatches


# ---------------------------------------------------------------------------
# Post-combinator gates (cs:159-212)
# ---------------------------------------------------------------------------


def _apply_completed_only_gates(fields: dict[str, FieldResolution], page_text: str) -> None:
    """Pass 1: completed-only fields resolved by inference need their Arabic
    label genuinely present somewhere on the page (cs:164-175); otherwise the
    value has nothing to latch onto and is forced to (None, none, 0.0)."""
    for field in sorted(_COMPLETED_ONLY_FIELDS):
        resolution = fields.get(field)
        if resolution is None or resolution.source != "inference":
            continue
        labels = [normalize_label(label) for label in _FIELD_TO_LABELS.get(field, ())]
        if labels and not any(label in page_text for label in labels):
            fields[field] = FieldResolution(None, "none", 0.0)


def _apply_proposal_override(
    fields: dict[str, FieldResolution], root: HtmlElement, page_text: str
) -> None:
    """Bid-count override (cs:177-198).

    Real pages never render an عدد العروض/عدد المقترحات label, so ANY
    text-derived proposal value is untrustworthy: actual ``data-bid-item`` rows
    always win — emitting "{n} عروض" for EVERY n including 1 (trap §4.1-4) —
    and only survive when neither label occurs anywhere on the page.
    """
    proposal_labels = [
        normalize_label(label) for label in _FIELD_TO_LABELS.get("proposal_count", ())
    ]
    bid_count = _count_bid_items(root)
    if bid_count > 0:
        fields["proposal_count"] = FieldResolution(f"{bid_count} عروض", "structural", 1.0)
    elif not any(label in page_text for label in proposal_labels):
        fields["proposal_count"] = FieldResolution(None, "none", 0.0)


def _apply_completion_status_gate(fields: dict[str, FieldResolution]) -> None:
    """Pass 2: started_since/deal_date/delivery_date are only meaningful for
    completed projects (cs:200-212); completed_projects_count EXEMPT (trap
    §4.1-13). Source/confidence are preserved, value nulled."""
    status_resolution = fields.get("project_status")
    status_value = status_resolution.value if status_resolution is not None else None
    if status_value is None or _COMPLETED_STATUS_TEXT not in status_value:
        for field in sorted(_COMPLETED_ONLY_FIELDS):
            if field == "completed_projects_count":
                continue
            resolution = fields.get(field)
            if resolution is not None:
                fields[field] = replace(resolution, value=None)


# ---------------------------------------------------------------------------
# Title (cs:330-390)
# ---------------------------------------------------------------------------


def _strip_site_suffix(title: str) -> str:
    """Strip Mostaql site suffixes (cs:364-390).

    First pass: LAST occurrence of each separator whose trailing suffix
    contains a site keyword (case-insensitive) is cut. Second pass: a trailing
    bare keyword is removed. Both passes mutate sequentially.
    """
    for separator in _SITE_SUFFIX_SEPARATORS:
        idx = title.rfind(separator)
        if idx > 0:
            suffix = title[idx + len(separator) :]
            if any(k.lower() in suffix.lower() for k in _SITE_KEYWORDS):
                title = title[:idx].strip()

    for keyword in _SITE_KEYWORDS:
        if title.lower().endswith(keyword.lower()):
            title = title[: len(title) - len(keyword)].strip()

    return title


def _extract_title(root: HtmlElement) -> str:
    """Title chain h1 -> og:title -> <title> (cs:330-359); empty chain result
    triggers MissingTitle at the caller."""
    h1 = _first(root.xpath("//h1"))
    if h1 is not None:
        text = normalize(h1.text_content())
        if text:
            stripped = _strip_site_suffix(text)
            if stripped:
                return stripped

    og = _first(root.xpath("//meta[@property='og:title' or @name='og:title']"))
    og_content = og.get("content", "") if og is not None else ""
    og_title = normalize(og_content)
    if og_title:
        stripped = _strip_site_suffix(og_title)
        if stripped:
            return stripped

    title_tag = _first(root.xpath("//title"))
    doc_title = normalize(title_tag.text_content()) if title_tag is not None else ""
    return _strip_site_suffix(doc_title)


# ---------------------------------------------------------------------------
# Description (cs:392-472)
# ---------------------------------------------------------------------------


def _find_densest_text_block(root: HtmlElement) -> HtmlElement | None:
    """Last-resort description heuristic (cs:449-472): div/article/section with
    <=2 direct block children carrying >200 chars of normalized text, longest
    wins; short nav/footer blurbs ignored entirely."""
    best: HtmlElement | None = None
    best_length = 200

    for node in root.xpath("//div|//article|//section"):
        if len(node.xpath("./div|./article|./section")) > 2:
            continue
        text = normalize(node.text_content())
        if len(text) > best_length:
            best = node
            best_length = len(text)

    return best


def _extract_description(root: HtmlElement) -> str:
    """Description chain (cs:398-442): text-wrapper-div inside
    #projectDetailsTab (multiline-preserving), then og:description vs densest
    block — the block wins ONLY when strictly longer than the og teaser."""
    details_tab = _first(root.xpath("//*[@id='projectDetailsTab']"))
    if details_tab is not None:
        desc = _coalesce(
            _first(_by_token_contains(details_tab, "div", "text-wrapper-div")), details_tab
        )
    else:
        desc = _first(_by_token_contains(root, "div", "text-wrapper-div"))

    if desc is not None:
        text = normalize_multiline(desc)
        if text:
            return text

    og = _first(root.xpath("//meta[@property='og:description' or @name='description']"))
    og_content = og.get("content", "") if og is not None else ""
    og_text = normalize(og_content)

    densest = _find_densest_text_block(root)
    if densest is not None:
        text = normalize_multiline(densest)
        if len(text) > len(og_text):
            return text

    return og_text


# ---------------------------------------------------------------------------
# Skills (cs:480-538)
# ---------------------------------------------------------------------------


def _looks_like_skill_href(href: str) -> bool:
    """Identifier-blind skill-link shapes, OrdinalIgnoreCase (cs:535-538)."""
    lowered = href.lower()
    return "/skills/" in lowered or "skill=" in lowered or "/tag/" in lowered


def _skills_from_list(skills_list: HtmlElement) -> list[ProjectSkill]:
    """Direct-<li> reads inside a located skills list (cs:488-503)."""
    result: list[ProjectSkill] = []
    for li in skills_list.xpath("./li"):
        name = normalize(li.text_content())
        if not name:
            continue
        link = _first(li.xpath(".//a"))
        url = link.get("href") if link is not None else None
        result.append(ProjectSkill(name=name, url=url))
    return result


def _skills_from_href_sweep(root: HtmlElement) -> list[ProjectSkill]:
    """Identifier-blind fallback (cs:510-532): every skill on Mostaql is a link
    into the skill taxonomy, recognized by href shape; name length 1..60 and
    OrdinalIgnoreCase dedupe."""
    result: list[ProjectSkill] = []
    seen: set[str] = set()
    for anchor in root.xpath("//a[@href]"):
        href = anchor.get("href", "")
        if not _looks_like_skill_href(href):
            continue
        name = normalize(anchor.text_content())
        if len(name) == 0 or len(name) > 60:
            continue
        key = name.upper()
        if key in seen:
            continue
        seen.add(key)
        result.append(ProjectSkill(name=name, url=href))
    return result


def _extract_skills(root: HtmlElement) -> list[ProjectSkill]:
    """Skills: exact-token ul.skills, then substring-token fallback, then the
    zero-skills href-shape sweep (cs:480-538)."""
    skills_list = _coalesce(
        # cs:482 - exact-token class match via the concat trick.
        _first(_by_exact_token(root, "ul", "skills")),
        _first(_by_token_contains(root, "ul", "skills")),
    )

    result = _skills_from_list(skills_list) if skills_list is not None else []
    if result:
        return result

    return _skills_from_href_sweep(root)


# ---------------------------------------------------------------------------
# Owner assembly (cs:214-225 + cs:540-720)
# ---------------------------------------------------------------------------


def _owner_name_node(scope: HtmlElement) -> HtmlElement | None:
    """Five-step name-node chain inside a scope (cs:556-560, cs:702-706)."""
    for xpath in _OWNER_NAME_XPATHS:
        node = _first(scope.xpath(xpath))
        if node is not None:
            return node
    return None


def _extract_owner_name(root: HtmlElement, label_driven: dict[str, str]) -> str | None:
    """Owner display-name fallback chain (cs:548-591)."""
    owner_card = find_owner_card(root)
    if owner_card is not None:
        name_node = _owner_name_node(owner_card)
        if name_node is not None:
            text = normalize(name_node.text_content())
            if text:
                return text

    labelled = label_driven.get(normalize_label("صاحب المشروع"))
    if labelled and len(labelled) <= 80:
        return labelled

    area = _details_area(root)
    for anchor in area.xpath(".//a[contains(@href, '/u/')]"):
        text = normalize(anchor.text_content())
        if 0 < len(text) <= 60:
            return text

    return None


def _absolutize_profile_url(url: str) -> str:
    """Absolutize against https://mostaql.com, inserting '/' unless the href
    already starts with one (cs:608-611, cs:625-628)."""
    if url.lower().startswith("http"):
        return url
    slash = "" if url.startswith("/") else "/"
    return f"{_PROFILE_BASE_URL}{slash}{url}"


def _extract_owner_profile_url(root: HtmlElement) -> str | None:
    """Owner profile URL (cs:597-630): card /u/-link first, then the scoped
    details-area fallback; relative hrefs absolutized."""
    owner_card = find_owner_card(root)
    if owner_card is not None:
        profile_link = _first(owner_card.xpath(".//a[contains(@href, '/u/')]"))
        if profile_link is not None:
            url = profile_link.get("href", "")
            if url:
                return _absolutize_profile_url(url)

    area = _details_area(root)
    any_link = _first(area.xpath(".//a[contains(@href, '/u/')]"))
    fallback_url = any_link.get("href", "") if any_link is not None else ""
    if fallback_url:
        return _absolutize_profile_url(fallback_url)
    return None


def _stable_hash(text: str) -> int:
    """Synthetic owner id: h*31+ord(c) over a wrapping signed 64-bit long,
    abs()-ed (cs:693-698, cs:711-716; trap §4.1-3). Collision-prone BY DESIGN."""
    mask64 = (1 << 64) - 1
    h = 0
    for ch in text:
        h = (h * 31 + ord(ch)) & mask64
    if h >= 1 << 63:
        h -= 1 << 64
    return abs(h)


def _username_from_href(href: str) -> str | None:
    """Username segment after "/u/" in a profile href (cs:658-664)."""
    parts = [part for part in href.split("/") if part]
    try:
        u_idx = parts.index("u")
    except ValueError:
        return None
    return parts[u_idx + 1] if u_idx + 1 < len(parts) else None


def _owner_card_numeric_id(card: HtmlElement) -> int | None:
    """data-user-id on the card itself, else any descendant carrying it
    (cs:644-648); parsed with Int64.TryParse fall-through semantics."""
    id_attr = card.get("data-user-id", "")
    if not id_attr:
        nested = _first(card.xpath(".//*[@data-user-id]"))
        id_attr = nested.get("data-user-id", "") if nested is not None else ""
    try:
        return int(id_attr)
    except ValueError:
        return None


def _area_username(root: HtmlElement) -> str | None:
    """Fallback username from the FIRST /u/-link in the details area
    (cs:668-687) — no text-length filter here, unlike the name chain."""
    area = _details_area(root)
    link = _first(area.xpath(".//a[contains(@href, '/u/')]"))
    href = link.get("href", "") if link is not None else ""
    if href:
        return _username_from_href(href)
    return None


def _extract_owner_id(root: HtmlElement) -> int:
    """Owner numeric id cascade (cs:637-720): data-user-id -> username segment
    (hashed) -> display name (hashed) -> 0."""
    owner_card = find_owner_card(root)
    username: str | None = None

    if owner_card is not None:
        numeric_id = _owner_card_numeric_id(owner_card)
        if numeric_id is not None:
            return numeric_id
        profile_link = _first(owner_card.xpath(".//a[contains(@href, '/u/')]"))
        href = profile_link.get("href", "") if profile_link is not None else ""
        if href:
            username = _username_from_href(href)

    if username is None:
        username = _area_username(root)

    if username is not None:
        return _stable_hash(username)

    name_scope = find_owner_card(root)
    if name_scope is None:
        name_scope = root
    name_node = _owner_name_node(name_scope)
    owner_name = normalize(name_node.text_content()) if name_node is not None else None
    if owner_name:
        return _stable_hash(owner_name)

    return 0


# ---------------------------------------------------------------------------
# Numeric parsers (cs:722-773)
# ---------------------------------------------------------------------------


def parse_percent(text: str | None) -> float | None:
    """Leading percent number, Arabic-digit tolerant (cs:725-747).

    ``\\d+(?:[.,]\\d+)?`` after ToAsciiDigits; comma decimal normalized to dot;
    invariant float parse (None on failure).
    """
    if not text:
        return None
    match = _PERCENT_NUMBER_RE.search(to_ascii_digits(text))
    if match is None:
        return None
    normalized = match.group().replace(",", ".")
    try:
        return float(normalized)
    except ValueError:
        return None


def parse_leading_int(text: str | None) -> int | None:
    """First integer, thousands-separator tolerant (cs:749-770).

    The grouped alternative ``\\d{1,3}(?:[.,]\\d{3})+`` is preferred over the
    plain digit run (regex alternation order); separators deleted; values past
    Int32.MaxValue fail TryParse and yield None, as in .NET.
    """
    if not text:
        return None
    match = _GROUPED_INT_RE.search(to_ascii_digits(text))
    if match is None:
        return None
    digits = match.group().replace(",", "").replace(".", "")
    try:
        value = int(digits)
    except ValueError:
        return None
    return value if value <= _INT32_MAX else None


def _field_value(fields: dict[str, FieldResolution], field: str) -> str | None:
    resolution = fields.get(field)
    return resolution.value if resolution is not None else None


def _build_owner(
    root: HtmlElement, label_driven: dict[str, str], fields: dict[str, FieldResolution]
) -> Owner:
    """Assemble the Owner model (cs:214-225)."""
    return Owner(
        owner_id=_extract_owner_id(root),
        name=_extract_owner_name(root, label_driven) or "",
        profile_url=_extract_owner_profile_url(root),
        hiring_rate_percent=parse_percent(_field_value(fields, "hire_rate")),
        completed_projects_count=parse_leading_int(
            _field_value(fields, "completed_projects_count")
        ),
        registered_at=_field_value(fields, "registration_date"),
        open_projects_count=parse_leading_int(_field_value(fields, "open_projects_count")),
        in_progress_projects_count=parse_leading_int(_field_value(fields, "in_progress_count")),
        ongoing_communications_count=parse_leading_int(
            _field_value(fields, "ongoing_conversations")
        ),
    )


class DetailParser:
    """Parses a Mostaql project detail page into a fully populated ProjectDetails."""

    @staticmethod
    def parse(project_id: int, html: str) -> ProjectDetails:
        """End-to-end port of DetailParser.Parse (cs:72-266), attachments dropped.

        Raises PARSE-001 on empty HTML and PARSE-002 when the title chain is
        exhausted; returns details with ``url=""`` (the scraper attaches the
        canonical URL afterwards — trap §4.1-2) and identical
        discovered_at/enriched_at instants (trap §4.1-22).
        """
        if not html or not html.strip():  # cs:74-77
            raise_empty_html("DetailParser")

        try:
            root: HtmlElement = document_fromstring(html)
        except etree.ParserError:
            # HAP never throws on junk input; its empty DOM exhausts the title
            # chain — mirror that outcome instead of crashing.
            raise_missing_title(project_id)

        title = _extract_title(root)
        if not title:  # cs:84-87
            raise_missing_title(project_id)

        description = _extract_description(root)
        skills = _extract_skills(root)

        structural = extract_meta_fields(root)
        label_driven = label_driven_extract(root)
        fields, mismatches = _resolve_fields(root, structural, label_driven)

        # Whole-page gate text: NormalizeLabel(DeEntitize(InnerText)) in C#
        # (cs:164) — entities already decoded by lxml, see ENTITY RULE.
        page_text = normalize_label(root.text_content())

        _apply_completed_only_gates(fields, page_text)
        _apply_proposal_override(fields, root, page_text)
        _apply_completion_status_gate(fields)

        owner = _build_owner(root, label_driven, fields)

        published_text = _field_value(fields, "published_date")
        proposal_num, proposal_cleaned = parse_proposals(_field_value(fields, "proposal_count"))

        now = datetime.now(UTC)  # ONE instant stamped on both fields (cs:261-262)

        return ProjectDetails(
            project_id=project_id,
            title=title,
            url="",  # trap §4.1-2: scraper attaches URL post-parse
            description=description,
            budget=_field_value(fields, "budget"),
            delivery_days=parse_leading_int(_field_value(fields, "duration")),
            project_status=_field_value(fields, "project_status"),
            publish_time_number=parse_relative_number(published_text),
            publish_time_text=published_text or "",
            proposal_count=proposal_num,
            proposal_count_text=proposal_cleaned,
            skills=skills,
            owner=owner,
            enrichment_status=EnrichmentStatus.ENRICHED,
            discovered_at=now,
            enriched_at=now,
            field_provenance=fields,
            mismatches=mismatches,
        )
