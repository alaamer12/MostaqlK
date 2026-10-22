namespace MostaqlK.Core.Parsing;

/// <summary>
/// Per-source DOM selector vocabulary consumed by <see cref="HtmlStructureEngine"/> and the
/// module's thin parsers — every class/id/attribute/path fragment a site's markup happens to
/// use today, as immutable data. The engines interpolate these into their XPath/attribute
/// lookups; a redesign that only renames selectors is a data edit in one place, and a second
/// source supplies its own map to reuse the same engines. No behavior lives here.
/// </summary>
/// <param name="MetaPanelId">id of the detail page's meta panel wrapper (was the inline
/// project-meta-panel id in ExtractMetaFields).</param>
/// <param name="MetaContainerClassSubstring">fallback class substring for the meta panel
/// container (was the inline meta-container lookup).</param>
/// <param name="MetaRowClassSubstring">class substring of one meta panel row.</param>
/// <param name="MetaLabelClassSubstring">class substring of a meta row's label cell.</param>
/// <param name="MetaValueClassSubstring">class substring of a meta row's value cell.</param>
/// <param name="OwnerStatsTableClassSubstring">class substring of the owner card's stats
/// table (was the inline table-class lookup).</param>
/// <param name="OwnerStatRowClassSubstring">class substring marking redesign label/value
/// rows inside the owner card (was the inline justify-between scan).</param>
/// <param name="OwnerCardClassSubstrings">class substrings that identify the owner profile
/// card directly (was the inline profile_card / profile-card pair).</param>
/// <param name="OwnerCardWrapperClassSubstrings">class substrings of the div that wraps a
/// profile link into a card (was the inline card / box pair).</param>
/// <param name="OwnerNameClassSubstring">class substring of the owner name heading.</param>
/// <param name="OwnerUserIdAttribute">data attribute optionally carrying the numeric owner
/// id (was the inline data-user-id lookup).</param>
/// <param name="DetailsTabId">id of the project-details content tab (was the inline
/// projectDetailsTab id repeated across three parsers).</param>
/// <param name="ProjectDetailsClassSubstring">class substring fallback for the details area.</param>
/// <param name="DescriptionClassSubstring">class substring of the brief/description block
/// (was the inline text-wrapper-div lookup).</param>
/// <param name="SkillsListClassSubstring">class substring of the skills list.</param>
/// <param name="SkillHrefSubstrings">href fragments identifying a skill taxonomy link
/// (identifier-blind skills fallback).</param>
/// <param name="ProfileUrlPathSegment">href fragment marking a user-profile link.</param>
/// <param name="ProfileUrlSegmentToken">the path token (between slashes) that precedes the
/// username in a profile URL.</param>
/// <param name="BidItemAttribute">attribute present on every real bid row (deterministic
/// proposal count).</param>
/// <param name="AttachmentItemClassSubstring">class substring of an attachment list item.</param>
/// <param name="AttachmentFileTypeAttribute">attribute naming a link's file type.</param>
/// <param name="AttachmentBadgeClassSubstring">class substring of the extension badge.</param>
/// <param name="ListingRowClass">exact class of a listing table row.</param>
/// <param name="ListingItemClass">exact class of a redesign listing card.</param>
/// <param name="ListingMetaClass">class of a listing row's meta list.</param>
/// <param name="ListingBriefClass">class substring of a listing row's brief paragraph.</param>
/// <param name="ProposalIconSpanClass">span class marking the proposals meta item.</param>
/// <param name="ProposalIconElementClass">icon-element class marking the proposals meta item.</param>
/// <param name="ProjectHrefPathSegment">href fragment marking a project link (redesign-blind
/// listing fallback).</param>
/// <param name="ProjectIdUrlPattern">regex extracting the numeric project id from a project URL.</param>
public sealed record SelectorMap(
    string MetaPanelId,
    string MetaContainerClassSubstring,
    string MetaRowClassSubstring,
    string MetaLabelClassSubstring,
    string MetaValueClassSubstring,
    string OwnerStatsTableClassSubstring,
    string OwnerStatRowClassSubstring,
    IReadOnlyList<string> OwnerCardClassSubstrings,
    IReadOnlyList<string> OwnerCardWrapperClassSubstrings,
    string OwnerNameClassSubstring,
    string OwnerUserIdAttribute,
    string DetailsTabId,
    string ProjectDetailsClassSubstring,
    string DescriptionClassSubstring,
    string SkillsListClassSubstring,
    IReadOnlyList<string> SkillHrefSubstrings,
    string ProfileUrlPathSegment,
    string ProfileUrlSegmentToken,
    string BidItemAttribute,
    string AttachmentItemClassSubstring,
    string AttachmentFileTypeAttribute,
    string AttachmentBadgeClassSubstring,
    string ListingRowClass,
    string ListingItemClass,
    string ListingMetaClass,
    string ListingBriefClass,
    string ProposalIconSpanClass,
    string ProposalIconElementClass,
    string ProjectHrefPathSegment,
    string ProjectIdUrlPattern)
{
    /// <summary>
    /// An all-empty selector map for non-parsing consumers (test stubs, placeholder
    /// descriptors). Never used by a live source — real sources ship their selectors as data.
    /// </summary>
    public static SelectorMap Empty { get; } = new(
        MetaPanelId: string.Empty,
        MetaContainerClassSubstring: string.Empty,
        MetaRowClassSubstring: string.Empty,
        MetaLabelClassSubstring: string.Empty,
        MetaValueClassSubstring: string.Empty,
        OwnerStatsTableClassSubstring: string.Empty,
        OwnerStatRowClassSubstring: string.Empty,
        OwnerCardClassSubstrings: [],
        OwnerCardWrapperClassSubstrings: [],
        OwnerNameClassSubstring: string.Empty,
        OwnerUserIdAttribute: string.Empty,
        DetailsTabId: string.Empty,
        ProjectDetailsClassSubstring: string.Empty,
        DescriptionClassSubstring: string.Empty,
        SkillsListClassSubstring: string.Empty,
        SkillHrefSubstrings: [],
        ProfileUrlPathSegment: string.Empty,
        ProfileUrlSegmentToken: string.Empty,
        BidItemAttribute: string.Empty,
        AttachmentItemClassSubstring: string.Empty,
        AttachmentFileTypeAttribute: string.Empty,
        AttachmentBadgeClassSubstring: string.Empty,
        ListingRowClass: string.Empty,
        ListingItemClass: string.Empty,
        ListingMetaClass: string.Empty,
        ListingBriefClass: string.Empty,
        ProposalIconSpanClass: string.Empty,
        ProposalIconElementClass: string.Empty,
        ProjectHrefPathSegment: string.Empty,
        ProjectIdUrlPattern: string.Empty);
}
