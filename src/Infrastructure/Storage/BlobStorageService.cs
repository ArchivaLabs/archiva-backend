using Archiva.Application.Common.Interfaces;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Archiva.Infrastructure.Storage;

public class BlobStorageService : IStorageService
{
    private const string ContainerName = "documents";
    private readonly BlobServiceClient _blobServiceClient;

    public BlobStorageService(BlobServiceClient blobServiceClient)
    {
        _blobServiceClient = blobServiceClient;
    }

    public async Task<(string BlobUrl, string BlobName)> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    )
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);

        // Ensure the container exists, create it if it doesn't
        await containerClient.CreateIfNotExistsAsync(
            PublicAccessType.Blob,
            cancellationToken: cancellationToken
        );

        // Prefix with a GUID to avoid name collisions for files with the same name
        var blobName = $"{Guid.NewGuid():N}/{fileName}";
        var blobClient = containerClient.GetBlobClient(blobName);

        await blobClient.UploadAsync(
            fileStream,
            new BlobHttpHeaders { ContentType = contentType },
            cancellationToken: cancellationToken
        );

        return (blobClient.Uri.ToString(), blobName);
    }

    public async Task DeleteAsync (string blobName, CancellationToken cancellationToken = default)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(ContainerName);
        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);
    }
}
