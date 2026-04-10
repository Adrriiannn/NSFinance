using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantAliasConfiguration : IEntityTypeConfiguration<MerchantAlias>
{
    public void Configure(EntityTypeBuilder<MerchantAlias> builder)
    {
        builder.ToTable("MerchantAliases");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.AliasText).HasMaxLength(320).IsRequired();
        builder.Property(x => x.NormalizedAliasText).HasMaxLength(320).IsRequired();
        builder.Property(x => x.AliasType).HasConversion<int>();
        builder.Property(x => x.Confidence).HasColumnType("double precision");
        builder.Property(x => x.Source).HasMaxLength(120).IsRequired();
        builder.Property(x => x.FirstSeenUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastSeenUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.MerchantId);
        builder.HasIndex(x => x.NormalizedAliasText);
        builder.HasIndex(x => new { x.NormalizedAliasText, x.IsActive });
        builder.HasIndex(x => new { x.MerchantId, x.NormalizedAliasText, x.AliasType }).IsUnique();
        builder.HasIndex(x => new { x.MerchantId, x.IsExactMatchPreferred, x.IsActive });

        builder.HasOne(x => x.Merchant)
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => x.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
