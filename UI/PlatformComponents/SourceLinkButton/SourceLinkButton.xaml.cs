using System.Windows.Input;
using MostaqlK.Core.Sources;
using MostaqlK.UI.DesignSystem.BrandTokens;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// The neutral "open this project on its source site" chip (ex-MostaqlLinkButton). The label resolves
/// from the active source's descriptor (re-applied on <see cref="IBrandTokenProvider.TokensSwapped"/>,
/// following the Loaded/Unloaded event-lifetime rule); the icon/label ink paints from the app-level
/// <c>{DynamicResource BrandLink}</c> palette (<see cref="BrandPalette"/>), which
/// re-resolves on both source swap and theme change — so this class no longer touches TextColor
/// (a local set would have displaced the dynamic reference).
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
    private SourceRegistry? _registry;

    public SourceLinkButton()
    {
        InitializeComponent();
        Loaded += OnSourceLoaded;
        Unloaded += OnSourceUnloaded;
    }

    private void OnSourceLoaded(object? sender, EventArgs e)
    {
        var services = IPlatformApplication.Current?.Services;
        _registry = services?.GetService<SourceRegistry>();
        var provider = services?.GetService<IBrandTokenProvider>();

        ApplyName(_registry);
        if (provider is not null && _tokens is null)
        {
            _tokens = provider;
            // Ink itself now repaints through the DynamicResource palette; this subscription keeps
            // the descriptor-driven LABEL in sync with the morphed identity.
            _tokens.TokensSwapped += OnTokensSwapped;
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

    private void OnTokensSwapped(BrandTokenSet set) => ApplyName(_registry);

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
}
