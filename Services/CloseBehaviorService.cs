using MostaqlK.Core.Sources;

namespace MostaqlK.Services;

/// <summary>
/// The action taken when the user clicks the native window's X (close) button.
/// </summary>
public enum CloseAction
{
    /// <summary>Hide the window (Avast-style "keep running in background"); the pipeline keeps polling and the tray icon stays.</summary>
    MinimizeToTray,

    /// <summary>Actually quit the process.</summary>
    Exit
}

/// <summary>
/// Persists the user's answer to the "close to tray vs. exit" confirmation dialog (see
/// <c>ExitConfirmationBox</c> in <c>UI/DesignSystem</c>), so a checked "remember my
/// choice" box makes every subsequent X-button click idempotent — it silently repeats the same
/// action instead of showing the dialog again. Deliberately platform-neutral (just
/// <see cref="Microsoft.Maui.Storage.Preferences"/> reads/writes) so the decision itself stays
/// testable without any WinUI dependency; the dialog lives in the Design System
/// (<c>ConfirmationBox</c>/<c>ExitConfirmationBox</c> via <c>ModalPresenter</c>) and the native
/// Closing/Hide wiring stays in <c>MauiProgram</c>.
/// </summary>
public class CloseBehaviorService
{
    /// <summary>
    /// The single settings path (per-source namespacing, legacy-flat fallback, the channel-rooted
    /// store). Reading <c>Microsoft.Maui.Storage.Preferences</c> directly here worked — the static
    /// default IS the file-backed store — but it put these two keys outside the one interface every
    /// other setting goes through, so the settings page rendered a row whose value a different code
    /// path owned.
    /// </summary>
    private readonly ISettingsRepository _settings;

    public CloseBehaviorService(ISettingsRepository settings) => _settings = settings;

    /// <summary>
    /// Returns the remembered action, or <c>null</c> if the user never checked "remember my
    /// choice" (or the very first launch) — in which case the confirmation dialog must be shown.
    /// </summary>
    public virtual CloseAction? GetRememberedAction()
    {
        if (!_settings.GetGlobal(SettingsKeys.CloseBehaviorRemembered, false))
        {
            return null;
        }

        var stored = _settings.GetGlobal(SettingsKeys.CloseBehaviorAction, nameof(CloseAction.MinimizeToTray));
        return Enum.TryParse<CloseAction>(stored, out var action) ? action : CloseAction.MinimizeToTray;
    }

    /// <summary>
    /// Stores <paramref name="action"/> so future X-button clicks skip the dialog entirely.
    /// Written synchronously (not through the deferred queue the settings page uses): this runs from
    /// the close dialog itself, milliseconds before the process is allowed to leave.
    /// </summary>
    public virtual void RememberAction(CloseAction action)
    {
        _settings.SetGlobal(SettingsKeys.CloseBehaviorRemembered, true);
        _settings.SetGlobal(SettingsKeys.CloseBehaviorAction, action.ToString());
    }

    /// <summary>Clears the remembered choice, so the confirmation dialog is shown again next time.</summary>
    public virtual void ForgetRememberedAction() => _settings.SetGlobal(SettingsKeys.CloseBehaviorRemembered, false);
}
