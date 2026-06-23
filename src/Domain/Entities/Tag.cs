namespace Archiva.Domain.Entities;

public class Tag : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid OrganizationId { get; set; }
    public IList<MeetingTag> MeetingTags { get; set; } = new List<MeetingTag>();
}
