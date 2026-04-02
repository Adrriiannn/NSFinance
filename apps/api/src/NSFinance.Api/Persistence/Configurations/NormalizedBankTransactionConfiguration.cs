using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class NormalizedBankTransactionConfiguration : IEntityTypeConfiguration<NormalizedBankTransaction>
{
    public void Configure(EntityTypeBuilder<NormalizedBankTransaction> builder)
    {
        builder.ToTable("NormalizedBankTransactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(180);
        builder.Property(x => x.NormalizedProviderTransactionId).HasMaxLength(180);
        builder.Property(x => x.DedupeKey).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.BookedAtUtc).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512).IsRequired();
        builder.Property(x => x.TransactionType).HasMaxLength(80);
        builder.Property(x => x.TransactionStatus).HasMaxLength(80);
        builder.Property(x => x.SourceEndpoint).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderStatus).HasMaxLength(80);
        builder.Property(x => x.StatusNormalizationReason).HasMaxLength(120);
        builder.Property(x => x.ProviderTimestampRaw).HasMaxLength(80);
        builder.Property(x => x.ValueTimestampRaw).HasMaxLength(80);
        builder.Property(x => x.TimestampSource).HasMaxLength(64);
        builder.Property(x => x.TimestampPrecision).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TimestampNormalizedByPolicy).HasMaxLength(64);
        builder.Property(x => x.NormalizationPolicyKey).HasMaxLength(64);
        builder.Property(x => x.NormalizationPolicyFamily).HasMaxLength(64);
        builder.Property(x => x.InterpretationConfidenceTier).HasMaxLength(24);
        builder.Property(x => x.InterpretationReasonJson).HasColumnType("jsonb");
        builder.Property(x => x.ImportedUtc).IsRequired();
        builder.Property(x => x.LastNormalizedUtc).IsRequired();

        builder.HasIndex(x => x.RawBankTransactionId).IsUnique();
        builder.HasIndex(x => x.LinkedBankAccountId);
        builder.HasIndex(x => x.FinancialAccountId);
        builder.HasIndex(x => x.ProjectedTransactionId);
        builder.HasIndex(x => new { x.LinkedBankAccountId, x.ProviderTransactionId });
        builder.HasIndex(x => new { x.LinkedBankAccountId, x.DedupeKey });

        builder.HasOne(x => x.RawBankTransaction)
            .WithOne(x => x.NormalizedTransaction)
            .HasForeignKey<NormalizedBankTransaction>(x => x.RawBankTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.LinkedBankAccount)
            .WithMany()
            .HasForeignKey(x => x.LinkedBankAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.FinancialAccount)
            .WithMany()
            .HasForeignKey(x => x.FinancialAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.ProjectedTransaction)
            .WithMany()
            .HasForeignKey(x => x.ProjectedTransactionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
