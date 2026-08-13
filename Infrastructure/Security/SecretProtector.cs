using System.Security.Cryptography;
using System.Text;

namespace MostaqlK.Infrastructure.Security;

/// <summary>
/// Encrypts small secrets (currently only the Mostaql session cookie) before they are written to
/// the local SQLite file, so that opening <c>mostaqlk.db</c> with any SQLite browser shows an
/// opaque blob rather than a usable session.
/// <para>
/// Key material is never stored next to the ciphertext: on Windows the key is derived by the OS
/// from the logged-in user account via DPAPI (<see cref="DataProtectionScope.CurrentUser"/>), so
/// copying the database to another machine - or opening it as another Windows user - makes the
/// blob undecryptable. A per-app <see cref="Entropy"/> value is mixed in so another application
/// running as the same user cannot decrypt it by accident either.
/// </para>
/// <para>
/// Non-Windows platforms are not a V1 target; there the code falls back to an AES-GCM key derived
/// from machine + user identifiers, which is obfuscation (the inputs are discoverable) rather than
/// real protection. <see cref="IsHardwareBacked"/> tells the UI which of the two is in effect.
/// </para>
/// </summary>
public static class SecretProtector
{
    /// <summary>App-specific secondary entropy - changing it invalidates every stored secret.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MostaqlK.v1.secret-store");

    /// <summary>True when the OS keystore (DPAPI) protects the secret rather than the derived-key fallback.</summary>
    public static bool IsHardwareBacked => OperatingSystem.IsWindows();

    /// <summary>Encrypts <paramref name="plaintext"/> and returns a Base64 blob safe to store as TEXT.</summary>
    public static string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);

        if (OperatingSystem.IsWindows())
        {
            return Convert.ToBase64String(ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser));
        }

        return Convert.ToBase64String(FallbackEncrypt(bytes));
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
            var plain = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser)
                : FallbackDecrypt(cipher);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return null;
        }
    }

    // ---- non-Windows fallback: AES-GCM under a key derived from machine + user identity ----

    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static byte[] FallbackEncrypt(byte[] plain)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(DeriveFallbackKey(), TagSize);
        aes.Encrypt(nonce, plain, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        cipher.CopyTo(output, NonceSize + TagSize);
        return output;
    }

    private static byte[] FallbackDecrypt(byte[] blob)
    {
        if (blob.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Secret blob is too short to be valid.");
        }

        var nonce = blob.AsSpan(0, NonceSize);
        var tag = blob.AsSpan(NonceSize, TagSize);
        var cipher = blob.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(DeriveFallbackKey(), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return plain;
    }

    private static byte[] DeriveFallbackKey()
    {
        var material = string.Join(
            '|',
            Environment.MachineName,
            Environment.UserName,
            Environment.OSVersion.Platform.ToString());

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(material),
            outputLength: 32,
            salt: Entropy,
            info: Encoding.UTF8.GetBytes("cookie"));
    }
}
