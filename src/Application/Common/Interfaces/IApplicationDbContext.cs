using Archiva.Domain.Entities;

namespace Archiva.Application.Common.Interfaces;

public interface IApplicationDbContext
{
    DbSet<Meeting> Meetings { get; }
    DbSet<Document> Documents { get; }
    DbSet<Tag> Tags { get; }
    DbSet<MeetingTag> MeetingTags { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationUser> OrganizationUsers { get; }
    DbSet<UserInvitation> UserInvitations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
