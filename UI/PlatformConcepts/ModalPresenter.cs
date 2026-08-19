using Microsoft.Maui.Controls;
using MostaqlK.UI.PlatformComponents;

namespace MostaqlK.UI.PlatformConcepts;

/// <summary>
/// Overlay/modal presentation surface. Structurally different per platform: a bottom sheet on
/// mobile vs a dialog/popup on desktop.
/// Windows (V1): backs real confirmation dialogs via a native WinUI <c>ContentDialog</c> (not a
/// MAUI page/alert). Callers such as the window's <c>AppWindow.Closing</c> handler may run while
/// the window is already tearing down, so this cannot depend on MAUI's own navigation stack/Shell
/// being alive. Mobile branches stay TODO until V3.
/// <para>
/// Per the barrel/per-platform-file convention (see <c>cross-platform-ui-conventions.md</c>,
/// "no in-body <c>#if PLATFORM</c> outside <c>CurrentPlatform.cs</c>"), this shared shell carries
/// zero platform logic itself: the real dialog lives in <c>ModalPresenter.Windows.cs</c> and the
/// mobile stub lives in <c>ModalPresenter.Android.cs</c>/<c>ModalPresenter.MaciOS.cs</c> (the
/// latter shared by iOS + MacCatalyst) — all exporting the shared <c>_ModalPresenter.Mobile.cs</c>
/// stub, since every mobile platform agrees on the same "not implemented until V3" answer today.
/// </para>
/// </summary>
public static partial class ModalPresenter
{
    /// <summary>
    /// Optional view-factory shape selector kept for non-confirmation overlay hosts. Confirmation
    /// dialogs use <see cref="ShowConfirmationAsync"/> instead — they need a native window handle
    /// for <c>XamlRoot</c> and cannot be expressed as a plain <see cref="View"/>.
    /// </summary>
    public static readonly Func<View>? Current = PlatformSelect.For<Func<View>>(
        android: null, // TODO: BottomSheet — added only when V3 mobile work starts.
        ios: null, // TODO: BottomSheet — added only when V3 mobile work starts.
        windows: CreateDialogContainer,
        macCatalyst: null); // TODO: Dialog/Popup-equivalent — added only when V3 mobile work starts.

    /// <summary>Wording/behaviour for a single confirmation prompt. The Windows-only default-button
    /// choice lives in the <c>DefaultButton</c> partial property declared in
    /// <c>ModalPresenterWindowsDefaultButton</c> — see <c>ModalPresenter.Windows.cs</c>
    /// for its Windows value and <c>_ModalPresenter.Mobile.cs</c> for the mobile no-op.</summary>
    public sealed class ConfirmationOptions
    {
        public required string Title { get; init; }
        public required string Message { get; init; }
        public required string PrimaryButtonText { get; init; }
        public required string SecondaryButtonText { get; init; }

        /// <summary>Text for the "remember my choice" checkbox; omit to hide it entirely.</summary>
        public string? RememberCheckBoxText { get; init; }
    }

    /// <summary>
    /// <paramref name="IsSecondary"/> is true only when the user explicitly picked the secondary
    /// (destructive/exit) button — dismissing the dialog any other way (Primary, or closing it
    /// without a choice) is treated as the safe/non-destructive outcome.
    /// </summary>
    public sealed record ConfirmationResult(bool IsSecondary, bool Remember);

    private static View CreateDialogContainer()
    {
        // Generic overlay container for non-confirmation hosts. Confirmation prompts go through
        // ShowConfirmationAsync (native ContentDialog) instead.
        return new ContentView();
    }

    /// <summary>
    /// Shows a confirmation prompt for the current platform. On Windows this is a native WinUI
    /// <c>ContentDialog</c> (see <c>ModalPresenter.Windows.cs</c>); on Android/iOS it is today's
    /// safe/non-destructive stub (see <c>_ModalPresenter.Mobile.cs</c>). The parameter type stays
    /// platform-neutral (<see cref="object"/>) here so call sites never need an <c>#if</c> of their
    /// own — the Windows partial casts it back to <c>Microsoft.UI.Xaml.Window</c> internally.
    /// </summary>
    public static Task<ConfirmationResult> ShowConfirmationAsync(object? window, ConfirmationOptions options)
        => ShowConfirmationAsyncCore(window, options);

    /// <summary>Platform-specific implementation resolved at compile time by the <c>.Windows.cs</c>/<c>.Android.cs</c>/<c>.iOS.cs</c> partial for the current TFM.</summary>
    private static partial Task<ConfirmationResult> ShowConfirmationAsyncCore(object? window, ConfirmationOptions options);
}
