using MostaqlK.Core.Sources;

namespace MostaqlK.UI.DesignSystem.BrandTokens;

/// <summary>
/// Default <see cref="IBrandTokenProvider"/>: resolves each source's palette through the
/// <see cref="SourceRegistry"/>, so the hexes stay where the rest of a source's facts live
/// (its module descriptor) and this provider only owns *which* source is current.
/// </summary>
public sealed class SourceBrandTokenProvider : IBrandTokenProvider
{
    private readonly SourceRegistry _registry;

    public SourceBrandTokenProvider(SourceRegistry registry)
    {
        _registry = registry;
        CurrentSource = SourceId.Default;
        Current = _registry.Get(CurrentSource).Brands;
    }

    public SourceId CurrentSource { get; private set; }

    public BrandTokenSet Current { get; private set; }

    public event Action<BrandTokenSet>? TokensSwapped;

    public void SwapTo(SourceId source)
    {
        if (source == CurrentSource)
        {
            return;
        }

        CurrentSource = source;
        Current = _registry.Get(source).Brands;
        TokensSwapped?.Invoke(Current);
    }
}
