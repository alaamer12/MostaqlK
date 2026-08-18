using System.Security.Cryptography;

namespace MostaqlK.Infrastructure.Security;

/// <summary>
/// Windows implementation of <see cref="SecretProtector"/> using DPAPI (<see cref="DataProtectionScope.CurrentUser"/>).
/// </summary>
public static partial class SecretProtector
{
    public static partial bool IsHardwareBacked => true;

    static partial void PlatformProtectInternal(byte[] plainBytes, ref byte[]? cipherBytes)
    {
        cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
    }

    static partial void PlatformUnprotectInternal(byte[] cipherBytes, ref byte[]? plainBytes)
    {
        plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
    }
}
