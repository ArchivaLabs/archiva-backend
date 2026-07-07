using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;

namespace Archiva.Application.Auth.Command.SyncUser;

public record SyncUserCommand : IRequest<SyncUserResult>
{
    public string UserId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? AvatarUrl { get; init; }
}

public record SyncUserResult
{
    public string Status { get; init; } = string.Empty;
    public int? OrganizationId { get; init; }
    public string? DisplayName { get; init; }
    public string? Email { get; init; }
    public string? AvatarUrl { get; init; }
    public string? OrganizationName { get; init; }
    public string? OrganizationUrl { get; init; }
    public string? Role { get; init; }
    public string? UserId { get; init; }
}

// Handler
public class SyncUserCommandHandler : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    private readonly IApplicationDbContext _context;

    public SyncUserCommandHandler(IApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<SyncUserResult> Handle(
        SyncUserCommand request,
        CancellationToken cancellationToken
    )
    {
        // Check if user already exists in the organization
        var existingMember = await _context
            .OrganizationUsers.Include(u => u.Organization)
            .FirstOrDefaultAsync(
                u => u.Email == request.Email || u.UserId == request.UserId,
                cancellationToken
            );

        // If it's an existing user, log them in.
        if (existingMember is not null)
        {
            return new SyncUserResult
            {
                Status = "existing",
                OrganizationId = existingMember.OrganizationId,
                OrganizationName = existingMember.Organization.Name,
                OrganizationUrl = existingMember.Organization.LogoUrl,
                Role = existingMember.Role.ToString(),
                UserId = existingMember.UserId,
                DisplayName = existingMember.UserName,
                AvatarUrl = existingMember.AvatarUrl,
                Email = existingMember.Email,
            };
        }

        // Check if the user has a pending invitation
        var invitation = await _context
            .UserInvitations.Include(i => i.Organization)
            .FirstOrDefaultAsync(
                i => i.Email == request.Email && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow,
                cancellationToken
            );

        // If the user has a pending invitation, invite them.
        if (invitation is not null)
        {
            var member = new OrganizationUser
            {
                UserId = request.UserId,
                Email = request.Email,
                UserName = request.DisplayName,
                AvatarUrl = request.AvatarUrl,
                OrganizationId = invitation.OrganizationId,
                Organization = invitation.Organization,
                Role = invitation.Role,
                JoinedAt = DateTime.UtcNow,
            };

            invitation.IsAccepted = true;
            _context.OrganizationUsers.Add(member);
            await _context.SaveChangesAsync(cancellationToken);

            return new SyncUserResult
            {
                Status = "invited",
                OrganizationId = invitation.OrganizationId,
                Role = UserRole.User.ToString(),
                UserId = member.UserId,
                DisplayName = request.DisplayName,
                Email = request.Email,
                AvatarUrl = request.AvatarUrl,
                OrganizationName = invitation.Organization.Name,
                OrganizationUrl = invitation.Organization.LogoUrl,
            };
        }

        // If the user is a brand new user, they would need to create their organization.
        return new SyncUserResult { Status = "new" };
    }
}
