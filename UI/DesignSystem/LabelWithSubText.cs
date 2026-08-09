namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Compound label exposing a SubText slot; the canonical binding target for
/// DomainError.ExternalMessage (Text) and FixMessage (SubText) (see system-components.md, section 13.3
/// and 13.4). The sub-text row is hidden — not just empty — when SubText is null.
/// </summary>
public class LabelWithSubText : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(LabelWithSubText), null, propertyChanged: OnTextChanged);

    public static readonly BindableProperty SubTextProperty =
        BindableProperty.Create(nameof(SubText), typeof(string), typeof(LabelWithSubText), null, propertyChanged: OnSubTextChanged);

    private readonly Label _textLabel;
    private readonly Label _subTextLabel;

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
        _textLabel = new Label { FontSize = 15, HorizontalTextAlignment = TextAlignment.Center };
        _subTextLabel = new Label
        {
            FontSize = 12,
            TextColor = Colors.Gray,
            HorizontalTextAlignment = TextAlignment.Center,
            IsVisible = false,
        };

        Content = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { _textLabel, _subTextLabel },
        };
    }

    private static void OnTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((LabelWithSubText)bindable)._textLabel.Text = (string?)newValue;
    }

    private static void OnSubTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var self = (LabelWithSubText)bindable;
        self._subTextLabel.Text = (string?)newValue;
        self._subTextLabel.IsVisible = !string.IsNullOrEmpty((string?)newValue);
    }
}
