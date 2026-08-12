namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// A thin vertical divider the user can drag to resize the column next to it (the pipeline
/// dashboard panel in <c>MainWindowPage</c>). The handle owns the whole resize contract so call
/// sites never hand-roll pan maths: it clamps the dragged value between <see cref="Minimum"/> and
/// <see cref="Maximum"/>, so each section keeps a minimum width and panning simply stops there
/// instead of squeezing the content.
/// <para>
/// <see cref="Value"/> is two-way bindable and always holds the *live* width during a drag, which
/// makes the resize feel direct (no commit-on-release lag). <see cref="DragSign"/> exists because
/// the panel can sit on either side of the handle - and because the app is RTL, "drag left grows
/// the panel" is a per-call-site fact, not a global one.
/// </para>
/// </summary>
public partial class SplitterHandle : ContentView
{
    private const double HoverDuration = 140; // fast hover response, per the UI motion conventions

    private readonly BoxView _line;
    private double _valueAtDragStart;

    public SplitterHandle()
    {
        WidthRequest = 8;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Fill;
        BackgroundColor = Colors.Transparent;

        // A 1px rule inside an 8px hit area: the grab target has to be comfortable even though the
        // divider itself must read as the same hairline used everywhere else in the layout.
        _line = new BoxView
        {
            WidthRequest = 1,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill,
            Color = IdleColor
        };

        Content = new Grid { Children = { _line } };

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;
        GestureRecognizers.Add(pan);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += (_, _) => SetHovered(true);
        pointer.PointerExited += (_, _) => SetHovered(false);
        GestureRecognizers.Add(pointer);
    }

    private static Color IdleColor => Application.Current?.RequestedTheme == AppTheme.Dark
        ? Color.FromArgb("#1E293B")
        : Color.FromArgb("#E2E8F0");

    private static Color ActiveColor => Application.Current?.RequestedTheme == AppTheme.Dark
        ? Color.FromArgb("#5CA8DE")
        : Color.FromArgb("#2386C8");

    // ------------------------------------------------------------------ bindable resize contract

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(double), typeof(SplitterHandle), 320.0,
        defaultBindingMode: BindingMode.TwoWay,
        coerceValue: (b, v) => ((SplitterHandle)b).Clamp((double)v));

    /// <summary>Live width of the resized section. Updated continuously while dragging.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty MinimumProperty = BindableProperty.Create(
        nameof(Minimum), typeof(double), typeof(SplitterHandle), 240.0,
        propertyChanged: (b, _, _) => ((SplitterHandle)b).Reclamp());

    /// <summary>Smallest width the resized section may reach; panning stops here.</summary>
    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly BindableProperty MaximumProperty = BindableProperty.Create(
        nameof(Maximum), typeof(double), typeof(SplitterHandle), 560.0,
        propertyChanged: (b, _, _) => ((SplitterHandle)b).Reclamp());

    /// <summary>
    /// Largest width the resized section may reach. Hosts recompute this from the window width so
    /// the *other* section (the project feed) keeps its own minimum width too.
    /// </summary>
    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public static readonly BindableProperty DragSignProperty = BindableProperty.Create(
        nameof(DragSign), typeof(double), typeof(SplitterHandle), 1.0);

    /// <summary>
    /// <c>+1</c> when dragging toward larger X grows the section, <c>-1</c> when it shrinks it
    /// (the section sits on the left of the handle).
    /// </summary>
    public double DragSign
    {
        get => (double)GetValue(DragSignProperty);
        set => SetValue(DragSignProperty, value);
    }

    /// <summary>Raised once when a drag finishes, so hosts can persist the final width.</summary>
    public event EventHandler<double>? DragCompleted;

    // ------------------------------------------------------------------ interaction

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _valueAtDragStart = Value;
                SetHovered(true);
                break;

            case GestureStatus.Running:
                Value = Clamp(_valueAtDragStart + (e.TotalX * DragSign));
                break;

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                SetHovered(false);
                DragCompleted?.Invoke(this, Value);
                break;
        }
    }

    private void SetHovered(bool hovered)
    {
        _line.Color = hovered ? ActiveColor : IdleColor;
        _line.WidthRequest = hovered ? 2 : 1;
        _ = _line.FadeToAsync(hovered ? 1 : 0.9, (uint)HoverDuration, Easing.CubicOut);
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is not null)
        {
            // Windows: give the handle a west-east resize cursor so it advertises itself as
            // draggable before the user tries.
            ApplyPlatformCursor();
        }
    }

    private double Clamp(double value)
    {
        var min = Minimum;
        var max = Math.Max(min, Maximum);
        return Math.Clamp(double.IsNaN(value) ? min : value, min, max);
    }

    private void Reclamp() => Value = Clamp(Value);

    /// <summary>Implemented per OS (see <c>SplitterHandle.Windows.cs</c>); a no-op elsewhere.</summary>
    partial void ApplyPlatformCursor();
}
