namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A <see cref="Border"/> that always carries its own, per-instance <see cref="PressableEffect"/>.
/// </summary>
/// <remarks>
/// .NET MAUI documents that "behaviors that have state should not be shared between controls in a
/// Style in a ResourceDictionary" (see the Behaviors docs) — a stateful <see cref="Behavior{T}"/>
/// declared inside <c>&lt;Style.Behaviors&gt;</c> is a single shared instance, and every control
/// that consumes the style re-attaches to that SAME instance. For a style used once (e.g. a
/// single sidebar row) this is invisible, but for a style repeated many times — like
/// <c>OutlineChipButtonStyle</c> applied to a button inside every card of a
/// <see cref="CollectionView"/> — every repetition stomps over the previous one's
/// <c>_associatedView</c>, leaving all but the very last one with no working hover/press effect.
/// Subclassing <see cref="Border"/> and adding the behavior in the constructor (the same pattern
/// <see cref="PlatformComponents.AppCard"/> already uses) guarantees one dedicated
/// <see cref="PressableEffect"/> instance per <see cref="Border"/>, regardless of how many times a
/// <c>Style</c> targeting this type is reused.
/// </remarks>
public class PressableBorder : Border
{
    public PressableBorder()
    {
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = true });
    }
}
