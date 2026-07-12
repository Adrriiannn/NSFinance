using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class TransactionalMessageConfiguration : IEntityTypeConfiguration<TransactionalMessage>
{
    public void Configure(EntityTypeBuilder<TransactionalMessage> builder)
    {
        builder.ToTable("TransactionalMessages");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Channel).HasMaxLength(24).IsRequired();
        builder.Property(x => x.TemplateKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Recipient).HasMaxLength(320).IsRequired();
        builder.Property(x => x.EncryptedPayload).HasColumnType("text").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(32).IsRequired();
        builder.Property(x => x.ProviderMessageId).HasMaxLength(160);
        builder.Property(x => x.LeaseId).HasMaxLength(64);
        builder.Property(x => x.LastFailureCode).HasMaxLength(80);

        builder.HasOne(x => x.User)
            .WithMany(x => x.TransactionalMessages)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.IdentityChallenge)
            .WithOne(x => x.Message)
            .HasForeignKey<TransactionalMessage>(x => x.IdentityChallengeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.IdentityChallengeId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.NextAttemptUtc });
        builder.HasIndex(x => x.ProviderMessageId);
    }
}
