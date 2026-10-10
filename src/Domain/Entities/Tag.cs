namespace Archiva.Domain.Entities;

public class Tag : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public IList<MeetingTag> MeetingTags { get; set; } = new List<MeetingTag>();
}
