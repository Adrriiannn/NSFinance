using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantKnowledgeSeedRunConfiguration : IEntityTypeConfiguration<MerchantKnowledgeSeedRun>
{
    public void Configure(EntityTypeBuilder<MerchantKnowledgeSeedRun> builder)
    {
        builder.ToTable("MerchantKnowledgeSeedRuns");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangesJson).HasColumnType("jsonb").IsRequired();

        // One seed run per catalog version: the concurrency guard.
        builder.HasIndex(x => x.CharacteristicsVersion).IsUnique();
    }
}
