using MostaqlK.Core;

namespace MostaqlK.Infrastructure.Notifications;

/// <summary>
/// Error factory for notification delivery failures. Reuses the "UI" domain since
/// toast delivery is a user-facing concern (see <see cref="ErrorCodeRegistry"/>).
/// </summary>
public static class NotificationErrors
{
    public static DomainError ToastDeliveryFailed(Exception cause) => new(
        Code: "UI-001",
        InternalMessage: $"Failed to deliver Windows toast: {cause.Message}",
        ExternalMessage: "تعذر إرسال إشعار.",
        Cause: cause);
}
