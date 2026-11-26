using System.Reflection;
using Archiva.Application.Common.Interfaces;
using Archiva.Domain.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Archiva.Infrastructure.Data;

public class ApplicationDbContext : DbContext, IApplicationDbContext, IDataProtectionKeyContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<Meeting> Meetings => Set<Meeting>();
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentAnalysisUsage> DocumentAnalysisUsages => Set<DocumentAnalysisUsage>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<MeetingTag> MeetingTags => Set<MeetingTag>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationUser> OrganizationUsers => Set<OrganizationUser>();
    public DbSet<UserInvitation> UserInvitations => Set<UserInvitation>();

    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();
    public DbSet<GoogleSession> GoogleSessions => Set<GoogleSession>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<GoogleSession>().HasKey(session => session.Key);
        builder.Entity<GoogleSession>().Property(session => session.Key).HasMaxLength(64);
        builder.Entity<GoogleSession>().HasIndex(session => session.ExpiresAt);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}
