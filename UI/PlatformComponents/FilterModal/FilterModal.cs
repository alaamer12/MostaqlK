using Microsoft.Maui;
using Microsoft.Maui.Layouts;
using Microsoft.Maui.Controls.Shapes;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents.FilterModal;

/// <summary>
/// The v3 typed-facet filter dialog (contract §8; <c>.repertoire/design/postmvp/v3-multisource/projects.html</c>
/// "Filters modal"). Renders ONE control per <see cref="ISourceDescriptor.FilterFacets"/> entry of the
/// active source — <see cref="FilterFacetKind.MultiChoice"/> becomes a chip group straight from the
/// facet's own <see cref="FilterFacetSpec.Options"/>, <see cref="FilterFacetKind.Range"/> becomes two
/// numeric entries with the spec's defaults — and nothing else: the unit never names a facet, a kind it
/// cannot render, or a site (growth-without-branches, contract §10). A source with no facets simply
/// produces no controls (its stored query still round-trips untouched through the descriptor).
/// </summary>
/// <remarks>
/// Mechanics (all through the descriptor seam, none in the UI):
/// <list type="bullet">
/// <item><see cref="Open"/> pre-fills the selection by <see cref="ISourceDescriptor.ParseFacetSelection"/>
/// of the source's stored <c>QueryParams</c>, so what Settings/onboarding wrote is what the modal shows.</item>
/// <item><b>تطبيق</b> hands back the FULL selection — edited facet keys plus every parsed key the modal
/// did not render (skills slugs, <c>page</c>, unknown params) — through <see cref="ApplyRequested"/>;
/// the host feeds it to <see cref="ISourceDescriptor.BuildQueryFromFacets"/> and persists it. The UI
/// never string-concatenates a query.</item>
/// <item>Host-managed visibility, like <c>RecentNotificationsFlyout</c>: the overlay covers the page and
/// a backdrop tap is a cancel. (Esc-to-close is a native-dialog affordance the in-layout overlay cannot
/// intercept without platform code — surfaced for Windows smoke, see the B3 report.)</item>
/// </list>
/// Colors: brand chrome reads the live <see cref="BrandPalette"/> statics (the active source's palette,
/// theme-projected); neutral surface/line/ink pairs are the same slate values every swept layout in the
/// app uses — state chrome, never a brand hex. The whole facet tree is rebuilt on every
/// <see cref="Open"/>, so a theme or morph between openings always lands and no app-lifetime event is
/// ever subscribed (event-lifetime rule).
/// </remarks>
public sealed class FilterModal : ContentView
{
    // Mockup modal-card max-width 30rem ≈ 480px.
    private const double CardMaxWidth = 480;

    // Mockup `.modal-overlay { background: rgba(2, 6, 23, .55) }` → ARGB 8C020617 (neutral scrim, not brand).
    private static readonly Color ScrimColor = Color.FromArgb("#8C020617");

    // Neutral chrome pairs (light/dark) mirroring the swept layouts: --surface / --line / --ink-1 / --ink-2 / --ink-3 / --surface-2.
    private const string SurfaceLight = "#FFFFFF", SurfaceDark = "#0F172A";
    private const string LineLight = "#E2E8F0", LineDark = "#1E293B";
    private const string Ink1Light = "#0F172A", Ink1Dark = "#F1F5F9";
    private const string Ink2Light = "#475569", Ink2Dark = "#94A3B8";
    private const string Ink3Light = "#64748B", Ink3Dark = "#94A3B8";
    private const string InputBgLight = "#F8FAFC", InputBgDark = "#1E293B";

    private readonly VerticalStackLayout _facetHost;
    private ISourceDescriptor? _descriptor;

    // Parsed baseline of the stored query: every key the modal does not render passes back untouched.
    private IReadOnlyDictionary<string, IReadOnlyList<string>> _passthrough =
        new Dictionary<string, IReadOnlyList<string>>();

    // One entry per rendered facet: how to read its current UI state back into the selection / reset it.
    private readonly List<Action<Dictionary<string, IReadOnlyList<string>>>> _collectors = [];
    private readonly List<Action> _clearers = [];

    /// <summary>Raised by <b>تطبيق</b> with the full facet selection (edited + passed-through) for the host to build + persist.</summary>
    public event EventHandler<IReadOnlyDictionary<string, IReadOnlyList<string>>>? ApplyRequested;

    /// <summary>Raised by <b>إلغاء</b>, the header X, or a backdrop tap — the host only hides the unit.</summary>
    public event EventHandler? CloseRequested;

    public FilterModal()
    {
        _facetHost = new VerticalStackLayout { Spacing = 16 };

        // Header: [brand-ink sliders glyph] "الفلاتر" ......... [X]
        var titleIcon = new AppIcon
        {
            // Mockup header icon `fa-solid fa-sliders ... color: var(--brand-ink)` — Filter is the
            // closest real artwork in the AppIcon catalog; the ink key keeps it themed.
            Icon = AppIconGlyph.Filter,
            FontSize = 14,
            TextColor = BrandPalette.BrandInk,
            VerticalOptions = LayoutOptions.Center,
        };
        var titleLabel = new Label
        {
            // Mockup verbatim modal title.
            Text = "الفلاتر",
            FontFamily = "TajawalBold",
            FontSize = 16,
            VerticalOptions = LayoutOptions.Center,
            TextColor = Ink(1),
        };
        var titleRow = new HorizontalStackLayout { Spacing = 8, Children = { titleIcon, titleLabel } };
        var closeButton = BuildHeaderCloseButton();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) }, ColumnSpacing = 12 };
        Grid.SetColumn(titleRow, 0);
header.Children.Add(titleRow);
        Grid.SetColumn(closeButton, 1);
header.Children.Add(closeButton);

        var body = new ScrollView { Content = _facetHost };

        var note = new Label
        {
            // Mockup verbatim per-source footnote.
            Text = "تُطبَّق الفلاتر على المنصة الحالية فقط — كل منصة تحتفظ بفلاترها.",
            FontFamily = "Tajawal",
            FontSize = 11,
            TextColor = Ink(3),
        };

        var column = new VerticalStackLayout
        {
            Spacing = 0,
            Children =
            {
                Section(header, newThickness: new Thickness(20, 16), bottomDivider: true),
                Section(body, newThickness: new Thickness(20, 16)),
                Section(note, newThickness: new Thickness(20, 0, 20, 14)),
                Section(BuildFooter(), newThickness: new Thickness(20, 12), topDivider: true),
            },
        };

        var card = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = 16 },
            StrokeThickness = 1,
            Stroke = Line(),
            BackgroundColor = Surface(),
            Padding = 0,
            WidthRequest = CardMaxWidth,
            MaximumWidthRequest = CardMaxWidth,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.RightToLeft,
            Content = column,
        };

        var backdrop = new BoxView { Color = ScrimColor };
        var backdropTap = new TapGestureRecognizer();
        backdropTap.Tapped += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        backdrop.GestureRecognizers.Add(backdropTap);

        var root = new Grid();
        root.Children.Add(backdrop);
        root.Children.Add(card);

        Content = root;
        FlowDirection = FlowDirection.RightToLeft;
        IsVisible = false;
    }

    /// <summary>
    /// Rebuilds the modal's controls from the source's typed facet vocabulary, pre-filled from the
    /// source's stored query string via the descriptor's own parser. The host calls this every time
    /// it shows the unit, so the facets always belong to the currently-active source.
    /// </summary>
    public void Open(ISourceDescriptor descriptor, string? storedQueryParams)
    {
        _descriptor = descriptor;
        _passthrough = descriptor.ParseFacetSelection(storedQueryParams);

        _facetHost.Children.Clear();
        _collectors.Clear();
        _clearers.Clear();

        foreach (var facet in descriptor.FilterFacets)
        {
            // Presence-by-data: a choice facet with no option vocabulary (nafezly `skills` is
            // search/session-driven and has no anonymous option list — capture-manifest §3) has
            // nothing honest to render; its parsed values still pass through untouched.
            if (facet.Kind == FilterFacetKind.MultiChoice && facet.Options.Count == 0)
            {
                continue;
            }

            switch (facet.Kind)
            {
                case FilterFacetKind.MultiChoice:
                    BuildChoiceFacet(facet);
                    break;
                case FilterFacetKind.Range:
                    BuildRangeFacet(facet);
                    break;
            }
        }
    }

    private void BuildChoiceFacet(FilterFacetSpec facet)
    {
        var selected = _passthrough.TryGetValue(facet.Key, out var stored)
            ? new HashSet<string>(stored, StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);

        var chips = new FlexLayout { Direction = FlexDirection.Row, Wrap = FlexWrap.Wrap };
        var cells = new List<(Button Chip, FilterFacetOption Option)>();
        foreach (var option in facet.Options)
        {
            var chip = new Button
            {
                // Chip caption is the site's OWN Arabic label, straight from the module's spec data.
                Text = option.LabelAr,
                FontFamily = "TajawalMedium",
                FontSize = 12,
                Padding = new Thickness(13, 6),
                BorderWidth = 1,
                CornerRadius = 8,
                MinimumHeightRequest = 0,
                AutomationId = $"FilterModal_Chip_{facet.Key}_{option.Value}",
            };
            SemanticProperties.SetDescription(chip, option.LabelAr);
            chip.Clicked += (_, _) =>
            {
                if (!selected.Remove(option.Value))
                {
                    selected.Add(option.Value);
                }

                PaintChip(chip, selected.Contains(option.Value));
            };
            PaintChip(chip, selected.Contains(option.Value));
            cells.Add((chip, option));
            chips.Children.Add(chip);
        }

        _collectors.Add(target =>
        {
            var chosen = facet.Options.Where(o => selected.Contains(o.Value)).Select(o => o.Value).ToList();
            if (chosen.Count > 0)
            {
                target[facet.Key] = chosen;
            }
            else
            {
                target.Remove(facet.Key);
            }
        });
        _clearers.Add(() =>
        {
            selected.Clear();
            foreach (var (chip, _) in cells)
            {
                PaintChip(chip, pressed: false);
            }
        });

        var group = new VerticalStackLayout { Spacing = 8 };
        group.Children.Add(BuildFacetCaption(facet.LabelAr, 12));
        group.Children.Add(chips);
        _facetHost.Children.Add(group);
    }

    private void BuildRangeFacet(FilterFacetSpec facet)
    {
        var parsed = _passthrough.TryGetValue(facet.Key, out var stored) ? stored : Array.Empty<string>();
        var initialMin = parsed.Count > 0 ? parsed[0] : string.Empty;
        var initialMax = parsed.Count > 1 ? parsed[1] : string.Empty;

        var minEntry = BuildNumberEntry(facet, lower: true, initial: initialMin);
        var maxEntry = BuildNumberEntry(facet, lower: false, initial: initialMax);

        var row = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star) }, ColumnSpacing = 12 };
        Grid.SetColumn(minEntry.Box, 0);
row.Children.Add(minEntry.Box);
        Grid.SetColumn(maxEntry.Box, 1);
row.Children.Add(maxEntry.Box);

        var group = new VerticalStackLayout { Spacing = 8 };
        group.Children.Add(BuildFacetCaption(facet.LabelAr, 12));
        group.Children.Add(row);
        _facetHost.Children.Add(group);

        _collectors.Add(target =>
        {
            var min = minEntry.Entry.Text?.Trim() ?? string.Empty;
            var max = maxEntry.Entry.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(min) && string.IsNullOrEmpty(max))
            {
                // Both bounds blank → the facet is unset; a one-sided band is not the site's grammar
                // (capture-manifest §3 always echoes "min,max"), so a partial band is dropped rather
                // than guessed at (surfaced in the B3 report).
                target.Remove(facet.Key);
                return;
            }

            target[facet.Key] = new List<string> { min, max };
        });
        _clearers.Add(() =>
        {
            minEntry.Entry.Text = string.Empty;
            maxEntry.Entry.Text = string.Empty;
        });
    }

    private (VerticalStackLayout Box, AppEntry Entry) BuildNumberEntry(
        FilterFacetSpec facet, bool lower, string initial)
    {
        // Mockup bounds labels: "{facet caption} الدنيا/القصوى" — the mockup's "الميزانية الدنيا"
        // verbatim pattern, the upper caption its exact parallel. The facet caption itself is the
        // module's own Arabic label, never a UI constant.
        var caption = BuildFacetCaption($"{facet.LabelAr} {(lower ? "الدنيا" : "القصوى")}", 11);
        caption.StyleId = lower ? "min" : "max";

        var entry = new AppEntry
        {
            Keyboard = Keyboard.Numeric,
            FontFamily = "Tajawal",
            FontSize = 13,
            Placeholder = lower ? facet.MinDefault ?? string.Empty : facet.MaxDefault ?? string.Empty,
            Text = initial,
            // The site's own pricing slider lives in a dir="ltr" island (capture-manifest §3/§4).
            FlowDirection = FlowDirection.LeftToRight,
            TextColor = Ink(1),
            PlaceholderColor = Ink(3),
            BackgroundColor = Colors.Transparent,
            AutomationId = $"FilterModal_Entry_{facet.Key}_{(lower ? "min" : "max")}",
        };

        var box = new Border
        {
            StrokeThickness = 1,
            Stroke = Line(),
            BackgroundColor = InputBackground(),
            Padding = new Thickness(12, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = entry,
        };

        var stacked = new VerticalStackLayout { Spacing = 6 };
        stacked.Children.Add(caption);
        stacked.Children.Add(box);
        return (stacked, entry);
    }

    private static Label BuildFacetCaption(string text, double size) => new()
    {
        Text = text,
        FontFamily = "TajawalMedium",
        FontSize = size,
        TextColor = Ink(3),
    };

    private void PaintChip(Button chip, bool pressed)
    {
        if (pressed)
        {
            // Mockup `.fchip[aria-pressed="true"]`: brand-soft fill + brand border + brand-ink text.
            chip.BackgroundColor = BrandPalette.BrandSoft;
            chip.BorderColor = BrandPalette.Brand;
            chip.TextColor = BrandPalette.BrandInk;
        }
        else
        {
            chip.BackgroundColor = Surface();
            chip.BorderColor = Line();
            chip.TextColor = Ink(2);
        }
    }

    private Border BuildHeaderCloseButton()
    {
        var icon = new AppIcon
        {
            Icon = AppIconGlyph.Close,
            FontSize = 14,
            TextColor = Ink(3),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        // Transparent Button overlay carries the hit target (the app's AutomationId convention).
        var hit = new Button
        {
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Text = string.Empty,
            Padding = 0,
            WidthRequest = 32,
            HeightRequest = 32,
            MinimumHeightRequest = 0,
        };
        hit.AutomationId = "FilterModal_CloseButton";
        SemanticProperties.SetDescription(hit, "إغلاق");
        hit.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var cell = new Grid();
        cell.Children.Add(icon);
        cell.Children.Add(hit);

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            Content = cell,
        };
    }

    private Grid BuildFooter()
    {
        var clearButton = new Button
        {
            // Mockup verbatim footer copy.
            Text = "مسح الكل",
            FontFamily = "TajawalMedium",
            FontSize = 13,
            BackgroundColor = Colors.Transparent,
            BorderWidth = 0,
            Padding = new Thickness(8, 4),
            MinimumHeightRequest = 0,
            TextColor = Ink(3),
        };
        clearButton.AutomationId = "FilterModal_ClearButton";
        clearButton.Clicked += (_, _) =>
        {
            // Clears only the facets THIS source exposes; keys the modal never renders (skills slugs,
            // page, unknown params) survive — "each platform keeps its own filters", per the modal's
            // own footnote.
            foreach (var clear in _clearers)
            {
                clear();
            }
        };

        var cancel = new Button
        {
            Text = "إلغاء",
            FontFamily = "TajawalMedium",
            FontSize = 13,
            Padding = new Thickness(16, 8),
            BorderWidth = 1,
            BorderColor = Line(),
            CornerRadius = 8,
            MinimumHeightRequest = 0,
            BackgroundColor = Surface(),
            TextColor = Ink(1),
        };
        cancel.AutomationId = "FilterModal_CancelButton";
        cancel.Clicked += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        var apply = new Button
        {
            Text = "تطبيق",
            FontFamily = "TajawalBold",
            FontSize = 13,
            Padding = new Thickness(16, 8),
            BorderWidth = 0,
            CornerRadius = 8,
            MinimumHeightRequest = 0,
            // Mockup primary button: `background: var(--brand); color: var(--brand-on)`.
            BackgroundColor = BrandPalette.Brand,
            TextColor = BrandPalette.OnBrand,
        };
        apply.AutomationId = "FilterModal_ApplyButton";
        apply.Clicked += (_, _) =>
        {
            if (_descriptor is null)
            {
                return;
            }

            var selection = new Dictionary<string, IReadOnlyList<string>>(_passthrough);
            foreach (var collect in _collectors)
            {
                collect(selection);
            }

            ApplyRequested?.Invoke(this, selection);
        };

        var right = new HorizontalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.End, Children = { cancel, apply } };

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) } };
        Grid.SetColumn(clearButton, 0);
footer.Children.Add(clearButton);
        Grid.SetColumn(right, 1);
footer.Children.Add(right);
        return footer;
    }

    /// <summary>A modal band (header/body/note/footer) with the mockup's padding and 1px --line dividers.</summary>
    private static Border Section(View content, Thickness newThickness, bool topDivider = false, bool bottomDivider = false)
    {
        var line = new BoxView { HeightRequest = 1, Color = Line() };

        View inner = content;
        if (topDivider || bottomDivider)
        {
            var stack = new VerticalStackLayout { Spacing = 0 };
            if (topDivider)
            {
                stack.Children.Add(line);
            }

            stack.Children.Add(content);
            if (bottomDivider)
            {
                stack.Children.Add(line);
            }

            inner = stack;
        }

        return new Border
        {
            Content = inner,
            Padding = topDivider || bottomDivider
                ? new Thickness(newThickness.HorizontalThickness, 0, newThickness.HorizontalThickness, newThickness.Bottom)
                : newThickness,
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
        };
    }

    private static bool IsDarkTheme => Application.Current?.RequestedTheme == AppTheme.Dark;

    private static Color Surface() => Color.FromArgb(IsDarkTheme ? SurfaceDark : SurfaceLight);

    private static Color InputBackground() => Color.FromArgb(IsDarkTheme ? InputBgDark : InputBgLight);

    private static Color Line() => Color.FromArgb(IsDarkTheme ? LineDark : LineLight);

    private static Color Ink(int level) => level switch
    {
        1 => Color.FromArgb(IsDarkTheme ? Ink1Dark : Ink1Light),
        2 => Color.FromArgb(IsDarkTheme ? Ink2Dark : Ink2Light),
        _ => Color.FromArgb(IsDarkTheme ? Ink3Dark : Ink3Light),
    };
}
