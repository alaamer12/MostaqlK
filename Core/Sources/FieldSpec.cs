namespace MostaqlK.Core.Sources;

/// <summary>
/// One entry of a source's <see cref="ISourceDescriptor.SchemaMap"/>: which parsed field the
/// UI renders, inside which section, and in what order. Pure data — each source seeds the
/// order its details/card UI assumes today, so schema-driven rendering (v3 UI) reproduces the
/// current layout without a single <c>if (source)</c> branch. (backend-ui-contract §1/§8.1)
/// </summary>
/// <param name="Key">Stable field key (e.g. "budget", "project_status", "owner_name").</param>
/// <param name="Section">Section id the field belongs to (e.g. "summary", "project", "owner").</param>
/// <param name="Order">Render position within the source, lower first.</param>
public sealed record FieldSpec(string Key, string Section, int Order);
