using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantEvidenceConfiguration : IEntityTypeConfiguration<MerchantEvidence>
{
    public void Configure(EntityTypeBuilder<MerchantEvidence> builder)
    {
        builder.ToTable("MerchantEvidence");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EvidenceType).HasConversion<int>();
        builder.Property(x => x.EvidenceSummary).HasMaxLength(1200).IsRequired();
        builder.Property(x => x.Confidence).HasColumnType("double precision");
        builder.Property(x => x.SourceReference).HasMaxLength(1024);
        builder.Property(x => x.CapturedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.MerchantId);
        builder.HasIndex(x => x.EvidenceType);
        builder.HasIndex(x => x.CapturedUtc);

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.Evidence)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
