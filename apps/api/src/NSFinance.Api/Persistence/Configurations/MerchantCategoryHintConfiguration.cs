using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantCategoryHintConfiguration : IEntityTypeConfiguration<MerchantCategoryHint>
{
    public void Configure(EntityTypeBuilder<MerchantCategoryHint> builder)
    {
        builder.ToTable("MerchantCategoryHints");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Confidence).HasColumnType("double precision");
        builder.Property(x => x.HintStrength).HasConversion<int>();
        builder.Property(x => x.Source).HasMaxLength(120).IsRequired();

        builder.HasIndex(x => x.MerchantId);
        builder.HasIndex(x => new { x.DomainId, x.CategoryId, x.SubcategoryId });
        builder.HasIndex(x => new { x.MerchantId, x.IsActive });
        builder.HasIndex(x => new { x.MerchantId, x.DomainId, x.CategoryId, x.SubcategoryId, x.Source }).IsUnique();

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.CategoryHints)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
