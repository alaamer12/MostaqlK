using MostaqlK.Core.Sources;

namespace MostaqlK.UI.DesignSystem.BrandTokens;

/// <summary>
/// The live palette of whichever source currently owns the app's identity. Consumers read
/// <see cref="Current"/> once and subscribe to <see cref="TokensSwapped"/> while they are
/// loaded (subscribe on Loaded, unsubscribe on Unloaded - the event-lifetime rule), so a
/// component can never outlive its subscription into the next page's identity.
/// </summary>
public interface IBrandTokenProvider
{
    public SourceId CurrentSource { get; }

    public BrandTokenSet Current { get; }

    /// <summary>Raised after <see cref="Current"/> changes; handlers re-read the set.</summary>
    public event Action<BrandTokenSet>? TokensSwapped;

    /// <summary>
    /// Points the palette at another source and fires <see cref="TokensSwapped"/>.
    /// No-op when already on that source.
    /// </summary>
    public void SwapTo(SourceId source);

    /// <summary>
    /// The current source's palette projected for one theme: the four base fields carry that
    /// theme's variants (dark maps the <c>*Dark</c> fields onto the base names). Link fields are
    /// theme-explicit by name and pass through unchanged. Consumers bind through this instead of
    /// <see cref="Current"/> wherever theme-variant colors are wanted.
    /// </summary>
    public BrandTokenSet CurrentFor(AppTheme theme);
}
