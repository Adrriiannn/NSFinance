using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class OpenBankingConnectionConfiguration : IEntityTypeConfiguration<OpenBankingConnection>
{
    public void Configure(EntityTypeBuilder<OpenBankingConnection> builder)
    {
        builder.ToTable("OpenBankingConnections");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderName).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ProviderEnvironment).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ProviderConnectionReference).HasMaxLength(180);
        builder.Property(x => x.ProviderId).HasMaxLength(180);
        builder.Property(x => x.ProviderDisplayName).HasMaxLength(180);
        builder.Property(x => x.ProviderIconUri).HasMaxLength(1024);
        builder.Property(x => x.ProviderLogoUri).HasMaxLength(1024);
        builder.Property(x => x.ProviderBrandBgColor).HasMaxLength(32);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.LastErrorCode).HasMaxLength(80);
        builder.Property(x => x.LastErrorReason).HasMaxLength(512);
        builder.Property(x => x.AuthStateNonce).HasMaxLength(256);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.ProviderName, x.ProviderEnvironment });
        builder.HasIndex(x => x.AuthStateNonce)
            .IsUnique()
            .HasFilter("\"AuthStateNonce\" IS NOT NULL");

        builder.HasOne(x => x.User)
            .WithMany(x => x.OpenBankingConnections)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
