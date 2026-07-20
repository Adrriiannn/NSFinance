using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantKnowledgeCandidateConfiguration : IEntityTypeConfiguration<MerchantKnowledgeCandidate>
{
    public void Configure(EntityTypeBuilder<MerchantKnowledgeCandidate> builder)
    {
        builder.ToTable("MerchantKnowledgeCandidates");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedDescriptor).HasMaxLength(200).IsRequired();
        builder.Property(x => x.RawDescriptorSample).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ObservedDirection).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LastOutcomeCode).HasMaxLength(120);
        builder.Property(x => x.InvestigationSummaryJson).HasColumnType("jsonb");
        builder.Property(x => x.ObservedSpendAbs).HasPrecision(18, 2);

        builder.HasIndex(x => x.NormalizedDescriptor).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextEligibleUtc });
    }
}
