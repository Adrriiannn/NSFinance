using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.DeviceFingerprint).HasMaxLength(180).IsRequired();
        builder.Property(x => x.DeviceLabel).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(40);
        builder.Property(x => x.OsVersion).HasMaxLength(64);
        builder.Property(x => x.AppVersion).HasMaxLength(32);
        builder.Property(x => x.FirstSeenUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastSeenUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.IsTrusted).HasDefaultValue(false);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Devices)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.DeviceFingerprint }).IsUnique();
    }
}
