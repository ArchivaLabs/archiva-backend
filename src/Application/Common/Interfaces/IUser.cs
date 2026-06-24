namespace Archiva.Application.Common.Interfaces;

public interface IUser
{
    string? Id { get; }
    string? Name { get; }
    string? Email { get; }
    string? OrganizationId { get; }
    List<string>? Roles { get; }
}
