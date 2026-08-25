"""StructuralExtractor: markup-anchored + label-driven meta extraction (C# StructuralExtractor.cs).

Behavioral-parity port of ``Infrastructure/Http/Parsers/StructuralExtractor.cs``
(plan §3.C.2 / §3.D). Attachment extraction (``ExtractAttachments`` /
``AttachmentCandidate``, cs:584-721) is intentionally DROPPED per the scope lock
(refactor-python-plan.md §2: no assets).

ENTITY RULE (parity decision, plan §4.1 trap 9): HtmlAgilityPack keeps HTML
entities encoded in node text, so C# wraps reads in
``Normalize(HtmlEntity.DeEntitize(node.InnerText))``. lxml.html already decodes
entities at parse time and ``text_content()`` fuses adjacent text nodes exactly
like HAP InnerText. Every C# text read therefore maps to plain
``normalize(node.text_content())`` — NO extra ``html.unescape`` (double-decoding
would corrupt payloads such as literal ``&amp;amp;`` text). Likewise
``NormalizeMultiline``'s inner ``DeEntitize`` pass becomes a no-op and is
omitted (see :func:`normalize_multiline`).

Class-matching styles are deliberately mixed, as in C# (trap §4.1-10):
per-TOKEN substring (:func:`_by_token_contains`, cs:414-425), the exact-token
``concat`` XPath trick (:func:`_by_exact_token`), and RAW whole-attribute
substring (cs:403-405) — replicate each exactly where C# uses it.
"""

import re
from typing import cast

from lxml.html import HtmlElement  # type: ignore[import-untyped]

from mostaql.text.normalization import (
    LABEL_TRIM_CHARS,
    normalize,
    normalize_label,
    to_ascii_digits,
)

__all__ = [
    "KNOWN_LABELS",
    "LABEL_TRIM_CHARS",
    "extract_meta_fields",
    "find_owner_card",
    "label_driven_extract",
    "normalize",
    "normalize_label",
    "normalize_multiline",
    "to_ascii_digits",
]

#: Known Arabic labels expected somewhere on a project-detail page, independent
#: of whichever element currently wraps them (cs:31-50, verbatim, 17 entries).
KNOWN_LABELS: tuple[str, ...] = (
    "حالة المشروع",
    "تاريخ النشر",
    "الميزانية",
    "مدة التنفيذ",
    "المهارات",
    "تاريخ التسجيل",
    "معدل التوظيف",
    "المشاريع المفتوحة",
    "مشاريع قيد التنفيذ",
    "مشاريع منجزة",
    "التواصلات الجارية",
    "بدأ تنفيذه منذ",
    "تاريخ الصفقة",
    "موعد التسليم",
    "صاحب المشروع",
    "عدد العروض",
    "عدد المقترحات",
)

_OWNER_SECTION_LABEL = "صاحب المشروع"

#: Block-level tags whose boundaries become a line break (cs:171-174).
_BLOCK_LEVEL_TAGS = frozenset(
    {"br", "p", "div", "li", "tr", "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "ul", "ol"}
)

_TRIM_CHARS = "".join(LABEL_TRIM_CHARS)
_KNOWN_LABEL_NORMS = frozenset(normalize_label(label) for label in KNOWN_LABELS)

# cs:191-193 - whitespace collapsing passes applied after the DOM walk.
_HORIZONTAL_WS_RE = re.compile(r"[ \t]+")
_AROUND_NEWLINE_RE = re.compile(r"[ \t]*\r?\n[ \t]*")
_THREE_PLUS_NEWLINES_RE = re.compile(r"\n{3,}")


def _first(nodes: object) -> HtmlElement | None:
    """SelectSingleNode equivalent: first node of an xpath result, or None."""
    nodes_list = cast("list[HtmlElement]", nodes)
    return nodes_list[0] if nodes_list else None


def _elements(nodes: object) -> list[HtmlElement]:
    """Typed view over an xpath node-set result."""
    return cast("list[HtmlElement]", nodes)


def _is_element(node: HtmlElement) -> bool:
    """True for real elements (comments/PIs carry a non-str callable ``tag``)."""
    return isinstance(node.tag, str)


def _append_multiline_text(node: HtmlElement, parts: list[str]) -> None:
    """Depth-first walk appending OWN text with block-boundary newlines (cs:197-218).

    HAP exposes inter-tag text as text-node CHILDREN; lxml stores the same runs
    as ``node.text`` plus each child's ``tail``. Appending ``child.tail`` right
    after processing the child reproduces the HAP ChildNodes ordering exactly.
    Comments contribute nothing, as in C# (neither Text nor Element NodeType).
    """
    if node.text:
        parts.append(node.text)
    for child in node:
        if not _is_element(child):
            pass  # comment/PI: skipped entirely, mirroring the C# NodeType filter
        elif child.tag.lower() == "br":  # cs:205-208
            parts.append("\n")
        else:  # cs:209-216
            _append_multiline_text(child, parts)
            if child.tag.lower() in _BLOCK_LEVEL_TAGS:
                parts.append("\n")
        if child.tail:
            parts.append(child.tail)


def normalize_multiline(element: HtmlElement) -> str:
    """Render an element's text the way a browser visually would (cs:185-195).

    Unlike ``InnerText``/``text_content()`` (which concatenate every text run),
    this walks the DOM turning ``<br>``/paragraph/div/list-item boundaries into
    real line breaks, collapses horizontal whitespace within lines, and folds
    3+ consecutive newlines down to a single paragraph gap. The C# version
    DeEntitizes the joined text first; lxml already decoded entities at parse
    time, so that pass is deliberately omitted here (ENTITY RULE above).
    """
    parts: list[str] = []
    _append_multiline_text(element, parts)
    text = "".join(parts)
    text = _HORIZONTAL_WS_RE.sub(" ", text)
    text = _AROUND_NEWLINE_RE.sub("\n", text)
    text = _THREE_PLUS_NEWLINES_RE.sub("\n\n", text)
    return text.strip()


def _own_text(node: HtmlElement) -> str:
    """Text belonging DIRECTLY to this node, not nested children (cs:221-232).

    Direct HAP text children map to ``node.text`` plus every direct child's
    ``tail``; joined without separators, then trimmed.
    """
    chunks: list[str] = [node.text or ""]
    chunks.extend(child.tail or "" for child in node)
    return "".join(chunks).strip()


def _get_text(node: HtmlElement) -> str:
    """C# GetText (cs:234): Normalize(DeEntitize(InnerText)) -> text_content()."""
    return normalize(node.text_content())


def _by_exact_token(root: HtmlElement, tag: str, class_name: str) -> list[HtmlElement]:
    """Exact-token class match via the XPath ``concat`` trick (trap §4.1-10).

    Matches ``class="a skills b"`` but NOT ``class="skills-extra"``.
    """
    xpath = f"//{tag}[contains(concat(' ', normalize-space(@class), ' '), ' {class_name} ')]"
    return _elements(root.xpath(xpath))


def _by_token_contains(root: HtmlElement, tag: str, class_substring: str) -> list[HtmlElement]:
    """Per-TOKEN substring match, OrdinalIgnoreCase (C# SelectByClassContains cs:414-425).

    Each whitespace-separated class token is checked for containing the
    substring — ``meta-row-x`` matches ``meta-row``; the needle may also sit
    inside a single token only (never spanning tokens).
    """
    needle = class_substring.lower()
    matched: list[HtmlElement] = []
    for node in _elements(root.xpath(f".//{tag}")):
        cls = node.get("class", "")
        if any(needle in token.lower() for token in cls.split(" ") if token):
            matched.append(node)
    return matched


def _raw_class_contains(element: HtmlElement, *substrings: str) -> bool:
    """RAW substring check across the WHOLE class attribute (cs:403-405).

    Unlike :func:`_by_token_contains`, the needle may span token boundaries
    (``"oo ba"`` matches ``class="foo bar"``).
    """
    cls = element.get("class", "")
    lowered = cls.lower()
    return any(s.lower() in lowered for s in substrings)


def _next_sibling_element(node: HtmlElement) -> HtmlElement | None:
    """Next sibling ELEMENT, skipping text/comment nodes (cs:461-469)."""
    for sibling in node.itersiblings():
        if _is_element(sibling):
            return sibling
    return None


def _has_element_children(node: HtmlElement) -> bool:
    return any(_is_element(child) for child in node)


def _read_owner_stat_table(stats: HtmlElement, results: dict[str, str]) -> None:
    """Path A: stat <table> rows with EXACTLY 2 <td> cells (cs:269-281)."""
    for tr in _elements(stats.xpath(".//tr")):
        tds = _elements(tr.xpath("./td"))
        if len(tds) == 2:
            results[normalize_label(_get_text(tds[0]))] = _get_text(tds[1])


def _flex_pairs(owner_card: HtmlElement, results: dict[str, str]) -> None:
    """Path B primary heuristic (cs:287-306): ``justify-between`` rows with
    exactly 2 element children read as [label, value] pairs."""
    for row in _elements(owner_card.xpath(".//*[contains(@class, 'justify-between')]")):
        children = [child for child in row if _is_element(child)]
        if len(children) != 2:
            continue
        label = _get_text(children[0])
        value = _get_text(children[1])
        if label and value:
            results[normalize_label(label)] = value


def _fill_exact_label(results: dict[str, str], node: HtmlElement, norm: str) -> None:
    """Exact-match gap fill: the next sibling ELEMENT holds the value (cs:316-329)."""
    if norm in results:
        return
    following = _next_sibling_element(node)
    if following is not None:
        results[norm] = _get_text(following)


def _read_gap_fillers(owner_card: HtmlElement, results: dict[str, str]) -> None:
    """Path C heuristics run when the owner card has NO stat table (cs:283-356).

    The secondary label sweep ALWAYS runs after the flex pairs, filling only
    keys that are still missing (``ContainsKey`` guards).
    """
    _flex_pairs(owner_card, results)

    for node in _elements(owner_card.xpath(".//*")):
        text = _get_text(node)
        if not text:
            continue
        norm = normalize_label(text)
        if norm in _KNOWN_LABEL_NORMS:
            _fill_exact_label(results, node, norm)
        else:
            _repair_concatenated(results, text, norm)


def _repair_concatenated(results: dict[str, str], text: str, normalized_text: str) -> None:
    """Concatenated ``label+value`` repair for labels not yet resolved (cs:330-354)."""
    for label in KNOWN_LABELS:
        normalized_label = normalize_label(label)
        if normalized_label in results:
            continue
        if not (
            normalized_text.startswith(normalized_label)
            and len(normalized_text) > len(normalized_label)
        ):
            continue
        raw_text = text.strip()
        if label in raw_text:
            value = raw_text.replace(label, "").strip(_TRIM_CHARS)
            if value:
                results[normalized_label] = value


def extract_meta_fields(root: HtmlElement) -> dict[str, str]:
    """Structural (class/id based) meta extraction keyed by NormalizeLabel (cs:246-360).

    Panel: ``div#project-meta-panel`` else the first ``div`` whose class tokens
    contain ``meta-container``; rows carry ``meta-label``/``meta-value`` divs.
    Then the owner profile card's stats: Path A stat table (exactly-2-td rows),
    else the flex/grid + gap-filler heuristics (:func:`_read_gap_fillers`).
    Later sources overwrite earlier ones on key collision, as in C#.
    """
    results: dict[str, str] = {}

    card = _first(root.xpath("//div[@id='project-meta-panel']"))
    if card is None:
        card = _first(_by_token_contains(root, "div", "meta-container"))

    if card is not None:
        for row in _by_token_contains(card, "div", "meta-row"):
            label = _first(_by_token_contains(row, "div", "meta-label"))
            value = _first(_by_token_contains(row, "div", "meta-value"))
            if label is not None and value is not None:
                results[normalize_label(_get_text(label))] = _get_text(value)

    owner_card = find_owner_card(root)
    if owner_card is not None:
        stats = _first(_by_token_contains(owner_card, "table", "table"))
        if stats is not None:
            _read_owner_stat_table(stats, results)
        else:
            _read_gap_fillers(owner_card, results)

    return results


def _owner_card_semantic(root: HtmlElement) -> HtmlElement | None:
    """Cascade step 2 (cs:373-389): nearest div ancestor of a "صاحب المشروع"
    label having more than one descendant element."""
    for label in _find_label_elements(root, _OWNER_SECTION_LABEL):
        candidate = next(label.iterancestors("div"), None)
        if candidate is not None and len(_elements(candidate.xpath(".//*"))) > 1:
            return candidate
    return None


def _owner_card_from_details_area(root: HtmlElement) -> HtmlElement | None:
    """Cascade step 3 (cs:391-410): /u/-link inside the details area, up to a
    card/box-classed div ancestor (RAW whole-attribute substring match)."""
    details_area = _first(root.xpath("//*[@id='projectDetailsTab']"))
    if details_area is None:
        details_area = _first(root.xpath("//div[contains(@class, 'project-details')]"))
    if details_area is None:
        details_area = _first(root.xpath("//article"))
    if details_area is None:
        return None
    profile_link = _first(details_area.xpath(".//a[contains(@href, '/u/')]"))
    if profile_link is None:
        return None
    return next(
        (
            ancestor
            for ancestor in profile_link.iterancestors("div")
            if _raw_class_contains(ancestor, "card", "box")
        ),
        None,
    )


def find_owner_card(root: HtmlElement) -> HtmlElement | None:
    """Three-location cascade for the owner profile card (cs:366-411)."""
    # 1. Structural: dedicated profile-card classes (per-token substring).
    card = _first(_by_token_contains(root, "div", "profile_card"))
    if card is None:
        card = _first(_by_token_contains(root, "div", "profile-card"))
    if card is not None:
        return card

    # 2. Semantic: the "صاحب المشروع" section header's content-bearing ancestor.
    card = _owner_card_semantic(root)
    if card is not None:
        return card

    # 3. Last resort: a profile-looking card around the details-area /u/-link.
    return _owner_card_from_details_area(root)


def _find_label_elements(root: HtmlElement, label: str) -> list[HtmlElement]:
    """Elements whose own/leaf text matches ``label`` exactly (cs:435-458).

    Two shapes qualify, checked per descendant in document order: elements
    whose OwnText (direct text-node children, trimmed) label-normalizes equal
    to the target, PLUS leaf elements (no element children) whose FULL text
    equals it. Compared through NormalizeLabel on BOTH sides so trailing
    colons, diacritics and alef/ya/ta-marbuta spelling variants still match.
    """
    target = normalize_label(label)
    matches: list[HtmlElement] = []
    for element in _elements(root.xpath(".//*")):
        # Two qualifying shapes, C# if/elif (cs:444-451): OwnText match first,
        # then full-text match on leaf elements only. Short-circuit preserved.
        matched = normalize_label(_own_text(element)) == target or (
            normalize_label(_get_text(element)) == target and not _has_element_children(element)
        )
        if matched:
            matches.append(element)
    return matches


def _rung_next_sibling(label_element: HtmlElement) -> tuple[str, str] | None:
    """Rung 1 (cs:477-485): the next sibling element's text."""
    sibling = _next_sibling_element(label_element)
    if sibling is not None:
        text = _get_text(sibling)
        if text:
            return (text, "next_sibling_of_label")
    return None


def _rung_next_td(label_element: HtmlElement) -> tuple[str, str] | None:
    """Rung 2 (cs:487-502): when the label itself is a <td>, the next <td>,
    skipping non-td siblings."""
    if label_element.tag != "td":
        return None
    next_td = _next_sibling_element(label_element)
    while next_td is not None and next_td.tag != "td":
        next_td = _next_sibling_element(next_td)
    if next_td is not None:
        text = _get_text(next_td)
        if text:
            return (text, "next_td")
    return None


def _rung_parent_next_sibling(parent: HtmlElement) -> tuple[str, str] | None:
    """Rung 3 (cs:507-515): the parent's next sibling element."""
    parent_sibling = _next_sibling_element(parent)
    if parent_sibling is not None:
        text = _get_text(parent_sibling)
        if text:
            return (text, "parent_next_sibling")
    return None


def _rung_parent_minus_label(
    label_element: HtmlElement, parent: HtmlElement
) -> tuple[str, str] | None:
    """Rung 4 (cs:517-526): parent text minus its leading label text."""
    parent_text = _get_text(parent)
    label_text = _get_text(label_element)
    if parent_text.startswith(label_text) and parent_text != label_text:
        remainder = normalize(parent_text[len(label_text) :].lstrip(_TRIM_CHARS))
        if remainder:
            return (remainder, "parent_text_minus_label")
    return None


def _rung_grandparent_cell(parent: HtmlElement, label_text: str) -> tuple[str, str] | None:
    """Rung 5 (cs:528-549): first grandchild-of-row cell that isn't the label's
    own wrapper — catches value-before-label redesign layouts."""
    grandparent = parent.getparent()
    if grandparent is None:
        return None
    for child in grandparent:
        if not _is_element(child) or child is parent:
            continue
        text = _get_text(child)
        if text and normalize_label(text) != normalize_label(label_text):
            return (text, "grandparent_sibling_cell")
    return None


def _walk_to_value(label_element: HtmlElement) -> tuple[str | None, str | None]:
    """Identifier-blind adjacency ladder for a label's value (cs:475-553).

    Rungs, first non-empty wins: next_sibling_of_label -> next_td ->
    parent_next_sibling -> parent_text_minus_label -> grandparent_sibling_cell.
    Returns ``(value, method)``; ``(None, None)`` when every rung fails.
    """
    hit = (
        _rung_next_sibling(label_element)
        or _rung_next_td(label_element)
        or _try_parent_rungs(label_element)
    )
    return hit if hit is not None else (None, None)


def _try_parent_rungs(label_element: HtmlElement) -> tuple[str, str] | None:
    """Rungs 3-5 all operate through the label's parent (cs:504-550)."""
    parent = label_element.getparent()
    if parent is None:
        return None
    hit = _rung_parent_next_sibling(parent)
    if hit is not None:
        return hit
    hit = _rung_parent_minus_label(label_element, parent)
    if hit is not None:
        return hit
    label_text = _get_text(label_element)
    return _rung_grandparent_cell(parent, label_text)


def label_driven_extract(root: HtmlElement) -> dict[str, str]:
    """Resolves every known label via the WalkToValue ladder (cs:556-578).

    For each known label the FIRST label element yielding a non-empty value
    wins; results are keyed by NormalizeLabel.
    """
    results: dict[str, str] = {}
    for label in KNOWN_LABELS:
        elements = _find_label_elements(root, label)
        for element in elements:
            value, _method = _walk_to_value(element)
            if value:
                results[normalize_label(label)] = value
                break
    return results
