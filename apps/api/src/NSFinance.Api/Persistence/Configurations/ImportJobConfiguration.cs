using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ImportJobConfiguration : IEntityTypeConfiguration<ImportJob>
{
    public void Configure(EntityTypeBuilder<ImportJob> builder)
    {
        builder.ToTable("ImportJobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.FileName).HasMaxLength(260).IsRequired();
        builder.Property(x => x.Kind)
            .HasMaxLength(32)
            .HasDefaultValue(ImportJobKinds.Legacy)
            .IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SourceFingerprint).HasMaxLength(64);
        builder.Property(x => x.MappingFingerprint).HasMaxLength(64);
        builder.Property(x => x.ParserVersion).HasMaxLength(32);
        builder.Property(x => x.MappingVersion).HasMaxLength(32);
        builder.Property(x => x.MappingJson).HasColumnType("jsonb");
        builder.Property(x => x.AccountCurrency).HasMaxLength(3);
        builder.Property(x => x.Locale).HasMaxLength(32);
        builder.Property(x => x.TimeZoneId).HasMaxLength(64);
        builder.Property(x => x.FailureCode).HasMaxLength(96);
        builder.Property(x => x.Revision).HasDefaultValue(1).IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new
        {
            x.UserId,
            x.FinancialAccountId,
            x.Kind,
            x.SourceFingerprint,
            x.MappingFingerprint,
            x.ParserVersion,
            x.MappingVersion
        })
            .IsUnique()
            .HasFilter("\"Kind\" = 'statement_csv'")
            .HasDatabaseName(StatementImportIndexNames.ImportJobIdempotency);
        builder.HasIndex(x => new
        {
            x.UserId,
            x.FinancialAccountId,
            x.SourceFingerprint
        })
            .IsUnique()
            .HasFilter("\"Kind\" = 'statement_csv' AND \"Status\" = 'committed'")
            .HasDatabaseName(StatementImportIndexNames.ImportJobCommittedSource);
        builder.HasIndex(x => new { x.UserId, x.Kind, x.Status, x.UpdatedUtc });
        builder.HasIndex(x => new { x.FinancialAccountId, x.CreatedUtc });
        builder.HasIndex(x => new { x.Status, x.ExpiresUtc });
        builder.HasIndex(x => new { x.FinancialAccountId, x.UserId });

        builder.HasOne(x => x.User)
            .WithMany(x => x.ImportJobs)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FinancialAccount)
            .WithMany(x => x.ImportJobs)
            .HasForeignKey(x => new { x.FinancialAccountId, x.UserId })
            .HasPrincipalKey(x => new { x.Id, x.UserId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
