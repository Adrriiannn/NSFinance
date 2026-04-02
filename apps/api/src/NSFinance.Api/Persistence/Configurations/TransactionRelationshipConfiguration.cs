using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class TransactionRelationshipConfiguration : IEntityTypeConfiguration<TransactionRelationship>
{
    public void Configure(EntityTypeBuilder<TransactionRelationship> builder)
    {
        builder.ToTable("TransactionRelationships");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.RelationshipKey).HasMaxLength(220).IsRequired();
        builder.Property(x => x.RelationshipType).HasConversion<int>();
        builder.Property(x => x.RelationshipStatus).HasConversion<int>();
        builder.Property(x => x.RelationshipDirection).HasConversion<int>();
        builder.Property(x => x.ConfidenceTier).HasMaxLength(24).IsRequired();
        builder.Property(x => x.MatchReasonsJson).HasColumnType("jsonb");
        builder.Property(x => x.ProviderPolicyKey).HasMaxLength(64);
        builder.Property(x => x.AnalyticsTreatment).HasMaxLength(64);
        builder.Property(x => x.VirtualDestinationLabel).HasMaxLength(120);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.RelationshipKey).IsUnique();
        builder.HasIndex(x => x.SourceTransactionId);
        builder.HasIndex(x => x.TargetTransactionId);
        builder.HasIndex(x => x.RelationshipType);
        builder.HasIndex(x => x.RelationshipStatus);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.SourceTransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Transaction>()
            .WithMany()
            .HasForeignKey(x => x.TargetTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<RawBankTransaction>()
            .WithMany()
            .HasForeignKey(x => x.SourceRawBankTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<RawBankTransaction>()
            .WithMany()
            .HasForeignKey(x => x.TargetRawBankTransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(x => x.SourceFinancialAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<FinancialAccount>()
            .WithMany()
            .HasForeignKey(x => x.TargetFinancialAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

