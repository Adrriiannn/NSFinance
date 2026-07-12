using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class IdentityChallengeConfiguration : IEntityTypeConfiguration<IdentityChallenge>
{
    public void Configure(EntityTypeBuilder<IdentityChallenge> builder)
    {
        builder.ToTable("IdentityChallenges");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Purpose).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Channel).HasMaxLength(24).IsRequired();
        builder.Property(x => x.DestinationHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.GrantHash).HasMaxLength(128);
        builder.Property(x => x.RequestedByIp).HasMaxLength(64);
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.ConcurrencyToken).IsConcurrencyToken();

        builder.HasOne(x => x.User)
            .WithMany(x => x.IdentityChallenges)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.DestinationHash, x.Purpose, x.CreatedUtc });
        builder.HasIndex(x => new { x.UserId, x.Purpose, x.CreatedUtc });
        builder.HasIndex(x => x.GrantHash);
    }
}
