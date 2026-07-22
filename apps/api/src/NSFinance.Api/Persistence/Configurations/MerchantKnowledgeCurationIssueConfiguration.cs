using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantKnowledgeCurationIssueConfiguration : IEntityTypeConfiguration<MerchantKnowledgeCurationIssue>
{
    public void Configure(EntityTypeBuilder<MerchantKnowledgeCurationIssue> builder)
    {
        builder.ToTable("MerchantKnowledgeCurationIssues");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.IssueType).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.EvidenceJson).HasColumnType("jsonb");

        builder.HasIndex(x => new { x.KnowledgeId, x.IssueType });
        builder.HasIndex(x => x.Status);
    }
}
