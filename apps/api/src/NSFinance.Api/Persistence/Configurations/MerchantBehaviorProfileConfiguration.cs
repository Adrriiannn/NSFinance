using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantBehaviorProfileConfiguration : IEntityTypeConfiguration<MerchantBehaviorProfile>
{
    public void Configure(EntityTypeBuilder<MerchantBehaviorProfile> builder)
    {
        builder.ToTable("MerchantBehaviorProfiles");

        builder.HasKey(x => x.MerchantId);
        builder.Property(x => x.PaymentBehaviorConfidence).HasColumnType("double precision");
        builder.Property(x => x.BehaviorSummary).HasMaxLength(1200).IsRequired();

        builder.HasOne(x => x.Merchant)
            .WithOne(x => x.BehaviorProfile)
            .HasForeignKey<MerchantBehaviorProfile>(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
