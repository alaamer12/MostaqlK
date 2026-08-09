# UI Component Rules

> **Companion to:** [`DESIGN.md`](../product/DESIGN.md) (tokens, palette, icon tiers, typography) · [`ui-ux-design.md`](../../v1/product/ui-ux-design.md) (layout, interaction) · [`errors-handling.md`](errors-handling.md) (`DomainError.ExternalMessage` / `FixMessage` binding)
> **Status:** Binding — every new View and ContentView must conform.

---

## Table of Contents

1. [Responsiveness Contract](#1-responsiveness-contract)
2. [Skeleton Loading System](#2-skeleton-loading-system)
3. [Text Display Rules](#3-text-display-rules)
4. [Sub-Text Slot System](#4-sub-text-slot-system)
5. [Icon System — Three Tiers](#5-icon-system--three-tiers)
6. [Illustrations and Letterbox Panels](#6-illustrations-and-letterbox-panels)
7. [RTL and Bidirectional Text](#7-rtl-and-bidirectional-text)
8. [Component Checklist](#8-component-checklist)

---

## 1. Responsiveness Contract

### 1.1 — No Fixed Heights on Text-Bearing Elements

Every container that holds text **must** size to its content. Fixed heights are only permitted on purely decorative or icon-only elements.

```xml
<!-- WRONG — clips long Arabic project titles -->
<Frame HeightRequest="60">
    <Label Text="{Binding Title}" />
</Frame>

<!-- CORRECT — grows with content -->
<Border Padding="12">
    <Label Text="{Binding Title}" />
</Border>
```

**Rule:** `HeightRequest` on any element that directly or indirectly contains a `Label` or `Editor` is a red flag. Treat it as a code-review failure unless accompanied by an explicit justification comment and a confirmed `LineBreakMode` on every label inside.

### 1.2 — Fluid Grid and List Row Sizing

List rows and grid cells use automatic row sizing:

```xml
<!-- CollectionView item template: row grows with content -->
<CollectionView.ItemTemplate>
    <DataTemplate x:DataType="vm:ProjectSummaryViewModel">
        <views:ProjectCard />
        <!-- ProjectCard has no HeightRequest — height is driven by content -->
    </DataTemplate>
</CollectionView.ItemTemplate>
```

**Never** set `ItemSizingStrategy="MeasureFirstItem"` on a `CollectionView` whose items can have variable content heights (Arabic vs. short English titles, sub-text lines, etc.).

### 1.3 — Overflow-Safe Text

Long unbroken strings (owner handles, URLs, long numbers) must not blow out container width:

```xml
<Label
    Text="{Binding OwnerHandle}"
    LineBreakMode="CharacterWrap"
    MaxLines="2" />
```

For containers where horizontal overflow must be contained:

```xml
<Label
    Text="{Binding Url}"
    LineBreakMode="TailTruncation"
    MaxLines="1" />
```

### 1.4 — Safe Area and Density-Independent Sizing

- All sizes in `dp` (MAUI default density-independent unit) — never pixel values.
- Use `SafeAreaInsets` / `OnPlatform` only for system chrome overlap (status bar, nav bar) — never for content sizing.
- All padding/margin values come from the design token set (`Spacing.XS` through `Spacing.XL`) — no ad-hoc numeric literals.

---

## 2. Skeleton Loading System

### 2.1 — The Pairing Rule

**Every content container must have a sibling Skeleton component.**

The skeleton occupies exactly the same visual footprint as its content sibling. They toggle via `IsVisible` binding — never replace one with the other in code-behind:

```xml
<!-- Paired siblings: skeleton and real content share the same slot -->
<Grid>

    <!-- Skeleton: visible while IsLoading = true -->
    <views:ShimmerBox
        IsVisible="{Binding IsLoading}"
        WidthRequest="240"
        HeightRequest="20"
        CornerRadius="4" />

    <!-- Real content: visible while IsLoading = false -->
    <Label
        Text="{Binding Title}"
        IsVisible="{Binding IsLoading, Converter={StaticResource InvertBool}}"
        Style="{StaticResource TitleLabel}" />

</Grid>
```

**One container = one skeleton counterpart.** A card with a title, subtitle, and badge gets three skeleton elements matching the shape of each:

```xml
<!-- ProjectCard skeleton layout -->
<VerticalStackLayout Spacing="8" IsVisible="{Binding IsLoading}">

    <!-- Title skeleton: matches TitleLabel height (~20dp) -->
    <views:ShimmerBox HeightRequest="20" CornerRadius="4"
                      HorizontalOptions="Fill" />

    <!-- Subtitle skeleton: narrower, ~14dp -->
    <views:ShimmerBox HeightRequest="14" WidthRequest="160" CornerRadius="4" />

    <!-- Badge skeleton: pill shape -->
    <views:ShimmerBox HeightRequest="22" WidthRequest="80" CornerRadius="11" />

</VerticalStackLayout>
```

### 2.2 — `ShimmerBox` Implementation

```csharp
// MostaqlK/Views/Shared/ShimmerBox.xaml.cs
/// <summary>
/// A rectangular skeleton placeholder with a shimmer animation.
/// Pair one with every real content element. Visible only while the parent is loading.
/// </summary>
public partial class ShimmerBox : ContentView
{
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(float), typeof(ShimmerBox), 6f,
            propertyChanged: (b, _, _) => ((ShimmerBox)b).ApplyCornerRadius());

    public float CornerRadius
    {
        get => (float)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public ShimmerBox() => InitializeComponent();

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler is not null)
            StartShimmer();
    }

    /// <summary>
    /// Starts the shimmer sweep animation. Repeats indefinitely until
    /// <see cref="ContentView.IsVisible"/> is set to <c>false</c>.
    /// </summary>
    private void StartShimmer()
    {
        var shimmer   = ShimmerOverlay;   // named element in XAML
        shimmer.TranslationX = -300;

        var animation = new Animation(
            v => shimmer.TranslationX = v,
            start:  -300,
            end:     300,
            easing:  Easing.Linear);

        animation.Commit(this,
            name:   "Shimmer",
            length: 1_400,
            repeat: () => true);
    }

    private void ApplyCornerRadius()
    {
        if (BackgroundShape is RoundRectangle r)
            r.CornerRadius = new CornerRadius(CornerRadius);
    }
}
```

```xml
<!-- MostaqlK/Views/Shared/ShimmerBox.xaml -->
<ContentView x:Class="MostaqlK.Views.Shared.ShimmerBox">
    <Border
        BackgroundColor="{AppThemeBinding
            Light={StaticResource SkeletonBase},
            Dark={StaticResource SkeletonBaseDark}}"
        StrokeThickness="0">
        <Border.StrokeShape>
            <RoundRectangle x:Name="BackgroundShape" CornerRadius="6" />
        </Border.StrokeShape>

        <!-- Shimmer overlay: gradient bar that slides across the base -->
        <Grid ClipsToBounds="True">
            <BoxView
                x:Name="ShimmerOverlay"
                Color="Transparent"
                HeightRequest="{Binding Source={RelativeSource AncestorType={x:Type ContentView}}, Path=Height}"
                WidthRequest="120">
                <BoxView.Background>
                    <LinearGradientBrush StartPoint="0,0" EndPoint="1,0">
                        <GradientStop Color="Transparent" Offset="0.0" />
                        <GradientStop Color="{AppThemeBinding
                            Light={StaticResource SkeletonShimmer},
                            Dark={StaticResource SkeletonShimmerDark}}"
                                      Offset="0.5" />
                        <GradientStop Color="Transparent" Offset="1.0" />
                    </LinearGradientBrush>
                </BoxView.Background>
            </BoxView>
        </Grid>
    </Border>
</ContentView>
```

### 2.3 — Skeleton Design Tokens

Add to `Resources/Styles/Colors.xaml`:

```xml
<!-- Light theme -->
<Color x:Key="SkeletonBase">#E8ECF0</Color>
<Color x:Key="SkeletonShimmer">#FFFFFF</Color>

<!-- Dark theme -->
<Color x:Key="SkeletonBaseDark">#2C3240</Color>
<Color x:Key="SkeletonShimmerDark">#3C4558</Color>
```

The shimmer color must always be lighter than the base and blend naturally — never a harsh contrast.

### 2.4 — Animation Specification

| Property | Value | Rationale |
|---|---|---|
| Type | TranslationX sweep (left → right) | Mimics a light sweep — the most recognizable skeleton pattern |
| Duration | 1 400 ms | Slow enough to read as "loading," fast enough to not feel broken |
| Easing | `Easing.Linear` | Constant-speed sweep; easing would make the shimmer appear to stutter |
| Loop | Infinite | Repeats until `IsVisible` flips to `false` |
| Direction | Always left → right (fixed) | Decorative; never RTL-flipped |

### 2.5 — Skeleton Rules

1. **Never skip** — a blank area while loading is always wrong.
2. **Match the shape** — if the real content is a pill badge, the skeleton is a rounded box with the same height and approximate width.
3. **Stop when hidden** — `animation.Commit(..., repeat: () => true)` stops automatically when the control is removed from the visual tree. If kept in tree but hidden, explicitly cancel via `this.AbortAnimation("Shimmer")` in an `IsVisibleChanged` handler.
4. **Do not nest skeletons** — one `ShimmerBox` per atomic content unit. A single `ShimmerBox` covering a whole card is acceptable only for the very first load of a cold-start screen.

---

## 3. Text Display Rules

### 3.1 — Two Modes: Wrapping vs. Truncation

Every text element is classified into one of two modes. Choose based on context:

| Mode | `LineBreakMode` | `MaxLines` | When to use |
|---|---|---|---|
| **Wrapping** | `WordWrap` (default for Arabic) | Unlimited | Main content: project titles in the feed, full descriptions in detail view, error messages |
| **Truncation + Ellipsis** | `TailTruncation` | 1–2 | Supplementary / compact: owner names in compact rows, category chips, breadcrumbs |

**Default is Wrapping.** Only switch to Truncation when the design explicitly calls for a single-line or fixed-line-count element.

### 3.2 — `TruncatingLabel` Helper

For elements that need smart truncation with an optional character-count threshold (not just line-count), use the `TruncatingLabel` control:

```csharp
// MostaqlK/Views/Shared/TruncatingLabel.xaml.cs

/// <summary>
/// A Label that truncates with an ellipsis (…) when text exceeds a character threshold.
/// Falls back to MAUI's built-in <see cref="LineBreakMode.TailTruncation"/> when
/// <see cref="MaxChars"/> is not set.
/// </summary>
public partial class TruncatingLabel : ContentView
{
    /// <summary>
    /// Maximum number of characters before truncation is applied.
    /// <c>null</c> means standard <see cref="LineBreakMode.TailTruncation"/> driven
    /// by available width/line count, not character count.
    /// </summary>
    public static readonly BindableProperty MaxCharsProperty =
        BindableProperty.Create(nameof(MaxChars), typeof(int?), typeof(TruncatingLabel), null,
            propertyChanged: (b, _, _) => ((TruncatingLabel)b).UpdateText());

    /// <summary>The source text to display.</summary>
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(TruncatingLabel), string.Empty,
            propertyChanged: (b, _, _) => ((TruncatingLabel)b).UpdateText());

    public int?   MaxChars { get => (int?)GetValue(MaxCharsProperty);  set => SetValue(MaxCharsProperty, value); }
    public string Text     { get => (string)GetValue(TextProperty);    set => SetValue(TextProperty, value); }

    public TruncatingLabel() => InitializeComponent();

    private void UpdateText() =>
        InnerLabel.Text = Truncate(Text ?? string.Empty, MaxChars);

    /// <summary>
    /// Truncates <paramref name="text"/> to <paramref name="maxChars"/> characters,
    /// appending a horizontal ellipsis (U+2026) if truncation occurred.
    /// </summary>
    /// <param name="text">The source text. Never <c>null</c>.</param>
    /// <param name="maxChars">
    /// Maximum character count inclusive of the ellipsis.
    /// When <c>null</c>, the original text is returned unchanged.
    /// </param>
    /// <returns>The (possibly truncated) display string.</returns>
    internal static string Truncate(string text, int? maxChars)
    {
        if (maxChars is null || text.Length <= maxChars.Value)
            return text;

        // Reserve 1 character for the ellipsis (U+2026)
        var cutoff = Math.Max(0, maxChars.Value - 1);
        return string.Concat(text.AsSpan(0, cutoff), "\u2026");
    }
}
```

```xml
<!-- MostaqlK/Views/Shared/TruncatingLabel.xaml -->
<ContentView x:Class="MostaqlK.Views.Shared.TruncatingLabel">
    <Label x:Name="InnerLabel"
           LineBreakMode="TailTruncation"
           MaxLines="1" />
</ContentView>
```

**Usage:**

```xml
<!-- Hard cap at 40 chars — useful for compact list rows -->
<views:TruncatingLabel
    Text="{Binding OwnerName}"
    MaxChars="40"
    Style="{StaticResource BodyLabelSecondary}" />

<!-- No MaxChars — relies on container width + TailTruncation -->
<views:TruncatingLabel
    Text="{Binding CategoryName}"
    Style="{StaticResource ChipLabel}" />
```

### 3.3 — Text Element Reference Table

| Element | Mode | MaxLines | MaxChars |
|---|---|---|---|
| Project title (feed card) | Wrapping | 3 | — |
| Project title (detail view) | Wrapping | unlimited | — |
| Owner name (feed card) | Truncation | 1 | 35 |
| Owner name (detail header) | Wrapping | — | — |
| Category chip | Truncation | 1 | 20 |
| Description (feed card) | Wrapping | 2 | — |
| Description (detail view) | Wrapping | unlimited | — |
| Budget display | Truncation | 1 | 25 |
| Error message (`ExternalMessage`) | Wrapping | unlimited | — |
| Fix hint (`FixMessage`) | Wrapping | 3 | — |
| Status badge label | Truncation | 1 | 15 |
| Toast title | Truncation | 1 | 60 |
| Toast body | Wrapping | 2 | — |

### 3.4 — Long Unbroken Strings

For content that may contain long unbroken tokens (URLs, hash IDs, long English names in an Arabic context), use `CharacterWrap` to prevent horizontal overflow:

```xml
<Label
    Text="{Binding RawUrl}"
    LineBreakMode="CharacterWrap"
    MaxLines="2" />
```

`CharacterWrap` breaks at any character boundary — acceptable for technical strings where word boundaries are meaningless.

---

## 4. Sub-Text Slot System

### 4.1 — The Rule

**Every label component supports a `SubText` slot.** The sub-text slot is the designated binding point for:

- `DomainError.ExternalMessage` — user-facing Arabic error description
- `DomainError.FixMessage` — optional Arabic fix guidance (nullable)
- Supplementary contextual text (e.g. "آخر تحديث منذ ساعتين" below an owner name)

The slot renders **nothing** when not populated — no blank space is reserved.

### 4.2 — `LabelWithSubText` Control

```csharp
// MostaqlK/Views/Shared/LabelWithSubText.xaml.cs

/// <summary>
/// A compound label: primary text on top, optional sub-text below in a smaller, muted style.
/// Use as the standard binding target for <see cref="DomainError.ExternalMessage"/>
/// and <see cref="DomainError.FixMessage"/>.
/// </summary>
public partial class LabelWithSubText : ContentView
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(LabelWithSubText), null);

    public static readonly BindableProperty SubTextProperty =
        BindableProperty.Create(nameof(SubText), typeof(string), typeof(LabelWithSubText), null,
            propertyChanged: (b, _, newVal) =>
            {
                var self = (LabelWithSubText)b;
                // Hide the sub-label row entirely when null or empty — no blank space
                self.SubLabel.IsVisible = !string.IsNullOrEmpty((string?)newVal);
            });

    public static readonly BindableProperty SubTextColorProperty =
        BindableProperty.Create(nameof(SubTextColor), typeof(Color), typeof(LabelWithSubText),
            defaultValueCreator: _ => Application.Current!.Resources["TextMuted"] as Color);

    public string? Text         { get => (string?)GetValue(TextProperty);        set => SetValue(TextProperty, value); }
    public string? SubText      { get => (string?)GetValue(SubTextProperty);     set => SetValue(SubTextProperty, value); }
    public Color   SubTextColor { get => (Color)GetValue(SubTextColorProperty);  set => SetValue(SubTextColorProperty, value); }

    public LabelWithSubText() => InitializeComponent();
}
```

```xml
<!-- MostaqlK/Views/Shared/LabelWithSubText.xaml -->
<ContentView x:Class="MostaqlK.Views.Shared.LabelWithSubText"
             x:Name="Root">
    <VerticalStackLayout Spacing="2">

        <!-- Primary text: wrapping by default -->
        <Label
            Text="{Binding Source={x:Reference Root}, Path=Text}"
            Style="{StaticResource BodyLabel}"
            LineBreakMode="WordWrap" />

        <!-- Sub-text: hidden by default; shown when SubText is non-empty -->
        <Label
            x:Name="SubLabel"
            Text="{Binding Source={x:Reference Root}, Path=SubText}"
            TextColor="{Binding Source={x:Reference Root}, Path=SubTextColor}"
            Style="{StaticResource CaptionLabel}"
            LineBreakMode="WordWrap"
            IsVisible="False" />

    </VerticalStackLayout>
</ContentView>
```

### 4.3 — Binding `DomainError` to the Sub-Text Slot

**ViewModel side:**

```csharp
// ViewModel: expose DomainError fields as observable properties
[ObservableProperty] private string? _errorMessage;    // ExternalMessage
[ObservableProperty] private string? _fixSuggestion;   // FixMessage (may be null)
[ObservableProperty] private bool    _hasError;

// Set inside the LoadFeedAsync command:
case Result<IReadOnlyList<ProjectSummary>>.Err err:
    _logger.LogWarning(err.Error.Cause,
        "Error {Code}: {InternalMessage}",
        err.Error.Code, err.Error.InternalMessage);

    ErrorMessage  = err.Error.ExternalMessage;  // never null
    FixSuggestion = err.Error.FixMessage;        // null for self-healing errors
    HasError      = true;
    break;
```

**View side:**

```xml
<!-- Error banner with sub-text slot -->
<views:LabelWithSubText
    IsVisible="{Binding HasError}"
    Text="{Binding ErrorMessage}"
    SubText="{Binding FixSuggestion}"
    SubTextColor="{AppThemeBinding
        Light={StaticResource WarningText},
        Dark={StaticResource WarningTextDark}}" />
```

When `FixSuggestion` is `null` (self-healing errors like rate-limiting), `SubLabel.IsVisible` remains `false` — no XAML `Trigger` or `Converter` required.

### 4.4 — Sub-Text in Compound Components

Components that already compose primary + secondary content (e.g. `ProjectCard`) expose sub-text as a third-level slot inside their own layout:

```xml
<VerticalStackLayout Spacing="4">

    <!-- Primary: wrapping title -->
    <Label Text="{Binding Title}"
           Style="{StaticResource TitleLabel}"
           LineBreakMode="WordWrap"
           MaxLines="3" />

    <!-- Secondary: truncated owner name -->
    <views:TruncatingLabel
           Text="{Binding OwnerName}"
           MaxChars="35"
           Style="{StaticResource BodyLabelSecondary}" />

    <!-- Sub-text slot: ExternalMessage — only rendered when HasError is true -->
    <Label Text="{Binding ErrorMessage}"
           Style="{StaticResource ErrorLabel}"
           LineBreakMode="WordWrap"
           IsVisible="{Binding HasError}" />

    <!-- Fix slot: FixMessage — only rendered when non-null -->
    <Label Text="{Binding FixSuggestion}"
           Style="{StaticResource FixHintLabel}"
           LineBreakMode="WordWrap"
           MaxLines="3"
           IsVisible="{Binding FixSuggestion,
               Converter={StaticResource NotNullOrEmptyConverter}}" />

</VerticalStackLayout>
```

---

## 5. Icon System — Three Tiers

> **Full token specification:** [`DESIGN.md § Icon system — three tiers`](../product/DESIGN.md#icon-system--three-tiers)

### 5.1 — MAUI Color Mapping

All icons use **Tabler Icons** (outline style). Color is set exclusively through the tier token — never a hardcoded color directly on an icon element.

| Tier | Token | Style key | When |
|---|---|---|---|
| **1 — Neutral** | `{StaticResource TextSecondary}` | `IconNeutral` | Inactive nav items, secondary actions, disabled controls |
| **2 — Brand** | `{StaticResource AccentPrimary}` | `IconBrand` | The one primary action or state per screen |
| **2 — Positive** | `{StaticResource AccentPositive}` | `IconPositive` | Live / enriched / success state |
| **3 — Conceptual** | Row hue from `ConceptualColors` | `IconConceptual` | Settings / listing rows — one distinct hue per row concept |

```xml
<!-- Tier 1: inactive nav item -->
<Image Source="settings.png" Style="{StaticResource IconNeutral}" />

<!-- Tier 2: primary action on the current screen -->
<Image Source="refresh.png" Style="{StaticResource IconBrand}" />

<!-- Tier 2: positive/success state -->
<Image Source="circle-check.png" Style="{StaticResource IconPositive}" />

<!-- Tier 3: settings row (unique hue per row concept) -->
<Image Source="clock.png">
    <Image.Behaviors>
        <behaviors:TintColorBehavior TintColor="{StaticResource ConceptualTeal}" />
    </Image.Behaviors>
</Image>
```

### 5.2 — Three Required States per Icon

Every interactive icon must implement all three states via `VisualStateManager` — not `Opacity` alone:

```xml
<Image Source="bell.png">
    <VisualStateManager.VisualStateGroups>
        <VisualStateGroup Name="CommonStates">

            <VisualState Name="Normal">
                <VisualState.Setters>
                    <Setter Property="Opacity" Value="1" />
                </VisualState.Setters>
            </VisualState>

            <VisualState Name="PointerOver">
                <VisualState.Setters>
                    <Setter Property="Opacity" Value="0.8" />
                </VisualState.Setters>
            </VisualState>

            <VisualState Name="Disabled">
                <VisualState.Setters>
                    <Setter Property="Opacity" Value="0.38" />
                    <!-- Disabled icons always use Tier 1 (neutral) color, regardless of their tier -->
                </VisualState.Setters>
            </VisualState>

        </VisualStateGroup>
    </VisualStateManager.VisualStateGroups>
</Image>
```

| State | Tier 1 | Tier 2 | Tier 3 |
|---|---|---|---|
| **Normal** | `TextSecondary` at 100% | `AccentPrimary` / `AccentPositive` at 100% | Row hue at 100% |
| **Hover** | `TextSecondary` at 80% | `AccentPrimary` at 80% | Row hue at 80% |
| **Disabled** | `TextSecondary` at 38% | `TextSecondary` at 38% (not brand) | `TextSecondary` at 38% |

---

## 6. Illustrations and Letterbox Panels

> **Design spec:** [`DESIGN.md § Onboarding illustrations`](../product/DESIGN.md#onboarding-illustrations)

### 6.1 — Letterbox Asset Specification

Each onboarding panel is a full-width illustration on a fixed dark canvas:

| Property | Value |
|---|---|
| Canvas color | `#0F1B2D` (deep navy) — **fixed, not theme-reactive** |
| Aspect ratio | 16:9 or 4:3 — consistent across all panels in a given flow |
| Subject | One clear centered scene per panel |
| Accent colors | Brand blue `#2386C8` + nature green `#2E9E6B` only |
| Sparkle accents | 3–5 small `+` / `✦` glyphs placed asymmetrically — decorative only |
| Feature pill | Optional above the headline: rounded pill, `AccentPrimary` fill, white bold label |
| Headline | White, bold, below the illustration |
| Body copy | Single Arabic line, `#A0AEC0` on the dark canvas |

### 6.2 — MAUI Letterbox Layout

```xml
<!-- OnboardingPage.xaml — single panel -->
<Grid>

    <!-- Fixed dark canvas — no AppThemeBinding -->
    <BoxView Color="#0F1B2D" />

    <VerticalStackLayout
        Spacing="16"
        VerticalOptions="Center"
        Padding="32,0">

        <!-- Illustration (SVG preferred for crisp scaling) -->
        <Image Source="onboarding_polling.svg"
               Aspect="AspectFit"
               HeightRequest="240" />

        <!-- Optional feature pill -->
        <Border BackgroundColor="{StaticResource AccentPrimary}"
                HorizontalOptions="Center"
                Padding="12,4">
            <Border.StrokeShape>
                <RoundRectangle CornerRadius="16" />
            </Border.StrokeShape>
            <Label Text="مراقبة مستقل"
                   TextColor="White"
                   FontAttributes="Bold"
                   FontSize="12" />
        </Border>

        <!-- Headline -->
        <Label Text="مشاريع جديدة، فور نشرها"
               TextColor="White"
               FontAttributes="Bold"
               FontSize="22"
               HorizontalTextAlignment="Center"
               LineBreakMode="WordWrap" />

        <!-- Supporting copy -->
        <Label Text="يراقب التطبيق مستقل باستمرار ويُنبّهك في اللحظة المناسبة"
               TextColor="#A0AEC0"
               FontSize="15"
               HorizontalTextAlignment="Center"
               LineBreakMode="WordWrap"
               MaxLines="2" />

    </VerticalStackLayout>
</Grid>
```

### 6.3 — Sticker Assets (Inline Illustrations)

Stickers are smaller SVG/PNG illustrations used for empty states, confirmation dialogs, and feature callouts:

| Usage | Size (dp) | Context |
|---|---|---|
| Empty feed | 120×120 | Replaces the project list when no results exist |
| Empty search | 120×120 | Shown when a search returns zero results |
| Error state (full screen) | 160×160 | Catastrophic load failure (DB unreadable, etc.) |
| Success confirmation | 80×80 | Post-action dialogs or success toasts |

**Stickers must have light/dark asset variants** — they are not fixed-canvas like letterbox panels. Provide both `sticker_name_light.svg` and `sticker_name_dark.svg` and load via `AppThemeBinding`:

```xml
<Image Source="{AppThemeBinding
    Light=empty_feed_light.svg,
    Dark=empty_feed_dark.svg}"
       Aspect="AspectFit"
       HeightRequest="120" />
```

---

## 7. RTL and Bidirectional Text

> **Full spec:** [`DESIGN.md § RTL support`](../product/DESIGN.md#rtl-support)

### 7.1 — FlowDirection

```xml
<!-- Set once at the Shell or root ContentPage level — never per-control -->
<Shell FlowDirection="RightToLeft" />
```

**Do not** set `FlowDirection` on individual `Label` or `StackLayout` elements unless the element contains *only* LTR content (e.g. a URL, a budget number, an English-only chip).

### 7.2 — Mixed Arabic/Latin Content

Numeric metadata (proposal counts, budget ranges, dates) is LTR embedded in RTL context. Use Unicode bidi isolation to prevent mis-ordering:

```csharp
// Format mixed-direction strings with Unicode bidi isolate markers
// U+2068 FIRST STRONG ISOLATE + content + U+2069 POP DIRECTIONAL ISOLATE
private static string FormatBudget(string amount, string currency)
    => $"\u2068{amount} {currency}\u2069";
```

For labels mixing Arabic labels with LTR values, use `FormattedText` with per-span `FlowDirection`:

```xml
<Label>
    <Label.FormattedText>
        <FormattedString>
            <Span Text="الميزانية: " FlowDirection="RightToLeft" />
            <Span Text="{Binding BudgetFormatted}" FlowDirection="LeftToRight" />
        </FormattedString>
    </Label.FormattedText>
</Label>
```

### 7.3 — Logical vs. Physical Properties

**Never use physical spacing properties.** Only logical equivalents are allowed:

| Forbidden | Use instead | Why |
|---|---|---|
| `Margin="16,0,0,0"` (left only) | Style with uniform margin + `HorizontalOptions` | Physical left becomes wrong side in RTL |
| Border on left side only | Border on `Start` side via `FlowDirection`-aware behavior | Start = right in RTL, left in LTR |

The unread accent bar appears on the **inline-start** side automatically via `HorizontalOptions="Start"` when `FlowDirection` is set on the parent:

```xml
<!-- Unread accent bar: appears on Start side = right in RTL, left in LTR -->
<Border
    IsVisible="{Binding IsUnread}"
    WidthRequest="4"
    BackgroundColor="{StaticResource AccentPrimary}"
    HorizontalOptions="Start"
    VerticalOptions="Fill" />
```

---

## 8. Component Checklist

Before merging any new View or ContentView, verify all items:

### Responsiveness
- [ ] No `HeightRequest` on any element that contains a `Label` (without justification comment)
- [ ] `CollectionView` / `ListView` uses auto-sized rows — no `ItemSizingStrategy="MeasureFirstItem"` on variable-height lists
- [ ] Long unbroken strings use `CharacterWrap` or `TailTruncation`
- [ ] All sizes in `dp`, no pixel values

### Skeletons
- [ ] Every content-bearing element has a `ShimmerBox` sibling of matching visual footprint
- [ ] Skeleton is hidden (and animation stopped) when `IsLoading = false`
- [ ] No blank/empty areas appear during loading state
- [ ] Skeleton shape matches the real content (pill → rounded box; wide label → wide box)

### Text Display
- [ ] Main content uses `WordWrap` (`LineBreakMode="WordWrap"`)
- [ ] Compact / single-line elements use `TailTruncation` with appropriate `MaxLines`
- [ ] `TruncatingLabel` used (not raw `Label`) wherever `MaxChars` is specified
- [ ] URL / hash / technical strings use `CharacterWrap`

### Sub-Text Slot
- [ ] All error states use `LabelWithSubText` or equivalent compound component
- [ ] `ExternalMessage` → primary text; `FixMessage` → sub-text
- [ ] Sub-text is invisible (not just empty) when `null` — no blank space reserved
- [ ] `IsVisible` on sub-label driven by `string.IsNullOrEmpty`, not `Converter`-only

### Icons
- [ ] Each icon uses the correct tier style key (`IconNeutral` / `IconBrand` / `IconConceptual`)
- [ ] All three states (Normal, Hover, Disabled) implemented via `VisualStateManager`
- [ ] No hardcoded color on any icon — tier token only
- [ ] Disabled icons use `TextSecondary` at 38% regardless of their normal tier

### Illustrations
- [ ] Onboarding letterbox panels use `#0F1B2D` fixed canvas — no `AppThemeBinding`
- [ ] Sticker assets have light/dark variants loaded via `AppThemeBinding`
- [ ] Empty states include a sticker illustration, not just text

### RTL
- [ ] `FlowDirection` set at Shell/Page level only
- [ ] Numeric/LTR content in Arabic strings uses `\u2068...\u2069` bidi isolate or `FormattedText` with per-span `FlowDirection`
- [ ] No physical margin/padding (e.g. `Margin="16,0,0,0"` for left-only margin)
- [ ] Unread accent bar uses `HorizontalOptions="Start"` (not `"Left"`)
