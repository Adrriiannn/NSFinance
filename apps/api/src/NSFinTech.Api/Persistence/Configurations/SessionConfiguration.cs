using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastSeenUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.DeviceLabel).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Platform).HasMaxLength(40);
        builder.Property(x => x.OsVersion).HasMaxLength(64);
        builder.Property(x => x.AppVersion).HasMaxLength(32);
        builder.Property(x => x.IpAddress).HasMaxLength(64);
        builder.Property(x => x.RiskFlagsJson).HasColumnType("jsonb");
        builder.Property(x => x.RevocationReason).HasMaxLength(120);

        builder.HasOne(x => x.User)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Device)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.DeviceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresUtc);
        builder.HasIndex(x => x.RefreshTokenFamilyId);
    }
}
