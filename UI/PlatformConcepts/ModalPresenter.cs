using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Overlay/modal presentation surface. Structurally different per platform: a bottom sheet on
/// mobile vs a dialog/popup on desktop.
/// Windows (V1): stood in with a modal <see cref="ContentPage"/> pushed onto the navigation
/// stack (<c>Navigation.PushModalAsync</c>), the closest idiomatic MAUI equivalent to a desktop
/// dialog without pulling in the CommunityToolkit Popup dependency.
/// </summary>
public static class ModalPresenter
{
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: null, // TODO: BottomSheet — added only when V3 mobile work starts.
        ios: null, // TODO: BottomSheet — added only when V3 mobile work starts.
        windows: CreateDialog,
        macCatalyst: null); // TODO: Dialog/Popup-equivalent — added only when V3 mobile work starts.

    private static View CreateDialog()
    {
        // Windows "Dialog" stand-in: a centered content container. TODO: replace with a real
        // modal ContentPage pushed via PushModalAsync, or Popup, once a concrete dialog use
        // case (e.g. confirm/settings) is implemented.
        return new ContentView();
    }
}
