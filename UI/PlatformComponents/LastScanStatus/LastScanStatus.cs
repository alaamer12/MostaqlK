using System.Windows.Input;
using MostaqlK.Core.Formatting;
using MostaqlK.Services.Diagnostics;

namespace MostaqlK.UI.PlatformComponents.LastScanStatus;

/// <summary>
/// The one "آخر فحص: منذ لحظات" readout, shared by the projects footer and the pipeline dashboard's
/// discovery card. Both places used to hand-roll their own label, their own relative-time wording
/// and their own ticking timer, which is how they ended up disagreeing with each other; this unit
/// owns all three. It formats through <see cref="LastScanText"/> and re-words itself once a second
/// so the elapsed figure stays honest without the host having to push updates.
/// </summary>
public partial class LastScanStatus : ContentView
{
    private readonly IDispatcherTimer? _timer;

    /// <summary>When the last scan completed - bind to <c>GlobalStatus.LastScanCompletedAt</c>.</summary>
    public static readonly BindableProperty LastScanAtProperty = BindableProperty.Create(
        nameof(LastScanAt), typeof(DateTimeOffset?), typeof(LastScanStatus), null,
        propertyChanged: OnStateChanged);

    /// <summary>Shows the ↻ affordance; only the footer needs it.</summary>
    public static readonly BindableProperty ShowRefreshProperty = BindableProperty.Create(
        nameof(ShowRefresh), typeof(bool), typeof(LastScanStatus), false,
        propertyChanged: OnShowRefreshChanged);

    public static readonly BindableProperty RefreshCommandProperty = BindableProperty.Create(
        nameof(RefreshCommand), typeof(ICommand), typeof(LastScanStatus), null);

    public static readonly BindableProperty FontSizeProperty = BindableProperty.Create(
        nameof(FontSize), typeof(double), typeof(LastScanStatus), 11.0);

    /// <summary>Defaults to the muted metadata slate both hosts already use.</summary>
    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor), typeof(Color), typeof(LastScanStatus), Color.FromArgb("#64748B"));

    /// <summary>
    /// Set on the inner <see cref="Label"/>, not on this view: the UI tests read the text of
    /// <c>Projects_LastScanLabel</c>, and an id on the wrapper would not surface that.
    /// </summary>
    public static readonly BindableProperty LabelAutomationIdProperty = BindableProperty.Create(
        nameof(LabelAutomationId), typeof(string), typeof(LastScanStatus), null,
        propertyChanged: OnLabelAutomationIdChanged);

    public static readonly BindableProperty RefreshAutomationIdProperty = BindableProperty.Create(
        nameof(RefreshAutomationId), typeof(string), typeof(LastScanStatus), null,
        propertyChanged: OnRefreshAutomationIdChanged);

    public DateTimeOffset? LastScanAt
    {
        get => (DateTimeOffset?)GetValue(LastScanAtProperty);
        set => SetValue(LastScanAtProperty, value);
    }

    public bool ShowRefresh
    {
        get => (bool)GetValue(ShowRefreshProperty);
        set => SetValue(ShowRefreshProperty, value);
    }

    public ICommand? RefreshCommand
    {
        get => (ICommand?)GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Color? TextColor
    {
        get => (Color?)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public string? LabelAutomationId
    {
        get => (string?)GetValue(LabelAutomationIdProperty);
        set => SetValue(LabelAutomationIdProperty, value);
    }

    public string? RefreshAutomationId
    {
        get => (string?)GetValue(RefreshAutomationIdProperty);
        set => SetValue(RefreshAutomationIdProperty, value);
    }

    public LastScanStatus()
    {
        InitializeComponent();
        ApplyText();

        _timer = Dispatcher.CreateTimer();
        if (_timer is not null)
        {
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (_, _) => ApplyText();
        }
    }

    /// <summary>
    /// The re-wording tick only runs while the view is actually realised: an off-screen copy of the
    /// readout has nothing to say, and a stray timer on a detached view is exactly the kind of
    /// unmanaged loop the pipeline UI conventions rule out.
    /// </summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            _timer?.Stop();
            return;
        }

        ApplyText();
        _timer?.Start();
    }

    private static void OnStateChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((LastScanStatus)bindable).ApplyText();

    private static void OnShowRefreshChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((LastScanStatus)bindable).RefreshHost.IsVisible = (bool)newValue;

    private static void OnLabelAutomationIdChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((LastScanStatus)bindable).TextLabel.AutomationId = (string?)newValue ?? string.Empty;

    private static void OnRefreshAutomationIdChanged(BindableObject bindable, object oldValue, object newValue) =>
        ((LastScanStatus)bindable).RefreshButton.AutomationId = (string?)newValue ?? string.Empty;

    private void ApplyText()
    {
        var text = LastScanText.Labelled(LastScanAt);

        // Only assign on change: this runs every second, and a needless Text write costs a layout
        // pass on the whole footer/panel row.
        if (!string.Equals(TextLabel.Text, text, StringComparison.Ordinal))
        {
            TextLabel.Text = text;
        }
    }

    [TraceInteraction("LastScanStatus_Refresh")]
    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Rethrown, Label = "LastScanStatus_Refresh")]
    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        using var _ = TraceScope.Begin("LastScanStatus_Refresh");
        try
        {
            var command = RefreshCommand;
            if (command?.CanExecute(null) == true)
            {
                command.Execute(null);
            }
        }
        catch (Exception ex)
        {
            _.MarkFaulted(ex);
            throw;
        }
    }
}
