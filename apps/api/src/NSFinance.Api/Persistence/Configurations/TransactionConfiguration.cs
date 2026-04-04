using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.ToTable("Transactions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(512).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(140);
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.Property(x => x.TransferKind).HasConversion<int?>();
        builder.Property(x => x.LinkedTransferTransactionId);
        builder.Property(x => x.LinkedTransferMatchedUtc);
        builder.Property(x => x.TransferMatchConfidenceScore);
        builder.Property(x => x.TransferMatchConfidenceTier).HasMaxLength(24);
        builder.Property(x => x.TransferMatchReason).HasMaxLength(240);
        builder.Property(x => x.DeterministicEnrichmentVersion);
        builder.Property(x => x.LastDeterministicEnrichedUtc);
        builder.Property(x => x.DeterministicClassificationStatus).HasConversion<int>();
        builder.Property(x => x.DeterministicClassificationVersion);
        builder.Property(x => x.DeterministicClassificationRuleKey).HasMaxLength(96);
        builder.Property(x => x.DeterministicClassificationCategoryId);
        builder.Property(x => x.DeterministicClassificationSubcategoryId);
        builder.Property(x => x.DeterministicLinkedTransactionId);
        builder.Property(x => x.DeterministicRelationshipType).HasMaxLength(40);
        builder.Property(x => x.DeterministicRelationshipGroupId);
        builder.Property(x => x.DeterministicMatchScore);
        builder.Property(x => x.DeterministicReasonCode).HasMaxLength(96);
        builder.Property(x => x.DeterministicReasonDetailJson).HasColumnType("jsonb");
        builder.Property(x => x.DeterministicClassificationEvaluatedUtc);
        builder.Property(x => x.DeterministicClassificationTerminal).HasDefaultValue(false);
        builder.Property(x => x.DeterministicDeferredRetryEligible).HasDefaultValue(false);
        builder.Property(x => x.DeterministicLastRetryConsideredUtc);
        builder.Property(x => x.NeedsDeterministicReclassification).HasDefaultValue(false);
        builder.Property(x => x.DeterministicSourceSignature).HasMaxLength(160);
        builder.Property(x => x.BookedAtUtc).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.MetadataUpdatedUtc);

        builder.HasIndex(x => x.TaxonomyCategoryId);
        builder.HasIndex(x => x.TaxonomySubcategoryId);
        builder.HasIndex(x => x.TransferKind);
        builder.HasIndex(x => x.LinkedTransferTransactionId);
        builder.HasIndex(x => x.TransferMatchConfidenceTier);
        builder.HasIndex(x => x.DeterministicEnrichmentVersion);
        builder.HasIndex(x => x.DeterministicClassificationStatus);
        builder.HasIndex(x => x.DeterministicClassificationVersion);
        builder.HasIndex(x => x.DeterministicClassificationTerminal);
        builder.HasIndex(x => x.DeterministicDeferredRetryEligible);
        builder.HasIndex(x => x.NeedsDeterministicReclassification);
        builder.HasIndex(x => x.DeterministicLinkedTransactionId);
        builder.HasIndex(x => x.DeterministicRelationshipGroupId);
        builder.HasIndex(x => new { x.DeterministicClassificationVersion, x.DeterministicClassificationTerminal });

        builder.HasOne(x => x.FinancialAccount)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.FinancialAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Category)
            .WithMany(x => x.Transactions)
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
