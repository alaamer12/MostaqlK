using System.Security.Cryptography;
using System.Text;

namespace MostaqlK.Infrastructure.Security;

/// <summary>
/// Encrypts small secrets (currently only the Mostaql session cookie) before they are written to
/// the local SQLite file, so that opening <c>mostaqlk.db</c> with any SQLite browser shows an
/// opaque blob rather than a usable session.
/// <para>
/// Key material is never stored next to the ciphertext: on Windows the key is derived by the OS
/// from the logged-in user account via DPAPI (<c>DataProtectionScope.CurrentUser</c>), while on
/// mobile platforms (Android/iOS) it leverages hardware-backed keystores via SecureStorage/AES-GCM.
/// </para>
/// </summary>
public static partial class SecretProtector
{
    /// <summary>App-specific secondary entropy - changing it invalidates every stored secret.</summary>
    internal static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MostaqlK.v1.secret-store");

    /// <summary>True when hardware/OS-keystore backed protection is in effect for the current platform.</summary>
    public static partial bool IsHardwareBacked { get; }

    /// <summary>Encrypts <paramref name="plaintext"/> and returns a Base64 blob safe to store as TEXT.</summary>
    public static string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        return Convert.ToBase64String(PlatformProtect(bytes));
    }

    /// <summary>
    /// Reverses <see cref="Protect"/>. Returns <c>null</c> instead of throwing when the blob is
    /// corrupt or was written by a different user/machine - a wrong or foreign secret must degrade
    /// to "no cookie configured", never crash the pipeline at startup.
    /// </summary>
    public static string? TryUnprotect(string? cipherBase64)
    {
        if (string.IsNullOrWhiteSpace(cipherBase64))
        {
            return null;
        }

        try
        {
            var cipher = Convert.FromBase64String(cipherBase64);
            var plain = PlatformUnprotect(cipher);
            return plain != null ? Encoding.UTF8.GetString(plain) : null;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    static partial void PlatformProtectInternal(byte[] plainBytes, ref byte[]? cipherBytes);
    static partial void PlatformUnprotectInternal(byte[] cipherBytes, ref byte[]? plainBytes);

    private static byte[] PlatformProtect(byte[] plainBytes)
    {
        byte[]? result = null;
        PlatformProtectInternal(plainBytes, ref result);
        return result ?? throw new CryptographicException("Failed to protect secret on current platform.");
    }

    private static byte[] PlatformUnprotect(byte[] cipherBytes)
    {
        byte[]? result = null;
        PlatformUnprotectInternal(cipherBytes, ref result);
        return result ?? throw new CryptographicException("Failed to unprotect secret on current platform.");
    }
}
