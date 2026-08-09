namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Windows-only tweaks for <see cref="SearchInputField"/> (V1 scope). The search icon and clear
/// button are composed in XAML call sites today (leading/trailing content wrapped around the
/// entry) rather than a custom handler; this partial exists per the base-component-first
/// convention so a future native icon-slot handler mapping has a home without renaming the type.
/// </summary>
public partial class SearchInputField
{
}
