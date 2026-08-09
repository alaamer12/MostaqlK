using System.Windows.Input;
using Microsoft.Maui.Controls;

namespace MostaqlK.UI.PlatformComponents;

/// <summary>
/// Concrete search box used across the app (project feed search, advanced search): extends
/// <see cref="DebouncedEntry"/> with a leading search icon and a trailing clear ("x") button.
/// The clear button is only shown while there is text to clear.
/// </summary>
public partial class SearchInputField : DebouncedEntry
{
    public static readonly BindableProperty ClearCommandProperty = BindableProperty.Create(
        nameof(ClearCommand),
        typeof(ICommand),
        typeof(SearchInputField));

    /// <summary>Invoked (in addition to clearing <see cref="Entry.Text"/>) when the clear ("x") button is pressed.</summary>
    public ICommand? ClearCommand
    {
        get => (ICommand?)GetValue(ClearCommandProperty);
        set => SetValue(ClearCommandProperty, value);
    }

    public SearchInputField()
    {
        Placeholder = "بحث في المشاريع...";
        // The search glyph + clear ("x") button are rendered by the platform partial
        // (see SearchInputField.Windows.cs) since MAUI's stock Entry has no native
        // leading/trailing icon slots on every platform.
    }

    /// <summary>Clears the current text and invokes <see cref="ClearCommand"/>, if any.</summary>
    public void Clear()
    {
        Text = string.Empty;
        if (ClearCommand?.CanExecute(null) == true)
        {
            ClearCommand.Execute(null);
        }
    }
}
