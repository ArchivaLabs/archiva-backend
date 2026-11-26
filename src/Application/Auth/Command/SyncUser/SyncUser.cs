using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Archiva.Application.Auth.Command.SyncUser;

// Identity comes from the authenticated principal via IUser.
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

public class SyncUserCommandHandler(
    IApplicationDbContext context,
    IUser currentUser,
    ILogger<SyncUserCommandHandler> logger
) : IRequestHandler<SyncUserCommand, SyncUserResult>
{
    public async Task<SyncUserResult> Handle(
        SyncUserCommand request,
        CancellationToken cancellationToken
    )
    {
        var provider = currentUser.AuthenticationProvider;
        using var scope = logger.BeginScope(
            new Dictionary<string, object> { ["AuthenticationProvider"] = provider }
        );

        var userId = currentUser.Id;
        if (string.IsNullOrWhiteSpace(userId))
        {
            logger.LogWarning(
                "Auth sync denied: {Reason}",
                provider == "Microsoft" ? "MissingObjectId" : "MissingSubject"
            );
            throw new UnauthorizedAccessException("Required identity claim is missing");
        }

        var email = currentUser.Email?.Trim();
        if (string.IsNullOrWhiteSpace(email))
        {
            logger.LogWarning(
                "Auth sync denied: {Reason} for user {UserId}",
                "MissingEmail",
                userId
            );
            throw new UnauthorizedAccessException("Required identity claim is missing");
        }

        var existingMember = await FindMembership(userId, cancellationToken);
        if (existingMember is not null)
            return Report("existing", existingMember);

        // Email is only used to redeem invitations. Membership is keyed by the
        // authenticated subject supplied by IUser, including its Google namespace.
        var reason = "InvitationIneligible";
        if (currentUser.CanClaimInvitation)
        {
            var invitations = await FindPendingInvitations(
                email.ToUpperInvariant(),
                cancellationToken
            );

            if (invitations.Count == 1)
            {
                var invitation = invitations[0];
                var member = new OrganizationUser
                {
                    UserId = userId,
                    Email = email,
                    UserName = currentUser.Name ?? string.Empty,
                    Organization = invitation.Organization,
                    OrganizationId = invitation.OrganizationId,
                    Role = invitation.Role,
                    JoinedAt = DateTime.UtcNow,
                };

                invitation.IsAccepted = true;
                context.OrganizationUsers.Add(member);
                try
                {
                    // The invitation concurrency token and unique membership index
                    // guard both writes in EF's implicit SaveChanges transaction.
                    await context.SaveChangesAsync(cancellationToken);
                    return Report("invited", member);
                }
                catch (DbUpdateException ex)
                {
                    context.OrganizationUsers.Entry(member).State = EntityState.Detached;
                    context.UserInvitations.Entry(invitation).State = EntityState.Detached;

                    if (
                        ex is not DbUpdateConcurrencyException
                        || !ex.Entries.Any(entry => ReferenceEquals(entry.Entity, invitation))
                    )
                    {
                        existingMember = await FindMembership(userId, cancellationToken);
                        if (existingMember is not null)
                            return Report("existing", existingMember, "ConcurrentMembership");

                        logger.LogError(
                            "Auth sync failed for {AuthenticationProvider}. Error: {ErrorType}",
                            provider,
                            ex.GetType().Name
                        );
                        throw;
                    }

                    reason = "InvitationChanged";
                }
            }
            else
            {
                reason = invitations.Count == 0 ? "NoPendingInvitation" : "AmbiguousInvitation";
            }
        }

        // Membership may have appeared after the first lookup, even if there
        // is no remaining invitation to claim.
        existingMember = await FindMembership(userId, cancellationToken);
        if (existingMember is not null)
            return Report("existing", existingMember, "ConcurrentMembership");

        var status = reason == "AmbiguousInvitation" ? "invitationReview" : "new";
        logger.LogInformation(
            "Auth sync resolved {Status} for {AuthenticationProvider}. Reason: {Reason}",
            status,
            provider,
            reason
        );
        return new SyncUserResult
        {
            Status = status,
            UserId = userId,
            DisplayName = currentUser.Name ?? string.Empty,
            Email = email,
        };
    }

    private Task<OrganizationUser?> FindMembership(
        string userId,
        CancellationToken cancellationToken
    ) =>
        context
            .OrganizationUsers.AsNoTracking()
            .Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

    private async Task<List<UserInvitation>> FindPendingInvitations(
        string normalizedEmail,
        CancellationToken cancellationToken
    )
    {
        var candidates = context
            .UserInvitations.Where(i =>
                !i.IsAccepted
                && i.ExpiresAt > DateTime.UtcNow
                && i.Email.Trim().ToUpper() == normalizedEmail
            )
            .Include(i => i.Organization);

        // SQL collation can equate distinct addresses. Require an ordinal match
        // on the normalized address before treating an invitation as eligible.
        var matches = new List<UserInvitation>(capacity: 2);
        await foreach (
            var candidate in candidates.AsAsyncEnumerable().WithCancellation(cancellationToken)
        )
        {
            if (candidate.Email.Trim().ToUpperInvariant() == normalizedEmail)
                matches.Add(candidate);
            if (matches.Count == 2)
                break;
        }
        return matches;
    }

    private SyncUserResult Report(string status, OrganizationUser member, string? reason = null)
    {
        if (status == "existing" && reason is null)
        {
            logger.LogInformation(
                "Auth sync resolved existing membership for user {UserId} in organization {OrganizationId}",
                member.UserId,
                member.OrganizationId
            );
        }
        else
        {
            logger.LogInformation(
                "Auth sync resolved {Status} for {AuthenticationProvider} in organization {OrganizationId} with role {Role}. Reason: {Reason}",
                status,
                currentUser.AuthenticationProvider,
                member.OrganizationId,
                member.Role,
                reason
            );
        }
        return new SyncUserResult
        {
            Status = status,
            OrganizationId = member.OrganizationId,
            OrganizationName = member.Organization.Name,
            OrganizationUrl = member.Organization.LogoUrl,
            Role = member.Role.ToString(),
            UserId = member.UserId,
            DisplayName = member.UserName,
            AvatarUrl = member.AvatarUrl,
            Email = member.Email,
        };
    }
}
