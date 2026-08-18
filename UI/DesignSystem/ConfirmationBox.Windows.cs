using Microsoft.Maui.Controls;
using WinUIWindow = Microsoft.UI.Xaml.Window;

namespace MostaqlK.UI.DesignSystem;

/// <summary>Windows half of <see cref="ConfirmationBox"/>.</summary>
public static partial class ConfirmationBox
{
    /// <summary>
    /// Resolves the app's native WinUI window from MAUI's window list (first open window's
    /// platform view). Used by ViewModel call sites that do not already hold a native handle
    /// (e.g. Settings destructive actions). Returns <c>null</c> if no window is ready yet.
    /// </summary>
    public static partial object? TryGetActiveNativeWindow()
    {
        var mauiWindow = Application.Current?.Windows.FirstOrDefault();
        return mauiWindow?.Handler?.PlatformView as WinUIWindow;
    }
}
