using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class PlaceRegistryEntryConfiguration : IEntityTypeConfiguration<PlaceRegistryEntry>
{
    public void Configure(EntityTypeBuilder<PlaceRegistryEntry> builder)
    {
        builder.ToTable("PlaceRegistry");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Provider).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ProviderPlaceId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.InternalTagsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.InternalMetricsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.FirstSeenAtUtc).IsRequired();
        builder.Property(x => x.LastSeenAtUtc).IsRequired();

        builder.HasIndex(x => new { x.Provider, x.ProviderPlaceId }).IsUnique();
        builder.HasIndex(x => x.LastSeenAtUtc);
    }
}
