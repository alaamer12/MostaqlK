using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// A <see cref="Switch"/> that always carries its own, per-instance <see cref="PressableEffect"/>.
/// </summary>
public class PressableSwitch : Switch
{
    public PressableSwitch()
    {
        Behaviors.Add(new PressableEffect { ApplyHoverHighlight = true });
    }
}
