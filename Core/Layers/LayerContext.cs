using MostaqlK.Core.Platform;

namespace MostaqlK.Core.Layers;

/// <summary>
/// Reports the current values of the sparse axes as a resolution context. The channel is fixed per
/// binary (read from <see cref="ChannelIdentity"/>); the source is supplied by the caller because
/// it switches at runtime and Core must not reach into Services.
/// </summary>
public static class LayerContext
{
    /// <summary>The current channel value: <see cref="ChannelLayer.Stable"/> or <see cref="ChannelLayer.Canary"/>.</summary>
    public static string Channel => ChannelIdentity.IsCanary ? ChannelLayer.Canary : ChannelLayer.Stable;

    /// <summary>
    /// Builds the context for a resolution. <paramref name="sourceKey"/> is the active source's id
    /// key; pass null when the seam does not vary by source. The channel coordinate is always
    /// present, so a channel-less registration can still match.
    /// </summary>
    public static IReadOnlyDictionary<LayerAxis, string> For(string? sourceKey = null)
    {
        var context = new Dictionary<LayerAxis, string>
        {
            [LayerAxis.Channel] = Channel,
        };

        if (!string.IsNullOrEmpty(sourceKey))
        {
            context[LayerAxis.Source] = sourceKey;
        }

        return context;
    }
}
