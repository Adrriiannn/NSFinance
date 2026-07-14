using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class UserFinancialCommitmentConfiguration : IEntityTypeConfiguration<UserFinancialCommitment>
{
    public void Configure(EntityTypeBuilder<UserFinancialCommitment> builder)
    {
        builder.ToTable("UserFinancialCommitments");

        builder.HasKey(item => item.Id);
        builder.Property(item => item.TargetCommitmentId).HasMaxLength(200);
        builder.Property(item => item.OriginType).HasMaxLength(24).IsRequired();
        builder.Property(item => item.State).HasMaxLength(24).IsRequired();
        builder.Property(item => item.DecisionMode).HasMaxLength(24).IsRequired();
        builder.Property(item => item.LastAction).HasMaxLength(24).IsRequired();
        builder.Property(item => item.SnapshotJson).HasColumnType("jsonb").IsRequired();
        builder.Property(item => item.OverrideJson).HasColumnType("jsonb");
        builder.Property(item => item.Revision).IsConcurrencyToken();
        builder.Property(item => item.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(item => item.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(item => new { item.UserId, item.TargetCommitmentId }).IsUnique();
        builder.HasIndex(item => new { item.UserId, item.State, item.EffectiveNextDateUtc });
        builder.HasIndex(item => new { item.UserId, item.UpdatedUtc });

        builder.HasOne(item => item.User)
            .WithMany(user => user.FinancialCommitments)
            .HasForeignKey(item => item.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.EffectiveAccount)
            .WithMany()
            .HasForeignKey(item => item.EffectiveAccountId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
