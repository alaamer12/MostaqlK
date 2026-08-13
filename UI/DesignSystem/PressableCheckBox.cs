using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A <see cref="CheckBox"/> that always carries its own, per-instance <see cref="PressableEffect"/>.
/// </summary>
public class PressableCheckBox : CheckBox
{
    public PressableCheckBox()
    {
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = true });
    }
}
