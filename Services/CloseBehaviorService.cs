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
    private const string RememberedKey = "close_behavior_remembered";
    private const string ActionKey = "close_behavior_action";

    /// <summary>
    /// Returns the remembered action, or <c>null</c> if the user never checked "remember my
    /// choice" (or the very first launch) — in which case the confirmation dialog must be shown.
    /// </summary>
    public virtual CloseAction? GetRememberedAction()
    {
        if (!Microsoft.Maui.Storage.Preferences.Get(RememberedKey, false))
        {
            return null;
        }

        var stored = Microsoft.Maui.Storage.Preferences.Get(ActionKey, nameof(CloseAction.MinimizeToTray));
        return Enum.TryParse<CloseAction>(stored, out var action) ? action : CloseAction.MinimizeToTray;
    }

    /// <summary>Stores <paramref name="action"/> so future X-button clicks skip the dialog entirely.</summary>
    public virtual void RememberAction(CloseAction action)
    {
        Microsoft.Maui.Storage.Preferences.Set(RememberedKey, true);
        Microsoft.Maui.Storage.Preferences.Set(ActionKey, action.ToString());
    }

    /// <summary>Clears the remembered choice, so the confirmation dialog is shown again next time.</summary>
    public virtual void ForgetRememberedAction()
    {
        Microsoft.Maui.Storage.Preferences.Remove(RememberedKey);
        Microsoft.Maui.Storage.Preferences.Remove(ActionKey);
    }
}
