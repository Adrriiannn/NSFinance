using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class MfaTrustedDeviceConfiguration : IEntityTypeConfiguration<MfaTrustedDevice>
{
    public void Configure(EntityTypeBuilder<MfaTrustedDevice> builder)
    {
        builder.ToTable("MfaTrustedDevices");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RevocationReason).HasMaxLength(80);

        builder.HasOne(x => x.User)
            .WithMany(x => x.MfaTrustedDevices)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Device)
            .WithMany(x => x.MfaTrustedDevices)
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.DeviceId, x.ExpiresUtc });
        builder.HasIndex(x => new { x.UserId, x.RevokedUtc });
    }
}
