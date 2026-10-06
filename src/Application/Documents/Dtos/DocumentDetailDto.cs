using Archiva.Domain.Enums;

namespace Archiva.Application.Documents.Dtos;

public record DocumentDetailDto
{
    public int Id { get; init; }
    public string FileName { get; init; } = string.Empty;
    public string FileType { get; init; } = string.Empty;
    public long FileSizeInBytes { get; init; }
    public string BlobUrl { get; init; } = string.Empty;
    public string? Description { get; init; }
    public string? UploadedBy { get; init; }
    public DateTimeOffset Created { get; init; }
    public int MeetingId { get; init; }
    public string MeetingTitle { get; init; } = string.Empty;
    public DateTime MeetingDate { get; init; }
    public List<string> Tags { get; init; } = [];
    public DocumentAnalysisStatus AnalysisStatus { get; init; }
    public string? AnalysisErrorCode { get; init; }
    public string? Summary { get; init; }
    public int BillableUnitCount { get; init; }
    public int SummaryInputCharacters { get; init; }
    public DateTimeOffset? AnalysisCompletedAt { get; init; }
}
