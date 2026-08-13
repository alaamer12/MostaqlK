using MostaqlK.Core;

namespace MostaqlK.Infrastructure.Database;

/// <summary>
/// Key/value store for the handful of user-supplied secrets the app has to remember between
/// runs (currently only the Mostaql session cookie). Values are always written encrypted -
/// see <see cref="MostaqlK.Infrastructure.Security.SecretProtector"/>.
/// </summary>
public interface ISecretRepository
{
    /// <summary>Returns the decrypted value, or <c>null</c> when absent/undecryptable.</summary>
    Task<Result<string?>> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Encrypts and upserts <paramref name="value"/> under <paramref name="key"/>.</summary>
    Task<Result<bool>> SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes the stored secret, if any.</summary>
    Task<Result<bool>> DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>When the secret was last written, for the settings screen's status line.</summary>
    Task<Result<DateTime?>> GetUpdatedAtAsync(string key, CancellationToken cancellationToken = default);
}
