using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ReferenceLaneJudgmentConfiguration : IEntityTypeConfiguration<ReferenceLaneJudgment>
{
    public void Configure(EntityTypeBuilder<ReferenceLaneJudgment> builder)
    {
        builder.ToTable("ReferenceLaneJudgments");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DefinitionKey).HasMaxLength(40);
        builder.Property(x => x.OutcomeCode).HasMaxLength(120);
        builder.Property(x => x.SummaryJson).HasColumnType("jsonb");

        // One judgment per row per catalog version: the cooldown that stops
        // re-judging, and the reopening that a version bump grants for free.
        builder.HasIndex(x => new { x.TransactionId, x.CharacteristicsVersion }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.JudgedUtc });
    }
}
