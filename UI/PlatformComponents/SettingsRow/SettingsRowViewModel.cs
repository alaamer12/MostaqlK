using System.Windows.Input;
using MostaqlK.Core.Sources;

namespace MostaqlK.UI.PlatformComponents.SettingsRow;

/// <summary>
/// One composed <see cref="SettingsDescriptor"/> bound to its live value. The row owns reading,
/// writing and its own validation/error state so the settings page is a list of these and nothing
/// else — adding or removing a descriptor changes the list, never the page.
/// </summary>
public sealed class SettingsRowViewModel
{
    public SettingsRowViewModel(
        SettingsDescriptor descriptor,
        Func<object?>? read = null,
        Action<object?>? write = null,
        ICommand? command = null,
        string? valueText = null)
    {
        Descriptor = descriptor;
        _read = read;
        _write = write;
        Command = command;
        _valueText = valueText;
    }

    public SettingsDescriptor Descriptor { get; }

    public ICommand? Command { get; }

    /// <summary>ReadOnlyStat / LinkOut caption (e.g. "98 مشروعًا").</summary>
    public string? ValueText => _valueText;
    private string? _valueText;

    private readonly Func<object?>? _read;
    private readonly Action<object?>? _write;

    private string? _errorText;
    public string? ErrorText
    {
        get => _errorText;
        set
        {
            if (_errorText == value) return;
            _errorText = value;
            ErrorChanged?.Invoke();
        }
    }

    /// <summary>Set by the host to repaint the inline error label.</summary>
    public event Action? ErrorChanged;

    public bool HasError => !string.IsNullOrEmpty(ErrorText);

    public object? Read() => _read?.Invoke();

    public void Write(object? value) => _write?.Invoke(value);

    public void SetValueText(string? text)
    {
        _valueText = text;
        ValueTextChanged?.Invoke();
    }

    public event Action? ValueTextChanged;
}
