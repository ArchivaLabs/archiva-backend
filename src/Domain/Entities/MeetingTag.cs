namespace Archiva.Domain.Entities;

public class MeetingTag
{
    public int MeetingId { get; set; }
    public Meeting Meeting { get; set; } = null!;

    public int TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}
