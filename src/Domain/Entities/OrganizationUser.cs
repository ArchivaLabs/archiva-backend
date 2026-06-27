namespace Archiva.Domain.Entities;

public class OrganizationUser
{
    public int OrganizationId { get; set; }
    public Organization Organization { get; set; } = null!;

    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? AvatarUrl { get; set; }
    public UserRole Role { get; set; }
    public DateTime JoinedAt { get; set; }
}
