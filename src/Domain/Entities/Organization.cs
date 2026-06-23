namespace Archiva.Domain.Entities;

public class Organization : BaseAuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string? LogoUrl { get; set; }
    public IList<OrganizationUser> Members { get; set; } = new List<OrganizationUser>();
}
