using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;

namespace Archiva.Application.Auth.Command.SyncUser;

// Empty command — all identity fields come from the validated JWT via IUser,
// not from the request body. This closes the org-membership enumeration oracle
// and the caller-supplied UserId injection vulnerability.
public record SyncUserCommand : IRequest<SyncUserResult>;

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
    private readonly IUser _currentUser;

    public SyncUserCommandHandler(IApplicationDbContext context, IUser currentUser)
    {
        _context = context;
        _currentUser = currentUser;
    }

    public async Task<SyncUserResult> Handle(
        SyncUserCommand request,
        CancellationToken cancellationToken
    )
    {
        // All identity fields come from the validated Microsoft JWT — not the
        // request body. An unauthenticated caller cannot supply a fake userId
        // or email because they would fail JWT validation before reaching here.
        var userId = _currentUser.Id!;
        var email = _currentUser.Email!;
        var displayName = _currentUser.Name ?? string.Empty;

        // Membership is keyed on the Entra object id alone — never on email.
        // Email is a mutable, tenant-controlled claim, so matching on it would
        // hand an existing member's organisation to anyone whose token happened
        // to carry the same preferred_username. It also disagreed with every
        // other handler (all of which resolve membership by UserId), producing
        // a user who synced as "existing" but was rejected by every other
        // endpoint. Email->identity binding belongs to the invitation flow below,
        // which stamps the real UserId onto the row when the invite is accepted.
        var existingMember = await _context
            .OrganizationUsers.Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

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
                i => i.Email == email && !i.IsAccepted && i.ExpiresAt > DateTime.UtcNow,
                cancellationToken
            );

        if (invitation is not null)
        {
            var member = new OrganizationUser
            {
                UserId = userId,
                Email = email,
                UserName = displayName,
                AvatarUrl = null, // Microsoft Graph photo not fetched at this stage
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
                DisplayName = displayName,
                Email = email,
                AvatarUrl = null,
                OrganizationName = invitation.Organization.Name,
                OrganizationUrl = invitation.Organization.LogoUrl,
            };
        }

        // Brand new user — needs to create their organisation. Identity fields are
        // still returned: the caller is authenticated, they simply have no
        // membership yet. Omitting UserId here left the client with no identity to
        // store, so it could not tell "signed in, not yet onboarded" apart from
        // "signed out" and bounced the user between onboarding and login.
        return new SyncUserResult
        {
            Status = "new",
            UserId = userId,
            DisplayName = displayName,
            Email = email,
        };
    }
}
