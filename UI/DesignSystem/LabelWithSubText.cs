namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Compound label exposing a SubText slot; the canonical binding target for
/// DomainError.ExternalMessage (Text) and FixMessage (SubText) (see system-components.md, section 13.3
/// and 13.4). The sub-text row must be hidden — not just empty — when SubText is null.
/// TODO: build the internal VerticalStackLayout of two Labels and toggle the sub-text row's IsVisible.
/// </summary>
public class LabelWithSubText : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(LabelWithSubText), null);

    public static readonly BindableProperty SubTextProperty =
        BindableProperty.Create(nameof(SubText), typeof(string), typeof(LabelWithSubText), null);

    /// <summary>Primary message, typically bound to DomainError.ExternalMessage.</summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>Secondary/fix message, typically bound to DomainError.FixMessage. Hides its row when null.</summary>
    public string? SubText
    {
        get => (string?)GetValue(SubTextProperty);
        set => SetValue(SubTextProperty, value);
    }

    public LabelWithSubText()
    {
        // TODO: compose the primary Label + sub-text Label, wiring IsVisible to SubText != null.
    }
}
