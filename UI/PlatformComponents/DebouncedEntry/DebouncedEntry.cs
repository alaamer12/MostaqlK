using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Extends <see cref="AppEntry"/> with keystroke debouncing: raises
/// <see cref="DebouncedTextChanged"/> (and invokes <see cref="DebouncedCommand"/>) only after
/// <see cref="DebounceMilliseconds"/> have elapsed with no further typing, using a
/// <see cref="CancellationTokenSource"/>-restart-on-keystroke pattern so every keystroke cancels
/// the previous pending fire and schedules a fresh one.
/// </summary>
public partial class DebouncedEntry : AppEntry
{
    public static readonly BindableProperty DebounceMillisecondsProperty = BindableProperty.Create(
        nameof(DebounceMilliseconds),
        typeof(int),
        typeof(DebouncedEntry),
        defaultValue: 300);

    public static readonly BindableProperty DebouncedCommandProperty = BindableProperty.Create(
        nameof(DebouncedCommand),
        typeof(ICommand),
        typeof(DebouncedEntry));

    private CancellationTokenSource? _debounceCts;

    /// <summary>How long to wait, after the last keystroke, before raising the debounced event.</summary>
    public int DebounceMilliseconds
    {
        get => (int)GetValue(DebounceMillisecondsProperty);
        set => SetValue(DebounceMillisecondsProperty, value);
    }

    /// <summary>Optional command invoked with the current <see cref="Entry.Text"/> once the debounce window elapses.</summary>
    public ICommand? DebouncedCommand
    {
        get => (ICommand?)GetValue(DebouncedCommandProperty);
        set => SetValue(DebouncedCommandProperty, value);
    }

    /// <summary>Raised once the debounce window elapses with no further keystrokes.</summary>
    public event EventHandler<TextChangedEventArgs>? DebouncedTextChanged;

    public DebouncedEntry()
    {
        TextChanged += OnTextChanged;
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        var cts = new CancellationTokenSource();
        _debounceCts = cts;

        _ = DebounceAndFireAsync(e, cts.Token);
    }

    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Ignored, Label = "DebouncedEntry_RestartOnKeystroke")]
    private async Task DebounceAndFireAsync(TextChangedEventArgs e, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceMilliseconds, cancellationToken);
        }
        catch (TaskCanceledException)
        {
            // Expected: a newer keystroke restarted the debounce window and cancelled this one.
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        DebouncedTextChanged?.Invoke(this, e);

        if (DebouncedCommand?.CanExecute(e.NewTextValue) == true)
        {
            DebouncedCommand.Execute(e.NewTextValue);
        }
    }
}
