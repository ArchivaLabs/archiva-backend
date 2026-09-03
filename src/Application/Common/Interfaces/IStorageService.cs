namespace Archiva.Application.Common.Interfaces;

public interface IStorageService
{
    /// <summary>
    /// Uploads a file stream to blob storage and returns the blob name.
    /// The blob name is what gets stored in the DB — never the URL, which
    /// changes between environments and expires when signed.
    /// </summary>
    Task<string> UploadAsync(
        Stream fileStream,
        string fileName,
        string contentType,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Returns a short-lived read URL for the given blob name.
    /// Under Azurite this is a direct SAS signed with the account key.
    /// In Azure this is a user delegation SAS — requires the app's managed
    /// identity to have Storage Blob Data Reader on the container.
    /// </summary>
    Task<string> GetReadUrlAsync(string blobName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a blob by its name.
    /// </summary>
    Task DeleteAsync(string blobName, CancellationToken cancellationToken = default);
}