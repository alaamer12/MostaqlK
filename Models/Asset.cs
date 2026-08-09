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
}
