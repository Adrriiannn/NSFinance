using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class PlacesShortLivedCacheEntryConfiguration : IEntityTypeConfiguration<PlacesShortLivedCacheEntry>
{
    public void Configure(EntityTypeBuilder<PlacesShortLivedCacheEntry> builder)
    {
        builder.ToTable("PlacesShortLivedCache");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.PlaceId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.FieldMaskHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();

        builder.HasIndex(x => new { x.Provider, x.PlaceId, x.FieldMaskHash }).IsUnique();
        builder.HasIndex(x => x.ExpiresAtUtc);
    }
}
