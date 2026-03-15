using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NSFinance.Api.Persistence.Entities;

namespace NSFinance.Api.Persistence.Configurations;

public class ExpensePlanConfiguration : IEntityTypeConfiguration<ExpensePlan>
{
    public void Configure(EntityTypeBuilder<ExpensePlan> builder)
    {
        builder.ToTable("ExpensePlans");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.CreatorDisplayNameSnapshot).HasMaxLength(160).IsRequired();
        builder.Property(x => x.CreatorTagSnapshot).HasMaxLength(90).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(160).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1200);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.Status).HasMaxLength(24).IsRequired();
        builder.Property(x => x.PlanType).HasMaxLength(24).IsRequired();
        builder.Property(x => x.PlanOriginType).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ExpectedIncomeTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.ExpectedSpendTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.ExpectedRemainingTotal).HasColumnType("numeric(18,2)");
        builder.Property(x => x.TagsJson)
            .HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb");
        builder.Property(x => x.StatusReason).HasMaxLength(200);
        builder.Property(x => x.RecurrenceRuleJson).HasColumnType("jsonb");
        builder.Property(x => x.SharingMode).HasMaxLength(24);
        builder.Property(x => x.SharedIdentity).HasMaxLength(80);
        builder.Property(x => x.CreatedAtUtc).HasDefaultValueSql("timezone('utc', now())");
        builder.Property(x => x.UpdatedAtUtc).HasDefaultValueSql("timezone('utc', now())");

        builder.HasIndex(x => new { x.UserId, x.Status, x.StartDateUtc });
        builder.HasIndex(x => new { x.UserId, x.UpdatedAtUtc });
        builder.HasIndex(x => x.SourcePlanId);
        builder.HasIndex(x => x.ImportedFromPublicPlanId);
        builder.HasIndex(x => x.SharedIdentity);

        builder.HasOne(x => x.User)
            .WithMany(x => x.ExpensePlans)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.SourcePlan)
            .WithMany(x => x.DerivedPlans)
            .HasForeignKey(x => x.SourcePlanId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ImportedFromPublicPlan)
            .WithMany(x => x.ImportedPlans)
            .HasForeignKey(x => x.ImportedFromPublicPlanId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
