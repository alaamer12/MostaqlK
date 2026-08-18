using MostaqlK.UI.PlatformConcepts;

namespace MostaqlK.UI.DesignSystem;

/// <summary>
/// Shared base confirmation unit — the Design System "AppEntry"-equivalent of the confirmation
/// hierarchy. Thin wrapper around <see cref="ModalPresenter"/>'s native dialog capability:
/// callers supply wording (title/message/buttons/optional remember-checkbox text); this owns
/// nothing platform-specific itself. Specialize via thin wrappers such as
/// <see cref="ExitConfirmationBox"/> (mirroring <c>DebouncedEntry</c> → <c>SearchInputField</c>).
/// <para>
/// Per the barrel/per-platform-file convention (see <c>cross-platform-ui-conventions.md</c>,
/// "no in-body <c>#if PLATFORM</c> outside <c>CurrentPlatform.cs</c>"), this shared shell carries
/// zero platform logic itself: <see cref="ShowAsync"/>'s parameter type stays platform-neutral
/// (<see cref="object"/>) so call sites never need an <c>#if</c> of their own, and
/// <see cref="TryGetActiveNativeWindow"/> is resolved per platform in
/// <c>ConfirmationBox.Windows.cs</c>/<c>ConfirmationBox.Android.cs</c>/<c>ConfirmationBox.MaciOS.cs</c>.
/// </para>
/// </summary>
public static partial class ConfirmationBox
{
    /// <summary>
    /// <paramref name="IsSecondary"/> is true only when the user explicitly picked the secondary
    /// (destructive) button — any other dismissal is the safe/non-destructive outcome. Shape is
    /// identical to <see cref="ModalPresenter.ConfirmationResult"/>.
    /// </summary>
    public sealed record Result(bool IsSecondary, bool Remember);

    /// <summary>
    /// Shows a confirmation dialog on <paramref name="window"/> (the platform-native window
    /// handle — on Windows a <c>Microsoft.UI.Xaml.Window</c>, resolved via
    /// <see cref="TryGetActiveNativeWindow"/> where a call site doesn't already hold one; a no-op
    /// safe default on mobile until V3). Must be called on the UI thread.
    /// </summary>
    public static async Task<Result> ShowAsync(
        object? window,
        string title,
        string message,
        string primaryText,
        string secondaryText,
        string? rememberText = null)
    {
        var result = await ModalPresenter.ShowConfirmationAsync(
            window,
            new ModalPresenter.ConfirmationOptions
            {
                Title = title,
                Message = message,
                PrimaryButtonText = primaryText,
                SecondaryButtonText = secondaryText,
                RememberCheckBoxText = rememberText,
            });

        return new Result(result.IsSecondary, result.Remember);
    }

    /// <summary>Platform-specific implementation resolved at compile time by the <c>.Windows.cs</c>/<c>.Android.cs</c>/<c>.MaciOS.cs</c> partial for the current TFM.</summary>
    public static partial object? TryGetActiveNativeWindow();
}
