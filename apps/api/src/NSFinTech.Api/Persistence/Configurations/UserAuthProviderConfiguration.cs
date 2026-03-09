using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class UserAuthProviderConfiguration : IEntityTypeConfiguration<UserAuthProvider>
{
    public void Configure(EntityTypeBuilder<UserAuthProvider> builder)
    {
        builder.ToTable("UserAuthProviders");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ProviderSubject).HasMaxLength(180);
        builder.Property(x => x.LinkedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastUsedAtUtc);
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        builder.HasOne(x => x.User)
            .WithMany(x => x.AuthProviders)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.UserId, x.ProviderType }).IsUnique();
        builder.HasIndex(x => new { x.ProviderType, x.ProviderSubject })
            .IsUnique()
            .HasFilter("\"ProviderSubject\" IS NOT NULL");
    }
}
