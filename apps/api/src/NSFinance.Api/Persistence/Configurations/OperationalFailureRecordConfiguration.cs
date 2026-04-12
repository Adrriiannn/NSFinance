using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class OperationalFailureRecordConfiguration : IEntityTypeConfiguration<OperationalFailureRecord>
{
    public void Configure(EntityTypeBuilder<OperationalFailureRecord> builder)
    {
        builder.ToTable("OperationalFailureRecords");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Area).HasConversion<int>();
        builder.Property(x => x.Severity).HasConversion<int>();
        builder.Property(x => x.FailureType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Fingerprint).HasMaxLength(320).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(128);
        builder.Property(x => x.SubjectKey).HasMaxLength(320);
        builder.Property(x => x.FailureMessage).HasMaxLength(1200);
        builder.Property(x => x.DetailsJson).HasMaxLength(4000);
        builder.Property(x => x.OccurrenceCount).HasDefaultValue(1);
        builder.Property(x => x.FirstOccurredUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastOccurredUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.Area, x.FailureType, x.Fingerprint }).IsUnique();
        builder.HasIndex(x => x.LastOccurredUtc);
        builder.HasIndex(x => x.Severity);
    }
}
