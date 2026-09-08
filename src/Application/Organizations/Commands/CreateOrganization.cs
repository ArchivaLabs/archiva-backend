using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using FluentValidation.Results;
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
            throw new ValidationException([
                new ValidationFailure(
                    nameof(CreateOrganizationCommand.Name),
                    $"You already belong to the organisation '{existingMembership.Organization.Name}'."
                ),
            ]);
        }

        // Create the Organization
        var newOrganization = new Organization { Name = request.Name, LogoUrl = request.LogoUrl };
        _context.Organizations.Add(newOrganization);
        await _context.SaveChangesAsync(cancellationToken);

        // Create the first user as the Admin of the organization
        var member = new OrganizationUser
        {
            OrganizationId = newOrganization.Id,
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
            OrganizationId = newOrganization.Id,
        });

        _context.Tags.AddRange(defaultTags);
        await _context.SaveChangesAsync(cancellationToken);

        return new CreateOrganizationResult
        {
            OrganizationId = newOrganization.Id,
            Role = UserRole.Admin.ToString(),
            UserId = userId,
            OrganizationName = newOrganization.Name,
            OrganizationLogoUrl = newOrganization.LogoUrl,
        };
    }
}
