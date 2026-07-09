using Archiva.Application.Documents.Dtos;
using Archiva.Application.Documents.Queries;

namespace Archiva.Application.Meetings.Queries;

public record MeetingDetailDto
{
    public int Id { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateTime MeetingDate { get; init; }
    public TimeSpan MeetingTime { get; init; }
    public string? Location { get; init; }
    public string? CreatedBy { get; init; }
    public string? CreatedByAvatar { get; init; }
    public List<string> Tags { get; init; } = [];
    public List<DocumentDto> Documents { get; init; } = [];
    public DateTimeOffset Created { get; init; }
}
