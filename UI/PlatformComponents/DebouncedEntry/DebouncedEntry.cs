using System.Windows.Input;
using Microsoft.Maui.Controls;
using MostaqlK.Core;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Extends <see cref="AppEntry"/> with keystroke debouncing: raises
/// <see cref="DebouncedTextChanged"/> (and invokes <see cref="DebouncedCommand"/>) only after
/// <see cref="DebounceMilliseconds"/> have elapsed with no further typing, using a shared
/// <see cref="Debouncer"/> so every keystroke cancels the previous pending fire and schedules a
/// fresh one.
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

    private readonly Debouncer _debouncer = new(TimeSpan.FromMilliseconds(300));

    /// <summary>How long to wait, after the last keystroke, before raising the debounced event.</summary>
    public int DebounceMilliseconds
    {
        get => (int)GetValue(DebounceMillisecondsProperty);
        set => SetValue(DebounceMillisecondsProperty, value);
    }

    /// <summary>Optional command invoked with the current <c>Text</c> once the debounce window elapses.</summary>
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
        // Keep the shared helper's delay in sync with the (bindable) property — callers may
        // change DebounceMilliseconds at runtime without reconstructing this entry.
        _debouncer.SetDelay(TimeSpan.FromMilliseconds(DebounceMilliseconds));
        _debouncer.Schedule(ct => FireDebouncedAsync(e, ct));
    }

    [MostaqlK.Core.ErrorOutcome(MostaqlK.Core.ErrorOutcome.Ignored, Label = "DebouncedEntry_RestartOnKeystroke")]
    private Task FireDebouncedAsync(TextChangedEventArgs e, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        DebouncedTextChanged?.Invoke(this, e);

        if (DebouncedCommand?.CanExecute(e.NewTextValue) == true)
        {
            DebouncedCommand.Execute(e.NewTextValue);
        }

        return Task.CompletedTask;
    }
}
