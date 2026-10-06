using Archiva.Domain.Entities;
using Archiva.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Archiva.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder
            .Property(document => document.AnalysisStatus)
            .HasDefaultValue(DocumentAnalysisStatus.Pending)
            .HasSentinel((DocumentAnalysisStatus)0);
        builder.Property(document => document.AnalysisUnitLimit).HasDefaultValue(20);
        builder.Property(document => document.SummaryInputCharacterLimit).HasDefaultValue(100_000);
    }
}
