using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class BankConnectionAttemptConfiguration : IEntityTypeConfiguration<BankConnectionAttempt>
{
    public void Configure(EntityTypeBuilder<BankConnectionAttempt> builder)
    {
        builder.ToTable("BankConnectionAttempts");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.ProviderName).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ProviderEnvironment).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.Property(x => x.LaunchOriginPath).HasMaxLength(256);
        builder.Property(x => x.AppReturnUri).HasMaxLength(2048);
        builder.Property(x => x.CallbackState).HasMaxLength(1024).IsRequired();
        builder.Property(x => x.PublicToken).HasMaxLength(128).IsRequired();
        builder.Property(x => x.FailureCode).HasMaxLength(80);
        builder.Property(x => x.FailureReason).HasMaxLength(512);
        builder.Property(x => x.TransitionVersion).HasDefaultValue(0).IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => new { x.UserId, x.Status, x.ExpiresUtc });
        builder.HasIndex(x => x.ConnectionId);
        builder.HasIndex(x => x.CreatedUtc);
        builder.HasIndex(x => x.CallbackState).IsUnique();
        builder.HasIndex(x => x.PublicToken);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Connection)
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SupersededByAttempt)
            .WithMany()
            .HasForeignKey(x => x.SupersededByAttemptId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
