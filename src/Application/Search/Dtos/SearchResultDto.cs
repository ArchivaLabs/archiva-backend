namespace Archiva.Application.Search.Dtos;

public record SearchResultDto
{
    public int Id { get; init; }
    public string Type { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Snippet { get; init; } = string.Empty;
    public DateTime MeetingDate { get; init; }
    public string Source { get; init; } = string.Empty;
    public List<string> Tags { get; init; } = [];
    public string? FileType { get; init; }
    public int MeetingId { get; init; }
}

public record SearchResultsPageDto
{
    public List<SearchResultDto> Results { get; init; } = [];
    public int TotalCount { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}

public record SearchFilterOptionsDto
{
    public List<string> Tags { get; init; } = [];
}
