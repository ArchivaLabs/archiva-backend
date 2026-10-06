using Archiva.Domain.Enums;

namespace Archiva.Domain.Entities;

public class Document : BaseAuditableEntity
{
    public string FileName { get; set; } = string.Empty;
    public string BlobUrl { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSizeInBytes { get; set; }
    public string? ExtractedText { get; set; }
    public string? Summary { get; set; }
    public string? Description { get; set; }
    public DocumentAnalysisStatus AnalysisStatus { get; set; } = DocumentAnalysisStatus.Pending;
    public string? AnalysisErrorCode { get; set; }
    public DateTimeOffset? AnalysisStartedAt { get; set; }
    public DateTimeOffset? AnalysisCompletedAt { get; set; }
    public DateTimeOffset? AnalysisLeaseUntil { get; set; }
    public DateTimeOffset? AnalysisQueuedAt { get; set; }
    public DateTimeOffset? AnalysisLastAttemptAt { get; set; }
    public int AnalysisAttemptCount { get; set; }
    public int AnalysisUnitLimit { get; set; } = 20;
    public int BillableUnitCount { get; set; }
    public int SummaryInputCharacters { get; set; }
    public int SummaryInputCharacterLimit { get; set; } = 100_000;
    public int OrganizationId { get; set; }

    // Foreign Keys
    public int MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;
}
