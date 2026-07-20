using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantKnowledgeConfiguration : IEntityTypeConfiguration<MerchantKnowledge>
{
    public void Configure(EntityTypeBuilder<MerchantKnowledge> builder)
    {
        builder.ToTable("MerchantKnowledge");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedPattern).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DirectionExpectation).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Source).HasMaxLength(40).IsRequired();
        builder.Property(x => x.VerificationEvidenceJson).HasColumnType("jsonb");

        // Composite uniqueness: one global row per pattern (code-level dedup
        // keeps null-UserId duplicates out) and one override per user+pattern.
        builder.HasIndex(x => new { x.UserId, x.NormalizedPattern }).IsUnique();
        builder.HasIndex(x => new { x.IsActive, x.Source });
    }
}
