using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

namespace Archiva.Infrastructure.Storage;

/// <summary>
/// Singleton cache for the Azure user delegation key used to sign SAS tokens.
/// BlobStorageService is scoped, so the cache cannot live on it — it lives here.
///
/// A user delegation key is valid for up to 7 days. We use a 6-hour lifetime
/// and renew 30 minutes before expiry so no in-flight request ever hits a
/// stale key. The TimeProvider injection allows unit tests to control time.
/// </summary>
public sealed class UserDelegationKeyProvider
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private UserDelegationKey? _cachedKey;
    private DateTimeOffset _keyExpiresOn;

    // How long each delegation key is valid
    private static readonly TimeSpan KeyLifetime = TimeSpan.FromHours(6);

    // Renew the key this long before it expires to avoid races
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(30);

    public UserDelegationKeyProvider(BlobServiceClient blobServiceClient, TimeProvider timeProvider)
    {
        _blobServiceClient = blobServiceClient;
        _timeProvider = timeProvider;
    }

    public async Task<UserDelegationKey> GetKeyAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();

        // Fast path — key is still fresh
        if (_cachedKey is not null && now < _keyExpiresOn - RenewalMargin)
            return _cachedKey;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            // Re-check inside the lock — another thread may have renewed it
            now = _timeProvider.GetUtcNow();
            if (_cachedKey is not null && now < _keyExpiresOn - RenewalMargin)
                return _cachedKey;

            var startsOn = now.AddMinutes(-5); // small backdated window for clock skew
            var expiresOn = now.Add(KeyLifetime);

            var response = await _blobServiceClient.GetUserDelegationKeyAsync(
                startsOn,
                expiresOn,
                cancellationToken
            );

            _cachedKey = response.Value;
            _keyExpiresOn = expiresOn;

            return _cachedKey;
        }
        finally
        {
            _lock.Release();
        }
    }
}
