using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class BankingOperationJobConfiguration : IEntityTypeConfiguration<BankingOperationJob>
{
    public void Configure(EntityTypeBuilder<BankingOperationJob> builder)
    {
        builder.ToTable("BankingOperationJobs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.OperationType).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.LeaseId).HasMaxLength(64);
        builder.Property(x => x.LastFailureCode).HasMaxLength(120);
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.ConnectionId, x.OperationType }).IsUnique();
        builder.HasIndex(x => new { x.OperationType, x.Status, x.NextAttemptUtc });
        builder.HasIndex(x => new { x.Status, x.LeaseExpiresUtc });
        builder.HasIndex(x => x.UserId);

        builder.HasOne(x => x.Connection)
            .WithMany()
            .HasForeignKey(x => x.ConnectionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
