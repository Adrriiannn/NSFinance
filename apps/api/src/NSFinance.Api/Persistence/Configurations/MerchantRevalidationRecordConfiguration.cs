using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantRevalidationRecordConfiguration : IEntityTypeConfiguration<MerchantRevalidationRecord>
{
    public void Configure(EntityTypeBuilder<MerchantRevalidationRecord> builder)
    {
        builder.ToTable("MerchantRevalidationRecords");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TriggerReason).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Outcome).HasConversion<int>();
        builder.Property(x => x.DecisionCode).HasMaxLength(80);
        builder.Property(x => x.PreviousStatus).HasConversion<int>();
        builder.Property(x => x.NewStatus).HasConversion<int>();
        builder.Property(x => x.LeadingEvidenceSummary).HasMaxLength(1200);
        builder.Property(x => x.ResultCode).HasMaxLength(120);
        builder.Property(x => x.DetailsJson).HasMaxLength(4000);
        builder.Property(x => x.AttemptedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.MerchantId);
        builder.HasIndex(x => x.AttemptedUtc);
        builder.HasIndex(x => new { x.MerchantId, x.AttemptedUtc });
        builder.HasIndex(x => x.Outcome);
        builder.HasIndex(x => x.ResultCode);

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.RevalidationRecords)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
