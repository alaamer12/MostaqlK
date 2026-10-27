using MostaqlK.UI.PlatformComponents.SettingsRow;

namespace MostaqlK.Features.Settings.ViewModels;

/// <summary>
/// One rendered section of the settings page: an Arabic caption plus the rows the catalog composed
/// into it. The page is a list of these and nothing else — a section appears because descriptors
/// carry its <c>Section</c>, and it disappears when they stop doing so.
/// </summary>
public sealed class SettingsGroupViewModel(string headerAr, IReadOnlyList<SettingsRowViewModel> rows)
{
    /// <summary>Section caption straight from the catalog's section key (the Arabic name lives in data).</summary>
    public string HeaderAr { get; } = headerAr;

    public IReadOnlyList<SettingsRowViewModel> Rows { get; } = rows;
}
