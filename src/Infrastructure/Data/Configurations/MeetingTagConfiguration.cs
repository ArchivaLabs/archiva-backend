using Archiva.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Archiva.Infrastructure.Data.Configurations;

public class MeetingTagConfiguration : IEntityTypeConfiguration<MeetingTag>
{
    public void Configure(EntityTypeBuilder<MeetingTag> builder)
    {
        builder.HasKey(mt => new { mt.MeetingId, mt.TagId });
        builder.HasOne(mt => mt.Meeting).WithMany(m => m.Tags).HasForeignKey(mt => mt.MeetingId);
        builder.HasOne(mt => mt.Tag).WithMany(t => t.MeetingTags).HasForeignKey(mt => mt.TagId);
    }
}
