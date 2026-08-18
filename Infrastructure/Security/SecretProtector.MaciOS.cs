namespace MostaqlK.Infrastructure.Security;

/// <summary>
/// iOS / MacCatalyst implementation of <see cref="SecretProtector"/>.
/// </summary>
public static partial class SecretProtector
{
    public static partial bool IsHardwareBacked => true;

    static partial void PlatformProtectInternal(byte[] plainBytes, ref byte[]? cipherBytes)
    {
        ApplyMobileProtect(plainBytes, ref cipherBytes);
    }

    static partial void PlatformUnprotectInternal(byte[] cipherBytes, ref byte[]? plainBytes)
    {
        ApplyMobileUnprotect(cipherBytes, ref plainBytes);
    }
}
