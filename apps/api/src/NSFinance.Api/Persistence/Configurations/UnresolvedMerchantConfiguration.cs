using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class UnresolvedMerchantConfiguration : IEntityTypeConfiguration<UnresolvedMerchant>
{
    public void Configure(EntityTypeBuilder<UnresolvedMerchant> builder)
    {
        builder.ToTable("UnresolvedMerchants");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.RawDescriptor).HasMaxLength(512).IsRequired();
        builder.Property(x => x.NormalizedDescriptor).HasMaxLength(320).IsRequired();
        builder.Property(x => x.OccurrenceCount).HasDefaultValue(1);
        builder.Property(x => x.InvestigationAttemptCount).HasDefaultValue(0);
        builder.Property(x => x.LastInvestigationFailureCode).HasMaxLength(120);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.Notes).HasMaxLength(1200);
        builder.Property(x => x.TotalObservedSpendAbs).HasPrecision(18, 2).HasDefaultValue(0m);
        builder.Property(x => x.QueuePriorityScore).HasDefaultValue(0d);
        builder.Property(x => x.QueueRetryCount).HasDefaultValue(0);
        builder.Property(x => x.InvestigationLockId);
        builder.Property(x => x.InvestigationInProgress).HasDefaultValue(false);
        builder.Property(x => x.FirstSeenUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.LastSeenUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => x.NormalizedDescriptor).IsUnique();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.LastSeenUtc);
        builder.HasIndex(x => x.LastInvestigationUtc);
        builder.HasIndex(x => x.NextEligibleInvestigationUtc);
        builder.HasIndex(x => x.QueuePriorityScore);
        builder.HasIndex(x => x.QueueEnqueuedAtUtc);
        builder.HasIndex(x => x.InvestigationInProgress);
        builder.HasIndex(x => x.QueueNextRetryUtc);
    }
}
