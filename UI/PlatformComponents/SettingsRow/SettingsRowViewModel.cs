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
        string? valueText = null,
        IReadOnlyDictionary<string, ICommand>? commands = null)
    {
        Descriptor = descriptor;
        _read = read;
        _write = write;
        Command = command;
        _commands = commands;
        _valueText = valueText;
    }

    public SettingsDescriptor Descriptor { get; }

    public ICommand? Command { get; }

    private readonly IReadOnlyDictionary<string, ICommand>? _commands;

    /// <summary>
    /// A composite row's extra commands (the cookie editor's open / save / clear), keyed by the role
    /// the renderer asks for. One <see cref="Command"/> cannot express a row with three buttons, and
    /// switching on a key here would move source knowledge back into shared code.
    /// </summary>
    public ICommand? CommandNamed(string role) =>
        _commands is not null && _commands.TryGetValue(role, out var command) ? command : Command;

    /// <summary>True while a composite row shows its expanded form (the cookie editor's paste field).</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            ExpandedChanged?.Invoke();
        }
    }

    private bool _isExpanded;

    public event Action? ExpandedChanged;

    /// <summary>Text the user is editing in an expanded composite (never the persisted value).</summary>
    public string EditText
    {
        get => _editText;
        set
        {
            if (_editText == value) return;
            _editText = value;
            EditTextChanged?.Invoke();
        }
    }

    private string _editText = string.Empty;

    public event Action? EditTextChanged;

    /// <summary>
    /// Whether the row draws the hairline above itself. The composition sets it (first row of a
    /// section and section headers do not), because that is a fact about position in the list, not
    /// about the setting — the renderer must not guess where it sits.
    /// </summary>
    public bool ShowTopDivider { get; init; } = true;

    /// <summary>
    /// The lines of a <see cref="SettingsShape.PlatformSwitches"/> row — one per registered platform,
    /// built by the composition from the source registry and <c>PlatformEnablement</c>. Empty for
    /// every other row. The renderer enumerates this and never asks which platforms exist.
    /// </summary>
    public IReadOnlyList<SettingsPlatformSwitchItem> PlatformItems { get; init; } = [];

    /// <summary>
    /// Raised when the user flips one platform's switch. The owner of the row decides whether the
    /// change is allowed (the at-least-one-enabled invariant lives in the composition, not here) and
    /// then rebuilds the row, so a refused flip repaints the switch back ON instead of lying about it.
    /// </summary>
    public event Action<SourceId, bool>? PlatformSwitchToggled;

    public void RaisePlatformSwitchToggled(SourceId source, bool enabled) => PlatformSwitchToggled?.Invoke(source, enabled);

    /// <summary>
    /// Timestamp a <see cref="SettingsShape.ReadOnlyStat"/> row hosts through the
    /// <c>LastScanStatus</c> unit (the one stat whose text keeps re-wording itself after the row is
    /// built). Null for every other row; settable because the host keeps it current.
    /// </summary>
    public DateTimeOffset? LastScanAt
    {
        get => _lastScanAt;
        set
        {
            if (_lastScanAt == value) return;
            _lastScanAt = value;
            LastScanAtChanged?.Invoke();
        }
    }

    private DateTimeOffset? _lastScanAt;

    public event Action? LastScanAtChanged;

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
