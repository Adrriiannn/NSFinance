using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinTech.Api.Persistence.Entities;

namespace NSFinTech.Api.Persistence.Configurations;

public class SessionRefreshTokenConfiguration : IEntityTypeConfiguration<SessionRefreshToken>
{
    public void Configure(EntityTypeBuilder<SessionRefreshToken> builder)
    {
        builder.ToTable("SessionRefreshTokens");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.RevocationReason).HasMaxLength(120);

        builder.HasOne(x => x.Session)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.ParentToken)
            .WithOne()
            .HasForeignKey<SessionRefreshToken>(x => x.ParentTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ReplacedByToken)
            .WithOne()
            .HasForeignKey<SessionRefreshToken>(x => x.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.FamilyId);
        builder.HasIndex(x => x.ExpiresUtc);
    }
}
