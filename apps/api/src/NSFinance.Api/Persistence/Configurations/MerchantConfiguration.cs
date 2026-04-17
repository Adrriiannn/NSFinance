using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CanonicalName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedCanonicalName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.CanonicalMerchantName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.NormalizedMerchantKey).HasMaxLength(320).IsRequired();
        builder.Property(x => x.WebsiteDomain).HasMaxLength(160);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.MerchantVertical).HasMaxLength(80);
        builder.Property(x => x.GoodsServicesType).HasMaxLength(80);
        builder.Property(x => x.MerchantSummary).HasMaxLength(1024);
        builder.Property(x => x.CategoryCandidates).HasColumnType("jsonb");
        builder.Property(x => x.AmbiguityFlags).HasMaxLength(512);
        builder.Property(x => x.InvestigationModel).HasMaxLength(120);
        builder.Property(x => x.Confidence).HasDefaultValue(0d);
        builder.Property(x => x.EvidenceQuality).HasDefaultValue(0d);
        builder.Property(x => x.FailureCount).HasDefaultValue(0);
        builder.Property(x => x.DisplayName).HasMaxLength(160).IsRequired();
        builder.Property(x => x.MerchantStatus).HasConversion<int>();
        builder.Property(x => x.MerchantType).HasConversion<int>();
        builder.Property(x => x.MerchantUsageType).HasConversion<int>();
        builder.Property(x => x.PrimaryCountryCode).HasMaxLength(2).IsRequired();
        builder.Property(x => x.OfficialWebsite).HasMaxLength(512);
        builder.Property(x => x.DescriptionSummary).HasMaxLength(1024);
        builder.Property(x => x.LastValidationResultCode).HasMaxLength(120);
        builder.Property(x => x.ValidationAttemptCount).HasDefaultValue(0);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.NormalizedCanonicalName).IsUnique();
        builder.HasIndex(x => x.NormalizedMerchantKey);
        builder.HasIndex(x => x.MerchantStatus);
        builder.HasIndex(x => x.MerchantType);
        builder.HasIndex(x => x.MerchantUsageType);
        builder.HasIndex(x => x.ParentMerchantId);
        builder.HasIndex(x => x.NextValidationDueUtc);
        builder.HasIndex(x => x.InvestigatedAtUtc);
        builder.HasIndex(x => x.InvestigationCooldownUntilUtc);
        builder.HasIndex(x => x.LastFailureUtc);

        builder.HasOne(x => x.ParentMerchant)
            .WithMany(x => x.ChildMerchants)
            .HasForeignKey(x => x.ParentMerchantId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
