using System.Windows.Input;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The neutral "open this project on its source site" chip (ex-MostaqlLinkButton). Label and
/// link colors come from the active source: the descriptor supplies the display name, the
/// brand-token provider supplies the palette (seeded to this chip's historical link blues, so
/// nothing repaints today). Subscriptions follow the Loaded/Unloaded event-lifetime rule.
/// </summary>
public partial class SourceLinkButton : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SourceLinkButton));

    /// <summary>Optional explicit chip text; when unset the active source's display name is used.</summary>
    public static readonly BindableProperty LabelProperty =
        BindableProperty.Create(nameof(Label), typeof(string), typeof(SourceLinkButton), defaultValue: null);

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private IBrandTokenProvider? _tokens;

    public SourceLinkButton()
    {
        InitializeComponent();
        Loaded += OnSourceLoaded;
        Unloaded += OnSourceUnloaded;
    }

    private void OnSourceLoaded(object? sender, EventArgs e)
    {
        var services = IPlatformApplication.Current?.Services;
        var registry = services?.GetService<SourceRegistry>();
        var provider = services?.GetService<IBrandTokenProvider>();

        ApplyName(registry);
        if (provider is not null && _tokens is null)
        {
            _tokens = provider;
            _tokens.TokensSwapped += OnTokensSwapped;
            ApplyPalette(_tokens.Current);
        }
    }

    private void OnSourceUnloaded(object? sender, EventArgs e)
    {
        if (_tokens is not null)
        {
            _tokens.TokensSwapped -= OnTokensSwapped;
            _tokens = null;
        }
    }

    private void OnTokensSwapped(BrandTokenSet set) => ApplyPalette(set);

    private void ApplyName(SourceRegistry? registry)
    {
        if (!string.IsNullOrEmpty(Label))
        {
            LinkLabel.Text = Label;
            return;
        }

        if (registry is null)
        {
            return;
        }

        var sourceId = _tokens?.CurrentSource ?? SourceId.Default;
        if (registry.TryGet(sourceId, out var descriptor))
        {
            LinkLabel.Text = $"عرض في {descriptor.DisplayNameAr}";
        }
    }

    private void ApplyPalette(BrandTokenSet set)
    {
        var hex = Application.Current?.PlatformAppTheme == AppTheme.Dark ? set.LinkDark : set.LinkLight;
        if (!Color.TryParse(hex, out var color))
        {
            return;
        }

        LinkIcon.TextColor = color;
        LinkLabel.TextColor = color;
    }
}
