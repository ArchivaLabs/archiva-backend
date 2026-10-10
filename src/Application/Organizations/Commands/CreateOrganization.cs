using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using FluentValidation.Results;
using Microsoft.Extensions.Logging;
using ValidationException = Archiva.Application.Common.Exceptions.ValidationException;

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
    public string OrganizationName { get; init; } = string.Empty;
    public string? OrganizationLogoUrl { get; init; }
}

// Handler
public class CreateOrganizationCommandHandler
    : IRequestHandler<CreateOrganizationCommand, CreateOrganizationResult>
{
    private readonly IApplicationDbContext _context;
    private readonly IUser _currentUser;
    private readonly ILogger<CreateOrganizationCommandHandler> _logger;

    // Default tags seeded for every now organization that is created.
    private static readonly string[] DefaultTags =
    [
        "Finance",
        "Legal",
        "HR",
        "Strategy",
        "Compliance",
        "Operations",
        "IT",
        "Procurement",
        "Marketing",
        "Executive",
        "Senate",
        "Faculty",
        "Research",
        "Audit",
        "Academic",
        "Governance",
        "Admissions",
        "Registry",
        "Examinations",
    ];

    public CreateOrganizationCommandHandler(
        IApplicationDbContext context,
        IUser currentUser,
        ILogger<CreateOrganizationCommandHandler> logger
    )
    {
        _context = context;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<CreateOrganizationResult> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken
    )
    {
        var userId =
            _currentUser.Id ?? throw new UnauthorizedAccessException("User is not authenticated");

        // A user belongs to exactly one organisation. Without this guard a repeated
        // call — a double submit, or a user who navigates back to onboarding —
        // creates a second organisation and a second membership row. Every other
        // handler resolves membership with an unordered FirstOrDefaultAsync, so a
        // user holding two memberships gets a non-deterministic organisation
        // context between requests: their meetings appear and vanish depending on
        // which row SQL happens to return.
        var existingMembership = await _context
            .OrganizationUsers.Include(u => u.Organization)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

        if (existingMembership is not null)
        {
            _logger.LogWarning(
                "Organization creation rejected: user {UserId} already belongs to organization {OrganizationId}",
                userId,
                existingMembership.OrganizationId
            );
            throw AlreadyMember(existingMembership.Organization.Name);
        }

        var newOrganization = new Organization { Name = request.Name, LogoUrl = request.LogoUrl };
        _context.Organizations.Add(newOrganization);

        var member = new OrganizationUser
        {
            Organization = newOrganization,
            UserId = userId,
            UserName = _currentUser.Name!,
            Email = _currentUser.Email!,
            Role = UserRole.Admin,
            JoinedAt = DateTime.UtcNow,
        };

        _context.OrganizationUsers.Add(member);

        var defaultTags = DefaultTags.Select(name => new Tag
        {
            Name = name,
            Organization = newOrganization,
        });

        _context.Tags.AddRange(defaultTags);
        try
        {
            // EF saves all three entity types in one implicit transaction.
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // The unique UserId index closes the race between the membership check
            // and insert. Only translate a failed save when the winner is visible.
            var concurrentMembership = await _context
                .OrganizationUsers.AsNoTracking()
                .Include(u => u.Organization)
                .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

            if (concurrentMembership is not null)
            {
                _logger.LogWarning(
                    ex,
                    "Concurrent organization creation rejected: user {UserId} belongs to organization {OrganizationId}",
                    userId,
                    concurrentMembership.OrganizationId
                );
                throw AlreadyMember(concurrentMembership.Organization.Name);
            }

            _logger.LogError(ex, "Organization creation failed for user {UserId}", userId);
            throw;
        }

        _logger.LogInformation(
            "Organization {OrganizationId} created with admin user {UserId}",
            newOrganization.Id,
            userId
        );

        return new CreateOrganizationResult
        {
            OrganizationId = newOrganization.Id,
            Role = UserRole.Admin.ToString(),
            UserId = userId,
            OrganizationName = newOrganization.Name,
            OrganizationLogoUrl = newOrganization.LogoUrl,
        };
    }

    private static ValidationException AlreadyMember(string organizationName) =>
        new([
            new ValidationFailure(
                nameof(CreateOrganizationCommand.Name),
                $"You already belong to the organisation '{organizationName}'."
            ),
        ]);
}
