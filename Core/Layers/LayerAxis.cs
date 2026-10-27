namespace MostaqlK.Core.Layers;

/// <summary>
/// The sparse axes a layer key may bind. Platform is deliberately absent: it is a <em>total</em>
/// axis resolved at build time by filename convention (<c>X.Windows.cs</c> / <c>_X.Mobile.cs</c>),
/// and per the layered-isolability standard a total axis lives in names, never in runtime keys.
/// </summary>
public enum LayerAxis
{
    /// <summary>The active source/site module (mostaql, nafezly, …). Switches at runtime.</summary>
    Source,

    /// <summary>The release channel (stable, canary). Fixed per binary, named explicitly in every key.</summary>
    Channel,
}

/// <summary>Typed identifiers for <see cref="LayerAxis.Channel"/> values, so a typo is a compile error.</summary>
public static class ChannelLayer
{
    public const string Stable = "stable";
    public const string Canary = "canary";
}
