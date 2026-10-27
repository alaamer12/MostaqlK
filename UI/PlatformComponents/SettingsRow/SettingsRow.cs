using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Layouts;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.SettingsRow;

/// <summary>
/// The settings plugin's single renderer: builds one row from its
/// <see cref="SettingsDescriptor"/> alone, switching on <see cref="SettingsShape"/> and composing
/// existing units (AppToggle, AppEntry, Picker, Slider, chip buttons, SourceLinkButton-style
/// links). Like <c>FilterModal</c> it never branches on a source id — a source appears here only
/// through the descriptors its contribution added or overrode. Adding a shape means one enum
/// member plus one branch; adding a setting means zero code in this file.
/// </summary>
public partial class SettingsRow : ContentView
{
    private const float LabelSize = 13f;
    private const float HintSize = 11f;

    /// <summary>
    /// The mockup's rows inherit CSS <c>line-height: 1.5</c>; MAUI's default label metrics are
    /// tighter, which compacted every row by ~1px and left an 8px drift by the bottom of the
    /// gallery — the diff heatmap's doubled hairlines. Matching the multiplier is what makes the
    /// two frames share a vertical rhythm.
    /// </summary>
    private const double MockupLineHeight = 1.5;

    private readonly Grid _root;
    private readonly VerticalStackLayout _labelCell;
    private readonly HorizontalStackLayout _valueCell;
    private readonly Label _titleLabel;
    private readonly Label _hintLabel;
    private readonly Label _errorLabel;

    private SettingsRowViewModel? _row;
    private Action? _repaint;
    private View? _spanChild;

    public SettingsRow()
    {
        _titleLabel = new Label
        {
            FontFamily = "TajawalBold",
            FontSize = LabelSize,
            LineHeight = MockupLineHeight,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        _hintLabel = new Label
        {
            FontFamily = "Tajawal",
            FontSize = HintSize,
            LineHeight = MockupLineHeight,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 2, 0, 0),
            IsVisible = false,
        };
        _errorLabel = new Label
        {
            FontFamily = "TajawalBold",
            FontSize = HintSize,
            LineHeight = MockupLineHeight,
            LineBreakMode = LineBreakMode.WordWrap,
            Margin = new Thickness(0, 3, 0, 0),
            IsVisible = false,
        };

        _labelCell = new VerticalStackLayout { Spacing = 0, VerticalOptions = LayoutOptions.Center };
        _labelCell.Children.Add(_titleLabel);
        _labelCell.Children.Add(_hintLabel);
        _labelCell.Children.Add(_errorLabel);

        _valueCell = new HorizontalStackLayout
        {
            Spacing = 10,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
        };

        _root = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
            },
            ColumnSpacing = 16,
            Padding = new Thickness(0, 11),
        };
        Grid.SetColumn(_labelCell, 0);
        Grid.SetColumn(_valueCell, 1);
        _root.Children.Add(_labelCell);
        _root.Children.Add(_valueCell);

        Content = _root;
    }

    /// <summary>Rebuilds the row from the descriptor bound to <paramref name="row"/>.</summary>
    public void SetRow(SettingsRowViewModel row)
    {
        if (_row is not null && _repaint is not null)
        {
            _row.ErrorChanged -= _repaint;
        }

        // A rebuild must be idempotent: the value cell clears below, and the two shapes that
        // reach outside it (banner spans the grid, cookie inserts a status line) undo their own
        // edit here rather than leaving a stale artifact behind.
        if (_spanChild is not null)
        {
            _root.Children.Remove(_spanChild);
            _spanChild = null;
        }
        _labelCell.IsVisible = true;
        while (_labelCell.Children.Count > 3)
        {
            _labelCell.Children.RemoveAt(1);
        }

        _row = row;
        var d = row.Descriptor;

        _titleLabel.Text = d.LabelAr;
        _hintLabel.Text = d.HintAr ?? string.Empty;
        _hintLabel.IsVisible = !string.IsNullOrEmpty(d.HintAr);

        _valueCell.Children.Clear();
        switch (d.Shape)
        {
            case SettingsShape.Toggle: BuildToggle(row); break;
            case SettingsShape.NumericBounded: BuildNumeric(row); break;
            case SettingsShape.TextEntry: BuildText(row); break;
            case SettingsShape.EnumPicker: BuildEnumPicker(row); break;
            case SettingsShape.MultiChoiceChips: BuildChips(row); break;
            case SettingsShape.SegmentedButtons: BuildSegmented(row); break;
            case SettingsShape.Slider: BuildSlider(row); break;
            case SettingsShape.ReadOnlyStat: BuildStat(row); break;
            case SettingsShape.ActionButton: BuildAction(row); break;
            case SettingsShape.LinkOut: BuildLink(row); break;
            case SettingsShape.InfoBanner: BuildInfoBanner(row); break;
            case SettingsShape.SectionHeader: BuildSectionHeader(row); break;
            case SettingsShape.FilePicker: BuildFilePicker(row); break;
            case SettingsShape.CookieEditor: BuildCookieEditor(row); break;
            default: throw new ArgumentOutOfRangeException(nameof(row), d.Shape, $"Unhandled {nameof(SettingsShape)} '{d.Shape}' — the renderer must cover every catalog shape (see settings-shapes.html).");
        }

        _repaint = RepaintError;
        row.ErrorChanged += _repaint;
        ApplyTheme();
        RepaintError();
    }

    // ------------------------------------------------------------------ shapes

    private void BuildToggle(SettingsRowViewModel row)
    {
        var toggle = new AppToggle
        {
            IsVisible = true,
            // .toggle[aria-checked="true"] { background: var(--brand) } — the track is brand, not
            // the OS accent, so both channels and both sources read their own identity here.
            OnColor = BrandPalette.Brand,
            ThumbColor = Colors.White,
            HorizontalOptions = LayoutOptions.End,
        };
        SetAutomationId(toggle, row.Descriptor.AutomationId);
        toggle.IsToggled = row.Read() as bool? ?? false;
        toggle.Toggled += (_, e) => row.Write(e.Value);
        _valueCell.Children.Add(toggle);
    }

    private void BuildNumeric(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var entry = NewEntry(row, keyboard: Keyboard.Numeric);
        // A bounded number is a technical value: .num-input.ltr isolates it so the field shows
        // 60 rather than the RTL context's Arabic-Indic form, same as every URL/params field.
        entry.FlowDirection = FlowDirection.LeftToRight;
        entry.HorizontalTextAlignment = TextAlignment.Center;
        entry.Text = row.Read()?.ToString() ?? d.DefaultLiteral ?? string.Empty;
        entry.TextChanged += (_, e) => row.Write(ParseInt(e.NewTextValue));
        _valueCell.Children.Add(NumBox(entry, width: 92));
        if (d.UnitAr is not null)
        {
            _valueCell.Children.Add(UnitLabel(d.UnitAr));
        }
    }

    private void BuildText(SettingsRowViewModel row)
    {
        var entry = NewEntry(row, keyboard: Keyboard.Plain);
        entry.Text = row.Read()?.ToString() ?? string.Empty;
        entry.TextChanged += (_, e) => row.Write(e.NewTextValue);
        _valueCell.Children.Add(NumBox(entry, width: 220));
    }

    /// <summary>
    /// Mockup anatomy: the editable value sits in a 1px hairline box of a FIXED total width
    /// (<c>.num-input</c> is 92px / <c>input</c> 220px including padding and border — Tailwind's
    /// border-box sizing), so the width is set on the box and the entry fills it.
    /// </summary>
    private Border NumBox(Entry entry, double width)
    {
        entry.HorizontalOptions = LayoutOptions.Fill;
        entry.VerticalOptions = LayoutOptions.Center;
        entry.VerticalTextAlignment = TextAlignment.Center;
        var box = Boxed(entry, radius: 8, padding: new Thickness(8, 0));
        box.WidthRequest = width;
        // .num-input is 29px tall (5px padding + 17px line + 2×1px border, border-box). A taller
        // box would push every row of the section down by the same amount, which is exactly the
        // drift the parity heatmap sees as doubled hairlines.
        box.HeightRequest = 29;
        box.Stroke = _row?.HasError == true ? Danger : Line;
        box.BackgroundColor = IsDark ? SurfaceDark : SurfaceLight;
        return box;
    }

    private void BuildEnumPicker(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var picker = new Picker
        {
            Title = d.PlaceholderAr,
            FontFamily = "TajawalBold",
            FontSize = 13,
            WidthRequest = 150,
            HorizontalOptions = LayoutOptions.End,
        };
        SetAutomationId(picker, d.AutomationId);
        var options = d.Options ?? [];
        foreach (var o in options)
        {
            picker.Items.Add(o.LabelAr);
        }

        var current = row.Read()?.ToString();
        picker.SelectedIndex = options.ToList().FindIndex(o => o.Value == current);
        picker.SelectedIndexChanged += (_, _) =>
        {
            if (picker.SelectedIndex >= 0 && picker.SelectedIndex < options.Count)
            {
                row.Write(options[picker.SelectedIndex].Value);
            }
        };
        picker.TextColor = Ink1;
        // .pick in the mockup is a bordered control; MAUI's Picker draws no frame of its own.
        _valueCell.Children.Add(Boxed(picker, radius: 8, padding: new Thickness(10, 5)));
    }

    private void BuildChips(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var host = new FlexLayout { Wrap = FlexWrap.Wrap, Direction = FlexDirection.Row, JustifyContent = FlexJustify.End };
        var selected = (row.Read() as IEnumerable<string>)?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        foreach (var option in d.Options ?? [])
        {
            var chip = new Button
            {
                Text = option.LabelAr,
                FontFamily = "TajawalBold",
                FontSize = 12,
                Padding = new Thickness(13, 5),
                BorderWidth = 1,
                CornerRadius = 999,
                MinimumHeightRequest = 0,
                AutomationId = $"{d.AutomationId}_Chip_{option.Value}",
            };
            chip.Clicked += (_, _) =>
            {
                if (!selected.Remove(option.Value)) selected.Add(option.Value);
                row.Write(selected.ToList());
                PaintChip(chip, selected.Contains(option.Value));
            };
            PaintChip(chip, selected.Contains(option.Value));
            host.Children.Add(chip);
        }
        _valueCell.Children.Add(host);
    }

    private void BuildSegmented(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var seg = new Border
        {
            StrokeThickness = 1,
            Stroke = Line,
            BackgroundColor = IsDark ? Surface2Dark : Surface2Light,
            Padding = new Thickness(2),
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new HorizontalStackLayout { Spacing = 2 },
        };
        var inner = (HorizontalStackLayout)seg.Content;
        var options = d.Options ?? [];
        var current = row.Read()?.ToString();
        foreach (var option in options)
        {
            var selectedNow = option.Value == current;
            var b = new Button
            {
                Text = option.LabelAr,
                FontFamily = "TajawalBold",
                FontSize = 12,
                Padding = new Thickness(12, 5),
                CornerRadius = 8,
                BorderWidth = 0,
                MinimumHeightRequest = 0,
                BackgroundColor = selectedNow ? (IsDark ? SurfaceDark : SurfaceLight) : Colors.Transparent,
                TextColor = selectedNow ? Ink1 : Ink3,
                AutomationId = $"{d.AutomationId}_{option.Value}",
            };
            b.Clicked += (_, _) => { row.Write(option.Value); SetRow(row); };
            inner.Children.Add(b);
        }
        _valueCell.Children.Add(seg);
    }

    private void BuildSlider(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var min = d.Min ?? 0;
        var max = d.Max ?? 100;
        var value = ToInt(row.Read(), min);
        var slider = new Slider(min, max, value) { WidthRequest = 160, VerticalOptions = LayoutOptions.Center };
        SetAutomationId(slider, d.AutomationId);
        var val = new Label
        {
            Text = value.ToString(),
            FontFamily = "TajawalBold",
            FontSize = 13,
            TextColor = Ink1,
            FlowDirection = FlowDirection.LeftToRight,
            VerticalOptions = LayoutOptions.Center,
        };
        slider.ValueChanged += (_, e) => { val.Text = ((int)e.NewValue).ToString(); row.Write((int)e.NewValue); };
        _valueCell.Children.Add(slider);
        _valueCell.Children.Add(val);
    }

    private void BuildStat(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var value = new Label
        {
            Text = row.ValueText ?? string.Empty,
            FontFamily = "TajawalBold",
            FontSize = LabelSize,
            TextColor = Ink1,
            VerticalOptions = LayoutOptions.Center,
        };
        row.ValueTextChanged += () => value.Text = row.ValueText ?? string.Empty;
        _valueCell.Children.Add(value);
        if (d.SecondaryLabelAr is not null || d.CommandName is not null)
        {
            _valueCell.Children.Add(LinkAction(d.SecondaryLabelAr ?? "عرض", d, row, "View"));
        }
    }

    private void BuildAction(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        _valueCell.Children.Add(PrimaryButton(d.ActionLabelAr ?? d.LabelAr, d, row, "Invoke"));
        if (d.SecondaryLabelAr is not null)
        {
            _valueCell.Children.Add(GhostButton(d.SecondaryLabelAr, d, row, "Secondary"));
        }
    }

    private void BuildLink(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        // The external-link glyph belongs to the shape, not to the copy.
        _valueCell.Children.Add(LinkAction((d.ActionLabelAr ?? d.LabelAr) + "  ↗", d, row, "Open"));
    }

    private void BuildInfoBanner(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var banner = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = BrandPalette.BrandSoft,
            Padding = new Thickness(14, 11),
            HorizontalOptions = LayoutOptions.Fill,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new HorizontalStackLayout
            {
                Spacing = 10,
                Children =
                {
                    new Label { Text = "ⓘ", FontFamily = "TajawalBold", FontSize = 13, TextColor = BrandPalette.BrandInk, VerticalOptions = LayoutOptions.Start },
                    new Label
                    {
                        Text = d.HintAr ?? d.LabelAr,
                        FontFamily = "Tajawal",
                        FontSize = 12,
                        TextColor = Ink2,
                        LineBreakMode = LineBreakMode.WordWrap,
                        HorizontalOptions = LayoutOptions.Fill,
                    },
                },
            },
        };
        // A banner is the whole row: it spans both columns instead of hugging the value end.
        _labelCell.IsVisible = false;
        Grid.SetColumn(banner, 0);
        Grid.SetColumnSpan(banner, 2);
        _root.Children.Add(banner);
        _spanChild = banner;
    }

    private void BuildSectionHeader(SettingsRowViewModel row)
    {
        _titleLabel.FontSize = 16;
        var icon = new Label { Text = "⚙", FontSize = 14, VerticalOptions = LayoutOptions.Center };
        _valueCell.Children.Add(icon);
    }

    private void BuildFilePicker(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var name = row.ValueText;
        if (string.IsNullOrEmpty(name))
        {
            _valueCell.Children.Add(GhostButton(d.ActionLabelAr ?? "اختيار ملف…", d, row, "Browse"));
            return;
        }

        _valueCell.Children.Add(new Label
        {
            Text = name,
            FontFamily = "Tajawal",
            FontSize = 13,
            TextColor = Ink2,
            FlowDirection = FlowDirection.LeftToRight,
            VerticalOptions = LayoutOptions.Center,
        });
        _valueCell.Children.Add(LinkAction(d.SecondaryLabelAr ?? "تغيير", d, row, "Change"));
    }

    private void BuildCookieEditor(SettingsRowViewModel row)
    {
        var d = row.Descriptor;
        var status = new Label
        {
            Text = row.ValueText ?? string.Empty,
            FontFamily = "TajawalBold",
            FontSize = HintSize,
            LineBreakMode = LineBreakMode.WordWrap,
            // .cookie-ok / .cookie-busy: the composite's status line carries its own tone.
            TextColor = d.HintTone switch
            {
                SettingsHintTone.Success => IsDark ? PositiveDark : PositiveLight,
                SettingsHintTone.Brand => BrandPalette.BrandInk,
                _ => Ink3,
            },
            IsVisible = !string.IsNullOrEmpty(row.ValueText),
        };
        row.ValueTextChanged += () => status.Text = row.ValueText ?? string.Empty;
        // The status line belongs under the label, not beside the buttons.
        _labelCell.Children.Insert(1, status);

        // A verification in flight is the one state that asks for the filled button.
        _valueCell.Children.Add(d.HintTone == SettingsHintTone.Brand
            ? PrimaryButton(d.ActionLabelAr ?? "رفع ملف", d, row, "Upload")
            : GhostButton(d.ActionLabelAr ?? "رفع ملف", d, row, "Upload"));
        if (d.SecondaryLabelAr is not null)
        {
            _valueCell.Children.Add(LinkAction(d.SecondaryLabelAr, d, row, "Unlink"));
        }
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Filled brand button — <c>.btn-primary</c>.</summary>
    private Button PrimaryButton(string text, SettingsDescriptor d, SettingsRowViewModel row, string idSuffix)
    {
        var b = new Button
        {
            Text = text,
            FontFamily = "TajawalBold",
            FontSize = 13,
            TextColor = BrandPalette.OnBrand,
            BackgroundColor = BrandPalette.Brand,
            BorderWidth = 0,
            CornerRadius = 10,
            Padding = new Thickness(19, 8),
            MinimumHeightRequest = 0,
            VerticalOptions = LayoutOptions.Center,
        };
        SetAutomationId(b, ActionId(d, idSuffix));
        if (row.Command is not null) b.Command = row.Command;
        return b;
    }

    /// <summary>Outlined neutral button — <c>.btn-ghost</c>.</summary>
    private Button GhostButton(string text, SettingsDescriptor d, SettingsRowViewModel row, string idSuffix)
    {
        var b = new Button
        {
            Text = text,
            FontFamily = "TajawalBold",
            FontSize = 13,
            TextColor = Ink2,
            BackgroundColor = IsDark ? SurfaceDark : SurfaceLight,
            BorderColor = Line,
            BorderWidth = 1,
            CornerRadius = 10,
            Padding = new Thickness(17, 8),
            MinimumHeightRequest = 0,
            VerticalOptions = LayoutOptions.Center,
        };
        SetAutomationId(b, ActionId(d, idSuffix));
        if (row.Command is not null) b.Command = row.Command;
        return b;
    }

    /// <summary>Borderless brand text button — <c>.set-edit</c> / <c>.link-out</c>.</summary>
    private Button LinkAction(string text, SettingsDescriptor d, SettingsRowViewModel row, string idSuffix)
    {
        var b = new Button
        {
            Text = text,
            FontFamily = "TajawalBold",
            FontSize = 12,
            TextColor = BrandPalette.BrandInk,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = new Thickness(2, 0),
            MinimumHeightRequest = 0,
            VerticalOptions = LayoutOptions.Center,
        };
        SetAutomationId(b, ActionId(d, idSuffix));
        if (row.Command is not null) b.Command = row.Command;
        return b;
    }

    private static string ActionId(SettingsDescriptor d, string idSuffix) =>
        string.IsNullOrEmpty(d.AutomationId) ? $"{d.Key}_{idSuffix}" : d.AutomationId + idSuffix;

    /// <summary>Mockup anatomy: an editable value sits in a 1px hairline box; MAUI controls draw no frame.</summary>
    private static Border Boxed(View content, double radius, Thickness padding) => new()
    {
        StrokeThickness = 1,
        Padding = padding,
        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = radius },
        Content = content,
    };

    private AppEntry NewEntry(SettingsRowViewModel row, Keyboard keyboard)
    {
        var d = row.Descriptor;
        var entry = new AppEntry
        {
            Keyboard = keyboard,
            FontFamily = "TajawalBold",
            FontSize = 13,
            HorizontalTextAlignment = d.IsLtr ? TextAlignment.Center : TextAlignment.End,
            FlowDirection = d.IsLtr ? FlowDirection.LeftToRight : FlowDirection.RightToLeft,
            IsPassword = d.IsPassword,
            Placeholder = d.PlaceholderAr,
        };
        SetAutomationId(entry, d.AutomationId);
        return entry;
    }

    private static Label UnitLabel(string text) => new()
    {
        Text = text,
        FontFamily = "TajawalBold",
        FontSize = LabelSize,
        VerticalOptions = LayoutOptions.Center,
    };

    private static void SetAutomationId(View v, string automationId)
    {
        if (!string.IsNullOrEmpty(automationId))
        {
            v.AutomationId = automationId;
        }
    }

    private static int? ParseInt(string? text) => int.TryParse(text, out var v) ? v : null;

    private static int ToInt(object? value, int fallback) => value switch
    {
        int i => i,
        string s when int.TryParse(s, out var p) => p,
        _ => fallback,
    };

    private void RepaintError()
    {
        if (_row is null) return;
        _errorLabel.Text = _row.ErrorText ?? string.Empty;
        _errorLabel.IsVisible = _row.HasError;
        // .num-input.invalid — the field itself carries the error, not only the message under it.
        foreach (var child in _valueCell.Children)
        {
            if (child is Border { Content: Entry } box)
            {
                box.Stroke = _row.HasError ? Danger : Line;
            }
        }
    }
}
