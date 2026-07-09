namespace Archiva.Application.Common.Interfaces;

public interface IStorageService
{
    Task<(string BlobUrl, string BlobName)> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    );

    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}
