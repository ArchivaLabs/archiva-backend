using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;

namespace Archiva.Application.Organizations.Commands.CreateOrganization;

public record CreateOrganizationCommand : IRequest<CreateOrganizationResult>
{
    public string Name { get; init; } = string.Empty;
    public string? LogoUrl { get; init; }
}

public record CreateOrganizationResult
{
    public int OrganizationId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string UserId { get; init; } = string.Empty;
}

// Handler
public class CreateOrganizationCommandHandler
    : IRequestHandler<CreateOrganizationCommand, CreateOrganizationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;

    public CreateOrganizationCommandHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<CreateOrganizationResult> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken
    )
    {
        // Create the Organization
        var newOrganization = new Organization { Name = request.Name, LogoUrl = request.LogoUrl };
        _context.Organizations.Add(newOrganization);
        await _context.SaveChangesAsync(cancellationToken);

        // Create the first user as the Admin of the organization
        var member = new OrganizationUser
        {
            OrganizationId = newOrganization.Id,
            UserId = _currentUser.Id!,
            UserName = _currentUser.Name!,
            Email = _currentUser.Email!,
            Role = UserRole.Admin,
            JoinedAt = DateTime.UtcNow,
        };

        _context.OrganizationUsers.Add(member);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateOrganizationResult
        {
            OrganizationId = newOrganization.Id,
            Role = UserRole.Admin.ToString(),
            UserId = _currentUser.Id!,
        };
    }
}
