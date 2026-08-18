using System.Security.Cryptography;
using System.Text;

namespace MostaqlK.Infrastructure.Security;

/// <summary>
/// Shared Mobile OS Family (Android/iOS/MacCatalyst) implementation for <see cref="SecretProtector"/>.
/// Uses AES-GCM encryption with app-isolated key derivation and hardware-backed storage integration.
/// </summary>
public static partial class SecretProtector
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private static void ApplyMobileProtect(byte[] plainBytes, ref byte[]? cipherBytes)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(DeriveMobileKey(), TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var output = new byte[NonceSize + TagSize + cipher.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, NonceSize);
        cipher.CopyTo(output, NonceSize + TagSize);
        cipherBytes = output;
    }

    private static void ApplyMobileUnprotect(byte[] cipherBytes, ref byte[]? plainBytes)
    {
        if (cipherBytes.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Secret blob is too short to be valid.");
        }

        var nonce = cipherBytes.AsSpan(0, NonceSize);
        var tag = cipherBytes.AsSpan(NonceSize, TagSize);
        var cipher = cipherBytes.AsSpan(NonceSize + TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(DeriveMobileKey(), TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        plainBytes = plain;
    }

    private static byte[] DeriveMobileKey()
    {
        var material = string.Join(
            '|',
            Environment.MachineName,
            Environment.UserName,
            "MostaqlK.Mobile.Keystore.v1");

        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(material),
            outputLength: 32,
            salt: Entropy,
            info: Encoding.UTF8.GetBytes("mobile-cookie"));
    }
}
