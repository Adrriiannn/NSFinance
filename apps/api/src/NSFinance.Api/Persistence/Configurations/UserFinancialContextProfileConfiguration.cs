using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public sealed class UserFinancialContextProfileConfiguration : IEntityTypeConfiguration<UserFinancialContextProfile>
{
    public void Configure(EntityTypeBuilder<UserFinancialContextProfile> builder)
    {
        builder.ToTable("UserFinancialContextProfiles");

        builder.HasKey(x => x.UserId);
        builder.Property(x => x.Country).HasMaxLength(8).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.MonthlyIncomeRange).HasMaxLength(64);
        builder.Property(x => x.KnownObligationsJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.BudgetStructureJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.ActivePlansJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.SpendingTendenciesJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.CategoryFlexibilityMarkersJson).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.AdviceStylePreference).HasMaxLength(24).IsRequired();
        builder.Property(x => x.CreatedUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasOne(x => x.User)
            .WithOne(x => x.FinancialContextProfile)
            .HasForeignKey<UserFinancialContextProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
