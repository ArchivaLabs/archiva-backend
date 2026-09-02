namespace Archiva.Domain.Entities;

public class Meeting : BaseAuditableEntity
{
    public string? Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime MeetingDate { get; set; }
    public TimeSpan MeetingTime { get; set; }
    public string? Location { get; set; }
    public int OrganizationId { get; set; }
    public string? CreatedByAvatar { get; set; }
    public string? CreatedById { get; init; }
    public IList<MeetingTag> Tags { get; set; } = new List<MeetingTag>();
    public IList<Document> Documents { get; set; } = new List<Document>();
}
