namespace MostaqlK.Core.Diagnostics;

/// <summary>
/// Rules for Win2D / DirectX canvas invalidation while the window is in the tray.
/// Isolated so tests can prove the 2-hour DXGI path is gated without a GPU soak.
/// </summary>
public static class BackgroundCanvasPolicy
{
    /// <summary>
    /// True when a new animation loop may start.
    /// </summary>
    public static bool AllowWake(bool isFrozen, bool isTicking, bool hasHandler, bool isInBackground) =>
        !isFrozen && !isTicking && hasHandler && !isInBackground;

    /// <summary>
    /// True when the current frame may invalidate the swapchain.
    /// </summary>
    public static bool AllowFrame(bool isInBackground) => !isInBackground;
}
