using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class MerchantAliasConflictConfiguration : IEntityTypeConfiguration<MerchantAliasConflict>
{
    public void Configure(EntityTypeBuilder<MerchantAliasConflict> builder)
    {
        builder.ToTable("MerchantAliasConflicts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.NormalizedAliasText).HasMaxLength(320).IsRequired();
        builder.Property(x => x.AliasType).HasConversion<int>();
        builder.Property(x => x.ProposedSource).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ProposedTrustLevel).HasConversion<int>();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.OccurrenceCount).HasDefaultValue(1);
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastSeenUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.NormalizedAliasText, x.AliasType, x.ExistingMerchantId, x.ProposedMerchantId }).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.LastSeenUtc);

        builder.HasOne(x => x.ExistingMerchant)
            .WithMany()
            .HasForeignKey(x => x.ExistingMerchantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ProposedMerchant)
            .WithMany()
            .HasForeignKey(x => x.ProposedMerchantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
