using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantKnowledgeFindingConfiguration : IEntityTypeConfiguration<MerchantKnowledgeFinding>
{
    public void Configure(EntityTypeBuilder<MerchantKnowledgeFinding> builder)
    {
        builder.ToTable("MerchantKnowledgeFindings");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CanonicalName).HasMaxLength(200);
        builder.Property(x => x.AcceptanceDecision).HasMaxLength(60).IsRequired();
        builder.Property(x => x.OutcomeCode).HasMaxLength(120).IsRequired();
        builder.Property(x => x.FindingsJson).HasColumnType("jsonb").IsRequired();

        builder.HasIndex(x => new { x.CandidateId, x.FindingVersion }).IsUnique();
        builder.HasIndex(x => x.KnowledgeId);
    }
}
