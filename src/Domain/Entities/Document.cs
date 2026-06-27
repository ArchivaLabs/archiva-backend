namespace Archiva.Domain.Entities;

public class Document : BaseAuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public string? ExtractedText { get; set; }
    public string? Description { get; set; }
    public int OrganizationId { get; set; }

    // Foreign Keys
    public int MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
}
