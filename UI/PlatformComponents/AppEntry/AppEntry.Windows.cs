using Microsoft.Maui.Handlers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WinThickness = Microsoft.UI.Xaml.Thickness;
using WinSolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows-only tweaks for <see cref="AppEntry"/> (V1 scope).
/// </summary>
public partial class AppEntry
{
    /// <summary>
    /// Strips the native WinUI <see cref="TextBox"/> chrome. MAUI's Entry maps to a TextBox whose
    /// template draws its own 1px border (and a bottom focus rule), so an AppEntry placed inside a
    /// design-system Border rendered a second, square-cornered border inside the rounded one —
    /// most visible in dark mode. Every AppEntry call site either composes its own Border
    /// (SearchInputField container, cookie editor) or is an inline editor that must stay
    /// chrome-less, so the unit owns the border, never the native control.
    /// </summary>
    static AppEntry()
    {
        EntryHandler.Mapper.AppendToMapping("AppEntryNoNativeChrome", (handler, view) =>
        {
            if (view is not AppEntry || handler.PlatformView is not TextBox textBox)
            {
                return;
            }

            textBox.BorderThickness = new WinThickness(0);
            textBox.Padding = new WinThickness(0);
            textBox.Background = new WinSolidColorBrush(Microsoft.UI.Colors.Transparent);

            // The template reads these theme resources on every visual state; the control
            // property alone is not enough to remove the focused bottom rule.
            textBox.Resources["TextControlBorderThemeThickness"] = new WinThickness(0);
            textBox.Resources["TextControlBorderThemeThicknessFocused"] = new WinThickness(0);
        });
    }
}
