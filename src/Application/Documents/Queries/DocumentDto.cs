namespace Archiva.Application.Documents.Queries;

public record DocumentDto
{
    public int Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public long FileSizeInBytes { get; init; }
    public string BlobUrl { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? UploadedBy { get; init; }
    public DateTimeOffset Created { get; init; }
}
