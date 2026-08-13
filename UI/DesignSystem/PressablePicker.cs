using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A <see cref="Picker"/> that always carries its own, per-instance <see cref="PressableEffect"/>.
/// </summary>
public class PressablePicker : Picker
{
    public PressablePicker()
    {
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = true });
    }
}
