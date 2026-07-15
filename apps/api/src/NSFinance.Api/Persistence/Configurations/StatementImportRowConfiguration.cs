using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class StatementImportRowConfiguration : IEntityTypeConfiguration<StatementImportRow>
{
    public void Configure(EntityTypeBuilder<StatementImportRow> builder)
    {
        builder.ToTable("StatementImportRows");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.RowFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceReferenceFingerprint).HasMaxLength(64);
        builder.Property(x => x.ValidationStatus).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ValidationCode).HasMaxLength(64);
        builder.Property(x => x.DuplicateClassification).HasMaxLength(16).IsRequired();
        builder.Property(x => x.ReviewDisposition).HasMaxLength(16).IsRequired();
        builder.Property(x => x.SourceEvidenceJson).HasColumnType("jsonb");
        builder.Property(x => x.EffectiveDate).HasColumnType("date");
        builder.Property(x => x.TimestampPrecision).HasMaxLength(16);
        builder.Property(x => x.Description).HasMaxLength(512);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.ImportJobId, x.RowNumber })
            .IsUnique()
            .HasDatabaseName(StatementImportIndexNames.BatchRowNumber);
        builder.HasIndex(x => new { x.ImportJobId, x.ValidationStatus, x.ReviewDisposition });
        builder.HasIndex(x => new { x.ImportJobId, x.RowFingerprint });
        builder.HasIndex(x => x.DuplicateCandidateTransactionId);
        builder.HasIndex(x => x.EvidenceExpiresUtc);
        builder.HasIndex(x => x.CommittedTransactionId)
            .IsUnique()
            .HasFilter("\"CommittedTransactionId\" IS NOT NULL")
            .HasDatabaseName(StatementImportIndexNames.CommittedTransaction);

        builder.HasOne(x => x.ImportJob)
            .WithMany(x => x.Rows)
            .HasForeignKey(x => x.ImportJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.DuplicateCandidateTransaction)
            .WithMany()
            .HasForeignKey(x => x.DuplicateCandidateTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.CommittedTransaction)
            .WithOne(x => x.StatementImportRow)
            .HasForeignKey<StatementImportRow>(x => x.CommittedTransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
