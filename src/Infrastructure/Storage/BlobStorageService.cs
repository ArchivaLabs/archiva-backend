using Archiva.Application.Common.Interfaces;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;

namespace Archiva.Infrastructure.Storage;

public class BlobStorageService : IStorageService
{
    private const string ContainerName = "documents";

    // SAS token lifetime — 15 minutes covers "sees list → clicks link".
    // The backdated StartsOn prevents intermittent 403s when the storage
    // account clock is a few seconds ahead of the app server clock.
    private static readonly TimeSpan SasTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SasClockSkewBuffer = TimeSpan.FromMinutes(5);

    private readonly BlobServiceClient _blobServiceClient;
    private readonly UserDelegationKeyProvider _keyProvider;

    public BlobStorageService(
        BlobServiceClient blobServiceClient,
        UserDelegationKeyProvider keyProvider
    )
    {
        _blobServiceClient = blobServiceClient;
        _keyProvider = keyProvider;
    }

    public async Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    )
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);

        // Private access — no public URL, only SAS-signed reads.
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken
        );

        // GUID prefix prevents name collisions for files with the same name.
        var blobName = $"{Guid.NewGuid():N}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            fileStream,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken
        );

        // Return only the blob name — never a URL. The URL is minted
        // per-request in GetReadUrlAsync so it is always fresh.
        return blobName;
    }

    public async Task<string> GetReadUrlAsync(
        string blobName,
        CancellationToken cancellationToken = default
    )
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var startsOn = DateTimeOffset.UtcNow.Subtract(SasClockSkewBuffer);
        var expiresOn = DateTimeOffset.UtcNow.Add(SasTtl);

        var sasBuilder = new BlobSasBuilder
        {
            BlobContainerName = ContainerName,
            BlobName = blobName,
            Resource = "b",
            StartsOn = startsOn,
            ExpiresOn = expiresOn,
        };
        sasBuilder.SetPermissions(BlobSasPermissions.Read);

        if (blobClient.CanGenerateSasUri)
        {
            // Azurite — connection string carries the account key, sign directly.
            return blobClient.GenerateSasUri(sasBuilder).ToString();
        }
        else
        {
            // Azure — sign with a user delegation key (managed identity).
            var delegationKey = await _keyProvider.GetKeyAsync(cancellationToken);
            var sasQueryParams = sasBuilder.ToSasQueryParameters(
                delegationKey,
                _blobServiceClient.AccountName
            );
            var uriBuilder = new BlobUriBuilder(blobClient.Uri) { Sas = sasQueryParams };
            return uriBuilder.ToUri().ToString();
        }
    }

    public async Task DeleteAsync(string blobName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
