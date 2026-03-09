using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class BankConnectionTokenConfiguration : IEntityTypeConfiguration<BankConnectionToken>
{
    public void Configure(EntityTypeBuilder<BankConnectionToken> builder)
    {
        builder.ToTable("BankConnectionTokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.EncryptedRefreshToken).HasMaxLength(2048);
        builder.Property(x => x.TokenObtainedUtc).IsRequired();
        builder.Property(x => x.IsRevoked).HasDefaultValue(false);
        builder.Property(x => x.RevokedUtc);

        builder.HasIndex(x => x.ConnectionId).IsUnique();

        builder.HasOne(x => x.Connection)
            .WithOne(x => x.Token)
            .HasForeignKey<BankConnectionToken>(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
