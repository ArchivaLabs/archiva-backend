namespace Archiva.Domain.Entities;

public class UserInvitation : BaseAuditableEntity
{
    public string Email { get; set; } = string.Empty;
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;
    public UserRole Role { get; set; } = UserRole.User;
    public bool IsAccepted { get; set; }
    public DateTime ExpiresAt { get; set; }
}
