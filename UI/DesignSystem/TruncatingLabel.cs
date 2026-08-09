namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Smart-truncation label with an optional character-count cap (see system-components.md, section 13.3).
/// Wraps MAUI's TailTruncation and appends a U+2026 ellipsis when <see cref="MaxChars"/> is exceeded.
/// TODO: override Text application to enforce MaxChars + ellipsis instead of relying on LineBreakMode alone.
/// </summary>
public class TruncatingLabel : Label
{
    public static readonly BindableProperty MaxCharsProperty =
        BindableProperty.Create(nameof(MaxChars), typeof(int?), typeof(TruncatingLabel), null);

    /// <summary>Maximum number of characters to display before truncating with an ellipsis. Null = no cap.</summary>
    public int? MaxChars
    {
        get => (int?)GetValue(MaxCharsProperty);
        set => SetValue(MaxCharsProperty, value);
    }

    public TruncatingLabel()
    {
        LineBreakMode = LineBreakMode.TailTruncation;
        // TODO: apply MaxChars-based truncation when Text/MaxChars change.
    }
}
