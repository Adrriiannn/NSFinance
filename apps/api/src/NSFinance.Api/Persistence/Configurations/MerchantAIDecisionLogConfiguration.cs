using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class MerchantAIDecisionLogConfiguration : IEntityTypeConfiguration<MerchantAIDecisionLog>
{
    public void Configure(EntityTypeBuilder<MerchantAIDecisionLog> builder)
    {
        builder.ToTable("MerchantAIDecisionLogs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Descriptor).HasMaxLength(512).IsRequired();
        builder.Property(x => x.NormalizedDescriptor).HasMaxLength(320).IsRequired();
        builder.Property(x => x.MerchantKey).HasMaxLength(320).IsRequired();
        builder.Property(x => x.DomainCandidates).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TriggerMode).HasMaxLength(8).IsRequired();
        builder.Property(x => x.DeterministicResult).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RegistryResult).HasMaxLength(120).IsRequired();
        builder.Property(x => x.AISkipReason).HasMaxLength(120).IsRequired();
        builder.Property(x => x.BudgetState).HasMaxLength(512).IsRequired();
        builder.Property(x => x.CooldownState).HasMaxLength(512).IsRequired();
        builder.Property(x => x.ModelUsed).HasMaxLength(120);
        builder.Property(x => x.FinalState).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.UserId, x.CreatedUtc });
        builder.HasIndex(x => new { x.ConnectionId, x.CreatedUtc });
        builder.HasIndex(x => new { x.SyncRunId, x.CreatedUtc });
        builder.HasIndex(x => new { x.MerchantKey, x.CreatedUtc });
        builder.HasIndex(x => x.AICallExecuted);
    }
}

