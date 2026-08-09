namespace MostaqlK.Models;

/// <summary>
/// An attachment/asset linked to a project (e.g. a brief document or reference image),
/// captured when <c>include_assets</c> is enabled.
/// </summary>
public sealed class Asset
{
    public long AssetId { get; set; }

    public long ProjectId { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public string? LocalPath { get; set; }

    public long? SizeBytes { get; set; }

    /// <summary>File extension (e.g. "docx", "pdf"), resolved from data-file-type, an ext badge, or the filename suffix.</summary>
    public string? Extension { get; set; }

    /// <summary>The original, unresolved href from the source page (kept even when <see cref="Url"/> is nulled out due to <see cref="RequiresAuth"/>).</summary>
    public string? RawUrl { get; set; }

    /// <summary>True when the link actually points at a login/register wall instead of the real file (anonymous session).</summary>
    public bool RequiresAuth { get; set; }

    /// <summary>Human-readable size text as displayed on the page (e.g. "(15.99KB)"), when available.</summary>
    public string? SizeText { get; set; }
}
