using Archiva.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Archiva.Infrastructure.Data.Configurations;

public class DocumentAnalysisUsageConfiguration : IEntityTypeConfiguration<DocumentAnalysisUsage>
{
    public void Configure(EntityTypeBuilder<DocumentAnalysisUsage> builder)
    {
        // Usage stays after document deletion so removing a blob cannot refund provider spend.
        builder.HasKey(usage => usage.Id);
        builder.HasIndex(usage => new { usage.DocumentId, usage.MonthStartUtc }).IsUnique();
        builder.HasIndex(usage => usage.MonthStartUtc);
    }
}
