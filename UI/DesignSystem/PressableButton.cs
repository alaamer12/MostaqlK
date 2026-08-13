using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A <see cref="Button"/> that always carries its own, per-instance <see cref="PressableEffect"/>.
/// </summary>
public class PressableButton : Button
{
    public PressableButton()
    {
        // For standard buttons, we usually don't want the hover background highlight 
        // because they already have their own background color, but we want the scale/opacity 
        // and the hand cursor.
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = false });
    }
}
